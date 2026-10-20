using LegacyMvc.MDL.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// 【1. モダン化した REST API コントローラー (AppRootUrl: /api/.*)】
/// 元々 LegacyMvc に AppRootUrl = "/api/.*" として存在していた汎用 REST API 群を
/// ModernBff へモダン化・移行したコントローラーです。
///
/// 【認証方式】
/// 事前に `/oauth/token` に API 用の `client_id` と `client_secret` を指定して `access_token` を取得し、
/// リクエストヘッダー `Authorization: Bearer <access_token>` を指定して呼び出す必要があります。
/// （※ ViewController `/View/*` や ClientController `/Client/*` は人間のログインセッションを使うため、この Bearer トークン方式は `/api/.*` 専用です）
/// </summary>
[ApiController]
[Route("api")]
public class RestApiController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly ILogger<RestApiController> _logger;

    public RestApiController(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        SharedLoginUserStore loginUserStore,
        ILogger<RestApiController> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _loginUserStore = loginUserStore;
        _logger = logger;
    }

    /// <summary>
    /// Authorization: Bearer &lt;access_token&gt; ヘッダーを検証します。
    /// 未指定または無効な場合は 401 Unauthorized レスポンスを返します。
    /// </summary>
    private async Task<(OAuthAccessTokenRecord? TokenRecord, string Source, IActionResult? UnauthorizedResult)> VerifyBearerTokenAsync()
    {
        var (tokenRecord, lookupSource) = await _loginUserStore.ValidateBearerAccessTokenAsync(HttpContext);
        if (tokenRecord == null)
        {
            Response.Headers["WWW-Authenticate"] = "Bearer realm=\"ModernBff REST API (/api/.*)\", error=\"invalid_token\"";
            var errResult = Unauthorized(new
            {
                error = "invalid_token",
                error_description = "REST API (/api/.*) の実行には、事前に /oauth/token で client_id と client_secret を指定して取得した access_token を 'Authorization: Bearer <access_token>' ヘッダーに設定する必要があります。",
                lookup_source = lookupSource,
                how_to_obtain_token = new
                {
                    endpoint = "POST /oauth/token (または GET /oauth/token)",
                    default_client_id = "strangler-rest-api-client",
                    default_client_secret = "strangler-rest-api-secret-2026"
                }
            });
            return (null, lookupSource, errResult);
        }

        return (tokenRecord, lookupSource, null);
    }

    /// <summary>
    /// GET /api/status
    /// モダン化された REST API (/api/.*) の稼働状況と Bearer トークン検証結果を返します。
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetApiStatus()
    {
        var (oauthToken, tokenSource, unauthorized) = await VerifyBearerTokenAsync();
        if (unauthorized != null) return unauthorized;

        return Ok(new
        {
            ApiCategory = "Modernized REST API (AppRootUrl: /api/.*)",
            Controller = nameof(RestApiController),
            AuthenticationScheme = "OAuth 2.0 Bearer AccessToken (/oauth/token)",
            VerifiedOAuthClient = oauthToken,
            TokenLookupSource = tokenSource,
            AvailableEndpoints = new[]
            {
                "POST /oauth/token (client_id + client_secret -> access_token 発行)",
                "GET  /api/status (Bearer 必須)",
                "GET  /api/items (Bearer 必須)",
                "GET  /api/drawings (Bearer 必須)",
                "GET  /api/projects (Bearer 必須)"
            },
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET /api/items?itmCd=ITM-001
    /// REST API による品目 (ITM_TBL) リソース取得 (Authorization: Bearer 必須)
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] string? itmCd = null)
    {
        var (oauthToken, tokenSource, unauthorized) = await VerifyBearerTokenAsync();
        if (unauthorized != null) return unauthorized;

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var query = db.ItmTbl.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(itmCd))
        {
            string kw = itmCd.Trim().ToLower();
            query = query.Where(i => i.ItmCd.ToLower().Contains(kw) || i.ItmNm.ToLower().Contains(kw));
        }

        var items = await query.OrderBy(i => i.ItmCd).ToListAsync();

        return Ok(new
        {
            ApiCategory = "Modernized REST API (/api/items)",
            Controller = nameof(RestApiController),
            AuthenticationScheme = "OAuth 2.0 Bearer AccessToken",
            VerifiedOAuthClient = oauthToken,
            TokenLookupSource = tokenSource,
            DatabaseProviderStatus = dbProviderStatus,
            Items = items,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET /api/drawings?keyword=DRW
    /// REST API による図面 (DRW_TBL) リソース取得 (Authorization: Bearer 必須)
    /// </summary>
    [HttpGet("drawings")]
    public async Task<IActionResult> GetDrawings([FromQuery] string? keyword = null)
    {
        var (oauthToken, tokenSource, unauthorized) = await VerifyBearerTokenAsync();
        if (unauthorized != null) return unauthorized;

        var cmd = new LegacyDrawingCrudCommand();
        var drawings = cmd.Search(keyword);

        return Ok(new
        {
            ApiCategory = "Modernized REST API (/api/drawings)",
            Controller = nameof(RestApiController),
            AuthenticationScheme = "OAuth 2.0 Bearer AccessToken",
            VerifiedOAuthClient = oauthToken,
            TokenLookupSource = tokenSource,
            Drawings = drawings,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET /api/projects?keyword=PRJ
    /// REST API による案件統合 (PRJ_TBL) リソース取得 (Authorization: Bearer 必須)
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects([FromQuery] string? keyword = null)
    {
        var (oauthToken, tokenSource, unauthorized) = await VerifyBearerTokenAsync();
        if (unauthorized != null) return unauthorized;

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        var cmd = new ProjectIntegrationCommand();
        var data = await cmd.GetIntegratedProjectsAsync(db, provider, connStr, keyword);

        return Ok(new
        {
            ApiCategory = "Modernized REST API (/api/projects)",
            Controller = nameof(RestApiController),
            AuthenticationScheme = "OAuth 2.0 Bearer AccessToken",
            VerifiedOAuthClient = oauthToken,
            TokenLookupSource = tokenSource,
            DatabaseProviderStatus = dbProviderStatus,
            Data = data,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
