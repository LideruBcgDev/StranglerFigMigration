using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers;

[ApiController]
[Route("api/bff/auth")]
public class AuthController : ControllerBase
{
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly IDistributedCache _distributedCache;

    // 検証用プリセットユーザ定義（ユーザ・組織・権限）
    private static readonly Dictionary<string, (string Password, string UserName, string OrgCode, string OrgName, List<string> Roles)> PresetUsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yamada"] = ("pass123", "山田 太郎", "ORG-SALES-01", "第一営業本部 エンタープライズ営業部", new List<string> { "Admin", "SalesManager", "CustomerRead", "CustomerWrite", "LegacyAccess" }),
        ["suzuki"] = ("pass123", "鈴木 花子", "ORG-CS-02", "カスタマーサクセス推進室", new List<string> { "Operator", "CustomerRead" }),
        ["sato"]   = ("pass123", "佐藤 健一", "ORG-AUDIT-99", "内部監査室", new List<string> { "Auditor", "CustomerRead", "LegacyAccess" })
    };

    public AuthController(SharedLoginUserStore loginUserStore, IDistributedCache distributedCache)
    {
        _loginUserStore = loginUserStore;
        _distributedCache = distributedCache;
    }

    /// <summary>
    /// POST /api/bff/auth/login
    /// Webログイン画面（ブラウザ または WinForms WebView2）から呼び出され、
    /// loginuser（ユーザ・組織・権限）を Redis に保存し、Cookie と JSON でセッショントークンを返します。
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { message = "ユーザIDを入力してください。" });
        }

        string userId = request.UserId.Trim();
        LoginUser loginUser;

        if (PresetUsers.TryGetValue(userId, out var preset))
        {
            if (!string.IsNullOrEmpty(request.Password) && request.Password != preset.Password)
            {
                return Unauthorized(new { message = "パスワードが正しくありません。（検証用パスワード: pass123）" });
            }

            loginUser = new LoginUser
            {
                SessionToken = Guid.NewGuid().ToString("N"),
                UserId = userId,
                UserName = preset.UserName,
                OrganizationCode = preset.OrgCode,
                OrganizationName = preset.OrgName,
                Roles = preset.Roles,
                AuthenticatedAtUtc = DateTime.UtcNow
            };
        }
        else
        {
            // 任意のIDでも検証しやすいようゲスト組織・権限でログイン可能にする
            loginUser = new LoginUser
            {
                SessionToken = Guid.NewGuid().ToString("N"),
                UserId = userId,
                UserName = $"検証ユーザ ({userId})",
                OrganizationCode = "ORG-GEN-00",
                OrganizationName = "総合企画部",
                Roles = new List<string> { "StandardUser", "CustomerRead" },
                AuthenticatedAtUtc = DateTime.UtcNow
            };
        }

        var ttl = TimeSpan.FromMinutes(60);

        // 1. LegacyMvc と直接共有する Redis キー ("StranglerSession:LoginUser:{token}") に保存
        var (storedInRedis, redisKey) = await _loginUserStore.SaveLoginUserAsync(loginUser, ttl);

        // 2. ASP.NET Core の IDistributedCache にも保存
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

        // 3. ブラウザ / WebView2 / HttpClient 共通で利用できる Cookie をセット
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
            Message = "ログインに成功しました。loginuser を共有セッション (Redis) に登録しました。",
            StoredInRedis = storedInRedis,
            RedisKey = redisKey,
            LoginUser = loginUser
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
