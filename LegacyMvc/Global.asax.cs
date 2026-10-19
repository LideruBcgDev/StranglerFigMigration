using System;
using System.Configuration;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using StackExchange.Redis;

namespace LegacyMvc
{
    public class MvcApplication : HttpApplication
    {
        // StackExchange.Redis の ConnectionMultiplexer をシングルトンで保持し、ModernBff と直接 KVS / セッション共有を行う
        private static Lazy<ConnectionMultiplexer> _lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            string connStr = ConfigurationManager.AppSettings["RedisConnectionString"] ?? "localhost:6379,abortConnect=false";
            return ConnectionMultiplexer.Connect(connStr);
        });

        public static ConnectionMultiplexer RedisConnection => _lazyConnection.Value;

        public static IDatabase GetRedisDatabase()
        {
            return RedisConnection.GetDatabase();
        }

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }

        protected void Session_Start(object sender, EventArgs e)
        {
            // セッション開始時に StackExchange.Redis にも共通セッション情報を書き込み（ModernBff との相互運用用）
            try
            {
                if (RedisConnection.IsConnected)
                {
                    var db = GetRedisDatabase();
                    string sessionId = Session.SessionID;
                    db.StringSet($"StranglerSession:SharedUser:{sessionId}", "LegacyAuthenticatedUser", TimeSpan.FromMinutes(30));
                }
            }
            catch
            {
                // Redis 未起動時でも動作検証を継続可能にするためのフォールバック
            }
        }

        protected void Application_End()
        {
            if (_lazyConnection.IsValueCreated)
            {
                _lazyConnection.Value.Dispose();
            }
        }
    }
}
