Imports System
Imports System.Configuration
Imports System.IO
Imports System.Web.Mvc
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.MDL.Data
Imports LegacyMvc.Models
Imports LegacyMvc.PRM
Imports Newtonsoft.Json

Namespace Controllers

    Public Class ItemPostModel
        Public Property ItmCd As String
        Public Property ItmNm As String
        Public Property Category As String
        Public Property UnitPrice As Decimal
        Public Property StockQty As Integer
    End Class

    Public Class DrawingPostModel
        Public Property DrawingNo As String
        Public Property Title As String
        Public Property ItmCd As String
        Public Property BomNo As String
        Public Property Revision As String
    End Class

    Public Class LegacyDataController
        Inherits Controller

        ' GET: /LegacyData/GetCustomer?id=1
        <HttpGet>
        Public Function GetCustomer(Optional id As Integer = 1) As ActionResult
            Dim sessionValue As String = "RedisOffline"
            Dim loginUserLookupStatus As String = String.Empty
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
        ' レガシー側 品目(ITM_TBL) 検索 API
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

        ' POST: /LegacyData/RegisterItem
        ' レガシー側 品目(ITM_TBL) 登録・更新 API (Windows Forms から呼び出し)
        <HttpPost>
        Public Function RegisterItem() As ActionResult
            Dim body As String
            Using reader As New StreamReader(Request.InputStream)
                body = reader.ReadToEnd()
            End Using

            Dim model As ItemPostModel = If(
                Not String.IsNullOrWhiteSpace(body),
                JsonConvert.DeserializeObject(Of ItemPostModel)(body),
                New ItemPostModel With {
                    .ItmCd = Request("ItmCd"),
                    .ItmNm = Request("ItmNm"),
                    .Category = Request("Category"),
                    .UnitPrice = Decimal.Parse(If(Request("UnitPrice"), "0")),
                    .StockQty = Integer.Parse(If(Request("StockQty"), "0"))
                })

            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            Dim cmd As New LegacyItemQueryCommand()
            Dim saved = cmd.RegisterOrUpdate(configuredProvider, connStr, model.ItmCd, model.ItmNm, model.Category, model.UnitPrice, model.StockQty)

            Return Json(New With {
                .Message = "レガシー側 (LegacyMvc.MDL) で品目 (ITM_TBL) を登録・更新しました。",
                .SavedItem = saved
            })
        End Function

        ' GET: /LegacyData/GetDrawings?keyword=DRW
        ' レガシー側 図面(DRW_TBL) 検索 API
        <HttpGet>
        Public Function GetDrawings(Optional keyword As String = Nothing) As ActionResult
            Dim loginUserLookupStatus As String = String.Empty
            Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, loginUserLookupStatus)

            Dim cmd As New LegacyDrawingCrudCommand()
            Dim drawings = cmd.Search(keyword)

            Return Json(New With {
                .Source = "LegacyMvc (VB.NET) -> LegacyMvc.MDL (LegacyDrawingCrudCommand)",
                .Table = "DRW_TBL (図面マスタ)",
                .SharedLoginUser = loginUser,
                .DrawingCount = drawings.Count,
                .Drawings = drawings,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

        ' POST: /LegacyData/RegisterDrawing
        ' レガシー側 図面(DRW_TBL) 登録・更新 API (Webブラウザから呼び出し)
        <HttpPost>
        Public Function RegisterDrawing() As ActionResult
            Dim body As String
            Using reader As New StreamReader(Request.InputStream)
                body = reader.ReadToEnd()
            End Using

            Dim model As DrawingPostModel = If(
                Not String.IsNullOrWhiteSpace(body),
                JsonConvert.DeserializeObject(Of DrawingPostModel)(body),
                New DrawingPostModel With {
                    .DrawingNo = Request("DrawingNo"),
                    .Title = Request("Title"),
                    .ItmCd = Request("ItmCd"),
                    .BomNo = Request("BomNo"),
                    .Revision = Request("Revision")
                })

            Dim cmd As New LegacyDrawingCrudCommand()
            Dim saved = cmd.RegisterOrUpdate(model.DrawingNo, model.Title, model.ItmCd, model.BomNo, model.Revision)

            Return Json(New With {
                .Message = "レガシー側 (LegacyMvc.MDL) で図面 (DRW_TBL) を登録・更新しました。",
                .SavedDrawing = saved
            })
        End Function

    End Class

End Namespace
