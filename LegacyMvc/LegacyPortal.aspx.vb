Imports System
Imports System.Configuration
Imports System.Web.UI
Imports System.Web.UI.WebControls
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
        ' 1. Redis 共有セッションから loginuser (ユーザ・組織・権限) を復元
        Dim lookupStatus As String = String.Empty
        Dim loginUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(New System.Web.HttpRequestWrapper(Request), lookupStatus)

        If loginUser IsNot Nothing Then
            lblLoginUserInfo.Text = $"ログイン中: {loginUser.UserName} ({loginUser.UserId}) | 組織: [{loginUser.OrganizationCode}] {loginUser.OrganizationName} | 権限: {String.Join(", ", loginUser.Roles)} ({lookupStatus})"
        Else
            lblLoginUserInfo.Text = $"未ログイン ({lookupStatus}) - /login からログインすると .aspx 画面でも同じ loginuser が共有表示されます。"
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
