Imports System
Imports System.Runtime.InteropServices
Imports System.Web
Imports Common.APP
Imports Newtonsoft.Json

Namespace Models

    ''' <summary>
    ''' Common.APP.LoginUser を継承し、LegacyMvc (.NET Framework 4.8.1 VB.NET) で
    ''' StackExchange.Redis から共有セッションを復元するヘルパーメソッドを提供するクラス。
    ''' </summary>
    Public Class SharedLoginUser
        Inherits LoginUser

        ''' <summary>
        ''' HTTP リクエストのヘッダーまたは Cookie からトークンを取り出し、
        ''' StackExchange.Redis から共有 loginuser (Common.APP.LoginUser) を復元します。
        ''' </summary>
        Public Shared Function ResolveFromRedis(request As HttpRequestBase, <Out> ByRef lookupStatus As String) As SharedLoginUser
            Dim token As String = request.Headers(HeaderName)
            If String.IsNullOrWhiteSpace(token) Then
                Dim cookie As HttpCookie = request.Cookies(CookieName)
                If cookie IsNot Nothing Then
                    token = cookie.Value
                End If
            End If

            If String.IsNullOrWhiteSpace(token) Then
                lookupStatus = "NoToken (未ログイン)"
                Return Nothing
            End If

            Dim redisKey As String = RedisKeyPrefix & token

            Try
                If MvcApplication.RedisConnection.IsConnected Then
                    Dim db = MvcApplication.GetRedisDatabase()
                    Dim json = db.StringGet(redisKey)
                    If json.HasValue Then
                        lookupStatus = $"LoadedFromRedis ({redisKey})"
                        Return JsonConvert.DeserializeObject(Of SharedLoginUser)(json.ToString())
                    End If
                    lookupStatus = $"RedisKeyNotFound ({redisKey})"
                    Return Nothing
                End If
                lookupStatus = "RedisDisconnected"
                Return Nothing
            Catch ex As Exception
                lookupStatus = $"RedisError: {ex.Message}"
                Return Nothing
            End Try
        End Function
    End Class

End Namespace
