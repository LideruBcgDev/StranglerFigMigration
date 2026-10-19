using System.Text.Json.Serialization;

namespace ModernBff.Models;

/// <summary>
/// ModernBff と LegacyMvc 間で Redis を介して共有するログインユーザ情報 (loginuser)
/// </summary>
public class LoginUser
{
    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("organizationCode")]
    public string OrganizationCode { get; set; } = string.Empty;

    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// 認証方式: "Password (USR_TBL)" | "OIDC (OpenID Connect)" | "SAML2 (SAML 2.0 Assertion)"
    /// </summary>
    [JsonPropertyName("authenticationMethod")]
    public string AuthenticationMethod { get; set; } = "Password (USR_TBL)";

    [JsonPropertyName("identityProvider")]
    public string IdentityProvider { get; set; } = "LocalDB (USR_TBL)";

    [JsonPropertyName("authenticatedAtUtc")]
    public DateTime AuthenticatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LoginRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class SsoSimulateRequest
{
    /// <summary>
    /// "OIDC" または "SAML2"
    /// </summary>
    public string Protocol { get; set; } = "OIDC";
    /// <summary>
    /// IdP (Entra ID / Okta / ADFS 等) から返却された Subject / NameID (USR_TBL.USR_ID にマッピング)
    /// </summary>
    public string NameId { get; set; } = "yamada";
    public string? IdpIssuer { get; set; }
}
