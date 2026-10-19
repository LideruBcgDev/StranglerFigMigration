Imports System.Web.Mvc
Imports System.Web.Routing

Public Class RouteConfig
    Public Shared Sub RegisterRoutes(routes As RouteCollection)
        routes.IgnoreRoute("{resource}.axd/{*pathInfo}")

        routes.MapMvcAttributeRoutes()

        routes.MapRoute(
            name:="Default",
            url:="{controller}/{action}/{id}",
            defaults:=New With {.controller = "LegacyData", .action = "GetCustomer", .id = UrlParameter.Optional}
        )
    End Sub
End Class
