using System.Collections.Concurrent;
using System.Text.Json;
using ModernBff.Models;

namespace ModernBff.Services;

/// <summary>
/// 【Minimal構成】ログインユーザ情報および OAuth Bearer トークンをインメモリで管理するストアサービス。
/// Redis は完全廃止し、スレッドセーフなインメモリストアで動作します。
/// </summary>
public class SharedLoginUserStore
{
    public const string CookieName = "StranglerAuthToken";
    public const string HeaderName = "X-Strangler-AuthToken";
    public const string KeyPrefix = "StranglerSession:LoginUser:";
    public const string OAuthKeyPrefix = "StranglerOAuth:AccessToken:";

    private readonly IConfiguration _configuration;
    private readonly ILogger<SharedLoginUserStore> _logger;
    private static readonly ConcurrentDictionary<string, string> MemoryStore = new();

    private bool? _overrideEnableNormalLogin;
    private bool? _overrideAutoRedirectToIdpOnLogin;
    private string? _overrideDefaultSsoProviderId;

    public SharedLoginUserStore(
        IConfiguration configuration,
        ILogger<SharedLoginUserStore> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool EnableNormalLogin =>
        _overrideEnableNormalLogin ?? _configuration.GetValue<bool>("Authentication:enableNormalLogin", true);

    public bool EnableRedisSession => false;

    public bool AutoRedirectToIdpOnLogin =>
        _overrideAutoRedirectToIdpOnLogin ?? _configuration.GetValue<bool>("Authentication:autoRedirectToIdpOnLogin", false);

    public string DefaultSsoProviderId =>
        _overrideDefaultSsoProviderId ?? (_configuration["Authentication:DefaultSsoProviderId"] ?? "oidc-entra-primary");

    public void UpdateRuntimeConfig(bool? enableNormalLogin, bool? autoRedirectToIdpOnLogin, string? defaultSsoProviderId = null)
    {
        if (enableNormalLogin.HasValue) _overrideEnableNormalLogin = enableNormalLogin.Value;
        if (autoRedirectToIdpOnLogin.HasValue) _overrideAutoRedirectToIdpOnLogin = autoRedirectToIdpOnLogin.Value;
        if (!string.IsNullOrWhiteSpace(defaultSsoProviderId)) _overrideDefaultSsoProviderId = defaultSsoProviderId;
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
                    Issuer = "https://login.microsoftonline.com/common/v2.0"
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

    public Task<(bool StoredInRedis, string Key)> SaveLoginUserAsync(LoginUser user, TimeSpan ttl)
    {
        string key = $"{KeyPrefix}{user.SessionToken}";
        string json = JsonSerializer.Serialize(user);
        MemoryStore[key] = json;
        return Task.FromResult((false, key));
    }

    public Task<(LoginUser? User, string Source)> GetLoginUserAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<(LoginUser?, string)>((null, "NoToken"));
        }

        string key = $"{KeyPrefix}{token}";
        if (MemoryStore.TryGetValue(key, out var json))
        {
            var user = JsonSerializer.Deserialize<LoginUser>(json);
            return Task.FromResult((user, $"InMemory ({key})"));
        }

        return Task.FromResult<(LoginUser?, string)>((null, $"NotFound ({key})"));
    }

    public Task RemoveLoginUserAsync(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            string key = $"{KeyPrefix}{token}";
            MemoryStore.TryRemove(key, out _);
        }
        return Task.CompletedTask;
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
    // REST API (/api/.*) 用 OAuth 2.0 Client Credentials / Password Grant (/oauth/token)
    // ====================================================================================

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

    public Task<(OAuthAccessTokenRecord Record, bool StoredInRedis)> IssueOAuthAccessTokenAsync(
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

        string key = $"{OAuthKeyPrefix}{record.AccessToken}";
        MemoryStore[key] = JsonSerializer.Serialize(record);
        return Task.FromResult((record, false));
    }

    /// <summary>
    /// 【パターンA】USR_TBL ユーザー認証による OAuth 2.0 Password Grant Bearer トークン発行
    /// AIツールや PowerShell からの呼び出し用に、USR_TBL のユーザー情報・権限を保持した access_token を発行します。
    /// </summary>
    public Task<(OAuthAccessTokenRecord Record, bool StoredInRedis)> IssueUserPasswordAccessTokenAsync(
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

        string key = $"{OAuthKeyPrefix}{record.AccessToken}";
        MemoryStore[key] = JsonSerializer.Serialize(record);
        return Task.FromResult((record, false));
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

    public Task<(OAuthAccessTokenRecord? TokenRecord, string LookupSource)> ValidateBearerAccessTokenAsync(HttpContext httpContext)
    {
        string? bearerToken = ExtractBearerToken(httpContext);
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return Task.FromResult<(OAuthAccessTokenRecord?, string)>((null, "Missing Authorization: Bearer <access_token> header"));
        }

        string key = $"{OAuthKeyPrefix}{bearerToken}";
        if (MemoryStore.TryGetValue(key, out var json))
        {
            var rec = JsonSerializer.Deserialize<OAuthAccessTokenRecord>(json);
            if (rec != null && rec.ExpiresAtUtc > DateTime.UtcNow)
            {
                return Task.FromResult<(OAuthAccessTokenRecord?, string)>((rec, $"Valid OAuth2 Bearer Token in Memory ({key})"));
            }
        }

        return Task.FromResult<(OAuthAccessTokenRecord?, string)>((null, "Invalid or expired Bearer access_token"));
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
