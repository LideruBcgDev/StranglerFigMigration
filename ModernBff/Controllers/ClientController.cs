using LegacyMvc.MDL.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.MDL.Data;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// 【Windows Forms クライアント専用 REST API コントローラー (ModernBff へ移行済)】
/// 元々 LegacyMvc に AppRootUrl = "/api/.*", RoutePrefix = "Client" として存在していた薄い REST API を
/// ModernBff 側へ移行したコントローラーです。
/// URL パス (`/api/Client/...` および `/Client/...`) を完全維持しているため、Windows Forms 側の呼び出し規約をそのまま継承できます。
/// </summary>
[ApiController]
[Route("api/Client")]
[Route("Client")]
public class ClientController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly ILogger<ClientController> _logger;

    public ClientController(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        SharedLoginUserStore loginUserStore,
        ILogger<ClientController> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _loginUserStore = loginUserStore;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/Client/items?itmCd=ITM-001
    /// Windows Forms クライアントからの品目 (ITM_TBL) 検索
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
            query = query.Where(i =>
                i.ItmCd.ToLower().Contains(kw) ||
                i.ItmNm.ToLower().Contains(kw) ||
                i.Category.ToLower().Contains(kw));
        }

        var items = await query.OrderBy(i => i.ItmCd).ToListAsync();

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: api/Client)",
            AccessedFrom = "Windows Forms Client",
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            ItemCount = items.Count,
            Items = items,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /api/Client/items
    /// Windows Forms クライアントからの品目 (ITM_TBL) 登録・更新
    /// </summary>
    [HttpPost("items")]
    public async Task<IActionResult> RegisterItem([FromBody] ItemRegistrationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ItmCd) || string.IsNullOrWhiteSpace(req.ItmNm))
        {
            return BadRequest(new { message = "品目コード (ItmCd) と品目名称 (ItmNm) は必須です。" });
        }

        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        // 1. LegacyMvc.MDL (VB.NET) 側の品目マスタ更新コマンドを実行
        var legacyCmd = new LegacyItemQueryCommand();
        var legacySaved = legacyCmd.RegisterOrUpdate(
            provider,
            connStr,
            req.ItmCd,
            req.ItmNm,
            req.Category,
            req.UnitPrice,
            req.StockQty);

        // 2. ModernBff.MDL 側の EF Core DbContext にも同期反映
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
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: api/Client)",
            Message = $"[ClientController] 品目 '{modernItem.ItmCd}' を ITM_TBL に登録・更新しました。",
            DatabaseProviderStatus = dbProviderStatus,
            SavedItem = modernItem,
            LegacyMdlResult = legacySaved
        });
    }

    /// <summary>
    /// POST /api/Client/workflow/apply
    /// Windows Forms クライアント（品目管理画面など）からのワークフロー申請
    /// </summary>
    [HttpPost("workflow/apply")]
    public async Task<IActionResult> ApplyWorkflowFromClient([FromBody] WorkflowApplyDto dto)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, _) = await _loginUserStore.GetLoginUserAsync(token);

        string userId = loginUser?.UserId ?? "yamada";
        string userName = loginUser?.UserName ?? "山田 太郎";

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out _);

        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        var cmd = new WorkflowCompleteCommand();
        var wf = await cmd.ApplyAsync(db, provider, connStr, dto, userId, userName);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: api/Client)",
            Message = $"[ClientController] ワークフロー '{wf.WfId}' を申請しました（対象: {wf.TargetType} / {wf.TargetId}）。",
            Workflow = wf
        });
    }

    /// <summary>
    /// GET /api/Client/customer/{id}
    /// Windows Forms クライアントからの顧客情報参照
    /// </summary>
    [HttpGet("customer/{id:int}")]
    public async Task<IActionResult> GetCustomer(int id = 1)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: api/Client)",
            AccessedFrom = "Windows Forms Client",
            CustomerId = id,
            CustomerName = $"株式会社モダン・フィグ・テクノロジーズ (ID: {id})",
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
