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

    [JsonPropertyName("authenticatedAtUtc")]
    public DateTime AuthenticatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LoginRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
