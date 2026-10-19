using Microsoft.AspNetCore.Mvc;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// モダン側新規機能：案件統合管理＆ワークフロー完了コントローラー (ModernBff -> ModernBff.MDL -> LegacyMvc.MDL)
/// ワークフロー完了時に同一トランザクションで LegacyMvc.MDL の BOM承認コマンド・図面承認コマンドを連続実行します。
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
    /// GET /api/bff/workflow/projects?prjId=PRJ-1001
    /// ModernBff.MDL の ProjectIntegrationCommand を実行し、
    /// モダン側の案件 (PRJ_TBL)・ワークフロー (WF_TBL)・品目 (ITM_TBL) と、
    /// レガシー側 (LegacyMvc.MDL) の BOM (BOM_TBL)・図面 (DRW_TBL) の承認状態を統合して返します。
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetIntegratedProjects([FromQuery] string? prjId = null)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var cmd = new ProjectIntegrationCommand();
        var result = await cmd.GetIntegratedProjectsAsync(db, prjId);

        return Ok(new
        {
            Source = "ModernBff -> ModernBff.MDL (ProjectIntegrationCommand) + LegacyMvc.MDL (BOM/Drawing Store)",
            DatabaseProviderStatus = dbProviderStatus,
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Data = result,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// GET or POST /api/bff/workflow/complete?wfId=WF-9001&simulateDrawingError=false
    /// ModernBff.MDL の WorkflowCompleteCommand を実行し、
    /// 単一トランザクション内で以下を一括実行します:
    ///   1. [ModernBff.MDL] ワークフロー (WF_TBL) 完了 & 案件 (PRJ_TBL) 完了
    ///   2. [LegacyMvc.MDL.BomApprovalCommand] レガシー BOM 承認
    ///   3. [LegacyMvc.MDL.DrawingApprovalCommand] レガシー 図面 承認
    /// ※ simulateDrawingError=true の場合、3番目の図面承認でエラーを発生させ、1と2もすべてロールバックされることを検証できます。
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
        string userName = loginUser?.UserName ?? "山田 太郎 (未ログイン時デフォルト)";
        string orgName = loginUser?.OrganizationName ?? "第一営業本部";

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var cmd = new WorkflowCompleteCommand();
        var executionResult = await cmd.ExecuteAsync(
            db,
            wfId,
            userId,
            userName,
            orgName,
            simulateDrawingError);

        return Ok(new
        {
            ArchitecturePattern = "ModernBff -> ModernBff.MDL.WorkflowCompleteCommand ==(同一DbTransaction共有)==> LegacyMvc.MDL (BomApprovalCommand & DrawingApprovalCommand)",
            DatabaseProviderStatus = dbProviderStatus,
            ExecutedByLoginUser = loginUser,
            LoginUserLookupSource = loginSource,
            Result = executionResult,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
