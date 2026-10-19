Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Web
Imports Newtonsoft.Json

Namespace Models

    ''' <summary>
    ''' ModernBff 側で発行され、Redis ("StranglerSession:LoginUser:{token}") を通じて
    ''' LegacyMvc (.NET Framework 4.8.1 VB.NET) と共有されるログインユーザ情報 (loginuser)
    ''' </summary>
    Public Class SharedLoginUser
        Public Const CookieName As String = "StranglerAuthToken"
        Public Const HeaderName As String = "X-Strangler-AuthToken"
        Public Const RedisKeyPrefix As String = "StranglerSession:LoginUser:"

        <JsonProperty("sessionToken")>
        Public Property SessionToken As String

        <JsonProperty("userId")>
        Public Property UserId As String

        <JsonProperty("userName")>
        Public Property UserName As String

        <JsonProperty("organizationCode")>
        Public Property OrganizationCode As String

        <JsonProperty("organizationName")>
        Public Property OrganizationName As String

        <JsonProperty("roles")>
        Public Property Roles As New List(Of String)()

        <JsonProperty("authenticatedAtUtc")>
        Public Property AuthenticatedAtUtc As DateTime

        ''' <summary>
        ''' HTTP リクエストのヘッダーまたは Cookie からトークンを取り出し、
        ''' StackExchange.Redis から共有 loginuser を復元します。
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
