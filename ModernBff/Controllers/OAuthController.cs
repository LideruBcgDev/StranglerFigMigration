using System.Text;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// 【REST API (/api/.*) 用 OAuth 2.0 トークン発行コントローラー (/oauth/token)】
/// 外部連携・バッチ等が REST API (/api/*) を呼び出す前に、API 用の ID (client_id) と Secret (client_secret) を指定して
/// Bearer access_token を取得するためのエンドポイントです。
/// ※ ViewController (/View/*) や ClientController (/Client/*) は人間のログインユーザセッション (Cookie / X-Strangler-AuthToken) を使用するため、
///    この /oauth/token (API用 ID/Secret) は REST API (/api/.*) 専用です。
/// </summary>
[ApiController]
[Route("oauth")]
public class OAuthController : ControllerBase
{
    private readonly SharedLoginUserStore _loginUserStore;

    public OAuthController(SharedLoginUserStore loginUserStore)
    {
        _loginUserStore = loginUserStore;
    }

    /// <summary>
    /// POST /oauth/token
    /// OAuth 2.0 Client Credentials Grant により client_id と client_secret を検証し、access_token (Bearer) を発行します。
    /// Form-UrlEncoded / JSON / Basic認証ヘッダー のすべてに対応しています。
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> IssueToken()
    {
        string? clientId = null;
        string? clientSecret = null;
        string grantType = "client_credentials";

        // 1. Authorization: Basic ヘッダーからの client_id:client_secret 抽出
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string raw = authHeader.ToString().Trim();
            if (raw.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(raw.Substring("Basic ".Length).Trim()));
                    int idx = decoded.IndexOf(':');
                    if (idx > 0)
                    {
                        clientId = decoded.Substring(0, idx);
                        clientSecret = decoded.Substring(idx + 1);
                    }
                }
                catch
                {
                }
            }
        }

        // 2. Form-UrlEncoded からの抽出
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            if (string.IsNullOrWhiteSpace(clientId))
                clientId = form["client_id"].ToString();
            if (string.IsNullOrWhiteSpace(clientSecret))
                clientSecret = form["client_secret"].ToString();
            if (!string.IsNullOrWhiteSpace(form["grant_type"]))
                grantType = form["grant_type"].ToString();
        }
        else
        {
            // 3. JSON ボディからの抽出
            try
            {
                var bodyReq = await Request.ReadFromJsonAsync<OAuthTokenRequest>();
                if (bodyReq != null)
                {
                    if (string.IsNullOrWhiteSpace(clientId))
                        clientId = bodyReq.ClientId;
                    if (string.IsNullOrWhiteSpace(clientSecret))
                        clientSecret = bodyReq.ClientSecret;
                    if (!string.IsNullOrWhiteSpace(bodyReq.GrantType))
                        grantType = bodyReq.GrantType;
                }
            }
            catch
            {
            }
        }

        // 4. クエリパラメータからの抽出 (フォールバック)
        if (string.IsNullOrWhiteSpace(clientId))
            clientId = Request.Query["client_id"].ToString();
        if (string.IsNullOrWhiteSpace(clientSecret))
            clientSecret = Request.Query["client_secret"].ToString();

        return await ValidateAndIssueTokenAsync(clientId, clientSecret, grantType);
    }

    /// <summary>
    /// GET /oauth/token?client_id=strangler-rest-api-client&client_secret=strangler-rest-api-secret-2026
    /// WinFormsClient のコンボボックスやブラウザからも簡単に /oauth/token の発行確認ができる GET エンドポイント
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> IssueTokenViaGet(
        [FromQuery(Name = "client_id")] string clientId = "strangler-rest-api-client",
        [FromQuery(Name = "client_secret")] string clientSecret = "strangler-rest-api-secret-2026",
        [FromQuery(Name = "grant_type")] string grantType = "client_credentials")
    {
        return await ValidateAndIssueTokenAsync(clientId, clientSecret, grantType);
    }

    private async Task<IActionResult> ValidateAndIssueTokenAsync(string? clientId, string? clientSecret, string grantType)
    {
        var clients = _loginUserStore.GetOAuthApiClients();
        var matchedClient = clients.FirstOrDefault(c =>
            string.Equals(c.ClientId, clientId?.Trim(), StringComparison.Ordinal) &&
            string.Equals(c.ClientSecret, clientSecret?.Trim(), StringComparison.Ordinal));

        if (matchedClient == null)
        {
            return Unauthorized(new
            {
                error = "invalid_client",
                error_description = "API用の client_id または client_secret が不正です。（既定値: client_id='strangler-rest-api-client', client_secret='strangler-rest-api-secret-2026'）"
            });
        }

        var ttl = TimeSpan.FromHours(1);
        var (tokenRecord, storedInRedis) = await _loginUserStore.IssueOAuthAccessTokenAsync(matchedClient, ttl);

        return Ok(new
        {
            access_token = tokenRecord.AccessToken,
            token_type = "Bearer",
            expires_in = (int)ttl.TotalSeconds,
            scope = string.Join(" ", tokenRecord.Scopes),
            client_id = tokenRecord.ClientId,
            client_name = tokenRecord.ClientName,
            grant_type = grantType,
            stored_in_redis = storedInRedis,
            usage_example = $"Authorization: Bearer {tokenRecord.AccessToken}"
        });
    }
}
