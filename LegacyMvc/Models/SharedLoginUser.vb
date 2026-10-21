Imports System
Imports System.Runtime.InteropServices
Imports System.Web
Imports Common.APP

Namespace Models

    ''' <summary>
    ''' 【Minimal構成】Common.APP.LoginUser を継承した互換用クラス。
    ''' Redis は廃止されており、セッションまたはリクエスト情報からユーザーを解決します。
    ''' </summary>
    Public Class SharedLoginUser
        Inherits LoginUser

        Public Shared Function ResolveFromRedis(request As HttpRequestBase, <Out> ByRef lookupStatus As String) As SharedLoginUser
            lookupStatus = "RedisDisabled (MinimalMode)"
            Return Nothing
        End Function
    End Class

End Namespace
