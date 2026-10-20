using System.Diagnostics;
using LegacyMvc.MDL.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DrawingProcessorWorker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Windows サービス (sc.exe) としてもスタンドアロンプロセスとしても起動可能 (IIS リサイクルの影響を受けない)
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "StranglerDrawingProcessorWorker";
        });

        builder.Services.AddHostedService<DrawingOcrAndThumbnailWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}

/// <summary>
/// 【IIS とは完全に分離された別プロセス Worker (BackgroundService / Windows Service)】
/// 図面登録時にキュー (%TEMP%\StranglerDrawingJobQueue または Redis / DB Outbox) に積まれたジョブを監視し、
/// 1. TIFF ファイルからサムネイル用 PNG ファイルの生成
/// 2. 図面ファイルの OCR テキスト抽出
/// 3. Elasticsearch への全文検索インデックス登録
/// を IIS のアプリケーションプールリサイクルやリクエストタイムアウトの影響を受けずに非同期実行します。
/// </summary>
public class DrawingOcrAndThumbnailWorker : BackgroundService
{
    private readonly ILogger<DrawingOcrAndThumbnailWorker> _logger;

    public DrawingOcrAndThumbnailWorker(ILogger<DrawingOcrAndThumbnailWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string workerId = $"DrawingProcessorWorker.exe (PID={Process.GetCurrentProcess().Id}, WindowsService/BackgroundWorker)";
        _logger.LogInformation("================================================================================");
        _logger.LogInformation("起動完了: {WorkerId}", workerId);
        _logger.LogInformation("監視キューディレクトリ: {QueueDir}", DrawingOutboxQueueStore.QueueDirectory);
        _logger.LogInformation("処理内容: [Step1] TIFF->サムネイルPNG生成 -> [Step2] 図面OCR解析 -> [Step3] Elasticsearch登録");
        _logger.LogInformation("================================================================================");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedJobs = DrawingOutboxQueueStore.ProcessPendingJobs(workerId);
                foreach (var job in processedJobs)
                {
                    _logger.LogInformation(
                        "[完了] JobId={JobId} | 図面={DrawingNo} ({Title}) | Thumbnail={Thumb} | ES_DocId={EsDocId}",
                        job.JobId,
                        job.DrawingNo,
                        job.Title,
                        job.GeneratedThumbnailPngPath,
                        job.ElasticsearchDocId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "図面非同期処理ジョブの実行中にエラーが発生しました。次回ポーリング時に再試行します。");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        _logger.LogInformation("Graceful Shutdown: 処理中のジョブを安全に完了してから {WorkerId} を終了します。", workerId);
    }
}
