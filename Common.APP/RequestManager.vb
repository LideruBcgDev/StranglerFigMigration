Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic

Namespace Global.Common.APP

    ''' <summary>
    ''' req_code に紐づくリクエストコンテキスト情報
    ''' </summary>
    Public Class RequestContextEntry
        Public Property ReqCode As String = String.Empty
        Public Property AspxPage As String = String.Empty
        Public Property CommandName As String = String.Empty
        Public Property LoginUser As LoginUser
        Public Property Parameters As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Property CreatedAtUtc As DateTime = DateTime.UtcNow
        Public Property ExpiresAtUtc As DateTime = DateTime.UtcNow.AddMinutes(5)

        Public ReadOnly Property IsExpired As Boolean
            Get
                Return DateTime.UtcNow > ExpiresAtUtc
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 【RequestManager クラス】
    ''' コマンド実行から Web 画面 (.aspx) を呼び出す際、一意の "req_code" を発行し、
    ''' ログイン情報や実行パラメータを一時保持・ブラウザアクセス時に復元するためのマネージャークラスです。
    ''' </summary>
    Public NotInheritable Class RequestManager
        Private Shared ReadOnly Store As New ConcurrentDictionary(Of String, RequestContextEntry)(StringComparer.OrdinalIgnoreCase)

        Private Sub New()
        End Sub

        ''' <summary>
        ''' リクエスト情報を登録し、一意の req_code を発行します。
        ''' </summary>
        Public Shared Function RegisterRequest(
            aspxPage As String,
            Optional user As LoginUser = Nothing,
            Optional commandName As String = Nothing,
            Optional params As IDictionary(Of String, String) = Nothing,
            Optional ttlMinutes As Integer = 5) As RequestContextEntry

            Dim reqCode As String = "REQ-" & Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant()
            Dim entry As New RequestContextEntry With {
                .ReqCode = reqCode,
                .AspxPage = If(aspxPage, String.Empty),
                .CommandName = If(commandName, String.Empty),
                .LoginUser = user,
                .CreatedAtUtc = DateTime.UtcNow,
                .ExpiresAtUtc = DateTime.UtcNow.AddMinutes(ttlMinutes)
            }

            If params IsNot Nothing Then
                For Each kv In params
                    entry.Parameters(kv.Key) = kv.Value
                Next
            End If

            Store(reqCode) = entry
            CleanupExpired()
            Return entry
        End Function

        ''' <summary>
        ''' req_code に紐づくリクエスト情報を取得します (有効期限内のみ)。
        ''' </summary>
        Public Shared Function GetRequest(reqCode As String) As RequestContextEntry
            If String.IsNullOrWhiteSpace(reqCode) Then Return Nothing

            Dim entry As RequestContextEntry = Nothing
            If Store.TryGetValue(reqCode.Trim(), entry) Then
                If Not entry.IsExpired Then
                    Return entry
                Else
                    Store.TryRemove(reqCode.Trim(), Nothing)
                End If
            End If
            Return Nothing
        End Function

        ''' <summary>
        ''' req_code に紐づくリクエスト情報を取得し、ストアから消費（ワンタイム削除）します。
        ''' </summary>
        Public Shared Function ConsumeRequest(reqCode As String) As RequestContextEntry
            If String.IsNullOrWhiteSpace(reqCode) Then Return Nothing

            Dim entry As RequestContextEntry = Nothing
            If Store.TryRemove(reqCode.Trim(), entry) Then
                If Not entry.IsExpired Then
                    Return entry
                End If
            End If
            Return Nothing
        End Function

        Private Shared Sub CleanupExpired()
            Try
                Dim now = DateTime.UtcNow
                For Each kv In Store
                    If kv.Value.ExpiresAtUtc < now Then
                        Store.TryRemove(kv.Key, Nothing)
                    End If
                Next
            Catch
            End Try
        End Sub
    End Class

End Namespace
