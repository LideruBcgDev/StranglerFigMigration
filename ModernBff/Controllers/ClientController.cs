using System.Text.Json;
using LegacyMvc.MDL.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.MDL.Commands;
using ModernBff.Services;

namespace ModernBff.Controllers;

public class ClientItemSaveRequestDto
{
    public ItemSaveIParam? IParam { get; set; }
    public string ItmCd { get; set; } = string.Empty;
    public string ItmNm { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int StockQty { get; set; }
}

/// <summary>
/// 【Windows Forms クライアント専用コントローラー (RoutePrefix = "Client")】
/// 1. レガシー互換: Windows Forms より設定された command.IParam.LoginUser を取得して ICommand.Validate() -> ICommand.Execute() を実行可能
/// 2. モダン統合: ModernBff では WebView2 共通ログインのセッショントークン (Redis) からサーバー側で自動解決できるため、
///    WinForms 側で手動で command.IParam.LoginUser を組み立てて送信する処理は本来不要（自動補完可能）になります。
/// </summary>
[ApiController]
[Route("Client")]
[Route("api/Client")]
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
    /// Windows Forms から渡された command.IParam.LoginUser、または ModernBff の Redis 共有セッションから
    /// ICommand.IParam (LoginUser / Session 情報) を設定します。
    /// </summary>
    private async Task<string> PopulateClientIParamAsync(IParamBase iParam, CommandLoginUser? bodyLoginUser = null)
    {
        string? token = _loginUserStore.ExtractToken(HttpContext);
        iParam.SessionId = string.IsNullOrWhiteSpace(token) ? "STATELESS-MODERN-BFF" : $"REDIS-TOKEN:{token}";

        // (A) クエリパラメータ loginUserId による明示指定 (権限NGテスト等にも対応)
        if (Request.Query.TryGetValue("loginUserId", out var qUserId) && !string.IsNullOrWhiteSpace(qUserId))
        {
            string role = Request.Query.TryGetValue("role", out var qRole) && !string.IsNullOrWhiteSpace(qRole)
                ? qRole.ToString()
                : "Engineer";

            iParam.LoginUser = new CommandLoginUser
            {
                UserId = qUserId.ToString(),
                UserName = Request.Query.TryGetValue("loginUserName", out var qName) ? qName.ToString() : qUserId.ToString(),
                OrganizationCode = "WINFORMS-CLIENT",
                OrganizationName = "WinForms 指定組織",
                Roles = new List<string> { role },
                AuthenticationMethod = "WinForms-Query-IParam.LoginUser",
                IsAuthenticated = true,
                SourceChannel = "ClientController: WindowsForms Query (command.IParam.LoginUser)"
            };
            return iParam.LoginUser.SourceChannel;
        }

        // (B) Windows Forms が POST ボディの command.IParam.LoginUser に設定した場合 (レガシー互換)
        if (bodyLoginUser != null && !string.IsNullOrWhiteSpace(bodyLoginUser.UserId))
        {
            bodyLoginUser.SourceChannel = "ClientController: WindowsForms RequestBody (command.IParam.LoginUser)";
            bodyLoginUser.IsAuthenticated = true;
            iParam.LoginUser = bodyLoginUser;
            return bodyLoginUser.SourceChannel;
        }

        // (C) Windows Forms が X-Client-IParam-LoginUser ヘッダーに設定した場合 (レガシー互換)
        if (Request.Headers.TryGetValue("X-Client-IParam-LoginUser", out var headerVal) && !string.IsNullOrWhiteSpace(headerVal))
        {
            try
            {
                string json = Uri.UnescapeDataString(headerVal.ToString());
                var headerUser = JsonSerializer.Deserialize<CommandLoginUser>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (headerUser != null && !string.IsNullOrWhiteSpace(headerUser.UserId))
                {
                    headerUser.SourceChannel = "ClientController: WindowsForms Header (command.IParam.LoginUser)";
                    headerUser.IsAuthenticated = true;
                    iParam.LoginUser = headerUser;
                    return headerUser.SourceChannel;
                }
            }
            catch
            {
            }
        }

        // (D) ModernBff 本来の推奨方式: トークンから Redis 共有セッションを自動解決 (WinForms 側での IParam.LoginUser 手動設定が不要)
        var (redisUser, loginSource) = await _loginUserStore.GetLoginUserAsync(token);
        if (redisUser != null)
        {
            iParam.LoginUser = new CommandLoginUser
            {
                UserId = redisUser.UserId,
                UserName = redisUser.UserName,
                OrganizationCode = redisUser.OrganizationCode,
                OrganizationName = redisUser.OrganizationName,
                Roles = redisUser.Roles,
                AuthenticationMethod = redisUser.AuthenticationMethod,
                IsAuthenticated = true,
                SourceChannel = $"ModernBff Auto-Resolved from Redis ({loginSource}) -> IParam.LoginUser"
            };
            return iParam.LoginUser.SourceChannel;
        }

        // (E) 未ログイン時の検証用デフォルト
        iParam.LoginUser = new CommandLoginUser
        {
            UserId = "yamada",
            UserName = "山田 太郎",
            OrganizationCode = "SALES-01",
            OrganizationName = "第一営業本部",
            Roles = new List<string> { "SalesManager", "Approver" },
            AuthenticationMethod = "WinForms-Default",
            IsAuthenticated = true,
            SourceChannel = "ClientController: Default Fallback (command.IParam.LoginUser)"
        };
        return iParam.LoginUser.SourceChannel;
    }

