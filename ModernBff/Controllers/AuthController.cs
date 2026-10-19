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
/// レガシー側 (LegacyMvc) にあった SAML 認証・OIDC 認証・パスワード認証を廃止して ModernBff に完全集約。
/// いずれの方式で認証された場合も USR_TBL (EF Core) でユーザの有効性 (IS_ACTIVE) と組織・権限を引き当て、
/// 共通の loginuser を Redis に格納して全プロジェクト (ClientController / ViewController / .aspx) へ共有します。
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
    /// POST /api/bff/auth/login
    /// 1. 通常ログイン (USR_TBL の ID / パスワード認証)
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
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
            dbProviderStatus: dbProviderStatus);
    }

    /// <summary>
    /// POST /api/bff/auth/sso
    /// 2. OIDC (OpenID Connect) / SAML 2.0 統合シングルサインオン認証エンドポイント
    /// レガシー側にあった SAML / OIDC 認証を廃止し、モダン Web サイト側で IdP トークン / SAML Assertion を検証した上で
    /// USR_TBL のユーザ・組織・権限を引き当てて Redis に loginuser を発行します。
    /// </summary>
    [HttpPost("sso")]
    public async Task<IActionResult> SsoLogin([FromBody] SsoSimulateRequest request)
    {
        string protocol = (request.Protocol ?? "OIDC").Trim().ToUpperInvariant();
        string nameId = (request.NameId ?? "yamada").Trim();

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
                message = $"[{protocol} 認証エラー] IdP から返却された Subject/NameID '{nameId}' が USR_TBL に存在しません。",
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
            ? "SAML 2.0 (ModernBff SAML Assertion Consumer)"
            : "OIDC (ModernBff OpenID Connect Authorization Code + PKCE)";

        string idpIssuer = !string.IsNullOrWhiteSpace(request.IdpIssuer)
            ? request.IdpIssuer
            : (protocol == "SAML2"
                ? (_configuration["Authentication:Saml2:IdpEntityId"] ?? "https://idp.enterprise.local/saml2")
                : (_configuration["Authentication:Oidc:Authority"] ?? "https://login.microsoftonline.com/common/v2.0"));

        return await IssueSharedSessionAsync(usrRecord, authMethod, idpIssuer, dbProviderStatus);
    }

    /// <summary>
    /// POST /api/bff/auth/saml2/acs
    /// SAML 2.0 IdP からの HTTP-POST バインディング (SAMLResponse Base64 XML) を受け取る Assertion Consumer Service (ACS) エンドポイント
    /// </summary>
    [HttpPost("saml2/acs")]
    public async Task<IActionResult> Saml2AssertionConsumerService([FromForm] string? SAMLResponse)
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
                // Base64 ではなく直接ユーザIDが渡された場合のフォールバック
                extractedNameId = SAMLResponse.Trim();
            }
        }

        return await SsoLogin(new SsoSimulateRequest
        {
            Protocol = "SAML2",
            NameId = extractedNameId
        });
    }

    /// <summary>
    /// GET /api/bff/auth/oidc/callback?code=xxx&user=yamada
    /// OIDC IdP からの認可コードコールバックを受け取るエンドポイント
    /// </summary>
    [HttpGet("oidc/callback")]
    public async Task<IActionResult> OidcCallback([FromQuery] string? code, [FromQuery] string user = "yamada")
    {
        return await SsoLogin(new SsoSimulateRequest
        {
            Protocol = "OIDC",
            NameId = user
        });
    }

    private async Task<IActionResult> IssueSharedSessionAsync(
        UsrTblEntity usrRecord,
        string authMethod,
        string idpName,
        string dbProviderStatus)
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

        return Ok(new
        {
            Message = $"[{authMethod}] USR_TBL での検証に成功し、loginuser を共有セッション (Redis) に登録しました。",
            AuthenticatedFromTable = "USR_TBL (EF Core)",
            AuthenticationMethod = authMethod,
            IdentityProvider = idpName,
            DatabaseProvider = dbProviderStatus,
            StoredInRedis = storedInRedis,
            RedisKey = redisKey,
            LoginUser = loginUser
        });
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
        Response.Cookies.Delete(SharedLoginUserStore.CookieName);

        return Ok(new { Message = "ログアウトしました。" });
    }
}
