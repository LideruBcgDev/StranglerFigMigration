using System;
using System.Web.Mvc;
using LegacyMvc.Models;

namespace LegacyMvc.Controllers
{
    public class LegacyDataController : Controller
    {
        // GET: /LegacyData/GetCustomer?id=1
        // YARP 経由: /legacy-api/LegacyData/GetCustomer?id=1
        [HttpGet]
        public ActionResult GetCustomer(int id = 1)
        {
            string sessionValue = "RedisOffline";

            // ModernBff の Web ログイン画面で確立された共有 loginuser（ユーザ・組織・権限）を Redis から取得
            SharedLoginUser loginUser = SharedLoginUser.ResolveFromRedis(Request, out string loginUserLookupStatus);

            try
            {
                // ASP.NET SessionState の更新および loginuser の格納
                Session["LastAccessedCustomerId"] = id;
                Session["LastAccessedAt"] = DateTime.UtcNow.ToString("O");
                if (loginUser != null)
                {
                    Session["loginuser"] = loginUser;
                }

                // StackExchange.Redis 経由で ModernBff と共有可能なキーにも保存
                if (MvcApplication.RedisConnection.IsConnected)
                {
                    var db = MvcApplication.GetRedisDatabase();
                    string sharedKey = $"StranglerShared:Customer:{id}:LastLegacyAccess";
                    string accessedBy = loginUser != null
                        ? $"{loginUser.UserName} ({loginUser.OrganizationName})"
                        : "Anonymous";
                    string valueToStore = $"{DateTime.UtcNow:O} by {accessedBy}";
                    db.StringSet(sharedKey, valueToStore, TimeSpan.FromMinutes(30));
                    sessionValue = $"SessionID={Session.SessionID}, RedisKey={sharedKey}";
                }
            }
            catch (Exception ex)
            {
                sessionValue = $"Fallback(NoRedis): {ex.Message}";
            }

            var customerData = new
            {
                Source = "LegacyMvc (.NET Framework 4.8.1 ASP.NET MVC)",
                CustomerId = id,
                CustomerName = $"株式会社レガシー商事 (ID: {id})",
                Tier = "Gold (Legacy Master)",
                SharedLoginUser = loginUser,
                LoginUserLookupStatus = loginUserLookupStatus,
                SharedSessionStatus = sessionValue,
                TimestampUtc = DateTime.UtcNow.ToString("O")
            };

            return Json(customerData, JsonRequestBehavior.AllowGet);
        }
    }
}
