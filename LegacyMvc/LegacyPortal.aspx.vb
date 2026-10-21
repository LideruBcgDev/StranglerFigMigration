Imports System
Imports System.Configuration
Imports System.Web.UI
Imports System.Web.UI.WebControls
Imports Common.APP
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.MDL.Data
Imports LegacyMvc.Models
Imports LegacyMvc.PRM

Public Partial Class LegacyPortal
    Inherits Page

    Protected WithEvents lblLoginUserInfo As Label
    Protected WithEvents lblDbProvider As Label
    Protected WithEvents lblServerTime As Label
    Protected WithEvents txtDrawingNo As TextBox
    Protected WithEvents txtTitle As TextBox
    Protected WithEvents txtItmCd As TextBox
    Protected WithEvents txtBomNo As TextBox
    Protected WithEvents txtRevision As TextBox
    Protected WithEvents btnRegisterDrawing As Button
    Protected WithEvents lblPostBackMessage As Label
    Protected WithEvents txtSearchKeyword As TextBox
    Protected WithEvents btnSearchDrawing As Button
    Protected WithEvents gvDrawings As GridView
    Protected WithEvents gvItems As GridView

    Protected Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load
        ' 1. URL の req_code から RequestManager 経由でログイン情報 (LoginUser) を復元
        Dim reqCode As String = Request.QueryString("req_code")
        Dim reqContext As RequestContextEntry = Nothing

        If Not String.IsNullOrWhiteSpace(reqCode) Then
            reqContext = RequestManager.GetRequest(reqCode)
            If reqContext IsNot Nothing AndAlso reqContext.LoginUser IsNot Nothing Then
                Session("loginuser") = reqContext.LoginUser
                Session("UserId") = reqContext.LoginUser.UserId
                Session("UserName") = reqContext.LoginUser.UserName
            End If
        End If

        ' 2. セッションから loginuser を復元
        Dim sessionUser As LoginUser = TryCast(Session("loginuser"), LoginUser)

        If reqContext IsNot Nothing AndAlso reqContext.LoginUser IsNot Nothing Then
            Dim u = reqContext.LoginUser
            lblLoginUserInfo.Text = $"✓ req_code: [{reqCode}] より引継ログイン中: {u.UserName} ({u.UserId}) | 組織: [{u.OrganizationCode}] {u.OrganizationName} | 権限: {String.Join(", ", u.Roles)} (コマンド: {reqContext.CommandName})"
        ElseIf sessionUser IsNot Nothing Then
            lblLoginUserInfo.Text = $"セッションログイン中: {sessionUser.UserName} ({sessionUser.UserId}) | 組織: [{sessionUser.OrganizationCode}] {sessionUser.OrganizationName} | 権限: {String.Join(", ", sessionUser.Roles)} (ASP.NET InProc Session)"
        Else
            lblLoginUserInfo.Text = "未ログイン - WinForms クライアントからのコマンド起動で loginuser が自動引き継ぎされます。"
        End If

        lblServerTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

        If Not IsPostBack Then
            BindGrids(String.Empty)
        End If
    End Sub

    Protected Sub btnRegisterDrawing_Click(sender As Object, e As EventArgs) Handles btnRegisterDrawing.Click
        Try
            Dim cmd As New LegacyDrawingCrudCommand()
            Dim saved = cmd.RegisterOrUpdate(
                txtDrawingNo.Text,
                txtTitle.Text,
                txtItmCd.Text,
                txtBomNo.Text,
                txtRevision.Text)

            lblPostBackMessage.Text = $"✓ Web Forms PostBack で図面 '{saved.DrawingNo} ({saved.Title})' を DRW_TBL に登録しました！"
            BindGrids(txtSearchKeyword.Text)
        Catch ex As Exception
            lblPostBackMessage.Text = "エラー: " & ex.Message
        End Try
    End Sub

    Protected Sub btnSearchDrawing_Click(sender As Object, e As EventArgs) Handles btnSearchDrawing.Click
        BindGrids(txtSearchKeyword.Text)
    End Sub

    Private Sub BindGrids(keyword As String)
        Dim drwCmd As New LegacyDrawingCrudCommand()
        gvDrawings.DataSource = drwCmd.Search(keyword)
        gvDrawings.DataBind()

        Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
        Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
        Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

        Dim itemCmd As New LegacyItemQueryCommand()
        Dim itemResult = itemCmd.Execute(configuredProvider, connStr, keyword)
        lblDbProvider.Text = itemResult.ProviderStatus

        gvItems.DataSource = itemResult.Items
        gvItems.DataBind()
    End Sub

End Class
