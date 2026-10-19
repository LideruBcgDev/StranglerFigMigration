using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// モダン側 (ModernBff) で EF Core を用いて品目テーブル (ITM_TBL) を参照するコントローラー。
/// appsettings.json の Database:Provider ("Oracle" | "Postgres" | "SqlServer" | "InMemory") に応じて接続先 DB を切り替えます。
/// </summary>
[ApiController]
[Route("api/bff/items")]
public class BffItemController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly ILogger<BffItemController> _logger;

    public BffItemController(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        SharedLoginUserStore loginUserStore,
        ILogger<BffItemController> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _loginUserStore = loginUserStore;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/bff/items?itmCd=ITM-001
    /// モダン側 (ModernBff) から EF Core 経由で ITM_TBL (品目マスタ) を参照します。
    /// </summary>
    [HttpGet]
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
            string code = itmCd.Trim();
            query = query.Where(i => i.ItmCd.ToLower() == code.ToLower());
        }

        var items = await query.OrderBy(i => i.ItmCd).ToListAsync();

        return Ok(new
        {
            Source = "ModernBff (.NET 10 Web API + EF Core)",
            Table = "ITM_TBL (品目マスタ)",
            ConfiguredProvider = _configuration["Database:Provider"] ?? "SqlServer",
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            ItemCount = items.Count,
            Items = items,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
