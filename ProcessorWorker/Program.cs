using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using LegacyMvc.MDL.Commands;
using LegacyMvc.PRM;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModernBff.MDL.Commands;
using Newtonsoft.Json;

namespace ProcessorWorker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 内部 HTTP エンドポイントの待ち受けポート (デフォルト: http://localhost:5055)
        string internalUrls = builder.Configuration["ProcessorWorker:InternalHttpUrl"] ?? "http://localhost:5055";
        builder.WebHost.UseUrls(internalUrls);

        // Windows サービス (sc.exe) としてもスタンドアロンプロセスとしても起動可能 (IIS リサイクルの影響を受けない)
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "StranglerProcessorWorker";
        });

        // 1. 戻り値不要な非同期ジョブ用ワーカー (TIFF->PNG、OCR、Elasticsearch、BOM/品目/図面メール送信)
        builder.Services.AddHostedService<SystemBackgroundProcessorWorker>();

        // 2. 戻り値が必要なオンデマンド同期処理用 Named Pipe RPC サーバー (\\.\pipe\StranglerProcessorWorker.StampPipe)
        builder.Services.AddHostedService<ProcessorWorkerNamedPipeServer>();

        var app = builder.Build();

        string workerId = $"ProcessorWorker.exe (PID={Process.GetCurrentProcess().Id}, Out-of-Process Worker)";

        // =========================================================================
        // 【同期エンドポイント1: 内部 HTTP API】
        // POST http://localhost:5055/internal/drawings/stamp
        // IIS (ModernBff / LegacyMvc) からオンデマンドで呼び出され、
        // 「承認スタンプ」＋「閲覧日時・閲覧ログインユーザ名 (社外秘透かし)」を焼き込んだ図面データを返却します。
        // =========================================================================
        app.MapPost("/internal/drawings/stamp", async (HttpContext httpContext, ILogger<Program> logger) =>
        {
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            string bodyJson = await reader.ReadToEndAsync();
            var req = JsonConvert.DeserializeObject<DrawingStampRequest>(bodyJson) ?? new DrawingStampRequest();

            logger.LogInformation(
                "[InternalHttp 同期スタンプ要求受信] 図面={DrawingNo} | 承認状態={ApprovalStatus} | 閲覧者={ViewerName}({ViewerId}) | 閲覧日時={ViewedAt}",
                req.DrawingNo,
                req.ApprovalStatus,
                req.ViewerUserName,
                req.ViewerUserId,
                req.ViewedAtLocalText);

            DrawingStampResponse response = DrawingStampBurner.BurnStampOnDemand(
                req,
                workerProcessName: workerId,
                transportUsed: "InternalHttp (POST /internal/drawings/stamp)",
                endpointName: $"{internalUrls}/internal/drawings/stamp");

            string respJson = JsonConvert.SerializeObject(response);
            return Results.Content(respJson, "application/json; charset=utf-8");
        });

        // ヘルスチェック & エンドポイント一覧確認用
        app.MapGet("/internal/health", () => Results.Ok(new
        {
            WorkerProcess = workerId,
            Status = "Running",
            AsyncPipelines = new[]
            {
                "Drawing Outbox Queue (TIFF -> Thumbnail PNG, OCR, Elasticsearch Indexing)",
                "System Email & Notification Outbox (ITM / BOM / DRW Workflow Completion)"
            },
            SyncRpcEndpoints = new
            {
                NamedPipe = $@"\\.\pipe\{DrawingStampBurner.DefaultNamedPipeName}",
                InternalHttp = $"{internalUrls}/internal/drawings/stamp"
            },
            TimestampUtc = DateTime.UtcNow.ToString("O")
        }));

        await app.RunAsync();
    }
}

/// <summary>
/// 【同期エンドポイント2: Windows 名前付きパイプ (Named Pipe) RPC サーバー】
/// \\.\pipe\StranglerProcessorWorker.StampPipe を待ち受け、同一筐体内の IIS (ModernBff / LegacyMvc) から
/// TCP ポートを介さず高速にオンデマンド・スタンプ焼き込み要求を受け取り、焼き込み済み画像を返却します。
/// </summary>
public class ProcessorWorkerNamedPipeServer : BackgroundService
{
    private readonly ILogger<ProcessorWorkerNamedPipeServer> _logger;

