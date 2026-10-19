using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Mcp;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers.Mcp;

/// <summary>
/// C# のメソッドに付与することで自動的に MCP ツールとして公開するためのカスタム属性。
/// Semantic Kernel の [KernelFunction] や ModelContextProtocol SDK の [McpServerTool] と同じ宣言的アプローチです。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public McpToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

/// <summary>
/// 【実装方式3: Attribute / Reflection 自動公開・REST ブリッジ方式コントローラー】
/// C# メソッドに `[McpTool]` 属性を付与するだけで、リフレクションによって自動的に
/// MCP ツール一覧スキーマ生成とメソッド呼び出しディスパッチを行う方式です。
/// 既存の ASP.NET Core コントローラー資産を最小限のコード変更で AI / MCP 対応させる場合に適しています。
/// </summary>
[ApiController]
[Route("api/bff/mcp/bridge")]
public class McpAttributeBridgeController : ControllerBase
{
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly StranglerMcpEngine _mcpEngine;

    public McpAttributeBridgeController(SharedLoginUserStore loginUserStore, StranglerMcpEngine mcpEngine)
    {
        _loginUserStore = loginUserStore;
        _mcpEngine = mcpEngine;
    }

    /// <summary>
    /// GET /api/bff/mcp/bridge/compare
    /// 3つの MCP コントローラー実装方式の比較一覧と、リフレクションで自動検出された [McpTool] メソッド一覧を返します。
    /// </summary>
    [HttpGet("compare")]
    public IActionResult CompareImplementations()
    {
        return Ok(new
        {
            ImplementationPattern = "方式3: Attribute / Reflection 自動公開・REST ブリッジ方式 (McpAttributeBridgeController)",
            AutoDiscoveredTools = DiscoverAttributedTools(),
            ComparisonMatrix = _mcpEngine.GetImplementationComparisonMatrix()
        });
    }

    /// <summary>
    /// GET /api/bff/mcp/bridge/invoke/{toolName}?customerId=1
    /// [McpTool] 属性が付与されたメソッドをリフレクションで検索し、共有 loginuser を注入して直接実行します。
    /// </summary>
    [HttpGet("invoke/{toolName}")]
    public async Task<IActionResult> InvokeTool(string toolName, [FromQuery] int customerId = 1)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        var method = typeof(McpAttributeBridgeController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                string.Equals(m.GetCustomAttribute<McpToolAttribute>()?.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (method == null)
        {
            return NotFound(new
            {
                Error = $"[McpTool(\"{toolName}\")] が付与されたメソッドが見つかりません。",
                AvailableTools = DiscoverAttributedTools()
            });
        }

        var attr = method.GetCustomAttribute<McpToolAttribute>()!;
        var result = method.Invoke(this, new object?[] { customerId, loginUser, loginSource });
        if (result is Task<object> taskResult)
        {
            result = await taskResult;
        }

        return Ok(new
        {
            ImplementationPattern = "方式3: Attribute / Reflection 自動公開・REST ブリッジ方式 (McpAttributeBridgeController)",
            InvokedTool = attr.Name,
            ToolDescription = attr.Description,
            ReflectionMethodName = method.Name,
            Result = result
        });
    }

    // =========================================================================
    // 以下、[McpTool] 属性を貼るだけで自動認識・公開される MCP ツール群
    // =========================================================================

    [McpTool("check_permission", "共有 loginuser の組織と権限ロールを検証し、ストラングラー移行先APIへのアクセス権限を判定します。")]
    [NonAction]
    public object CheckUserPermissionTool(
        [Description("対象顧客ID")] int customerId,
        LoginUser? loginUser,
        string loginSource)
    {
        bool isAuthenticated = loginUser != null;
        var roles = loginUser?.Roles ?? new List<string>();

        return new
        {
            CustomerId = customerId,
            IsAuthenticated = isAuthenticated,
            LoginUserLookupSource = loginSource,
            User = loginUser?.UserName ?? "未ログイン (Anonymous)",
            Organization = loginUser != null ? $"[{loginUser.OrganizationCode}] {loginUser.OrganizationName}" : "なし",
            Roles = roles,
            PermissionMatrix = new
            {
                CanReadCustomer = roles.Contains("CustomerRead") || roles.Contains("Admin"),
                CanWriteCustomer = roles.Contains("CustomerWrite") || roles.Contains("Admin"),
                CanCallLegacyMvc = roles.Contains("LegacyAccess") || roles.Contains("Admin")
            }
        };
    }

    [McpTool("get_migration_status", "ストラングラー・フィグによる LegacyMvc から ModernBff への機能別移行ステータスを返します。")]
    [NonAction]
    public object GetMigrationStatusTool(
        [Description("対象顧客ID")] int customerId,
        LoginUser? loginUser,
        string loginSource)
    {
        return new
        {
            RequestedBy = loginUser?.UserName ?? "Anonymous",
            Organization = loginUser?.OrganizationName ?? "None",
            Modules = new object[]
            {
                new { Module = "認証・ログイン (loginuser 発行)", Status = "Migrated to ModernBff (100%)", Backend = "ModernBff + Redis" },
                new { Module = "顧客参照 API", Status = "Canary / Strangler Routing (50%)", Backend = "ModernBff or LegacyMvc" },
                new { Module = "MCP AI エージェント連携", Status = "ModernBff Native (3方式実装済)", Backend = "ModernBff" }
            }
        };
    }

    private static IEnumerable<object> DiscoverAttributedTools()
    {
        return typeof(McpAttributeBridgeController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => new { Method = m, Attr = m.GetCustomAttribute<McpToolAttribute>() })
            .Where(x => x.Attr != null)
            .Select(x => new
            {
                name = x.Attr!.Name,
                description = x.Attr.Description,
                csharpMethod = $"{nameof(McpAttributeBridgeController)}.{x.Method.Name}",
                parameters = x.Method.GetParameters()
                    .Where(p => p.ParameterType != typeof(LoginUser) && p.Name != "loginSource")
                    .Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.Name,
                        description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""
                    })
            });
    }
}
