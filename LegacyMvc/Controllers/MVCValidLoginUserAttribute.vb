Imports System
Imports System.Web
Imports System.Web.Mvc
Imports Common.APP

Namespace Controllers

    ''' <summary>
    ''' 【レガシー既存仕様再現】MVCValidLoginUserAttribute
    ''' 画面・ViewController 実行前にログイン状態を検証するアクションフィルター属性。
    ''' 
    ''' 【動作仕様】
    ''' 1. HttpContext.Current?.User?.Identity?.IsAuthenticated が True の場合:
    '''    -> ModernBff (YARP) から転送された認証済みプリンシパル (GenericPrincipal) が
    '''       Global.asax.vb の Application_PostAuthenticateRequest で設定されているため、
    '''       何もしないで Exit Sub (正常通過) します。
    ''' 2. Session("loginuser") に有効な LoginUser が存在する場合:
    '''    -> Exit Sub (正常通過)
    ''' 3. 上記のいずれも満たさない (未ログイン) 場合:
    '''    -> モダン側のログイン画面 (/login?returnUrl=...) へリダイレクトします。
    ''' </summary>
    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method, Inherited:=True, AllowMultiple:=False)>
    Public Class MVCValidLoginUserAttribute
        Inherits ActionFilterAttribute

        Public Overrides Sub OnActionExecuting(filterContext As ActionExecutingContext)
            Dim httpContext = filterContext.HttpContext

            ' 1. User.Identity.IsAuthenticated の判定
            ' ModernBff からのプロキシ時は Global.asax.vb で GenericIdentity("yamada", "ModernBffProxyAuth") が
            ' 設定されるため、IsAuthenticated は常に True となり、そのまま Exit Sub します。
            If httpContext?.User?.Identity IsNot Nothing AndAlso httpContext.User.Identity.IsAuthenticated Then
                Exit Sub
            End If

            ' 2. Session("loginuser") のフォールバック判定
            If httpContext?.Session IsNot Nothing AndAlso httpContext.Session("loginuser") IsNot Nothing Then
                Exit Sub
            End If

            ' 3. 未ログイン時のリダイレクト処理
            Dim rawUrl As String = httpContext?.Request?.RawUrl
            If String.IsNullOrWhiteSpace(rawUrl) Then rawUrl = "/"

            ' モダン受付窓口 (ポート 5000 / HTTPS 443) の /login へリダイレクト
            Dim loginUrl As String = $"/login?returnUrl={Uri.EscapeDataString(rawUrl)}"
            filterContext.Result = New RedirectResult(loginUrl)
        End Sub
    End Class

End Namespace
