using LegacyMvc.MDL.Commands;
using LegacyMvc.MDL.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// 【1. モダン化した REST API コントローラー (AppRootUrl: /api/.*)】
/// 元々 LegacyMvc に AppRootUrl = "/api/.*" として存在していた汎用 REST API 群を
/// ModernBff (.NET 10 / .NET 9 Web API) へモダン化・移行したコントローラーです。
/// ClientController (/Client/*) や ViewController (/View/*) とは独立した純粋な RESTful API として機能します。
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
    /// GET /api/status
    /// モダン化された REST API (/api/.*) の稼働状況と認証セッション情報を返します。
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetApiStatus()
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        return Ok(new
        {
            ApiCategory = "Modernized REST API (AppRootUrl: /api/.*)",
            Controller = nameof(RestApiController),
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            AvailableEndpoints = new[]
            {
                "GET  /api/status",
                "GET  /api/items",
                "POST /api/items",
                "GET  /api/drawings",
                "POST /api/drawings",
                "GET  /api/projects",
                "POST /api/projects"
            },
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET /api/items?itmCd=ITM-001
    /// REST API による品目 (ITM_TBL) リソース取得
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] string? itmCd = null)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

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
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Items = items,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET /api/drawings?keyword=DRW
    /// REST API による図面 (DRW_TBL) リソース取得
    /// </summary>
    [HttpGet("drawings")]
    public async Task<IActionResult> GetDrawings([FromQuery] string? keyword = null)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        var cmd = new LegacyDrawingCrudCommand();
        var drawings = cmd.Search(keyword);

        return Ok(new
        {
            ApiCategory = "Modernized REST API (/api/drawings)",
            Controller = nameof(RestApiController),
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Drawings = drawings,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET /api/projects?keyword=PRJ
    /// REST API による案件統合 (PRJ_TBL) リソース取得
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects([FromQuery] string? keyword = null)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

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
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Data = data,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
