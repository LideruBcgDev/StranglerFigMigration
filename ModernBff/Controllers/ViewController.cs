using LegacyMvc.MDL.Commands;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// 【Web ブラウザ専用 REST API コントローラー (ModernBff へ移行済)】
/// 元々 LegacyMvc に AppRootUrl = "/api/.*", RoutePrefix = "View" として存在していたブラウザ向け REST API を
/// ModernBff 側へ移行したコントローラーです。
/// URL パス (`/api/View/...` および `/View/...`) で図面管理・案件統合管理・ワークフロー管理を提供します。
/// </summary>
[ApiController]
[Route("api/View")]
[Route("View")]
public class ViewController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly ILogger<ViewController> _logger;

    public ViewController(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        SharedLoginUserStore loginUserStore,
        ILogger<ViewController> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _loginUserStore = loginUserStore;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/View/drawings?keyword=DRW
    /// Web ブラウザからの図面 (DRW_TBL) 検索 (実処理は LegacyMvc.MDL VB.NET)
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
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            AccessedFrom = "Web Browser",
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            DrawingCount = drawings.Count,
            Drawings = drawings,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /api/View/drawings
    /// Web ブラウザからの図面 (DRW_TBL) 登録・更新
    /// </summary>
    [HttpPost("drawings")]
    public IActionResult RegisterDrawing([FromBody] DrawingRegistrationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DrawingNo) || string.IsNullOrWhiteSpace(req.Title))
        {
            return BadRequest(new { message = "図面番号 (DrawingNo) と図面名称 (Title) は必須です。" });
        }

        var cmd = new LegacyDrawingCrudCommand();
        var saved = cmd.RegisterOrUpdate(req.DrawingNo, req.Title, req.ItmCd, req.BomNo, req.Revision);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            Message = $"[ViewController] 図面 '{saved.DrawingNo} ({saved.Title})' を DRW_TBL に登録・更新しました。",
            SavedDrawing = saved
        });
    }

    /// <summary>
    /// GET /api/View/projects?keyword=PRJ
    /// Web ブラウザからの案件統合管理 (PRJ_TBL: 品目・図面関連付け) 検索
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
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            AccessedFrom = "Web Browser",
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Data = data,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /api/View/projects
    /// Web ブラウザからの案件統合管理 (PRJ_TBL) 登録・品目／図面関連付け保存
    /// </summary>
    [HttpPost("projects")]
    public async Task<IActionResult> RegisterProject([FromBody] ProjectRegistrationDto dto)
    {
        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var cmd = new ProjectIntegrationCommand();
        var saved = await cmd.RegisterOrUpdateProjectAsync(db, dto);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            Message = $"[ViewController] 案件 '{saved.PrjId} ({saved.PrjNm})' を登録・関連付け保存しました。",
            DatabaseProviderStatus = dbProviderStatus,
            SavedProject = saved
        });
    }

    /// <summary>
    /// POST /api/View/workflow/apply
    /// Web ブラウザからのワークフロー申請 (Step 1: 申請 - 案件/品目/図面)
    /// </summary>
    [HttpPost("workflow/apply")]
    public async Task<IActionResult> ApplyWorkflow([FromBody] WorkflowApplyDto dto)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, _) = await _loginUserStore.GetLoginUserAsync(token);

        string userId = loginUser?.UserId ?? "suzuki";
        string userName = loginUser?.UserName ?? "鈴木 花子";

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
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            Message = $"[ViewController] ワークフロー '{wf.WfId}' を申請しました（対象: {wf.TargetType} / {wf.TargetId}）。",
            Workflow = wf
        });
    }

    /// <summary>
    /// POST /api/View/workflow/verify?wfId=WF-9002
    /// Web ブラウザからのワークフロー照査 (Step 2: 照査)
    /// </summary>
    [HttpPost("workflow/verify")]
    [HttpGet("workflow/verify")]
    public async Task<IActionResult> VerifyWorkflow([FromQuery] string wfId)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, _) = await _loginUserStore.GetLoginUserAsync(token);

        string userId = loginUser?.UserId ?? "sato";
        string userName = loginUser?.UserName ?? "佐藤 健一";

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out _);

        var cmd = new WorkflowCompleteCommand();
        var wf = await cmd.VerifyAsync(db, wfId, userId, userName);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            Message = $"[ViewController] ワークフロー '{wf.WfId}' の照査が完了しました。",
            Workflow = wf
        });
    }

    /// <summary>
    /// POST /api/View/workflow/approve?wfId=WF-9001&simulateDrawingError=false
    /// Web ブラウザからのワークフロー最終承認 (Step 3: 承認 - 案件承認時は関連品目・図面も一括承認)
    /// </summary>
    [HttpPost("workflow/approve")]
    [HttpGet("workflow/approve")]
    public async Task<IActionResult> ApproveWorkflow([FromQuery] string wfId = "WF-9001", [FromQuery] bool simulateDrawingError = false)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        string userId = loginUser?.UserId ?? "yamada";
        string userName = loginUser?.UserName ?? "山田 太郎";
        string orgName = loginUser?.OrganizationName ?? "第一営業本部";

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        var cmd = new WorkflowCompleteCommand();
        var result = await cmd.ExecuteAsync(db, wfId, userId, userName, orgName, simulateDrawingError, provider, connStr);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: api/View)",
            DatabaseProviderStatus = dbProviderStatus,
            ExecutedByLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Result = result,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
