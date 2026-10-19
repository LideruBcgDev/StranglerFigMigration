using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ModernBff.Data;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers;

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
    /// EF Core を用いてデータベース (Oracle / Postgres / SqlServer 設定切替対応) の
    /// USR_TBL テーブルを検索し、ログイン可否チェックと loginuser（ユーザ・組織・権限）の生成・Redis共有を行います。
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { message = "ユーザIDを入力してください。" });
        }

        string userId = request.UserId.Trim();

        // 1. EF Core DbContext を取得し、USR_TBL でログイン可能か検証
        using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
            _serviceProvider,
            _configuration,
            _logger,
            out string dbProviderStatus);

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

        // 2. USR_TBL のレコードから共有 loginuser（ユーザ・組織・権限）を生成
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
            AuthenticatedAtUtc = DateTime.UtcNow
        };

        var ttl = TimeSpan.FromMinutes(60);

        // 3. LegacyMvc と直接共有する Redis キー ("StranglerSession:LoginUser:{token}") に保存
        var (storedInRedis, redisKey) = await _loginUserStore.SaveLoginUserAsync(loginUser, ttl);

        // 4. ASP.NET Core の IDistributedCache にも保存
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
            // Redis 未起動時は SharedLoginUserStore のインメモリフォールバックで継続
        }

        // 5. ブラウザ / WebView2 / HttpClient 共通で利用できる Cookie をセット
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
            Message = "USR_TBL での認証に成功しました。loginuser を共有セッション (Redis) に登録しました。",
            AuthenticatedFromTable = "USR_TBL (EF Core)",
            DatabaseProvider = dbProviderStatus,
            StoredInRedis = storedInRedis,
            RedisKey = redisKey,
            LoginUser = loginUser
        });
    }

    /// <summary>
    /// GET /api/bff/auth/users
    /// USR_TBL に登録されているユーザ一覧を確認する検証用エンドポイント
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
    /// 現在共有されている loginuser 情報を取得します。
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
                Message = "未ログインです。/login からログインしてください。",
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
