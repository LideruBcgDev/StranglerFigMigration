using System.Collections.Concurrent;
using System.Text.Json;
using ModernBff.Models;
using StackExchange.Redis;

namespace ModernBff.Services;

/// <summary>
/// loginuser 情報を Redis (Key: "StranglerSession:LoginUser:{token}") に保存・取得するサービス。
/// また、appsettings.json の Authentication 設定 (enableNormalLogin / autoRedirectToIdpOnLogin / 複数 IdP Providers) も管理します。
/// </summary>
public class SharedLoginUserStore
{
    public const string CookieName = "StranglerAuthToken";
    public const string HeaderName = "X-Strangler-AuthToken";
    public const string RedisKeyPrefix = "StranglerSession:LoginUser:";

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SharedLoginUserStore> _logger;
    private static readonly ConcurrentDictionary<string, string> FallbackMemoryStore = new();

    private bool? _overrideEnableNormalLogin;
    private bool? _overrideAutoRedirectToIdpOnLogin;
    private string? _overrideDefaultSsoProviderId;

    public SharedLoginUserStore(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<SharedLoginUserStore> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// enableNormalLogin 設定値 (True: ID/パスワード入力ログイン＋SSOボタン表示、False: SSOボタンのみ表示)
    /// </summary>
    public bool EnableNormalLogin =>
        _overrideEnableNormalLogin ?? _configuration.GetValue<bool>("Authentication:enableNormalLogin", true);

    /// <summary>
    /// autoRedirectToIdpOnLogin 設定値 (True: ログイン画面を表示せずに直接 SAML/OIDC IdP へリダイレクト)
    /// </summary>
    public bool AutoRedirectToIdpOnLogin =>
        _overrideAutoRedirectToIdpOnLogin ?? _configuration.GetValue<bool>("Authentication:autoRedirectToIdpOnLogin", false);

    /// <summary>
    /// シングルサインインボタン押下時（または autoRedirectToIdpOnLogin=true 時）の既定 IdP プロバイダID
    /// </summary>
    public string DefaultSsoProviderId =>
        _overrideDefaultSsoProviderId ?? (_configuration["Authentication:DefaultSsoProviderId"] ?? "oidc-entra-primary");

    public void UpdateRuntimeConfig(bool? enableNormalLogin, bool? autoRedirectToIdpOnLogin, string? defaultSsoProviderId = null)
    {
        if (enableNormalLogin.HasValue)
            _overrideEnableNormalLogin = enableNormalLogin.Value;
        if (autoRedirectToIdpOnLogin.HasValue)
            _overrideAutoRedirectToIdpOnLogin = autoRedirectToIdpOnLogin.Value;
        if (!string.IsNullOrWhiteSpace(defaultSsoProviderId))
            _overrideDefaultSsoProviderId = defaultSsoProviderId;
    }

    public List<IdpProviderConfig> GetIdpProviders()
    {
        var providers = _configuration.GetSection("Authentication:Providers").Get<List<IdpProviderConfig>>();
        if (providers == null || providers.Count == 0)
        {
            providers = new List<IdpProviderConfig>
            {
                new()
                {
                    ProviderId = "oidc-entra-primary",
                    DisplayName = "シングルサインイン (Microsoft Entra ID - OIDC)",
                    Protocol = "OIDC",
                    Enabled = true,
                    IsPrimary = true,
                    Issuer = _configuration["Authentication:Oidc:Authority"] ?? "https://login.microsoftonline.com/common/v2.0"
                },
                new()
                {
                    ProviderId = "saml2-enterprise",
                    DisplayName = "シングルサインイン (社内統合IdP - SAML 2.0)",
                    Protocol = "SAML2",
                    Enabled = true,
                    IsPrimary = false,
                    Issuer = _configuration["Authentication:Saml2:IdpEntityId"] ?? "https://idp.enterprise.local/saml2"
                }
            };
        }
        return providers.Where(p => p.Enabled).ToList();
    }

    public IdpProviderConfig GetSelectedOrPrimaryIdp(string? providerId = null)
    {
        var providers = GetIdpProviders();
        string targetId = !string.IsNullOrWhiteSpace(providerId) ? providerId : DefaultSsoProviderId;
        return providers.FirstOrDefault(p => string.Equals(p.ProviderId, targetId, StringComparison.OrdinalIgnoreCase))
            ?? providers.FirstOrDefault(p => p.IsPrimary)
            ?? providers.First();
    }

    public async Task<(bool StoredInRedis, string RedisKey)> SaveLoginUserAsync(LoginUser user, TimeSpan ttl)
    {
        string key = $"{RedisKeyPrefix}{user.SessionToken}";
        string json = JsonSerializer.Serialize(user);

        FallbackMemoryStore[key] = json;

        try
        {
            if (_redis.IsConnected)
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync(key, json, ttl);
                return (true, key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis への loginuser 保存に失敗しました。インメモリフォールバックを使用します。");
        }

        return (false, key);
    }

    public async Task<(LoginUser? User, string Source)> GetLoginUserAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, "NoToken");
        }

        string key = $"{RedisKeyPrefix}{token}";

        try
        {
            if (_redis.IsConnected)
            {
                var db = _redis.GetDatabase();
                var redisValue = await db.StringGetAsync(key);
                if (redisValue.HasValue)
                {
                    var user = JsonSerializer.Deserialize<LoginUser>(redisValue.ToString());
                    return (user, $"Redis ({key})");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis からの loginuser 取得に失敗しました。");
        }

        if (FallbackMemoryStore.TryGetValue(key, out var fallbackJson))
        {
            var user = JsonSerializer.Deserialize<LoginUser>(fallbackJson);
            return (user, $"InMemoryFallback ({key})");
        }

        return (null, $"NotFound ({key})");
    }

    public async Task RemoveLoginUserAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        string key = $"{RedisKeyPrefix}{token}";
        FallbackMemoryStore.TryRemove(key, out _);

        try
        {
            if (_redis.IsConnected)
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis からの loginuser 削除に失敗しました。");
        }
    }

    public string? ExtractToken(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue(HeaderName, out var headerVal) &&
            !string.IsNullOrWhiteSpace(headerVal))
        {
            return headerVal.ToString();
        }

        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieVal) &&
            !string.IsNullOrWhiteSpace(cookieVal))
        {
            return cookieVal;
        }

        return null;
    }
}
