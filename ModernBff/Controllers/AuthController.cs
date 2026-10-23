using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ModernBff.Data;
using ModernBff.MDL.Data;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// モダン側 (ModernBff) 統合認証コントローラー。
/// ・enableNormalLogin: True の場合は ID/パスワード入力＋シングルサインインボタン、False の場合はシングルサインインボタンのみ有効
/// ・autoRedirectToIdpOnLogin: True の場合はログイン画面を表示せず直接 SAML/OIDC IdP へリダイレクト
/// ・returnUrl: 未ログイン状態で http://localhost:5000/web/view/drawings?drw_no='drw004' 等へアクセスした際、
///   ログイン完了後に元の URL へ自動遷移して対象図面を表示します。
/// </summary>
[ApiController]
[Route("api/bff/auth")]
public class AuthController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        SharedLoginUserStore loginUserStore,
        IDistributedCache distributedCache,
        ILogger<AuthController> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _loginUserStore = loginUserStore;
        _distributedCache = distributedCache;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/bff/auth/config (および /api/bff/auth/providers)
    /// 現在の認証設定パラメータ (enableNormalLogin / autoRedirectToIdpOnLogin) と登録済 SAML/OIDC IdP 一覧を返します。
    /// </summary>
    [HttpGet("config")]
    [HttpGet("providers")]
    public IActionResult GetAuthConfig()
    {
        var providers = _loginUserStore.GetIdpProviders();
        var primary = _loginUserStore.GetSelectedOrPrimaryIdp();

        return Ok(new
        {
            enableNormalLogin = _loginUserStore.EnableNormalLogin,
            autoRedirectToIdpOnLogin = _loginUserStore.AutoRedirectToIdpOnLogin,
            defaultSsoProviderId = _loginUserStore.DefaultSsoProviderId,
            primaryProvider = primary,
            providers = providers,
            configSource = "ModernBff/appsettings.json (Authentication:enableNormalLogin, Authentication:autoRedirectToIdpOnLogin, Authentication:Providers)"
        });
    }

    /// <summary>
    /// POST /api/bff/auth/config
    /// 検証用に enableNormalLogin / autoRedirectToIdpOnLogin / defaultSsoProviderId を実行時に切り替えます。
    /// </summary>
    [HttpPost("config")]
    public IActionResult UpdateAuthConfig([FromBody] AuthConfigUpdateDto dto)
    {
        _loginUserStore.UpdateRuntimeConfig(dto.EnableNormalLogin, dto.AutoRedirectToIdpOnLogin, dto.DefaultSsoProviderId);
        return GetAuthConfig();
    }

    /// <summary>
    /// POST /api/bff/auth/login
    /// 1. 通常ログイン (USR_TBL の ID / パスワード認証)
    /// enableNormalLogin = false の場合は拒否します。
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!_loginUserStore.EnableNormalLogin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "現在 enableNormalLogin = false に設定されているため、ID/パスワードによる通常ログインは無効化されています。シングルサインイン (SAML/OIDC) ボタンからログインしてください。"
            });
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { message = "ユーザIDを入力してください。" });
        }

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        string userId = request.UserId.Trim();
        var usrRecord = await db.UsrTbl
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UsrId.ToLower() == userId.ToLower());

        if (usrRecord == null)
        {
            return Unauthorized(new
            {
                message = $"USR_TBL にユーザID '{userId}' が存在しません。（登録済: yamada, suzuki, sato, disabled_user）",
                checkedTable = "USR_TBL",
                databaseProvider = dbProviderStatus
            });
        }

        if (!usrRecord.IsActive)
        {
            return Unauthorized(new
            {
                message = $"ユーザ '{usrRecord.UsrNm} ({usrRecord.UsrId})' は USR_TBL.IS_ACTIVE = false のためログイン不可です。",
                checkedTable = "USR_TBL",
                databaseProvider = dbProviderStatus
            });
        }

        if (usrRecord.Password != request.Password)
        {
            return Unauthorized(new
            {
                message = "USR_TBL のパスワードと一致しません。（検証用パスワード: pass123）",
                checkedTable = "USR_TBL",
                databaseProvider = dbProviderStatus
            });
        }

        return await IssueSharedSessionAsync(
            usrRecord,
            authMethod: "Password (USR_TBL)",
            idpName: "ModernBff LocalDB (USR_TBL)",
            dbProviderStatus: dbProviderStatus,
            returnUrl: request.ReturnUrl);
    }

    /// <summary>
    /// POST /api/bff/auth/sso
    /// 2. シングルサインインボタン押下時の SAML 2.0 / OIDC 認証エンドポイント
    /// 複数の SAML/OIDC プロバイダ登録にも対応し、指定された ProviderId (未指定時はプライマリ IdP) で認証します。
    /// </summary>
    [HttpPost("sso")]
    public async Task<IActionResult> SsoLogin([FromBody] SsoSimulateRequest request)
    {
        var selectedProvider = _loginUserStore.GetSelectedOrPrimaryIdp(request.ProviderId);
        string protocol = (!string.IsNullOrWhiteSpace(request.Protocol) ? request.Protocol : selectedProvider.Protocol)
            .Trim().ToUpperInvariant();
        string nameId = string.IsNullOrWhiteSpace(request.NameId) ? "yamada" : request.NameId.Trim();

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var usrRecord = await db.UsrTbl
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UsrId.ToLower() == nameId.ToLower());

        if (usrRecord == null)
        {
            return Unauthorized(new
            {
                message = $"[{protocol} 認証エラー] IdP ({selectedProvider.DisplayName}) から返却された Subject/NameID '{nameId}' が USR_TBL に存在しません。",
                checkedTable = "USR_TBL",
                databaseProvider = dbProviderStatus
            });
        }

        if (!usrRecord.IsActive)
        {
            return Unauthorized(new
            {
                message = $"[{protocol} 認証エラー] ユーザ '{usrRecord.UsrNm} ({usrRecord.UsrId})' は USR_TBL.IS_ACTIVE = false のためログイン不可です。",
                checkedTable = "USR_TBL",
                databaseProvider = dbProviderStatus
            });
        }

        string authMethod = protocol == "SAML2"
            ? $"SAML 2.0 SSO ({selectedProvider.DisplayName})"
            : $"OIDC SSO ({selectedProvider.DisplayName})";

        string idpIssuer = !string.IsNullOrWhiteSpace(request.IdpIssuer)
            ? request.IdpIssuer
            : selectedProvider.Issuer;

        return await IssueSharedSessionAsync(
            usrRecord,
            authMethod,
            idpIssuer,
            dbProviderStatus,
            request.ReturnUrl);
    }

    /// <summary>
    /// GET /api/bff/auth/sso/redirect?returnUrl=...&amp;providerId=...&amp;user=yamada
    /// autoRedirectToIdpOnLogin = true の場合や、ブラウザ直接リダイレクトで SSO ログインを実行し、
    /// 認証 Cookie と Redis loginuser を発行した上で returnUrl (例: /web/view/drawings?drw_no='drw004') へ直接リダイレクトします。
    /// </summary>
    [HttpGet("sso/redirect")]
    public async Task<IActionResult> SsoDirectRedirect(
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? providerId = null,
        [FromQuery] string user = "yamada",
        [FromQuery] bool clientWebView = false)
    {
        var selectedProvider = _loginUserStore.GetSelectedOrPrimaryIdp(providerId);
        string protocol = selectedProvider.Protocol.ToUpperInvariant();

        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var usrRecord = await db.UsrTbl
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UsrId.ToLower() == user.Trim().ToLower())
            ?? await db.UsrTbl.AsNoTracking().FirstAsync(u => u.IsActive);

        string authMethod = $"{protocol} AutoRedirect SSO ({selectedProvider.DisplayName})";
        var (_, loginUser, payloadObj) = await CreateAndSaveSessionAsync(
            usrRecord,
            authMethod,
            selectedProvider.Issuer,
            dbProviderStatus,
            returnUrl);

        // WinForms WebView2 からのアクセスで autoRedirectToIdpOnLogin = true が発動した場合は、
        // WebView2 に STRANGLER_LOGIN_SUCCESS メッセージを送ってダイアログを自動完了させる
        if (clientWebView)
        {
            string jsonPayload = JsonSerializer.Serialize(payloadObj);
            string html = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""><title>SSO Auto Redirect Login</title></head>
<body style=""background:#0f172a;color:#f8fafc;font-family:sans-serif;padding:24px;"">
<h3>✓ autoRedirectToIdpOnLogin=true により {selectedProvider.DisplayName} で自動シングルサインインしました</h3>
<script>
if (window.chrome && window.chrome.webview) {{
    window.chrome.webview.postMessage(JSON.stringify({{
        type: 'STRANGLER_LOGIN_SUCCESS',
        payload: {jsonPayload}
    }}));
}}
</script>
</body></html>";
            return Content(html, "text/html; charset=utf-8");
        }

        string safeTarget = NormalizeReturnUrl(returnUrl);
        return Redirect(safeTarget);
    }

    /// <summary>
    /// POST /api/bff/auth/saml2/acs
    /// SAML 2.0 IdP からの HTTP-POST バインディング (SAMLResponse) を受け取る ACS エンドポイント
    /// </summary>
    [HttpPost("saml2/acs")]
    public async Task<IActionResult> Saml2AssertionConsumerService([FromForm] string? SAMLResponse, [FromForm] string? RelayState)
    {
        string extractedNameId = "yamada";
        if (!string.IsNullOrWhiteSpace(SAMLResponse))
        {
            try
            {
                var xmlBytes = Convert.FromBase64String(SAMLResponse);
                var xmlStr = Encoding.UTF8.GetString(xmlBytes);
                var doc = XDocument.Parse(xmlStr);
                var nameIdElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NameID");
                if (nameIdElem != null && !string.IsNullOrWhiteSpace(nameIdElem.Value))
                {
                    extractedNameId = nameIdElem.Value.Trim();
                }
            }
            catch
            {
                extractedNameId = SAMLResponse.Trim();
            }
        }

        return await SsoLogin(new SsoSimulateRequest
        {
            ProviderId = "saml2-enterprise",
            Protocol = "SAML2",
            NameId = extractedNameId,
            ReturnUrl = RelayState
        });
    }

    /// <summary>
    /// GET /api/bff/auth/oidc/callback?code=xxx&amp;user=yamada&amp;state=/web/view/drawings?drw_no='drw004'
    /// OIDC IdP からの認可コードコールバックを受け取るエンドポイント
    /// </summary>
    [HttpGet("oidc/callback")]
    public async Task<IActionResult> OidcCallback(
        [FromQuery] string? code,
        [FromQuery] string user = "yamada",
        [FromQuery] string? state = null)
    {
        return await SsoDirectRedirect(returnUrl: state, providerId: "oidc-entra-primary", user: user);
    }

    private async Task<(bool StoredInRedis, LoginUser User, object Payload)> CreateAndSaveSessionAsync(
        UsrTblEntity usrRecord,
        string authMethod,
        string idpName,
        string dbProviderStatus,
        string? returnUrl)
    {
        var roles = usrRecord.RolesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var loginUser = new LoginUser
        {
            SessionToken = Guid.NewGuid().ToString("N"),
            UserId = usrRecord.UsrId,
            UserName = usrRecord.UsrNm,
            OrganizationCode = usrRecord.OrgCd,
            OrganizationName = usrRecord.OrgNm,
            Roles = roles,
            AuthenticationMethod = authMethod,
            IdentityProvider = idpName,
            AuthenticatedAtUtc = DateTime.UtcNow
        };

        var ttl = TimeSpan.FromMinutes(60);
        var (storedInRedis, redisKey) = await _loginUserStore.SaveLoginUserAsync(loginUser, ttl);

        try
        {
            await _distributedCache.SetStringAsync(
                $"loginuser:{loginUser.SessionToken}",
                JsonSerializer.Serialize(loginUser),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
            HttpContext.Session.SetString("loginuser", JsonSerializer.Serialize(loginUser));
        }
        catch
        {
        }

        Response.Cookies.Append(SharedLoginUserStore.CookieName, loginUser.SessionToken, new CookieOptions
        {
            HttpOnly = false,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(ttl),
            SameSite = SameSiteMode.Lax
        });

        Response.Headers[SharedLoginUserStore.HeaderName] = loginUser.SessionToken;

        string safeReturnUrl = NormalizeReturnUrl(returnUrl);
        var payload = new
        {
            Message = $"[{authMethod}] USR_TBL での検証に成功し、loginuser を共有セッション (Redis) に登録しました。",
            AuthenticatedFromTable = "USR_TBL (EF Core)",
            AuthenticationMethod = authMethod,
            IdentityProvider = idpName,
            DatabaseProvider = dbProviderStatus,
            StoredInRedis = storedInRedis,
            RedisKey = redisKey,
            ReturnUrl = safeReturnUrl,
            LoginUser = loginUser
        };

        return (storedInRedis, loginUser, payload);
    }

    private async Task<IActionResult> IssueSharedSessionAsync(
        UsrTblEntity usrRecord,
        string authMethod,
        string idpName,
        string dbProviderStatus,
        string? returnUrl)
    {
        var (_, _, payload) = await CreateAndSaveSessionAsync(usrRecord, authMethod, idpName, dbProviderStatus, returnUrl);
        return Ok(payload);
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/legacy-drawings";
        }
        string trimmed = returnUrl.Trim();
        if (trimmed.StartsWith("/") && !trimmed.StartsWith("//"))
        {
            return trimmed;
        }
        return "/legacy-drawings";
    }

    /// <summary>
    /// GET /api/bff/auth/users
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsrTblList()
    {
        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

        var users = await db.UsrTbl.AsNoTracking().ToListAsync();
        return Ok(new
        {
            Table = "USR_TBL",
            DatabaseProvider = dbProviderStatus,
            Users = users
        });
    }

    /// <summary>
    /// GET /api/bff/auth/me
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var token = _loginUserStore.ExtractToken(HttpContext);
        var (user, source) = await _loginUserStore.GetLoginUserAsync(token);

        if (user == null)
        {
            return Unauthorized(new
            {
                Authenticated = false,
                Message = "未ログインです。/login からログイン（通常 / OIDC / SAML2）してください。",
                LookupSource = source
            });
        }

        return Ok(new
        {
            Authenticated = true,
            LookupSource = source,
            LoginUser = user
        });
    }

    /// <summary>
    /// POST /api/bff/auth/logout
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var token = _loginUserStore.ExtractToken(HttpContext);
        await _loginUserStore.RemoveLoginUserAsync(token);
        Response.Cookies.Delete(SharedLoginUserStore.CookieName, new CookieOptions { Path = "/" });

        return Ok(new
        {
            Message = "ログアウトしました。",
            LoginRedirectUrl = "/login"
        });
    }
}
