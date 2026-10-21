using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using Common.APP;
using LegacyMvc.MDL.Commands;
using LegacyMvc.PRM;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Models;
using ModernBff.PRM;
using ModernBff.Services;
using Newtonsoft.Json.Linq;

namespace ModernBff.Controllers;

/// <summary>
/// 【Web ブラウザ専用コントローラー (RoutePrefix = "View")】
/// ViewController では IIdentity (HttpContext.User.Identity) よりユーザ情報 (LoginUser) を取得して
/// command.IParam.LoginUser に設定し、command.Validate() で利用可否を判断した上で command.Execute() を実行します。
/// </summary>
[ApiController]
[Route("View")]
[Route("api/View")]
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

    private string BuildPublicRedirectUrl(string aspxPage, string reqCode)
    {
        string? proto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(proto))
        {
            proto = Request.IsHttps ? "https" : "http";
        }

        string? host = Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            host = Request.Headers["Host"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(host) && Request.Host.HasValue)
            {
                host = Request.Host.Value;
            }
        }
        if (string.IsNullOrWhiteSpace(host)) host = "localhost:5000";

        string pagePath = aspxPage.TrimStart('/');
        return $"{proto}://{host}/{pagePath}?req_code={Uri.EscapeDataString(reqCode)}";
    }

    /// <summary>
    /// ViewController 用: IIdentity (HttpContext.User.Identity) より LoginUser を取得し command.IParam.LoginUser に設定します。
    /// Redis 共有セッションがある場合は HttpContext.User (ClaimsPrincipal / IIdentity) にブリッジした上で取得します。
    /// </summary>
    private async Task<(ModernBff.Models.LoginUser? RedisUser, string Source)> PopulateViewIParamFromIIdentityAsync(IParamBase iParam)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (redisUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);

        iParam.SessionId = string.IsNullOrWhiteSpace(token) ? "VIEW-SESSION-STATELESS" : $"REDIS-SESSION:{token}";

        // クエリで検証用 loginUserId が指定された場合 (権限エラー検証用)
        if (Request.Query.TryGetValue("loginUserId", out var qUser) && !string.IsNullOrWhiteSpace(qUser))
        {
            var customIdentity = new GenericIdentity(qUser.ToString(), "QueryOverrideIdentity");
            HttpContext.User = new GenericPrincipal(customIdentity, new[] { "Viewer" });
        }
        else if (redisUser != null && (HttpContext.User?.Identity == null || !HttpContext.User.Identity.IsAuthenticated))
        {
            // Redis の共有セッション情報を HttpContext.User (IIdentity) へ投影
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, redisUser.UserId),
                new("DisplayName", redisUser.UserName),
                new("OrgCd", redisUser.OrganizationCode),
                new("OrgNm", redisUser.OrganizationName)
            };
            foreach (string r in redisUser.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, r));
            }
            var claimsIdentity = new ClaimsIdentity(claims, redisUser.AuthenticationMethod);
            HttpContext.User = new ClaimsPrincipal(claimsIdentity);
        }

        IIdentity? identity = HttpContext.User?.Identity;
        if (identity != null && identity.IsAuthenticated && !string.IsNullOrWhiteSpace(identity.Name))
        {
            iParam.LoginUser = new CommandLoginUser
            {
                UserId = identity.Name,
                UserName = redisUser?.UserName ?? identity.Name,
                OrganizationCode = redisUser?.OrganizationCode ?? "ENG-01",
                OrganizationName = redisUser?.OrganizationName ?? "技術開発部",
                Roles = redisUser?.Roles ?? new List<string> { "Engineer" },
                AuthenticationMethod = identity.AuthenticationType ?? "IIdentity",
                IsAuthenticated = identity.IsAuthenticated,
                SourceChannel = $"ViewController: IIdentity (Name={identity.Name}, AuthType={identity.AuthenticationType}, Source={loginSource})"
            };
        }
        else
        {
            // 未ログイン時の検証用デフォルト IIdentity
            IIdentity defaultIdentity = new GenericIdentity("suzuki", "DefaultBrowserIdentity");
            iParam.LoginUser = new CommandLoginUser
            {
                UserId = defaultIdentity.Name,
                UserName = "鈴木 花子",
                OrganizationCode = "ENG-01",
                OrganizationName = "技術開発部",
                Roles = new List<string> { "Engineer" },
                AuthenticationMethod = defaultIdentity.AuthenticationType ?? "DefaultBrowserIdentity",
                IsAuthenticated = defaultIdentity.IsAuthenticated,
                SourceChannel = $"ViewController: IIdentity Fallback (Name={defaultIdentity.Name})"
            };
        }

        return (redisUser, iParam.LoginUser.SourceChannel);
    }

    /// <summary>
    /// GET /View/drawings?keyword=DRW
    /// Web ブラウザからの図面 (DRW_TBL) 検索 (ICommand: DrawingSearchCommand.Validate -> Execute)
    /// </summary>
    [HttpGet("drawings")]
    public async Task<IActionResult> GetDrawings([FromQuery] string? keyword = null)
    {
        var cmd = new DrawingSearchCommand();
        cmd.TypedIParam.Keyword = keyword ?? string.Empty;
        cmd.TypedIParam.CommandId = "CMD_VIEW_DRAWING_SEARCH";

        var (_, loginSource) = await PopulateViewIParamFromIIdentityAsync(cmd.IParam);

        try
        {
            cmd.Validate();
            cmd.Execute();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
                CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSearchCommand)",
                IParam = cmd.IParam,
                OParam = cmd.OParam,
                Error = ex.Message
            });
        }

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            AccessedFrom = "Web Browser",
            CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSearchCommand)",
            ResponseType = cmd.ResponseType,
            IParam = cmd.IParam,
            OParam = cmd.OParam,
            CurrentLoginUser = cmd.IParam.LoginUser,
            LoginUserLookupSource = loginSource,
            DrawingCount = cmd.TypedOParam.Drawings.Count,
            Drawings = cmd.TypedOParam.Drawings,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /View/drawings
    /// Web ブラウザからの図面 (DRW_TBL) 登録・更新 (ICommand: DrawingSaveCommand.Validate -> Execute)
    /// </summary>
    [HttpPost("drawings")]
    public async Task<IActionResult> RegisterDrawing([FromBody] DrawingRegistrationRequest req)
    {
        var cmd = new DrawingSaveCommand();
        cmd.TypedIParam.DrawingNo = req.DrawingNo;
        cmd.TypedIParam.Title = req.Title;
        cmd.TypedIParam.ItmCd = req.ItmCd ?? "ITM-001";
        cmd.TypedIParam.BomNo = req.BomNo ?? string.Empty;
        cmd.TypedIParam.Revision = req.Revision ?? "Rev.A";
        cmd.TypedIParam.CommandId = "CMD_VIEW_DRAWING_SAVE";

        await PopulateViewIParamFromIIdentityAsync(cmd.IParam);

        try
        {
            cmd.Validate();
            cmd.Execute();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
                CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSaveCommand)",
                IParam = cmd.IParam,
                OParam = cmd.OParam,
                Error = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSaveCommand)",
            ResponseType = cmd.ResponseType,
            IParam = cmd.IParam,
            OParam = cmd.OParam,
            Message = $"[ViewController -> ICommand] 図面 '{cmd.TypedOParam.SavedDrawing.DrawingNo} ({cmd.TypedOParam.SavedDrawing.Title})' を DRW_TBL に登録し、別プロセス Worker キュー (JobId={cmd.TypedOParam.SavedDrawing.LastBackgroundJobId}) に [TIFF->PNG生成 / OCR / Elasticsearch登録] ジョブを投入しました。",
            SavedDrawing = cmd.TypedOParam.SavedDrawing
        });
    }

    /// <summary>
    /// GET /View/drawings/jobs?processNow=true
    /// 図面登録時にキューに積まれた非同期ジョブ（TIFF->サムネイルPNG生成、OCR解析、Elasticsearch登録）の状態を確認します。
    /// processNow=true の場合は、別プロセス Worker (ProcessorWorker) の処理を即時トリガーして結果を返します。
    /// </summary>
    [HttpGet("drawings/jobs")]
    [HttpPost("drawings/jobs")]
    public IActionResult GetDrawingBackgroundJobs([FromQuery] bool processNow = false)
    {
        List<DrawingBackgroundJobRecord> newlyProcessed = new();
        if (processNow)
        {
            newlyProcessed = DrawingOutboxQueueStore.ProcessPendingJobs("ProcessorWorker.exe (Out-of-Process Worker Simulation)");
        }

        var allJobs = DrawingOutboxQueueStore.GetAllJobs();
        return Ok(new
        {
            Architecture = "Web/IIS (Producer: Enqueue Only) ---> Persistent Outbox Queue ---> ProcessorWorker.exe (Consumer: Windows Service / Separate Process)",
            QueueDirectory = DrawingOutboxQueueStore.QueueDirectory,
            NewlyProcessedCount = newlyProcessed.Count,
            TotalJobCount = allJobs.Count,
            Jobs = allJobs
        });
    }

    /// <summary>
    /// GET /View/drawings/stamp?drw_no=drw004&amp;transport=AUTO&amp;format=json
    /// 【図面表示時 オンデマンド承認スタンプ＋閲覧日時・ログインユーザ名（社外秘透かし）焼き込み API】
    /// IIS (ModernBff) プロセス内で直接重い画像処理を行うのではなく、
    /// 別プロセス ProcessorWorker の同期エンドポイント (Named Pipe / 内部 HTTP) を呼び出して
    /// 「承認情報」＋「閲覧日時」＋「閲覧ログインユーザ名 (社外秘透かし)」をリアルタイムに焼き込んだ戻り値を受け取り、ブラウザへ返却します。
    /// </summary>
    [HttpGet("drawings/stamp")]
    public async Task<IActionResult> GetStampedDrawingPreview(
        [FromQuery(Name = "drw_no")] string? drwNo = null,
        [FromQuery] string? drawingNo = null,
        [FromQuery] string transport = "AUTO",
        [FromQuery] string format = "json")
    {
        var viewParam = new IParamBase { CommandId = "CMD_VIEW_DRAWING_STAMP_PREVIEW" };
        var (_, loginSource) = await PopulateViewIParamFromIIdentityAsync(viewParam);

        string targetDrwNo = (!string.IsNullOrWhiteSpace(drwNo) ? drwNo : drawingNo) ?? "drw004";
        targetDrwNo = targetDrwNo.Trim().Trim('\'', '"');
        if (string.IsNullOrWhiteSpace(targetDrwNo))
        {
            targetDrwNo = "drw004";
        }

        DrawingRecord? drw = LegacyApprovalStore.GetDrawing(targetDrwNo);
        var stampRequest = new DrawingStampRequest
        {
            DrawingNo = drw?.DrawingNo ?? targetDrwNo,
            Title = drw?.Title ?? $"図面 ({targetDrwNo})",
            Revision = drw?.Revision ?? "Rev.A",
            ItmCd = drw?.ItmCd ?? "ITM-001",
            BomNo = drw?.BomNo ?? "BOM-2026-001",
            SourceTiffPath = drw?.SourceTiffFileName ?? $"{targetDrwNo}.tif",
            ApprovalStatus = drw?.ApprovalStatus ?? "PENDING (未承認)",
            ApprovedBy = drw?.ApprovedBy ?? string.Empty,
            ApprovedOrg = drw?.ApprovedOrg ?? string.Empty,
            ApprovedAtUtc = drw?.ApprovedAtUtc,
            ViewerUserId = viewParam.LoginUser.UserId,
            ViewerUserName = viewParam.LoginUser.UserName,
            ViewerOrgName = viewParam.LoginUser.OrganizationName,
            ViewedAtLocalText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            ConfidentialLabel = "社外秘 / CONFIDENTIAL - DO NOT COPY",
            PreferredTransport = transport
        };

        string? configuredHttpUrl = _configuration["ProcessorWorker:InternalHttpStampUrl"];
        string? configuredPipeName = _configuration["ProcessorWorker:NamedPipeName"];

        DrawingStampResponse stampedResult = await ProcessorWorkerStampClient.RequestOnDemandStampAsync(
            stampRequest,
            configuredHttpUrl,
            configuredPipeName);

        if (string.Equals(format, "image", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
        {
            return Content(stampedResult.StampedSvgMarkup, "image/svg+xml; charset=utf-8");
        }

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            Architecture = "Browser ---> IIS (ModernBff) ---[Sync RPC: NamedPipe / InternalHttp]---> ProcessorWorker.exe (On-Demand Stamp & Watermark Burner) ---> Return Stamped Image to Browser",
            LoginUserLookupSource = loginSource,
            CurrentViewer = viewParam.LoginUser,
            StampRequest = stampRequest,
            StampResponse = stampedResult
        });
    }

    /// <summary>
    /// GET /View/projects?keyword=PRJ
    /// Web ブラウザからの案件統合管理 (PRJ_TBL: 品目・図面関連付け) 検索
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects([FromQuery] string? keyword = null)
    {
        var viewParam = new IParamBase { CommandId = "CMD_VIEW_PROJECTS_QUERY" };
        var (_, loginSource) = await PopulateViewIParamFromIIdentityAsync(viewParam);

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
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            AccessedFrom = "Web Browser",
            DatabaseProviderStatus = dbProviderStatus,
            IParamLoginUser = viewParam.LoginUser,
            LoginUserLookupSource = loginSource,
            Data = data,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /View/projects
    /// Web ブラウザからの案件統合管理 (PRJ_TBL) 登録・品目／図面関連付け保存
    /// </summary>
    [HttpPost("projects")]
    public async Task<IActionResult> RegisterProject([FromBody] ProjectRegistrationDto dto)
    {
        var viewParam = new IParamBase { CommandId = "CMD_VIEW_PROJECT_SAVE" };
        await PopulateViewIParamFromIIdentityAsync(viewParam);

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var cmd = new ProjectIntegrationCommand();
        var saved = await cmd.RegisterOrUpdateProjectAsync(db, dto);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            IParamLoginUser = viewParam.LoginUser,
            Message = $"[ViewController] 案件 '{saved.PrjId} ({saved.PrjNm})' を登録・関連付け保存しました。",
            DatabaseProviderStatus = dbProviderStatus,
            SavedProject = saved
        });
    }

    /// <summary>
    /// POST /View/workflow/apply
    /// Web ブラウザからのワークフロー申請 (Step 1: 申請 - 案件/品目/図面)
    /// </summary>
    [HttpPost("workflow/apply")]
    public async Task<IActionResult> ApplyWorkflow([FromBody] WorkflowApplyDto dto)
    {
        var viewParam = new IParamBase { CommandId = "CMD_VIEW_WF_APPLY" };
        await PopulateViewIParamFromIIdentityAsync(viewParam);

        string userId = viewParam.LoginUser.UserId;
        string userName = viewParam.LoginUser.UserName;

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
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            IParamLoginUser = viewParam.LoginUser,
            Message = $"[ViewController] ワークフロー '{wf.WfId}' を申請しました（対象: {wf.TargetType} / {wf.TargetId}）。",
            Workflow = wf
        });
    }

    /// <summary>
    /// POST /View/workflow/verify?wfId=WF-9002
    /// Web ブラウザからのワークフロー照査 (Step 2: 照査)
    /// </summary>
    [HttpPost("workflow/verify")]
    [HttpGet("workflow/verify")]
    public async Task<IActionResult> VerifyWorkflow([FromQuery] string wfId)
    {
        var viewParam = new IParamBase { CommandId = "CMD_VIEW_WF_VERIFY" };
        await PopulateViewIParamFromIIdentityAsync(viewParam);

        string userId = viewParam.LoginUser.UserId;
        string userName = viewParam.LoginUser.UserName;

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out _);

        var cmd = new WorkflowCompleteCommand();
        var wf = await cmd.VerifyAsync(db, wfId, userId, userName);

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            IParamLoginUser = viewParam.LoginUser,
            Message = $"[ViewController] ワークフロー '{wf.WfId}' の照査が完了しました。",
            Workflow = wf
        });
    }

    /// <summary>
    /// POST /View/workflow/approve?wfId=WF-9001&simulateDrawingError=false
    /// Web ブラウザからのワークフロー最終承認 (Step 3: 承認 - 案件承認時は関連品目・図面も一括承認)
    /// </summary>
    [HttpPost("workflow/approve")]
    [HttpGet("workflow/approve")]
    public async Task<IActionResult> ApproveWorkflow([FromQuery] string wfId = "WF-9001", [FromQuery] bool simulateDrawingError = false)
    {
        var viewParam = new IParamBase { CommandId = "CMD_VIEW_WF_APPROVE" };
        var (_, loginSource) = await PopulateViewIParamFromIIdentityAsync(viewParam);

        string userId = viewParam.LoginUser.UserId;
        string userName = viewParam.LoginUser.UserName;
        string orgName = viewParam.LoginUser.OrganizationName;

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
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            DatabaseProviderStatus = dbProviderStatus,
            IParamLoginUser = viewParam.LoginUser,
            LoginUserLookupSource = loginSource,
            Result = result,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /View/execute (または GET /View/execute?commandName=ITM.APP)
    /// 【Web ブラウザ用コマンド名指定ディスパッチ】
    /// &lt;command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1"&gt;
    /// ViewController では IIdentity より LoginUser を取得して command.IParam.LoginUser に設定し、
    /// Validate() -> Execute() を実行します。
    /// </summary>
    [HttpPost("execute")]
    [HttpGet("execute")]
    public async Task<IActionResult> ExecuteByCommandName(
        [FromBody] ClientCommandExecuteRequestDto? body = null,
        [FromQuery] string? commandName = null)
    {
        string resolvedName = !string.IsNullOrWhiteSpace(body?.CommandName)
            ? body!.CommandName
            : (!string.IsNullOrWhiteSpace(commandName) ? commandName! : "ITM.APP");

        JToken? iParamToken = null;
        if (body?.IParam != null && body.IParam.Value.ValueKind != JsonValueKind.Undefined && body.IParam.Value.ValueKind != JsonValueKind.Null)
        {
            iParamToken = JToken.Parse(body.IParam.Value.GetRawText());
        }

        LegacyCommands.EnsureRegistered();
        ModernCommands.EnsureRegistered();
        CommandDispatcher.RegisterAssembly(typeof(ProjectSearchCommand).Assembly);
        CommandConfigEntry? entry = null;
        var cmd = CommandDispatcher.CreateCommand(resolvedName, iParamToken, ref entry);

        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;
        if (cmd.IParam is ItemAppIParam appParam && string.IsNullOrWhiteSpace(appParam.ConfiguredProvider))
        {
            appParam.ConfiguredProvider = provider;
            appParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is ItemSearchIParam schParam && string.IsNullOrWhiteSpace(schParam.ConfiguredProvider))
        {
            schParam.ConfiguredProvider = provider;
            schParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is ItemSaveIParam savParam && string.IsNullOrWhiteSpace(savParam.ConfiguredProvider))
        {
            savParam.ConfiguredProvider = provider;
            savParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is ItemDuplicateIParam cpyParam && string.IsNullOrWhiteSpace(cpyParam.ConfiguredProvider))
        {
            cpyParam.ConfiguredProvider = provider;
            cpyParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is ItemReviseIParam revParam && string.IsNullOrWhiteSpace(revParam.ConfiguredProvider))
        {
            revParam.ConfiguredProvider = provider;
            revParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is ProjectSearchIParam prjSchParam && string.IsNullOrWhiteSpace(prjSchParam.ConfiguredProvider))
        {
            prjSchParam.ConfiguredProvider = provider;
            prjSchParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is ProjectSaveIParam prjSavParam && string.IsNullOrWhiteSpace(prjSavParam.ConfiguredProvider))
        {
            prjSavParam.ConfiguredProvider = provider;
            prjSavParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is WorkflowApplyIParam wfAppParam && string.IsNullOrWhiteSpace(wfAppParam.ConfiguredProvider))
        {
            wfAppParam.ConfiguredProvider = provider;
            wfAppParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is WorkflowVerifyIParam wfVrfParam && string.IsNullOrWhiteSpace(wfVrfParam.ConfiguredProvider))
        {
            wfVrfParam.ConfiguredProvider = provider;
            wfVrfParam.ConnectionString = connStr;
        }
        else if (cmd.IParam is WorkflowApproveIParam wfAprParam && string.IsNullOrWhiteSpace(wfAprParam.ConfiguredProvider))
        {
            wfAprParam.ConfiguredProvider = provider;
            wfAprParam.ConnectionString = connStr;
        }

        var (_, loginSource) = await PopulateViewIParamFromIIdentityAsync(cmd.IParam);

        // 【Web 画面 (.aspx) 起動リダイレクト判定】
        // CommandDefinitionAttribute に Aspx が指定されている場合、RequestManager で req_code を発行し、
        // クライアント (StranglerHttpClientUtility) にリダイレクト情報を返してブラウザ起動を促す
        if (entry != null && !string.IsNullOrWhiteSpace(entry.Aspx))
        {
            var paramDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var reqEntry = RequestManager.RegisterRequest(entry.Aspx, cmd.IParam.LoginUser, entry.Name, paramDict);
            string redirectUrl = BuildPublicRedirectUrl(entry.Aspx, reqEntry.ReqCode);

            Response.Headers["X-Strangler-Redirect-Url"] = redirectUrl;
            Response.Headers["X-Strangler-Req-Code"] = reqEntry.ReqCode;
            Response.Headers["X-Strangler-Aspx"] = entry.Aspx;

            return Ok(new
            {
                isRedirect = true,
                redirectUrl = redirectUrl,
                req_code = reqEntry.ReqCode,
                reqCode = reqEntry.ReqCode,
                aspx = entry.Aspx,
                commandName = entry.Name,
                message = $"Web 画面 ({entry.Aspx}) へのリダイレクトが要求されました。(req_code={reqEntry.ReqCode})",
                loginUser = cmd.IParam.LoginUser,
                iParam = cmd.IParam,
                oParam = cmd.OParam
            });
        }

        try
        {
            cmd.Validate();
            cmd.Execute();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
                CommandName = entry?.Name ?? resolvedName,
                CommandType = entry?.CommandTypeString ?? cmd.GetType().FullName,
                DbSlot = entry?.Db ?? 1,
                ResponseType = cmd.ResponseType,
                IParamType = cmd.IParam.GetType().FullName,
                OParamType = cmd.OParam.GetType().FullName,
                IParam = cmd.IParam,
                OParam = cmd.OParam,
                Error = ex.Message
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
                CommandName = entry?.Name ?? resolvedName,
                Error = ex.Message
            });
        }

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ViewController (RoutePrefix: View)",
            AccessedFrom = "Web Browser (Command Dispatcher via IIdentity)",
            CommandName = entry?.Name ?? resolvedName,
            CommandConfig = $"<command name=\"{entry?.Name}\" command=\"{entry?.CommandTypeString}\" db=\"{entry?.Db}\">",
            DbSlot = entry?.Db ?? 1,
            ResponseType = cmd.ResponseType,
            LoginUserLookupSource = loginSource,
            iParamType = cmd.IParam.GetType().FullName,
            oParamType = cmd.OParam.GetType().FullName,
            iParam = cmd.IParam,
            oParam = cmd.OParam,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
