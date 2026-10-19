using System;
using System.Configuration;
using System.Web.Mvc;
using LegacyMvc.MDL.Commands;
using LegacyMvc.MDL.Data;
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
        // 実際の検索処理は LegacyMvc.MDL.Data.LegacyItemQueryCommand で実行
        [HttpGet]
        public ActionResult GetItems(string itmCd = null)
        {
            SharedLoginUser loginUser = SharedLoginUser.ResolveFromRedis(Request, out string loginUserLookupStatus);

            string configuredProvider = ConfigurationManager.AppSettings["DatabaseProvider"] ?? "SqlServer";
            string connStr = ConfigurationManager.ConnectionStrings[configuredProvider]?.ConnectionString ?? string.Empty;

            var queryCmd = new LegacyItemQueryCommand();
            var (items, dbProviderStatus) = queryCmd.Execute(configuredProvider, connStr, itmCd);

            var responseData = new
            {
                Source = "LegacyMvc -> LegacyMvc.MDL (LegacyItemQueryCommand + EF Core)",
                Table = "ITM_TBL (品目マスタ)",
                ConfiguredProvider = configuredProvider,
                DatabaseProviderStatus = dbProviderStatus,
                SharedLoginUser = loginUser,
                LoginUserLookupStatus = loginUserLookupStatus,
                ItemCount = items.Count,
                Items = items,
                LegacyBoms = LegacyApprovalStore.GetAllBoms(),
                LegacyDrawings = LegacyApprovalStore.GetAllDrawings(),
                TimestampUtc = DateTime.UtcNow.ToString("O")
            };

            return Json(responseData, JsonRequestBehavior.AllowGet);
        }
    }
}
