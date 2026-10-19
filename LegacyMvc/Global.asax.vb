Imports System
Imports System.Configuration
Imports System.Web
Imports System.Web.Mvc
Imports System.Web.Routing
Imports StackExchange.Redis

Public Class MvcApplication
    Inherits HttpApplication

    ' StackExchange.Redis の ConnectionMultiplexer をシングルトンで保持し、ModernBff と直接 KVS / セッション共有を行う
    Private Shared ReadOnly _lazyConnection As New Lazy(Of ConnectionMultiplexer)(
        Function()
            Dim connStr As String = If(ConfigurationManager.AppSettings("RedisConnectionString"), "localhost:6379,abortConnect=false")
            Return ConnectionMultiplexer.Connect(connStr)
        End Function)

    Public Shared ReadOnly Property RedisConnection As ConnectionMultiplexer
        Get
            Return _lazyConnection.Value
        End Get
    End Property

    Public Shared Function GetRedisDatabase() As IDatabase
        Return RedisConnection.GetDatabase()
    End Function

    Protected Sub Application_Start()
        AreaRegistration.RegisterAllAreas()
        RouteConfig.RegisterRoutes(RouteTable.Routes)
    End Sub

    Protected Sub Session_Start(sender As Object, e As EventArgs)
        ' セッション開始時に StackExchange.Redis にも共通セッション情報を書き込み（ModernBff との相互運用用）
        Try
            If RedisConnection.IsConnected Then
                Dim db As IDatabase = GetRedisDatabase()
                Dim sessionId As String = Session.SessionID
                db.StringSet($"StranglerSession:SharedUser:{sessionId}", "LegacyAuthenticatedUser", TimeSpan.FromMinutes(30))
            End If
        Catch
            ' Redis 未起動時でも動作検証を継続可能にするためのフォールバック
        End Try
    End Sub

    Protected Sub Application_End()
        If _lazyConnection.IsValueCreated Then
            _lazyConnection.Value.Dispose()
        End If
    End Sub
End Class
