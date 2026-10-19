Imports System.Web.Mvc
Imports System.Web.Routing

Public Class RouteConfig
    Public Shared Sub RegisterRoutes(routes As RouteCollection)
        routes.IgnoreRoute("{resource}.axd/{*pathInfo}")
        ' Web Forms (.aspx) ページへの直接アクセスは MVC ルーティングから除外し、ASP.NET PageHandler に処理させる
        routes.IgnoreRoute("{*allaspx}", New With {.allaspx = ".*\.aspx(/.*)?"})

        routes.MapMvcAttributeRoutes()

        routes.MapRoute(
            name:="Default",
            url:="{controller}/{action}/{id}",
            defaults:=New With {.controller = "LegacyData", .action = "GetCustomer", .id = UrlParameter.Optional}
        )
    End Sub
End Class
