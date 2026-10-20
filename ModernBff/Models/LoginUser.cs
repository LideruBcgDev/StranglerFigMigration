using System.Text.Json.Serialization;

namespace ModernBff.Models;

/// <summary>
/// ModernBff と LegacyMvc・WinFormsClient 間で共有するログインユーザ情報 (Common.APP.LoginUser を継承)
/// </summary>
public class LoginUser : Common.APP.LoginUser
{
}

public class LoginRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public class SsoSimulateRequest
{
    /// <summary>
    /// 登録済 IdP プロバイダID (例: "oidc-entra-primary", "saml2-enterprise", "oidc-partner-tenant")
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// "OIDC" または "SAML2"
    /// </summary>
    public string Protocol { get; set; } = "OIDC";

    /// <summary>
    /// IdP (Entra ID / Okta / ADFS 等) から返却された Subject / NameID (USR_TBL.USR_ID にマッピング)
    /// </summary>
    public string NameId { get; set; } = "yamada";

    public string? IdpIssuer { get; set; }

    public string? ReturnUrl { get; set; }
}

public class IdpProviderConfig
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "OIDC";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;
}

public class AuthConfigUpdateDto
{
    [JsonPropertyName("enableNormalLogin")]
    public bool? EnableNormalLogin { get; set; }

    [JsonPropertyName("autoRedirectToIdpOnLogin")]
    public bool? AutoRedirectToIdpOnLogin { get; set; }

    [JsonPropertyName("defaultSsoProviderId")]
    public string? DefaultSsoProviderId { get; set; }
}

public class OAuthApiClientConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
}

public class OAuthTokenRequest
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = "client_credentials";

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

public class OAuthAccessTokenRecord
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("clientName")]
    public string ClientName { get; set; } = string.Empty;

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = new();

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("organizationCode")]
    public string? OrganizationCode { get; set; }

    [JsonPropertyName("organizationName")]
    public string? OrganizationName { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("authMethod")]
    public string AuthMethod { get; set; } = "OAuth2 Client Credentials";

    [JsonPropertyName("issuedAtUtc")]
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; }

    public LoginUser ToLoginUser()
    {
        return new LoginUser
        {
            SessionToken = AccessToken,
            UserId = !string.IsNullOrWhiteSpace(UserId) ? UserId : (!string.IsNullOrWhiteSpace(ClientId) ? ClientId : "oauth_client"),
            UserName = !string.IsNullOrWhiteSpace(UserName) ? UserName : (!string.IsNullOrWhiteSpace(ClientName) ? ClientName : "OAuth API Client"),
            OrganizationCode = !string.IsNullOrWhiteSpace(OrganizationCode) ? OrganizationCode : "API-SYSTEM",
            OrganizationName = !string.IsNullOrWhiteSpace(OrganizationName) ? OrganizationName : "外部・AI連携システム",
            Roles = Roles != null && Roles.Count > 0 ? Roles : (Scopes != null && Scopes.Count > 0 ? Scopes : new List<string> { "ApiConsumer" }),
            AuthenticationMethod = AuthMethod,
            IdentityProvider = "ModernBff OAuth (USR_TBL Pattern A)",
            AuthenticatedAtUtc = IssuedAtUtc,
            IsAuthenticated = true,
            SourceChannel = $"OAuth2 Bearer Token ({AuthMethod})"
        };
    }
}

