Imports System
Imports System.Collections.Generic
Imports System.Configuration
Imports System.Linq
Imports System.Text.Json
Imports System.Web
Imports System.Web.Security
Imports System.Web.UI
Imports System.Web.UI.WebControls
Imports Common.APP
Imports LegacyMvc.MDL.Data
Imports LegacyMvc.Models

Public Partial Class LegacyLogin
    Inherits Page

    Protected WithEvents txtUserId As TextBox
    Protected WithEvents txtPassword As TextBox
    Protected WithEvents btnLogin As Button
    Protected WithEvents lblMessage As Label
    Protected WithEvents btnYamada As Button
    Protected WithEvents btnSuzuki As Button
    Protected WithEvents btnSato As Button

    Protected Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load
    End Sub

    Protected Sub btnYamada_Click(sender As Object, e As EventArgs) Handles btnYamada.Click
        txtUserId.Text = "yamada"
        txtPassword.Text = "pass123"
    End Sub

    Protected Sub btnSuzuki_Click(sender As Object, e As EventArgs) Handles btnSuzuki.Click
        txtUserId.Text = "suzuki"
        txtPassword.Text = "pass123"
    End Sub

    Protected Sub btnSato_Click(sender As Object, e As EventArgs) Handles btnSato.Click
        txtUserId.Text = "sato"
        txtPassword.Text = "pass123"
    End Sub

    Protected Sub btnLogin_Click(sender As Object, e As EventArgs) Handles btnLogin.Click
        Dim uid As String = If(txtUserId.Text, String.Empty).Trim()
        Dim pwd As String = If(txtPassword.Text, String.Empty)

        If String.IsNullOrWhiteSpace(uid) Then
            lblMessage.Text = "ユーザIDを入力してください。"
            lblMessage.CssClass = "msg-error"
            Return
        End If

        Try
            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)
            Dim providerStatus As String = String.Empty

            Using db = LegacyStranglerDbContext.Create(configuredProvider, connStr, providerStatus)
                Dim userRecord = db.UsrTbl.FirstOrDefault(Function(u) u.UsrId.ToLower() = uid.ToLower())

                If userRecord Is Nothing Then
                    lblMessage.Text = $"USR_TBL にユーザ '{uid}' が存在しません。"
                    lblMessage.CssClass = "msg-error"
                    Return
                End If

                If Not userRecord.IsActive Then
                    lblMessage.Text = $"ユーザ '{userRecord.UsrNm}' は無効化されています。"
                    lblMessage.CssClass = "msg-error"
                    Return
                End If

                If userRecord.Password <> pwd Then
                    lblMessage.Text = "パスワードが正しくありません。"
                    lblMessage.CssClass = "msg-error"
                    Return
                End If

                ' 認証成功
                Dim roles = If(userRecord.RolesCsv, String.Empty).Split(","c).Select(Function(r) r.Trim()).Where(Function(r) r.Length > 0).ToList()
                Dim sessionToken As String = Guid.NewGuid().ToString("N")

                Dim loginUser As New LoginUser With {
                    .SessionToken = sessionToken,
                    .UserId = userRecord.UsrId,
                    .UserName = userRecord.UsrNm,
                    .OrganizationCode = userRecord.OrgCd,
                    .OrganizationName = userRecord.OrgNm,
                    .Roles = roles,
                    .AuthenticationMethod = "Password (LegacyMvc Web Forms USR_TBL)",
                    .IdentityProvider = "LegacyMvc LocalDB",
                    .AuthenticatedAtUtc = DateTime.UtcNow,
                    .IsAuthenticated = True,
                    .SourceChannel = "LegacyMvc Web Forms Login"
                }

                ' 1. レガシーセッションに保存
                Session("loginuser") = loginUser
                Session("UserId") = loginUser.UserId
                Session("UserName") = loginUser.UserName

                ' 2. レガシー Cookie (FormsAuthentication 等)
                Dim authCookie As New HttpCookie("LegacyAuthToken", sessionToken) With {
                    .Path = "/",
                    .HttpOnly = True
                }
                Response.Cookies.Add(authCookie)

                ' 3. WinForms WebView2 からのアクセス判定
                Dim isClientWebView As Boolean = String.Equals(Request.QueryString("clientWebView"), "true", StringComparison.OrdinalIgnoreCase)

                If isClientWebView Then
                    Dim payloadJson As String = System.Text.Json.JsonSerializer.Serialize(New With {
                        .loginUser = loginUser,
                        .message = "レガシーログイン成功",
                        .authChannel = "LegacyMvc Web Forms"
                    })

                    Dim script As String = $"<script>
if (window.chrome && window.chrome.webview) {{
    window.chrome.webview.postMessage(JSON.stringify({{
        type: 'LEGACY_LOGIN_SUCCESS',
        payload: {payloadJson}
    }}));
    window.chrome.webview.postMessage(JSON.stringify({{
        type: 'STRANGLER_LOGIN_SUCCESS',
        payload: {payloadJson}
    }}));
}}
</script>"
                    ClientScript.RegisterStartupScript(Me.GetType(), "NotifyWebView2", script)
                    lblMessage.Text = $"✓ ログイン成功 ({loginUser.UserName})。クライアントに通知中..."
                    lblMessage.CssClass = "msg-success"
                    Return
                End If

                ' 4. ブラウザの場合は LegacyPortal.aspx (または ReturnUrl) へ遷移
                Dim returnUrl As String = Request.QueryString("ReturnUrl")
                If Not String.IsNullOrWhiteSpace(returnUrl) AndAlso returnUrl.StartsWith("/") Then
                    Response.Redirect(returnUrl)
                Else
                    Response.Redirect("~/LegacyPortal.aspx")
                End If
            End Using
        Catch ex As Exception
            lblMessage.Text = "認証処理エラー: " & ex.Message
            lblMessage.CssClass = "msg-error"
        End Try
    End Sub

End Class