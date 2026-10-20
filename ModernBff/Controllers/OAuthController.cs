using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModernBff.Data;
using ModernBff.Models;
using ModernBff.Services;

namespace ModernBff.Controllers;

/// <summary>
/// 【REST API (/api/.*) および MCP (/api/bff/mcp/.*) 用 OAuth 2.0 トークン発行コントローラー (/oauth/token)】
/// 1) client_credentials: API用 client_id / client_secret による連携システム用トークン
/// 2) password (パターンA): USR_TBL の ユーザーID + パスワード による AIツール / PowerShell 連携用 Bearer トークン
/// </summary>
[ApiController]
[Route("oauth")]
public class OAuthController : ControllerBase
{
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthController> _logger;

    public OAuthController(
        SharedLoginUserStore loginUserStore,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<OAuthController> logger)
    {
        _loginUserStore = loginUserStore;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// POST /oauth/token
    /// OAuth 2.0 に準拠したトークン発行エンドポイント。
    /// - grant_type = "client_credentials": client_id + client_secret 検証
    /// - grant_type = "password": USR_TBL の user_id (または username) + password 検証 (パターンA)
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> IssueToken()
    {
        string? clientId = null;
        string? clientSecret = null;
        string? username = null;
        string? password = null;
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
            if (string.IsNullOrWhiteSpace(username))
                username = !string.IsNullOrWhiteSpace(form["username"]) ? form["username"].ToString() : form["user_id"].ToString();
            if (string.IsNullOrWhiteSpace(password))
                password = form["password"].ToString();
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
                    if (string.IsNullOrWhiteSpace(username))
                        username = !string.IsNullOrWhiteSpace(bodyReq.Username) ? bodyReq.Username : bodyReq.UserId;
                    if (string.IsNullOrWhiteSpace(password))
                        password = bodyReq.Password;
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
        if (string.IsNullOrWhiteSpace(username))
            username = !string.IsNullOrWhiteSpace(Request.Query["username"]) ? Request.Query["username"].ToString() : Request.Query["user_id"].ToString();
        if (string.IsNullOrWhiteSpace(password))
            password = Request.Query["password"].ToString();
        if (!string.IsNullOrWhiteSpace(Request.Query["grant_type"]))
            grantType = Request.Query["grant_type"].ToString();

        return await HandleTokenGrantAsync(clientId, clientSecret, username, password, grantType);
    }

    /// <summary>
    /// GET /oauth/token
    /// テストやブラウザ、PowerShell からの簡易トークン発行確認用エンドポイント
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> IssueTokenViaGet(
        [FromQuery(Name = "grant_type")] string grantType = "client_credentials",
        [FromQuery(Name = "client_id")] string clientId = "strangler-rest-api-client",
        [FromQuery(Name = "client_secret")] string clientSecret = "strangler-rest-api-secret-2026",
        [FromQuery(Name = "user_id")] string? userId = null,
        [FromQuery(Name = "username")] string? username = null,
        [FromQuery(Name = "password")] string? password = null)
    {
        string? targetUser = !string.IsNullOrWhiteSpace(username) ? username : userId;
        return await HandleTokenGrantAsync(clientId, clientSecret, targetUser, password, grantType);
    }

    private async Task<IActionResult> HandleTokenGrantAsync(
        string? clientId,
        string? clientSecret,
        string? username,
        string? password,
        string grantType)
    {
        // -------------------------------------------------------------
        // パターンA: grant_type = "password" (USR_TBL 認証による Bearer トークン発行)
        // -------------------------------------------------------------
        if (string.Equals(grantType, "password", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return BadRequest(new
                {
                    error = "invalid_request",
                    error_description = "grant_type='password' (パターンA) では username (または user_id) と password の指定が必須です。"
                });
            }

            using var db = DatabaseConfigurationExtensions.CreateWorkingContextWithFallback(
                _serviceProvider,
                _configuration,
                _logger,
                out string dbProviderStatus);

            string uid = username.Trim().ToLower();
            var usrRecord = await db.UsrTbl
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UsrId.ToLower() == uid);

            if (usrRecord == null)
            {
                return Unauthorized(new
                {
                    error = "invalid_grant",
                    error_description = $"USR_TBL にユーザID '{username}' が存在しません。（検証用アカウント: yamada, suzuki, sato / パスワード: pass123）",
                    database_provider = dbProviderStatus
                });
            }

            if (!usrRecord.IsActive)
            {
                return Unauthorized(new
                {
                    error = "invalid_grant",
                    error_description = $"ユーザ '{usrRecord.UsrNm} ({usrRecord.UsrId})' は USR_TBL.IS_ACTIVE = false のため利用できません。"
                });
            }

            if (usrRecord.Password != password)
            {
                return Unauthorized(new
                {
                    error = "invalid_grant",
                    error_description = "USR_TBL のパスワードと一致しません。（検証用パスワード: pass123）"
                });
            }

            var ttl = TimeSpan.FromHours(1);
            var (tokenRecord, storedInRedis) = await _loginUserStore.IssueUserPasswordAccessTokenAsync(usrRecord, ttl);

            return Ok(new
            {
                access_token = tokenRecord.AccessToken,
                token_type = "Bearer",
                expires_in = (int)ttl.TotalSeconds,
                scope = string.Join(" ", tokenRecord.Scopes),
                grant_type = "password",
                auth_source = "Pattern A (USR_TBL verification)",
                database_provider = dbProviderStatus,
                user_id = tokenRecord.UserId,
                user_name = tokenRecord.UserName,
                organization_code = tokenRecord.OrganizationCode,
                organization_name = tokenRecord.OrganizationName,
                roles = tokenRecord.Roles,
                stored_in_redis = storedInRedis,
                usage_example = $"Authorization: Bearer {tokenRecord.AccessToken}"
            });
        }

        // -------------------------------------------------------------
        // grant_type = "client_credentials" (既存 Client ID/Secret 認証)
        // -------------------------------------------------------------
        var clients = _loginUserStore.GetOAuthApiClients();
        var matchedClient = clients.FirstOrDefault(c =>
            string.Equals(c.ClientId, clientId?.Trim(), StringComparison.Ordinal) &&
            string.Equals(c.ClientSecret, clientSecret?.Trim(), StringComparison.Ordinal));

        if (matchedClient == null)
        {
            return Unauthorized(new
            {
                error = "invalid_client",
                error_description = "API用の client_id または client_secret が不正です。（既定値: client_id='strangler-rest-api-client', client_secret='strangler-rest-api-secret-2026'。または grant_type='password' で USR_TBL 認証が可能です）"
            });
        }

        var clientTtl = TimeSpan.FromHours(1);
        var (cTokenRecord, cStoredInRedis) = await _loginUserStore.IssueOAuthAccessTokenAsync(matchedClient, clientTtl);

        return Ok(new
        {
            access_token = cTokenRecord.AccessToken,
            token_type = "Bearer",
            expires_in = (int)clientTtl.TotalSeconds,
            scope = string.Join(" ", cTokenRecord.Scopes),
            client_id = cTokenRecord.ClientId,
            client_name = cTokenRecord.ClientName,
            grant_type = grantType,
            stored_in_redis = cStoredInRedis,
            usage_example = $"Authorization: Bearer {cTokenRecord.AccessToken}"
        });
    }
}
