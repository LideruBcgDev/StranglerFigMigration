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
    /// StranglerFig:EnableRedisSession (False の場合は Redis セッション共有をバイパスし、インメモリのみで動作)
    /// </summary>
    public bool EnableRedisSession =>
        _configuration.GetValue<bool>("StranglerFig:EnableRedisSession", true);

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

        if (!EnableRedisSession)
        {
            return (false, key);
        }

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

        if (EnableRedisSession)
        {
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

        if (!EnableRedisSession) return;

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

    // ====================================================================================
    // REST API (/api/.*) 用 OAuth 2.0 Client Credentials (/oauth/token) & Bearer 認証管理
    // ====================================================================================
    public const string OAuthRedisKeyPrefix = "StranglerOAuth:AccessToken:";

    public List<OAuthApiClientConfig> GetOAuthApiClients()
    {
        var clients = _configuration.GetSection("Authentication:OAuthApiClients").Get<List<OAuthApiClientConfig>>();
        if (clients == null || clients.Count == 0)
        {
            clients = new List<OAuthApiClientConfig>
            {
                new()
                {
                    ClientId = "strangler-rest-api-client",
                    ClientSecret = "strangler-rest-api-secret-2026",
                    ClientName = "外部システム連携用 REST API クライアント",
                    Scopes = new List<string> { "api.read", "api.write" }
                }
            };
        }
        return clients;
    }

    public async Task<(OAuthAccessTokenRecord Record, bool StoredInRedis)> IssueOAuthAccessTokenAsync(
        OAuthApiClientConfig client,
        TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        var record = new OAuthAccessTokenRecord
        {
            AccessToken = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9." + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            TokenType = "Bearer",
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            Scopes = client.Scopes,
            AuthMethod = "OAuth2 Client Credentials",
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(ttl)
        };

        string key = $"{OAuthRedisKeyPrefix}{record.AccessToken}";
        string json = JsonSerializer.Serialize(record);
        FallbackMemoryStore[key] = json;

        if (EnableRedisSession)
        {
            try
            {
                if (_redis.IsConnected)
                {
                    var db = _redis.GetDatabase();
                    await db.StringSetAsync(key, json, ttl);
                    return (record, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis への OAuth access_token 保存に失敗しました。インメモリフォールバックを使用します。");
            }
        }

        return (record, false);
    }

    /// <summary>
    /// 【パターンA】USR_TBL ユーザー認証による OAuth 2.0 Password Grant Bearer トークン発行
    /// AIツールや PowerShell からの呼び出し用に、USR_TBL のユーザー情報・権限を保持した access_token を発行します。
    /// </summary>
    public async Task<(OAuthAccessTokenRecord Record, bool StoredInRedis)> IssueUserPasswordAccessTokenAsync(
        ModernBff.MDL.Data.UsrTblEntity usrRecord,
        TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        var roles = (usrRecord.RolesCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var record = new OAuthAccessTokenRecord
        {
            AccessToken = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9." + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            TokenType = "Bearer",
            ClientId = "usr_tbl:" + usrRecord.UsrId,
            ClientName = usrRecord.UsrNm,
            UserId = usrRecord.UsrId,
            UserName = usrRecord.UsrNm,
            OrganizationCode = usrRecord.OrgCd,
            OrganizationName = usrRecord.OrgNm,
            Roles = roles,
            Scopes = roles,
            AuthMethod = "Password (USR_TBL Pattern A)",
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(ttl)
        };

        string key = $"{OAuthRedisKeyPrefix}{record.AccessToken}";
        string json = JsonSerializer.Serialize(record);
        FallbackMemoryStore[key] = json;

        if (EnableRedisSession)
        {
            try
            {
                if (_redis.IsConnected)
                {
                    var db = _redis.GetDatabase();
                    await db.StringSetAsync(key, json, ttl);
                    return (record, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis への OAuth access_token 保存に失敗しました。インメモリフォールバックを使用します。");
            }
        }

        return (record, false);
    }

    public string? ExtractBearerToken(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string raw = authHeader.ToString().Trim();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return raw.Substring("Bearer ".Length).Trim();
            }
        }
        return null;
    }

    public async Task<(OAuthAccessTokenRecord? TokenRecord, string LookupSource)> ValidateBearerAccessTokenAsync(HttpContext httpContext)
    {
        string? bearerToken = ExtractBearerToken(httpContext);
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return (null, "Missing Authorization: Bearer <access_token> header");
        }

        string key = $"{OAuthRedisKeyPrefix}{bearerToken}";

        if (EnableRedisSession)
        {
            try
            {
                if (_redis.IsConnected)
                {
                    var db = _redis.GetDatabase();
                    var val = await db.StringGetAsync(key);
                    if (val.HasValue)
                    {
                        var rec = JsonSerializer.Deserialize<OAuthAccessTokenRecord>(val.ToString());
                        if (rec != null && rec.ExpiresAtUtc > DateTime.UtcNow)
                        {
                            return (rec, $"Valid OAuth2 Bearer Token in Redis ({key})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis からの OAuth access_token 検証に失敗しました。");
            }
        }

        if (FallbackMemoryStore.TryGetValue(key, out var fallbackJson))
        {
            var rec = JsonSerializer.Deserialize<OAuthAccessTokenRecord>(fallbackJson);
            if (rec != null && rec.ExpiresAtUtc > DateTime.UtcNow)
            {
                return (rec, $"Valid OAuth2 Bearer Token in MemoryFallback ({key})");
            }
        }

        return (null, "Invalid or expired Bearer access_token");
    }

    /// <summary>
    /// HttpContext からログインユーザーを解決します。
    /// 1. まず Authorization: Bearer &lt;access_token&gt; ヘッダーを検証（REST API / MCP / PowerShell 用）
    /// 2. 次に StranglerAuthToken Cookie または X-Strangler-AuthToken ヘッダーを検証（従来のセッション用）
    /// 3. 見つからない場合は未ログインを返却
    /// </summary>
    public async Task<(LoginUser? User, string Source)> GetLoginUserFromContextAsync(HttpContext httpContext)
    {
        var (oauthToken, oauthSource) = await ValidateBearerAccessTokenAsync(httpContext);
        if (oauthToken != null)
        {
            return (oauthToken.ToLoginUser(), oauthSource);
        }

        string? token = ExtractToken(httpContext);
        return await GetLoginUserAsync(token);
    }
}

