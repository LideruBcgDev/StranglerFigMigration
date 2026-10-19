using System.Text.Json;
using System.Text.Json.Serialization;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Mcp;

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Error { get; set; }
}

/// <summary>
/// 各実装方式の MCP コントローラーから共通で利用される MCP ツール・リソース実行エンジン。
/// Redis 上の loginuser（ユーザ・組織・権限）の認識や、ModernBff / LegacyMvc のデータ取得を提供します。
/// </summary>
public class StranglerMcpEngine
{
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly IHttpClientFactory _httpClientFactory;

    public StranglerMcpEngine(SharedLoginUserStore loginUserStore, IHttpClientFactory httpClientFactory)
    {
        _loginUserStore = loginUserStore;
        _httpClientFactory = httpClientFactory;
    }

    public object GetCapabilities(string transportMode) => new
    {
        protocolVersion = "2025-03-26",
        transportMode,
        capabilities = new
        {
            tools = new { listChanged = false },
            resources = new { subscribe = false, listChanged = false }
        },
        serverInfo = new
        {
            name = $"StranglerFig-ModernBff-MCP ({transportMode})",
            version = "1.0.0"
        }
    };

    public object GetToolsList() => new
    {
        tools = new object[]
        {
            new
            {
                name = "get_shared_loginuser",
                description = "Redis 共有セッションから現在の loginuser（ユーザID・氏名・組織・権限ロール）を取得します。",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "get_customer_strangler",
                description = "ストラングラー・フィグ経由で顧客情報を取得します。source に 'modern' または 'legacy' を指定して比較できます。",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        customerId = new { type = "integer", description = "顧客ID (例: 1)" },
                        source = new { type = "string", description = "'modern' (ModernBff新ロジック) または 'legacy' (LegacyMvc内部呼出)" }
                    },
                    required = new[] { "customerId" }
                }
            },
            new
            {
                name = "compare_mcp_implementations",
                description = "ModernBff に実装されている3つの MCP コントローラー実装方式の特徴・メリデメ比較表を返します。",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            }
        }
    };

    public async Task<JsonRpcResponse> HandleJsonRpcAsync(JsonRpcRequest request, HttpContext httpContext, string implementationStyle)
    {
        string? token = _loginUserStore.ExtractToken(httpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        switch (request.Method)
        {
            case "initialize":
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = GetCapabilities(implementationStyle)
                };

            case "tools/list":
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = GetToolsList()
                };

            case "tools/call":
                string toolName = "get_customer_strangler";
                JsonElement args = default;

                if (request.Params is { ValueKind: JsonValueKind.Object } p)
                {
                    if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    {
                        toolName = n.GetString() ?? toolName;
                    }
                    p.TryGetProperty("arguments", out args);
                }

                var toolResult = await ExecuteToolAsync(toolName, args, loginUser, loginSource, token, implementationStyle);
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        implementationStyle,
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = JsonSerializer.Serialize(toolResult, new JsonSerializerOptions { WriteIndented = true })
                            }
                        },
                        structuredData = toolResult
                    }
                };

            default:
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new
                    {
                        code = -32601,
                        message = $"Method '{request.Method}' not found. Supported: initialize, tools/list, tools/call"
                    }
                };
        }
    }

    public async Task<object> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        LoginUser? loginUser,
        string loginSource,
        string? token,
        string implementationStyle)
    {
        switch (toolName)
        {
            case "get_shared_loginuser":
                return new
                {
                    McpImplementation = implementationStyle,
                    Tool = toolName,
                    Authenticated = loginUser != null,
                    LookupSource = loginSource,
                    LoginUser = loginUser ?? new LoginUser
                    {
                        UserId = "anonymous",
                        UserName = "未ログイン (Webログイン画面からログインすると組織・権限が共有されます)",
                        OrganizationCode = "NONE",
                        OrganizationName = "未所属",
                        Roles = new List<string> { "Anonymous" }
                    }
                };

            case "compare_mcp_implementations":
                return GetImplementationComparisonMatrix();

            case "get_customer_strangler":
            default:
                int customerId = 1;
                string source = "modern";

                if (arguments.ValueKind == JsonValueKind.Object)
                {
                    if (arguments.TryGetProperty("customerId", out var cid) && cid.TryGetInt32(out int parsedId))
                    {
                        customerId = parsedId;
                    }
                    if (arguments.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String)
                    {
                        source = src.GetString() ?? "modern";
                    }
                }

                // 権限チェックデモ: legacy ソース呼び出し時に LegacyAccess ロール有無を判定
                bool hasLegacyRole = loginUser?.Roles.Contains("LegacyAccess") == true || loginUser?.Roles.Contains("Admin") == true;

                if (string.Equals(source, "legacy", StringComparison.OrdinalIgnoreCase))
                {
                    object legacyResponse;
                    try
                    {
                        var client = _httpClientFactory.CreateClient("LegacyMvcClient");
                        using var req = new HttpRequestMessage(HttpMethod.Get, $"LegacyData/GetCustomer?id={customerId}");
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            req.Headers.TryAddWithoutValidation(SharedLoginUserStore.HeaderName, token);
                        }
                        var resp = await client.SendAsync(req);
                        var body = await resp.Content.ReadAsStringAsync();
                        legacyResponse = JsonDocument.Parse(body).RootElement.Clone();
                    }
                    catch (Exception ex)
                    {
                        legacyResponse = new
                        {
                            Fallback = true,
                            CustomerId = customerId,
                            CustomerName = $"株式会社レガシー商事 (ID: {customerId}) [MCP Legacy Fallback]",
                            Note = $"LegacyMvc 未起動のためフォールバック応答 ({ex.Message})"
                        };
                    }

                    return new
                    {
                        McpImplementation = implementationStyle,
                        Tool = "get_customer_strangler (source=legacy)",
                        CurrentLoginUser = loginUser,
                        LegacyRoleAuthorized = hasLegacyRole,
                        LegacyData = legacyResponse,
                        TimestampUtc = DateTime.UtcNow.ToString("O")
                    };
                }

                return new
                {
                    McpImplementation = implementationStyle,
                    Tool = "get_customer_strangler (source=modern)",
                    CurrentLoginUser = loginUser,
                    LoginUserLookupSource = loginSource,
                    Customer = new
                    {
                        CustomerId = customerId,
                        CustomerName = $"株式会社モダン・フィグ・テクノロジーズ (ID: {customerId})",
                        AccessedByOrganization = loginUser?.OrganizationName ?? "未ログイン",
                        GrantedRoles = loginUser?.Roles ?? new List<string>()
                    },
                    TimestampUtc = DateTime.UtcNow.ToString("O")
                };
        }
    }

    public object GetImplementationComparisonMatrix() => new
    {
        Title = "MCP (Model Context Protocol) コントローラー実装方式 3パターンの比較",
        Implementations = new object[]
        {
            new
            {
                Pattern = "方式1: Streamable HTTP JSON-RPC 方式 (McpStreamableHttpController)",
                Endpoint = "/api/bff/mcp/streamable",
                SpecVersion = "MCP 2025-03-26 Streamable HTTP 仕様準拠",
                Transport = "単一エンドポイント HTTP POST (JSON-RPC 2.0: initialize / tools/list / tools/call)",
                Pros = new[] { "最新のMCP標準仕様でありステートレス・スケールアウトが容易", "YARP などのリバースプロキシやロードバランサとの相性が最も良い", "WinForms の HttpClient からもシンプルに POST 呼び出し可能" },
                Cons = new[] { "サーバープッシュ通知が不要な場合は JSON-RPC エンベロープの記述がやや冗長" }
            },
            new
            {
                Pattern = "方式2: HTTP + SSE (Server-Sent Events) 双方向ストリーム方式 (McpSseController)",
                Endpoint = "GET /api/bff/mcp/sse (ストリーム接続) / POST /api/bff/mcp/sse/message",
                SpecVersion = "MCP 2024-11-05 HTTP+SSE 仕様準拠",
                Transport = "text/event-stream による長寿命ストリーム + メッセージ POST",
                Pros = new[] { "サーバー側からクライアントへ非同期進捗やツール更新通知をリアルタイムにプッシュ可能", "既存の SSE 対応 MCP クライアント (Claude Desktop / Inspector 等) と直接接続しやすい" },
                Cons = new[] { "YARP やプロキシでレスポンスバッファリング無効化やタイムアウト配慮が必要", "Redis Backplane 等がないと複数台構成でセッション維持が難しい" }
            },
            new
            {
                Pattern = "方式3: Attribute / Reflection 自動公開・REST ブリッジ方式 (McpAttributeBridgeController)",
                Endpoint = "GET /api/bff/mcp/bridge/tools / GET /api/bff/mcp/bridge/call/{toolName}",
                SpecVersion = "C# カスタム属性 ([McpTool]) による宣言的公開 + REST/JSON-RPC ハイブリッド",
                Transport = "GET/POST REST エンドポイント & リフレクションによるスキーマ自動生成",
                Pros = new[] { "既存の ASP.NET Core Controller や Service メソッドに [McpTool] 属性を貼るだけで MCP ツール化できる", "ブラウザや WinForms の GET リクエストだけでも即座に動作確認・デバッグが可能" },
                Cons = new[] { "純粋な MCP クライアントから呼ぶ場合は JSON-RPC ラッパーまたはブリッジを介す必要がある" }
            }
        }
    };
}
