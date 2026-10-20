using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Mcp;
using ModernBff.Services;

namespace ModernBff.Controllers.Mcp;

/// <summary>
/// 【実装方式2: HTTP + SSE (Server-Sent Events) ストリーム方式コントローラー】
/// MCP 2024-11-05 仕様のトランスポート方式。
/// GET /api/bff/mcp/sse で `text/event-stream` を開き、案件統合管理 (PRJ_TBL) とワークフロー (WF_TBL) の情報をプッシュ配信します。
/// </summary>
[ApiController]
[Route("api/bff/mcp/sse")]
public class McpSseController : ControllerBase
{
    private readonly StranglerMcpEngine _mcpEngine;
    private readonly SharedLoginUserStore _loginUserStore;
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> ActiveSseSessions = new();

    public McpSseController(StranglerMcpEngine mcpEngine, SharedLoginUserStore loginUserStore)
    {
        _mcpEngine = mcpEngine;
        _loginUserStore = loginUserStore;
    }

    /// <summary>
    /// GET /api/bff/mcp/sse
    /// SSE (`text/event-stream`) でイベントをストリーム返却します。
    /// デフォルト (`tool=get_projects_and_workflows`) では、案件統合管理・ワークフロー管理の統合情報をストリーム出力します。
    /// </summary>
    [HttpGet]
    public async Task ConnectSse(
        [FromQuery] bool oneShot = true,
        [FromQuery] string tool = "get_projects_and_workflows",
        [FromQuery] string? keyword = null,
        [FromQuery] int customerId = 1,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        string mcpSessionId = Guid.NewGuid().ToString("N");

        // 1. MCP SSE 仕様: 最初にメッセージ送信先 POST エンドポイントを通知する `event: endpoint`
        await WriteSseEventAsync("endpoint", $"/api/bff/mcp/sse/message?sessionId={mcpSessionId}");

        // 2. サーバー情報と現在の実装方式の説明イベント
        var initRpc = await _mcpEngine.HandleJsonRpcAsync(
            new JsonRpcRequest { Id = 1, Method = "initialize" },
            HttpContext,
            "方式2: HTTP + SSE ストリーム方式 (McpSseController)");
        await WriteSseEventAsync("message", JsonSerializer.Serialize(initRpc));

        // 3. 案件統合管理・ワークフロー情報 (または指定ツール) の実行結果をストリームプッシュ
        var callRpc = await _mcpEngine.HandleJsonRpcAsync(
            new JsonRpcRequest
            {
                Id = 2,
                Method = "tools/call",
                Params = JsonSerializer.SerializeToElement(new
                {
                    name = tool,
                    arguments = new { keyword, customerId, source = "modern" }
                })
            },
            HttpContext,
            "方式2: HTTP + SSE ストリーム方式 (McpSseController)");
        await WriteSseEventAsync("message", JsonSerializer.Serialize(callRpc));

        // 長寿命ストリームモード (`oneShot=false`) の場合はクライアント切断まで待機
        if (!oneShot)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ActiveSseSessions[mcpSessionId] = tcs;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
                    if (completed == tcs.Task)
                    {
                        string msg = await tcs.Task;
                        tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                        ActiveSseSessions[mcpSessionId] = tcs;
                        await WriteSseEventAsync("message", msg);
                    }
                    else
                    {
                        await WriteSseEventAsync("ping", $"{{\"timestamp\":\"{DateTime.UtcNow:O}\"}}");
                    }
                }
            }
            finally
            {
                ActiveSseSessions.TryRemove(mcpSessionId, out _);
            }
        }
    }

    /// <summary>
    /// GET /api/bff/mcp/sse/projects-workflows?keyword=PRJ
    /// 案件統合管理 (PRJ_TBL) とワークフロー (WF_TBL) の情報を返却します（3つのMCPコントローラーで同一データ）。
    /// </summary>
    [HttpGet("projects-workflows")]
    public async Task<IActionResult> GetProjectsAndWorkflows([FromQuery] string? keyword = null)
    {
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserFromContextAsync(HttpContext);
        var sharedData = await _mcpEngine.GetProjectsAndWorkflowsDataAsync(loginUser, loginSource, keyword);

        return Ok(new
        {
            ImplementationPattern = "方式2: HTTP + SSE ストリーム方式 (McpSseController)",
            Controller = nameof(McpSseController),
            SharedMcpData = sharedData
        });
    }

    /// <summary>
    /// POST /api/bff/mcp/sse/message?sessionId=xxx
    /// クライアントから SSE セッション宛てに JSON-RPC メッセージを送信します。
    /// </summary>
    [HttpPost("message")]
    public async Task<IActionResult> PostMessage([FromQuery] string? sessionId, [FromBody] JsonRpcRequest request)
    {
        var response = await _mcpEngine.HandleJsonRpcAsync(
            request,
            HttpContext,
            "方式2: HTTP + SSE ストリーム方式 (McpSseController)");

        string json = JsonSerializer.Serialize(response);

        if (!string.IsNullOrWhiteSpace(sessionId) && ActiveSseSessions.TryGetValue(sessionId, out var tcs))
        {
            tcs.TrySetResult(json);
        }

        return Accepted(response);
    }

    private async Task WriteSseEventAsync(string eventType, string data)
    {
        var builder = new StringBuilder();
        builder.Append("event: ").Append(eventType).Append('\n');
        foreach (var line in data.Split('\n'))
        {
            builder.Append("data: ").Append(line.TrimEnd('\r')).Append('\n');
        }
        builder.Append('\n');

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        await Response.Body.WriteAsync(bytes);
        await Response.Body.FlushAsync();
    }
}
