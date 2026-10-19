Imports System
Imports System.Configuration
Imports System.IO
Imports System.Web.Mvc
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.MDL.Data
Imports LegacyMvc.Models
Imports Newtonsoft.Json

Namespace Controllers

    ''' <summary>
    ''' 【レガシー側 ClientController (RoutePrefix = "Client") - VB.NET】
    ''' Windows Forms クライアントからアクセスされる専用コントローラー (/Client/...)。
    ''' REST API (/api/.*) とは別に存在するクライアント専用エンドポイントです。
    ''' </summary>
    <RoutePrefix("Client")>
    Public Class ClientController
        Inherits Controller

        ' GET: /Client/items
        <HttpGet>
        <Route("items")>
        Public Function GetItems(Optional itmCd As String = Nothing) As ActionResult
            Dim lookupStatus As String = String.Empty
            Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)

            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            Dim cmd As New LegacyItemQueryCommand()
            Dim res As LegacyItemQueryResult = cmd.Execute(configuredProvider, connStr, itmCd)

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .AccessedFrom = "Windows Forms Client",
                .DatabaseProviderStatus = res.ProviderStatus,
                .SharedLoginUser = loginUser,
                .LoginUserLookupStatus = lookupStatus,
                .ItemCount = res.Items.Count,
                .Items = res.Items,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

        ' POST: /Client/items
        <HttpPost>
        <Route("items")>
        Public Function RegisterItem() As ActionResult
            Dim body As String
            Using reader As New StreamReader(Request.InputStream)
                body = reader.ReadToEnd()
            End Using

            Dim model As ItemPostModel = JsonConvert.DeserializeObject(Of ItemPostModel)(body)
            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            Dim cmd As New LegacyItemQueryCommand()
            Dim saved = cmd.RegisterOrUpdate(configuredProvider, connStr, model.ItmCd, model.ItmNm, model.Category, model.UnitPrice, model.StockQty)

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .Message = $"[Legacy ClientController] 品目 '{saved.ItmCd}' を ITM_TBL に登録・更新しました。",
                .SavedItem = saved
            })
        End Function

        ' GET: /Client/customer/{id}
        <HttpGet>
        <Route("customer/{id:int}")>
        Public Function GetCustomer(Optional id As Integer = 1) As ActionResult
            Dim lookupStatus As String = String.Empty
            Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .CustomerId = id,
                .CustomerName = $"株式会社レガシー商事 (ID: {id})",
                .SharedLoginUser = loginUser,
                .LoginUserLookupStatus = lookupStatus,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

    End Class

    ''' <summary>
    ''' 【レガシー側 ViewController (RoutePrefix = "View") - VB.NET】
    ''' Web ブラウザからアクセスされる専用コントローラー (/View/...)。
    ''' REST API (/api/.*) や ClientController (/Client/...) とは別に存在するブラウザ専用エンドポイントです。
    ''' </summary>
    <RoutePrefix("View")>
    Public Class ViewController
        Inherits Controller

        ' GET: /View/drawings
        <HttpGet>
        <Route("drawings")>
        Public Function GetDrawings(Optional keyword As String = Nothing) As ActionResult
            Dim lookupStatus As String = String.Empty
            Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)

            Dim cmd As New LegacyDrawingCrudCommand()
            Dim list = cmd.Search(keyword)

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .AccessedFrom = "Web Browser",
                .SharedLoginUser = loginUser,
                .LoginUserLookupStatus = lookupStatus,
                .DrawingCount = list.Count,
                .Drawings = list,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

        ' POST: /View/drawings
        <HttpPost>
        <Route("drawings")>
        Public Function RegisterDrawing() As ActionResult
            Dim body As String
            Using reader As New StreamReader(Request.InputStream)
                body = reader.ReadToEnd()
            End Using

            Dim model As DrawingPostModel = JsonConvert.DeserializeObject(Of DrawingPostModel)(body)
            Dim cmd As New LegacyDrawingCrudCommand()
            Dim saved = cmd.RegisterOrUpdate(model.DrawingNo, model.Title, model.ItmCd, model.BomNo, model.Revision)

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .Message = $"[Legacy ViewController] 図面 '{saved.DrawingNo}' を DRW_TBL に登録・更新しました。",
                .SavedDrawing = saved
            })
        End Function

    End Class

End Namespace
