using System;
using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using LegacyMvc.Data;
using LegacyMvc.Models;
using Microsoft.EntityFrameworkCore;

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

            // ModernBff の Web ログイン画面 (USR_TBL 認証) で確立された共有 loginuser（ユーザ・組織・権限）を Redis から取得
            SharedLoginUser loginUser = SharedLoginUser.ResolveFromRedis(Request, out string loginUserLookupStatus);

            try
            {
                Session["LastAccessedCustomerId"] = id;
                Session["LastAccessedAt"] = DateTime.UtcNow.ToString("O");
                if (loginUser != null)
                {
                    Session["loginuser"] = loginUser;
                }

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

        // GET: /LegacyData/GetItems?itmCd=ITM-001
        // YARP 経由: /legacy-api/LegacyData/GetItems?itmCd=ITM-001
        [HttpGet]
        public ActionResult GetItems(string itmCd = null)
        {
            SharedLoginUser loginUser = SharedLoginUser.ResolveFromRedis(Request, out string loginUserLookupStatus);

            using (var db = LegacyStranglerDbContext.CreateFromWebConfig(out string dbProviderStatus))
            {
                var query = db.ItmTbl.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(itmCd))
                {
                    string code = itmCd.Trim();
                    query = query.Where(i => i.ItmCd.ToLower() == code.ToLower());
                }

                var items = query.OrderBy(i => i.ItmCd).ToList();

                var responseData = new
                {
                    Source = "LegacyMvc (.NET Framework 4.8.1 ASP.NET MVC + EF Core)",
                    Table = "ITM_TBL (品目マスタ)",
                    ConfiguredProvider = ConfigurationManager.AppSettings["DatabaseProvider"] ?? "SqlServer",
                    DatabaseProviderStatus = dbProviderStatus,
                    SharedLoginUser = loginUser,
                    LoginUserLookupStatus = loginUserLookupStatus,
                    ItemCount = items.Count,
                    Items = items,
                    TimestampUtc = DateTime.UtcNow.ToString("O")
                };

                return Json(responseData, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
