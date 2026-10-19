using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using ModernBff.Services;
using StackExchange.Redis;

namespace ModernBff.Controllers;

[ApiController]
[Route("api/bff/customer")]
public class BffCustomerController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly SharedLoginUserStore _loginUserStore;
    private readonly ILogger<BffCustomerController> _logger;

    public BffCustomerController(
        IHttpClientFactory httpClientFactory,
        IDistributedCache distributedCache,
        IConnectionMultiplexer redis,
        SharedLoginUserStore loginUserStore,
        ILogger<BffCustomerController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _distributedCache = distributedCache;
        _redis = redis;
        _loginUserStore = loginUserStore;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/bff/customer/{id}?useLegacyFallback=false
    /// ストラングラー・フィグ BFF エンドポイント:
    /// Redis から共有 loginuser（ユーザ・組織・権限）を復元し、
    /// useLegacyFallback=true の場合はトークンを引き継いで LegacyMvc を内部呼出します。
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCustomer(int id = 1, [FromQuery] bool useLegacyFallback = false)
    {
        // 1. 共有 loginuser の取得（Cookie または X-Strangler-AuthToken ヘッダー経由）
        string? token = _loginUserStore.ExtractToken(HttpContext);
        var (loginUser, loginUserSource) = await _loginUserStore.GetLoginUserAsync(token);

        string distributedSessionInfo = "RedisOffline";

        try
        {
            HttpContext.Session.SetString("LastBffCustomerId", id.ToString());
            await _distributedCache.SetStringAsync(
                $"BffCustomerAccess:{id}",
                DateTime.UtcNow.ToString("O"),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                });

            string? legacyLastAccess = null;
            if (_redis.IsConnected)
            {
                var db = _redis.GetDatabase();
                legacyLastAccess = await db.StringGetAsync($"StranglerShared:Customer:{id}:LastLegacyAccess");
            }

            distributedSessionInfo = $"BffSessionId={HttpContext.Session.Id}, LegacySharedTimestamp={legacyLastAccess ?? "None"}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis 分散セッションへのアクセスに失敗しました（フォールバック継続）");
            distributedSessionInfo = $"Fallback(NoRedis): {ex.Message}";
        }

        // パターンA: レガシー MVC を内部呼出 (トークンをヘッダーで引き継ぎ、LegacyMvc 側でも同じ loginuser を参照させる)
        if (useLegacyFallback)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("LegacyMvcClient");
                using var req = new HttpRequestMessage(HttpMethod.Get, $"LegacyData/GetCustomer?id={id}");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    req.Headers.TryAddWithoutValidation(SharedLoginUserStore.HeaderName, token);
                }

                var response = await client.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var legacyJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(legacyJson);

                    return Ok(new
                    {
                        Source = "ModernBff (.NET 10 Web API) -> Delegated to LegacyMvc",
                        Mode = "LegacyInternalCall",
                        CurrentLoginUser = loginUser,
                        LoginUserLookupSource = loginUserSource,
                        DistributedSessionInfo = distributedSessionInfo,
                        LegacyPayload = doc.RootElement.Clone(),
                        TimestampUtc = DateTime.UtcNow.ToString("O")
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LegacyMvc への内部呼出に失敗したため、BFF 側フォールバック応答を返します。");
                return Ok(new
                {
                    Source = "ModernBff (.NET 10 Web API) -> LegacyMvc Unreachable (Graceful Fallback)",
                    Mode = "LegacyInternalCallFallback",
                    CurrentLoginUser = loginUser,
                    LoginUserLookupSource = loginUserSource,
                    CustomerId = id,
                    CustomerName = $"株式会社レガシー商事 (ID: {id}) [BFF Fallback]",
                    DistributedSessionInfo = distributedSessionInfo,
                    ErrorDetail = ex.Message,
                    TimestampUtc = DateTime.UtcNow.ToString("O")
                });
            }
        }

        // パターンB: ModernBff の新ロジックを返却（loginuser の組織・権限に応じた表示制御も含む）
        bool canWriteCustomer = loginUser?.Roles.Contains("CustomerWrite") == true || loginUser?.Roles.Contains("Admin") == true;

        var modernCustomer = new
        {
            Source = "ModernBff (.NET 10 Web API - Modernized Logic)",
            Mode = "ModernNative",
            CurrentLoginUser = loginUser,
            LoginUserLookupSource = loginUserSource,
            AuthorizationCheck = new
            {
                IsAuthenticated = loginUser != null,
                AccessedByOrganization = loginUser?.OrganizationName ?? "未ログイン (Anonymous)",
                CanEditCustomer = canWriteCustomer
            },
            CustomerId = id,
            CustomerName = $"株式会社モダン・フィグ・テクノロジーズ (ID: {id})",
            Tier = "Platinum (Modernized Microservice)",
            Recommendations = new[] { "Cloud Migration Pack", "Zero-Downtime Strangler Routing" },
            DistributedSessionInfo = distributedSessionInfo,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        };

        return Ok(modernCustomer);
    }
}
