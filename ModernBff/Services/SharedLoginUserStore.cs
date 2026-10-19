using System.Collections.Concurrent;
using System.Text.Json;
using ModernBff.Models;
using StackExchange.Redis;

namespace ModernBff.Services;

/// <summary>
/// loginuser 情報を Redis (Key: "StranglerSession:LoginUser:{token}") に保存・取得するサービス。
/// LegacyMvc (.NET Framework 4.8.1) からも同一キー・同一 JSON 構造で直接読み取り可能。
/// Redis 未起動時でも動作検証できるようインメモリフォールバックを備えています。
/// </summary>
public class SharedLoginUserStore
{
    public const string CookieName = "StranglerAuthToken";
    public const string HeaderName = "X-Strangler-AuthToken";
    public const string RedisKeyPrefix = "StranglerSession:LoginUser:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SharedLoginUserStore> _logger;
    private static readonly ConcurrentDictionary<string, string> FallbackMemoryStore = new();

    public SharedLoginUserStore(IConnectionMultiplexer redis, ILogger<SharedLoginUserStore> logger)
    {
        _redis = redis;
        _logger = logger;
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
