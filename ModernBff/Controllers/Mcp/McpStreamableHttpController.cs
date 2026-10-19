using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Mcp;

namespace ModernBff.Controllers.Mcp;

/// <summary>
/// 【実装方式1: Streamable HTTP JSON-RPC コントローラー】
/// MCP 2025-03-26 仕様で導入された最新の標準トランスポート方式。
/// 単一のエンドポイント (/api/bff/mcp/streamable) で JSON-RPC 2.0 リクエストを受け付けます。
/// YARP ゲートウェイ経由でのルーティングやステートレスな水平スケールに最も適しています。
/// </summary>
[ApiController]
[Route("api/bff/mcp/streamable")]
public class McpStreamableHttpController : ControllerBase
{
    private readonly StranglerMcpEngine _mcpEngine;

    public McpStreamableHttpController(StranglerMcpEngine mcpEngine)
    {
        _mcpEngine = mcpEngine;
    }

    /// <summary>
    /// GET /api/bff/mcp/streamable
    /// ブラウザや WinFormsClient の GET リクエストからも簡単に動作確認できるよう、
    /// 方式1の概要と「tools/call (get_customer_strangler)」のサンプル実行結果を返します。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDemo([FromQuery] string tool = "get_customer_strangler", [FromQuery] string source = "modern", [FromQuery] int customerId = 1)
    {
        var sampleArgs = JsonSerializer.SerializeToElement(new { customerId, source });
        var rpcRequest = new JsonRpcRequest
        {
            Id = "demo-streamable-1",
            Method = "tools/call",
            Params = JsonSerializer.SerializeToElement(new
            {
                name = tool,
                arguments = sampleArgs
            })
        };

        var rpcResponse = await _mcpEngine.HandleJsonRpcAsync(
            rpcRequest,
            HttpContext,
            "方式1: Streamable HTTP JSON-RPC (McpStreamableHttpController)");

        return Ok(new
        {
            ImplementationPattern = "方式1: Streamable HTTP JSON-RPC (MCP 2025-03-26 標準仕様)",
            Controller = nameof(McpStreamableHttpController),
            Characteristics = new
            {
                Transport = "Single HTTP Endpoint (POST JSON-RPC 2.0 / GET Quick Demo)",
                YarpCompatibility = "◎ (通常のHTTPリクエストとしてステートレスに中継可能)",
                SessionSharing = "Cookie (StranglerAuthToken) または X-Strangler-AuthToken ヘッダーで Redis の loginuser を自動参照"
            },
            SimulatedJsonRpcRequest = rpcRequest,
            JsonRpcResponse = rpcResponse
        });
    }

    /// <summary>
    /// POST /api/bff/mcp/streamable
    /// 標準 MCP クライアントからの JSON-RPC 2.0 (initialize, tools/list, tools/call) を処理します。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PostJsonRpc([FromBody] JsonRpcRequest request)
    {
        var response = await _mcpEngine.HandleJsonRpcAsync(
            request,
            HttpContext,
            "方式1: Streamable HTTP JSON-RPC (McpStreamableHttpController)");

        return Ok(response);
    }
}