    public ProcessorWorkerNamedPipeServer(ILogger<ProcessorWorkerNamedPipeServer> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string pipeName = DrawingStampBurner.DefaultNamedPipeName;
        string workerId = $"ProcessorWorker.exe (PID={Process.GetCurrentProcess().Id}, NamedPipeServer)";

        _logger.LogInformation("[NamedPipe サーバー起動] 待ち受けパイプ: \\\\.\\pipe\\{PipeName}", pipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var serverStream = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await serverStream.WaitForConnectionAsync(stoppingToken);

                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using var reader = new StreamReader(serverStream, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                string? requestJson = await reader.ReadLineAsync(stoppingToken);

                if (!string.IsNullOrWhiteSpace(requestJson))
                {
                    var req = JsonConvert.DeserializeObject<DrawingStampRequest>(requestJson) ?? new DrawingStampRequest();

                    _logger.LogInformation(
                        "[NamedPipe 同期スタンプ要求受信] 図面={DrawingNo} | 閲覧者={ViewerName}({ViewerId}) | 閲覧日時={ViewedAt}",
                        req.DrawingNo,
                        req.ViewerUserName,
                        req.ViewerUserId,
                        req.ViewedAtLocalText);

                    DrawingStampResponse response = DrawingStampBurner.BurnStampOnDemand(
                        req,
                        workerProcessName: workerId,
                        transportUsed: "NamedPipe (IPC)",
                        endpointName: $@"\\.\pipe\{pipeName}");

                    string responseJson = JsonConvert.SerializeObject(response);
                    using var writer = new StreamWriter(serverStream, utf8NoBom, bufferSize: 4096, leaveOpen: true)
                    {
                        AutoFlush = true
                    };
                    await writer.WriteLineAsync(responseJson);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NamedPipe スタンプ要求処理中に一時エラーが発生しました。待ち受けを継続します。");
                await Task.Delay(200, stoppingToken);
            }
        }
    }
}

/// <summary>
/// 【IIS とは完全に分離されたシステム共通の非同期バックグラウンド Worker】
/// 戻り値が不要な非同期ジョブを一手に担います：
/// 1. 図面パイプライン: TIFF ファイルからサムネイル用 PNG 生成 -> 図面 OCR テキスト抽出 -> Elasticsearch 登録
/// 2. メール・通知配送パイプライン: 品目 (ITM)・部品表 (BOM)・図面 (DRW) のワークフロー承認完了メール送信 (モック) 等
/// </summary>
public class SystemBackgroundProcessorWorker : BackgroundService
{
    private readonly ILogger<SystemBackgroundProcessorWorker> _logger;

    public SystemBackgroundProcessorWorker(ILogger<SystemBackgroundProcessorWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string workerId = $"ProcessorWorker.exe (PID={Process.GetCurrentProcess().Id}, WindowsService/SystemBackgroundWorker)";
        _logger.LogInformation("================================================================================");
        _logger.LogInformation("起動完了: {WorkerId}", workerId);
        _logger.LogInformation("監視キューディレクトリ: {QueueDir}", DrawingOutboxQueueStore.QueueDirectory);
        _logger.LogInformation("担当業務1 (非同期・図面): [Step1] TIFF->サムネイルPNG生成 -> [Step2] 図面OCR解析 -> [Step3] Elasticsearch登録");
        _logger.LogInformation("担当業務2 (非同期・共通): 品目(ITM) / 部品表(BOM) / 図面(DRW) 承認完了メール送信・外部連携ジョブ処理");
        _logger.LogInformation("担当業務3 (同期・戻り値有): 図面表示時のオンデマンド承認印＋閲覧日時・ログインユーザ透かし焼き込み (NamedPipe & Internal HTTP)");
        _logger.LogInformation("================================================================================");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. 図面登録ジョブ (TIFF->PNG, OCR, Elasticsearch) の処理
                var processedDrawingJobs = DrawingOutboxQueueStore.ProcessPendingJobs(workerId);
                foreach (var job in processedDrawingJobs)
                {
                    _logger.LogInformation(
                        "[図面ジョブ完了] JobId={JobId} | 図面={DrawingNo} ({Title}) | Thumbnail={Thumb} | ES_DocId={EsDocId}",
                        job.JobId,
                        job.DrawingNo,
                        job.Title,
                        job.GeneratedThumbnailPngPath,
                        job.ElasticsearchDocId);
                }

                // 2. 品目・BOM・図面の完了メール送信キュー確認 (MockEmailNotificationService)
                var recentEmails = MockEmailNotificationService.GetRecentEmails();
                if (processedDrawingJobs.Count > 0 && recentEmails.Count > 0)
                {
                    _logger.LogInformation("[メール送信履歴確認] 直近の完了メール件数: {Count} 通 (ITM/BOM/DRW)", recentEmails.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessorWorker ジョブの実行中にエラーが発生しました。次回ポーリング時に再試行します。");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        _logger.LogInformation("Graceful Shutdown: 処理中のジョブを安全に完了してから {WorkerId} を終了します。", workerId);
    }
}
