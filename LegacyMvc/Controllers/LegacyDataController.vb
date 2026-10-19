Imports System
Imports System.Configuration
Imports System.Web.Mvc
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.MDL.Data
Imports LegacyMvc.Models

Namespace Controllers

    Public Class LegacyDataController
        Inherits Controller

        ' GET: /LegacyData/GetCustomer?id=1
        ' YARP 経由: /legacy-api/LegacyData/GetCustomer?id=1
        <HttpGet>
        Public Function GetCustomer(Optional id As Integer = 1) As ActionResult
            Dim sessionValue As String = "RedisOffline"
            Dim loginUserLookupStatus As String = String.Empty

            ' ModernBff の Web ログイン画面 (USR_TBL 認証) で確立された共有 loginuser（ユーザ・組織・権限）を Redis から取得
            Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, loginUserLookupStatus)

            Try
                Session("LastAccessedCustomerId") = id
                Session("LastAccessedAt") = DateTime.UtcNow.ToString("O")
                If loginUser IsNot Nothing Then
                    Session("loginuser") = loginUser
                End If

                If MvcApplication.RedisConnection.IsConnected Then
                    Dim db = MvcApplication.GetRedisDatabase()
                    Dim sharedKey As String = $"StranglerShared:Customer:{id}:LastLegacyAccess"
                    Dim accessedBy As String = If(loginUser IsNot Nothing, $"{loginUser.UserName} ({loginUser.OrganizationName})", "Anonymous")
                    Dim valueToStore As String = $"{DateTime.UtcNow:O} by {accessedBy}"
                    db.StringSet(sharedKey, valueToStore, TimeSpan.FromMinutes(30))
                    sessionValue = $"SessionID={Session.SessionID}, RedisKey={sharedKey}"
                End If
            Catch ex As Exception
                sessionValue = $"Fallback(NoRedis): {ex.Message}"
            End Try

            Dim customerData = New With {
                .Source = "LegacyMvc (.NET Framework 4.8.1 ASP.NET MVC - VB.NET)",
                .CustomerId = id,
                .CustomerName = $"株式会社レガシー商事 (ID: {id})",
                .Tier = "Gold (Legacy Master)",
                .SharedLoginUser = loginUser,
                .LoginUserLookupStatus = loginUserLookupStatus,
                .SharedSessionStatus = sessionValue,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }

            Return Json(customerData, JsonRequestBehavior.AllowGet)
        End Function

        ' GET: /LegacyData/GetItems?itmCd=ITM-001
        ' YARP 経由: /legacy-api/LegacyData/GetItems?itmCd=ITM-001
        ' 実際の検索処理は LegacyMvc.MDL (VB.NET) の LegacyItemQueryCommand で実行
        <HttpGet>
        Public Function GetItems(Optional itmCd As String = Nothing) As ActionResult
            Dim loginUserLookupStatus As String = String.Empty
            Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, loginUserLookupStatus)

            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            Dim queryCmd As New LegacyItemQueryCommand()
            Dim queryResult As LegacyItemQueryResult = queryCmd.Execute(configuredProvider, connStr, itmCd)

            Dim responseData = New With {
                .Source = "LegacyMvc (VB.NET) -> LegacyMvc.MDL (VB.NET LegacyItemQueryCommand + EF Core)",
                .Table = "ITM_TBL (品目マスタ)",
                .ConfiguredProvider = configuredProvider,
                .DatabaseProviderStatus = queryResult.ProviderStatus,
                .SharedLoginUser = loginUser,
                .LoginUserLookupStatus = loginUserLookupStatus,
                .ItemCount = queryResult.Items.Count,
                .Items = queryResult.Items,
                .LegacyBoms = LegacyApprovalStore.GetAllBoms(),
                .LegacyDrawings = LegacyApprovalStore.GetAllDrawings(),
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }

            Return Json(responseData, JsonRequestBehavior.AllowGet)
        End Function

    End Class

End Namespace
