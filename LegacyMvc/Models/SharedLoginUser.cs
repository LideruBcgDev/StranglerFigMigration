using System;
using System.Collections.Generic;
using System.Web;
using Newtonsoft.Json;

namespace LegacyMvc.Models
{
    /// <summary>
    /// ModernBff 側で発行され、Redis ("StranglerSession:LoginUser:{token}") を通じて
    /// LegacyMvc (.NET Framework 4.8.1) と共有されるログインユーザ情報 (loginuser)
    /// </summary>
    public class SharedLoginUser
    {
        public const string CookieName = "StranglerAuthToken";
        public const string HeaderName = "X-Strangler-AuthToken";
        public const string RedisKeyPrefix = "StranglerSession:LoginUser:";

        [JsonProperty("sessionToken")]
        public string SessionToken { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("userName")]
        public string UserName { get; set; }

        [JsonProperty("organizationCode")]
        public string OrganizationCode { get; set; }

        [JsonProperty("organizationName")]
        public string OrganizationName { get; set; }

        [JsonProperty("roles")]
        public List<string> Roles { get; set; } = new List<string>();

        [JsonProperty("authenticatedAtUtc")]
        public DateTime AuthenticatedAtUtc { get; set; }

        /// <summary>
        /// HTTP リクエストのヘッダーまたは Cookie からトークンを取り出し、
        /// StackExchange.Redis から共有 loginuser を復元します。
        /// </summary>
        public static SharedLoginUser ResolveFromRedis(HttpRequestBase request, out string lookupStatus)
        {
            string token = request.Headers[HeaderName];
            if (string.IsNullOrWhiteSpace(token))
            {
                HttpCookie cookie = request.Cookies[CookieName];
                if (cookie != null)
                {
                    token = cookie.Value;
                }
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                lookupStatus = "NoToken (未ログイン)";
                return null;
            }

            string redisKey = RedisKeyPrefix + token;

            try
            {
                if (MvcApplication.RedisConnection.IsConnected)
                {
                    var db = MvcApplication.GetRedisDatabase();
                    var json = db.StringGet(redisKey);
                    if (json.HasValue)
                    {
                        lookupStatus = $"LoadedFromRedis ({redisKey})";
                        return JsonConvert.DeserializeObject<SharedLoginUser>(json.ToString());
                    }
                    lookupStatus = $"RedisKeyNotFound ({redisKey})";
                    return null;
                }
                lookupStatus = "RedisDisconnected";
                return null;
            }
            catch (Exception ex)
            {
                lookupStatus = $"RedisError: {ex.Message}";
                return null;
            }
        }
    }
}
