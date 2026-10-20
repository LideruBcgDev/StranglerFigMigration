using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Mcp;
using ModernBff.Services;

namespace ModernBff.Controllers.Mcp;

/// <summary>
/// 【実装方式1: Streamable HTTP JSON-RPC コントローラー】
/// MCP 2025-03-26 仕様で導入された最新の標準トランスポート方式。
/// 単一のエンドポイント (/api/bff/mcp/streamable) で JSON-RPC 2.0 リクエストを受け付け、
/// 案件統合管理 (PRJ_TBL) およびワークフロー (WF_TBL) の情報を返却します。
/// </summary>
[ApiController]
[Route("api/bff/mcp/streamable")]
public class McpStreamableHttpController : ControllerBase
{
    private readonly StranglerMcpEngine _mcpEngine;
    private readonly SharedLoginUserStore _loginUserStore;

    public McpStreamableHttpController(StranglerMcpEngine mcpEngine, SharedLoginUserStore loginUserStore)
    {
        _mcpEngine = mcpEngine;
        _loginUserStore = loginUserStore;
    }

    /// <summary>
    /// GET /api/bff/mcp/streamable
    /// デフォルトで `tool=get_projects_and_workflows` を実行し、案件統合管理・ワークフロー情報を返却します。
    /// (`tool=get_customer_strangler` 等の指定にも対応)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDemo(
        [FromQuery] string tool = "get_projects_and_workflows",
        [FromQuery] string? keyword = null,
        [FromQuery] string source = "modern",
        [FromQuery] int customerId = 1)
    {
        var sampleArgs = JsonSerializer.SerializeToElement(new { keyword, customerId, source });
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
    /// GET /api/bff/mcp/streamable/projects-workflows?keyword=PRJ
    /// 案件統合管理 (PRJ_TBL) とワークフロー (WF_TBL) の情報を直接返却します（3つのMCPコントローラーで同一データ）。
    /// </summary>
    [HttpGet("projects-workflows")]
    public async Task<IActionResult> GetProjectsAndWorkflows([FromQuery] string? keyword = null)
    {
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserFromContextAsync(HttpContext);
        var sharedData = await _mcpEngine.GetProjectsAndWorkflowsDataAsync(loginUser, loginSource, keyword);

        return Ok(new
        {
            ImplementationPattern = "方式1: Streamable HTTP JSON-RPC (McpStreamableHttpController)",
            Controller = nameof(McpStreamableHttpController),
            SharedMcpData = sharedData
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
