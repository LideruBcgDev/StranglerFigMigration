using LegacyMvc.MDL.Commands;
using LegacyMvc.MDL.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.MDL.Data;
using ModernBff.Services;

namespace ModernBff.Controllers;

public class ItemRegistrationRequest
{
    public string ItmCd { get; set; } = string.Empty;
    public string ItmNm { get; set; } = string.Empty;
    public string Category { get; set; } = "一般品目";
    public decimal UnitPrice { get; set; }
    public int StockQty { get; set; }
}

public class DrawingRegistrationRequest
{
    public string DrawingNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ItmCd { get; set; } = "ITM-001";
    public string BomNo { get; set; } = string.Empty;
    public string Revision { get; set; } = "Rev.A";
}

/// <summary>
/// 品目 (ITM_TBL) およびレガシー図面 (DRW_TBL) の検索・登録 API。
/// WinFormsClient の「レガシー品目(ITM_TBL)管理画面」や、Web ブラウザの「レガシー図面(DRW_TBL)管理画面」から呼び出されます。
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
    /// 品目マスタ (ITM_TBL) の検索（モダン側 ModernBff.MDL とレガシー側 LegacyMvc.MDL を同期参照）
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
            string kw = itmCd.Trim().ToLower();
            query = query.Where(i =>
                i.ItmCd.ToLower().Contains(kw) ||
                i.ItmNm.ToLower().Contains(kw) ||
                i.Category.ToLower().Contains(kw));
        }

        var items = await query.OrderBy(i => i.ItmCd).ToListAsync();

        return Ok(new
        {
            Source = "ModernBff + LegacyMvc.MDL (VB.NET) Synchronized ITM_TBL",
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

    /// <summary>
    /// POST /api/bff/items
    /// レガシー側 (LegacyMvc.MDL - VB.NET) の LegacyItemQueryCommand.RegisterOrUpdate と
    /// モダン側 (ModernBff.MDL) の ITM_TBL の双方に品目を登録・更新します。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RegisterItem([FromBody] ItemRegistrationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ItmCd) || string.IsNullOrWhiteSpace(req.ItmNm))
        {
            return BadRequest(new { message = "品目コード (ItmCd) と品目名称 (ItmNm) は必須です。" });
        }

        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        // 1. レガシー側 (LegacyMvc.MDL - VB.NET) の品目登録コマンドを実行
        var legacyCmd = new LegacyItemQueryCommand();
        var legacySaved = legacyCmd.RegisterOrUpdate(
            provider,
            connStr,
            req.ItmCd,
            req.ItmNm,
            req.Category,
            req.UnitPrice,
            req.StockQty);

        // 2. モダン側 (ModernBff.MDL) の EF Core DbContext にも同期反映
        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        string code = req.ItmCd.Trim();
        var modernItem = await db.ItmTbl.FirstOrDefaultAsync(i => i.ItmCd.ToLower() == code.ToLower());
        if (modernItem == null)
        {
            modernItem = new ModernBff.MDL.Data.ItmTblEntity
            {
                ItmCd = code,
                ItmNm = req.ItmNm.Trim(),
                Category = string.IsNullOrWhiteSpace(req.Category) ? "一般品目" : req.Category.Trim(),
                UnitPrice = req.UnitPrice,
                StockQty = req.StockQty,
                ApprovalStatus = "DRAFT (未申請)",
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.ItmTbl.Add(modernItem);
        }
        else
        {
            modernItem.ItmNm = req.ItmNm.Trim();
            modernItem.Category = string.IsNullOrWhiteSpace(req.Category) ? modernItem.Category : req.Category.Trim();
            modernItem.UnitPrice = req.UnitPrice;
            modernItem.StockQty = req.StockQty;
            modernItem.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        return Ok(new
        {
            Message = $"品目 '{modernItem.ItmCd} ({modernItem.ItmNm})' を ITM_TBL に登録・更新しました (LegacyMvc.MDL VB.NET 同期完了)。",
            DatabaseProviderStatus = dbProviderStatus,
            SavedItem = modernItem,
            LegacyMdlResult = legacySaved
        });
    }

    /// <summary>
    /// GET /api/bff/items/drawings?keyword=DRW
    /// レガシー側 (LegacyMvc.MDL - VB.NET) の図面マスタ (DRW_TBL) を検索します。
    /// </summary>
    [HttpGet("drawings")]
    public async Task<IActionResult> GetDrawings([FromQuery] string? keyword = null)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        var drawingCmd = new LegacyDrawingCrudCommand();
        var drawings = drawingCmd.Search(keyword);

        return Ok(new
        {
            Source = "LegacyMvc.MDL (VB.NET LegacyDrawingCrudCommand)",
            Table = "DRW_TBL (レガシー図面マスタ)",
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            DrawingCount = drawings.Count,
            Drawings = drawings,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /api/bff/items/drawings
    /// レガシー側 (LegacyMvc.MDL - VB.NET) の図面マスタ (DRW_TBL) に新規登録・更新します。
    /// </summary>
    [HttpPost("drawings")]
    public IActionResult RegisterDrawing([FromBody] DrawingRegistrationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DrawingNo) || string.IsNullOrWhiteSpace(req.Title))
        {
            return BadRequest(new { message = "図面番号 (DrawingNo) と図面名称 (Title) は必須です。" });
        }

        var drawingCmd = new LegacyDrawingCrudCommand();
        var saved = drawingCmd.RegisterOrUpdate(req.DrawingNo, req.Title, req.ItmCd, req.BomNo, req.Revision);

        return Ok(new
        {
            Message = $"レガシー図面 '{saved.DrawingNo} ({saved.Title})' を DRW_TBL に登録・更新しました (LegacyMvc.MDL VB.NET)。",
            SavedDrawing = saved
        });
    }
}
