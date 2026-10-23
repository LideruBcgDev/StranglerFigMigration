Imports System
Imports System.Security.Principal
Imports System.Web
Imports System.Web.Mvc
Imports System.Web.Routing
Imports Common.APP
Imports Newtonsoft.Json

Public Class MvcApplication
    Inherits HttpApplication

    Protected Sub Application_Start()
        AreaRegistration.RegisterAllAreas()
        RouteConfig.RegisterRoutes(RouteTable.Routes)
    End Sub

    Protected Sub Application_PostAuthenticateRequest(sender As Object, e As EventArgs)
        ' モダン側 (ModernBff / YARP) から転送された認証ユーザーヘッダーを読み取り、ASP.NET の User (IIdentity) を設定
        Dim req = HttpContext.Current?.Request
        If req Is Nothing Then Return

        Dim userId As String = req.Headers("X-Strangler-User-Id")
        If Not String.IsNullOrWhiteSpace(userId) Then
            Dim rolesStr As String = If(req.Headers("X-Strangler-Roles"), String.Empty)
            Dim roles As String() = rolesStr.Split(New Char() {","c}, StringSplitOptions.RemoveEmptyEntries)

            Dim identity As New GenericIdentity(userId, "ModernBffProxyAuth")
            HttpContext.Current.User = New GenericPrincipal(identity, roles)
        End If
    End Sub

    Protected Sub Application_AcquireRequestState(sender As Object, e As EventArgs)
        ' セッションが利用可能になった段階で、モダン側ヘッダーから Session("loginuser") を自動復元
        Dim context = HttpContext.Current
        If context Is Nothing OrElse context.Session Is Nothing Then Return

        ' 既にセッションに格納済みの場合はスキップ
        If context.Session("loginuser") IsNot Nothing Then Return

        Dim req = context.Request
        Dim rawLoginUserJson As String = req.Headers("X-Strangler-LoginUser")
        If Not String.IsNullOrWhiteSpace(rawLoginUserJson) Then
            Try
                Dim userJson As String = Uri.UnescapeDataString(rawLoginUserJson)
                Dim loginUser As LoginUser = JsonConvert.DeserializeObject(Of LoginUser)(userJson)
                If loginUser IsNot Nothing Then
                    loginUser.SourceChannel = "ModernBff: YARP Injected Header (X-Strangler-LoginUser)"
                    context.Session("loginuser") = loginUser
                    context.Session("UserId") = loginUser.UserId
                    context.Session("UserName") = loginUser.UserName
                    Return
                End If
            Catch
            End Try
        End If

        Dim userId As String = req.Headers("X-Strangler-User-Id")
        If Not String.IsNullOrWhiteSpace(userId) Then
            Dim loginUser As New LoginUser With {
                .UserId = userId,
                .UserName = Uri.UnescapeDataString(If(req.Headers("X-Strangler-User-Name"), userId)),
                .OrganizationCode = If(req.Headers("X-Strangler-Org-Code"), "SALES-01"),
                .OrganizationName = Uri.UnescapeDataString(If(req.Headers("X-Strangler-Org-Name"), "営業本部")),
                .Roles = If(req.Headers("X-Strangler-Roles"), "").Split(New Char() {","c}, StringSplitOptions.RemoveEmptyEntries).ToList(),
                .AuthenticationMethod = "ModernBff-YARP-Proxy",
                .IsAuthenticated = True,
                .SourceChannel = "ModernBff: YARP Injected Headers"
            }
            context.Session("loginuser") = loginUser
            context.Session("UserId") = loginUser.UserId
            context.Session("UserName") = loginUser.UserName
        End If
    End Sub

    Protected Sub Session_Start(sender As Object, e As EventArgs)
        ' InProc インメモリセッション
    End Sub

    Protected Sub Application_End()
    End Sub
End Class