    /// <summary>
    /// GET /Client/items?itmCd=ITM-001
    /// Windows Forms からの品目 (ITM_TBL) 検索 (ICommand: Validate -> Execute)
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] string? itmCd = null)
    {
        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        var cmd = new ItemSearchCommand();
        cmd.TypedIParam.ConfiguredProvider = provider;
        cmd.TypedIParam.ConnectionString = connStr;
        cmd.TypedIParam.Keyword = itmCd ?? string.Empty;
        cmd.TypedIParam.CommandId = "CMD_CLIENT_ITEM_SEARCH";

        string source = await PopulateClientIParamAsync(cmd.IParam);

        try
        {
            cmd.Validate();
            cmd.Execute();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
                CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSearchCommand)",
                ResponseType = cmd.ResponseType,
                IParam = cmd.IParam,
                OParam = cmd.OParam,
                Error = ex.Message
            });
        }

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
            AccessedFrom = "Windows Forms Client",
            CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSearchCommand)",
            ResponseType = cmd.ResponseType,
            IParam = cmd.IParam,
            OParam = cmd.OParam,
            DatabaseProviderStatus = cmd.TypedOParam.ProviderStatus,
            CurrentLoginUser = cmd.IParam.LoginUser,
            LoginUserLookupSource = source,
            ItemCount = cmd.TypedOParam.Items.Count,
            Items = cmd.TypedOParam.Items,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// POST /Client/items
    /// Windows Forms からの品目 (ITM_TBL) 登録・更新 (ICommand: Validate -> Execute)
    /// </summary>
    [HttpPost("items")]
    public async Task<IActionResult> RegisterItem([FromBody] ClientItemSaveRequestDto req)
    {
        string provider = _configuration["Database:Provider"] ?? "SqlServer";
        string connStr = _configuration.GetConnectionString(provider) ?? string.Empty;

        var cmd = new ItemSaveCommand();
        cmd.TypedIParam.ConfiguredProvider = provider;
        cmd.TypedIParam.ConnectionString = connStr;
        cmd.TypedIParam.ItmCd = !string.IsNullOrWhiteSpace(req.IParam?.ItmCd) ? req.IParam!.ItmCd : req.ItmCd;
        cmd.TypedIParam.ItmNm = !string.IsNullOrWhiteSpace(req.IParam?.ItmNm) ? req.IParam!.ItmNm : req.ItmNm;
        cmd.TypedIParam.Category = !string.IsNullOrWhiteSpace(req.IParam?.Category) ? req.IParam!.Category : req.Category;
        cmd.TypedIParam.UnitPrice = req.IParam != null && req.IParam.UnitPrice > 0 ? req.IParam.UnitPrice : req.UnitPrice;
        cmd.TypedIParam.StockQty = req.IParam != null && req.IParam.StockQty > 0 ? req.IParam.StockQty : req.StockQty;
        cmd.TypedIParam.CommandId = "CMD_CLIENT_ITEM_SAVE";

        await PopulateClientIParamAsync(cmd.IParam, req.IParam?.LoginUser);

        try
        {
            cmd.Validate();
            cmd.Execute();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
                CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSaveCommand)",
                IParam = cmd.IParam,
                OParam = cmd.OParam,
                Error = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        // ModernBff.MDL 側の EF Core DbContext にも同期反映
        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        string code = cmd.TypedIParam.ItmCd.Trim();
        var modernItem = await db.ItmTbl.FirstOrDefaultAsync(i => i.ItmCd.ToLower() == code.ToLower());
        if (modernItem == null)
        {
            modernItem = new ModernBff.MDL.Data.ItmTblEntity
            {
                ItmCd = code,
                ItmNm = cmd.TypedIParam.ItmNm.Trim(),
                Category = string.IsNullOrWhiteSpace(cmd.TypedIParam.Category) ? "一般品目" : cmd.TypedIParam.Category.Trim(),
                UnitPrice = cmd.TypedIParam.UnitPrice,
                StockQty = cmd.TypedIParam.StockQty,
                ApprovalStatus = "DRAFT (未申請)",
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.ItmTbl.Add(modernItem);
        }
        else
        {
            modernItem.ItmNm = cmd.TypedIParam.ItmNm.Trim();
            modernItem.Category = string.IsNullOrWhiteSpace(cmd.TypedIParam.Category) ? modernItem.Category : cmd.TypedIParam.Category.Trim();
            modernItem.UnitPrice = cmd.TypedIParam.UnitPrice;
            modernItem.StockQty = cmd.TypedIParam.StockQty;
            modernItem.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
            CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSaveCommand)",
            ResponseType = cmd.ResponseType,
            IParam = cmd.IParam,
            OParam = cmd.OParam,
            Message = $"[ClientController -> ICommand] 品目 '{modernItem.ItmCd}' を ITM_TBL に登録・更新しました。",
            DatabaseProviderStatus = dbProviderStatus,
            SavedItem = modernItem,
            LegacyMdlResult = cmd.TypedOParam.SavedItem
        });
    }

    /// <summary>
    /// POST /Client/workflow/apply
    /// Windows Forms クライアント（品目管理画面など）からのワークフロー申請
    /// </summary>
    [HttpPost("workflow/apply")]
    public async Task<IActionResult> ApplyWorkflowFromClient([FromBody] WorkflowApplyDto dto)
    {
        var dummyParam = new IParamBase { CommandId = "CMD_CLIENT_WF_APPLY" };
        await PopulateClientIParamAsync(dummyParam);

        string userId = dummyParam.LoginUser.UserId;
        string userName = dummyParam.LoginUser.UserName;

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
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
            Message = $"[ClientController] ワークフロー '{wf.WfId}' を申請しました（対象: {wf.TargetType} / {wf.TargetId}）。",
            IParamLoginUser = dummyParam.LoginUser,
            Workflow = wf
        });
    }

    /// <summary>
    /// GET /Client/customer/{id}
    /// Windows Forms からの顧客情報参照 (ICommand: Validate -> Execute)
    /// </summary>
    [HttpGet("customer/{id:int}")]
    public async Task<IActionResult> GetCustomer(int id = 1)
    {
        var cmd = new CustomerQueryCommand();
        cmd.TypedIParam.CustomerId = id;
        cmd.TypedIParam.CommandId = "CMD_CLIENT_CUSTOMER_QUERY";

        string source = await PopulateClientIParamAsync(cmd.IParam);

        try
        {
            cmd.Validate();
            cmd.Execute();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
                CommandInterface = "LegacyMvc.MDL.Commands.ICommand (CustomerQueryCommand)",
                IParam = cmd.IParam,
                OParam = cmd.OParam,
                Error = ex.Message
            });
        }

        return Ok(new
        {
            Controller = "ModernBff.Controllers.ClientController (RoutePrefix: Client)",
            AccessedFrom = "Windows Forms Client",
            CommandInterface = "LegacyMvc.MDL.Commands.ICommand (CustomerQueryCommand)",
            ResponseType = cmd.ResponseType,
            IParam = cmd.IParam,
            OParam = cmd.OParam,
            CustomerId = cmd.TypedOParam.CustomerId,
            CustomerName = cmd.TypedOParam.CustomerName,
            CurrentLoginUser = cmd.IParam.LoginUser,
            LoginUserLookupSource = source,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        });
    }
}
