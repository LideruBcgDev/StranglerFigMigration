using System.Diagnostics;
using LegacyMvc.MDL.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModernBff.MDL.Commands;

namespace ProcessorWorker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Windows サービス (sc.exe) としてもスタンドアロンプロセスとしても起動可能 (IIS リサイクルの影響を受けない)
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "StranglerProcessorWorker";
        });

        builder.Services.AddHostedService<SystemBackgroundProcessorWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}

/// <summary>
/// 【IIS とは完全に分離されたシステム共通の別プロセス Worker (ProcessorWorker.exe / Windows Service)】
/// 図面・品目・BOM・ワークフローなどシステム全体の非同期バックグラウンド処理を一手に担います：
/// 1. 図面パイプライン: TIFF ファイルからサムネイル用 PNG 生成 -> 図面 OCR テキスト抽出 -> Elasticsearch 登録
/// 2. メール・通知配送パイプライン: 品目 (ITM)・部品表 (BOM)・図面 (DRW) のワークフロー承認完了メール送信 (モック) 等
/// IIS のアプリケーションプールリサイクルやリクエストタイムアウトの影響を受けずに安全に非同期実行します。
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
        _logger.LogInformation("担当業務1 (図面): [Step1] TIFF->サムネイルPNG生成 -> [Step2] 図面OCR解析 -> [Step3] Elasticsearch登録");
        _logger.LogInformation("担当業務2 (共通): 品目(ITM) / 部品表(BOM) / 図面(DRW) 承認完了メール送信・外部連携ジョブ処理");
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
