Imports System

Namespace Global.Common.APP

    ''' <summary>
    ''' 【独自リダイレクト例外 (RedirectException)】
    ''' コマンド呼び出し先に Web 画面 (.aspx) が指定されていた場合、
    ''' StranglerHttpClientUtility がこの例外を発生させ、クライアント側 (WinFormsClient) で
    ''' Process.Start(ex.RedirectUrl) によるブラウザ起動を行うための例外クラスです。
    ''' </summary>
    Public Class RedirectException
        Inherits Exception

        Public Property RedirectUrl As String = String.Empty
        Public Property ReqCode As String = String.Empty
        Public Property AspxPage As String = String.Empty
        Public Property CommandName As String = String.Empty

        Public Sub New()
            MyBase.New("Web 画面 (.aspx) へのリダイレクトが要求されました。")
        End Sub

        Public Sub New(redirectUrl As String, reqCode As String, aspxPage As String, Optional commandName As String = Nothing, Optional message As String = Nothing)
            MyBase.New(If(message, $"Web 画面 ({aspxPage}) へのリダイレクト要求を受信しました。RedirectUrl={redirectUrl}, ReqCode={reqCode}"))
            Me.RedirectUrl = redirectUrl
            Me.ReqCode = reqCode
            Me.AspxPage = aspxPage
            Me.CommandName = If(commandName, String.Empty)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub
    End Class

End Namespace
