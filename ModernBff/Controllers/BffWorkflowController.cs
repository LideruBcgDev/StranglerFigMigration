using Microsoft.AspNetCore.Mvc;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// モダン側新規機能：
/// 1. 案件統合管理 (PRJ_TBL) の検索・登録（品目 ITM_TBL や 図面 DRW_TBL の関連付け対応）
/// 2. ワークフロー管理 (WF_TBL) の「申請 (Apply) -> 照査 (Verify) -> 承認 (Approve)」
///    ・案件 (PROJECT)、品目 (ITEM)、図面 (DRAWING) のいずれからも申請可能
///    ・案件を承認した場合は、関連づいている品目と図面（およびBOM）も同一トランザクションで一括承認されます。
/// </summary>
[ApiController]
[Route("api/bff/workflow")]
public class BffWorkflowController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly ILogger<BffWorkflowController> _logger;

    public BffWorkflowController(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        SharedLoginUserStore loginUserStore,
        ILogger<BffWorkflowController> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _loginUserStore = loginUserStore;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/bff/workflow/projects?keyword=PRJ
    /// 案件統合管理の検索（関連品目・関連図面・ワークフロー一覧も統合して返却）
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetIntegratedProjects([FromQuery] string? prjId = null, [FromQuery] string? keyword = null)
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
        var result = await cmd.GetIntegratedProjectsAsync(db, provider, connStr, keyword ?? prjId);

        return Ok(new
        {
            Source = "ModernBff -> ModernBff.MDL (ProjectIntegrationCommand) + LegacyMvc.MDL (VB.NET)",
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Data = result,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /api/bff/workflow/projects
    /// 案件統合管理の新規登録・更新（品目 ITM_TBL や 図面 DRW_TBL の関連付けを保存）
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
            Message = $"案件 '{saved.PrjId} ({saved.PrjNm})' を登録・更新しました（関連品目: {saved.TargetItmCd} / 関連図面: {saved.LinkedDrawingNo}）。",
            DatabaseProviderStatus = dbProviderStatus,
            SavedProject = saved
        });
    }

    /// <summary>
    /// POST /api/bff/workflow/apply
    /// ワークフローStep1：【申請】（案件 PROJECT / 品目 ITEM / 図面 DRAWING から申請可能）
    /// </summary>
    [HttpPost("apply")]
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
            Message = $"ワークフロー '{wf.WfId}' を申請しました（対象種別: {wf.TargetType}, 対象ID: {wf.TargetId}）。",
            Workflow = wf
        });
    }

    /// <summary>
    /// POST /api/bff/workflow/verify?wfId=WF-9002
    /// ワークフローStep2：【照査】（APPLIED -> VERIFIED）
    /// </summary>
    [HttpPost("verify")]
    [HttpGet("verify")]
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
            Message = $"ワークフロー '{wf.WfId}' の照査が完了しました（ステータス: {wf.WfStatus}）。",
            Workflow = wf
        });
    }

    /// <summary>
    /// GET or POST /api/bff/workflow/complete?wfId=WF-9001&simulateDrawingError=false
    /// ワークフローStep3：【最終承認】（VERIFIED -> APPROVED）
    /// 案件 (PROJECT) の承認をした場合は、関連づいている品目 (ITM_TBL) と図面 (DRW_TBL) も同一トランザクションで一括承認されます。
    /// </summary>
    [HttpGet("complete")]
    [HttpPost("complete")]
    public async Task<IActionResult> CompleteWorkflow(
        [FromQuery] string wfId = "WF-9001",
        [FromQuery] bool simulateDrawingError = false)
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
        var executionResult = await cmd.ExecuteAsync(
            db,
            wfId,
            userId,
            userName,
            orgName,
            simulateDrawingError,
            provider,
            connStr);

        return Ok(new
        {
            ArchitecturePattern = "ModernBff -> ModernBff.MDL.WorkflowCompleteCommand ==(同一DbTransaction共有)==> LegacyMvc.MDL (VB.NET: 品目・BOM・図面一括承認)",
            DatabaseProviderStatus = dbProviderStatus,
            ExecutedByLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Result = executionResult,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
