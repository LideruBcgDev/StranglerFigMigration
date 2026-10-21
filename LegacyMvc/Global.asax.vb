Imports System
Imports System.Web
Imports System.Web.Mvc
Imports System.Web.Routing

Public Class MvcApplication
    Inherits HttpApplication

    Protected Sub Application_Start()
        AreaRegistration.RegisterAllAreas()
        RouteConfig.RegisterRoutes(RouteTable.Routes)
    End Sub

    Protected Sub Session_Start(sender As Object, e As EventArgs)
        ' InProc インメモリセッションの初期化
    End Sub

    Protected Sub Application_End()
    End Sub
End Class
