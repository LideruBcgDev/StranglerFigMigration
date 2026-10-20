Imports System.Text.Json
Imports Common.APP
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

''' <summary>
''' WebView2 を使用して Gateway 経由で ModernBff の Web ログイン画面 (/login) を表示し、
''' ログイン成功時に発行された Common.APP.LoginUser（ユーザ・組織・権限・セッショントークン）を受け取るダイアログ。
''' </summary>
Public Class WebLoginForm
    Inherits Form

    Private ReadOnly _loginUrl As String
    Private WithEvents webView As WebView2
    Private lblStatus As Label

    Public Property SessionToken As String = String.Empty
    Public Property UserSummary As String = String.Empty
    Public Property RawLoginResponseJson As String = String.Empty
    Public Property AuthenticatedUser As LoginUser = Nothing

    Public Sub New(gatewayBaseUrl As String)
        _loginUrl = gatewayBaseUrl.TrimEnd("/"c) & "/login?clientWebView=true"
        InitializeComponent("Web ログイン画面 (WebView2 - Gateway 経由 /login)")
    End Sub

    Public Sub New(loginUrl As String, title As String)
        _loginUrl = loginUrl
        InitializeComponent(title)
    End Sub

    Private Sub InitializeComponent(Optional windowTitle As String = "Web ログイン画面 (WebView2)")
        Me.Text = windowTitle
        Me.Size = New Size(620, 700)
        Me.StartPosition = FormStartPosition.CenterParent

        lblStatus = New Label() With {
            .Dock = DockStyle.Top,
            .Height = 28,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Padding = New Padding(8, 0, 0, 0),
            .BackColor = Color.FromArgb(30, 41, 59),
            .ForeColor = Color.White,
            .Text = $"接続先: {_loginUrl}"
        }

        webView = New WebView2() With {
            .Dock = DockStyle.Fill
        }

        Me.Controls.Add(webView)
        Me.Controls.Add(lblStatus)
    End Sub

    Protected Overrides Async Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Try
            Await webView.EnsureCoreWebView2Async(Nothing)
            AddHandler webView.CoreWebView2.WebMessageReceived, AddressOf OnWebMessageReceived
            webView.CoreWebView2.Navigate(_loginUrl)
        Catch ex As Exception
            MessageBox.Show(
                "WebView2 の初期化に失敗しました: " & ex.Message,
                "WebView2 エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub OnWebMessageReceived(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
        Try
            Dim msgJson As String = e.TryGetWebMessageAsString()
            If String.IsNullOrWhiteSpace(msgJson) Then Return

            Using doc As JsonDocument = JsonDocument.Parse(msgJson)
                Dim root As JsonElement = doc.RootElement
                Dim msgType As String = String.Empty
                Dim typeProp As JsonElement
                If root.TryGetProperty("type", typeProp) Then
                    msgType = If(typeProp.GetString(), String.Empty)
                End If

                If msgType = "STRANGLER_LOGIN_SUCCESS" OrElse msgType = "LEGACY_LOGIN_SUCCESS" Then
                    Dim payload As JsonElement = root.GetProperty("payload")
                    RawLoginResponseJson = JsonSerializer.Serialize(payload, New JsonSerializerOptions With {.WriteIndented = True})

                    Dim loginUserElem As JsonElement
                    If payload.TryGetProperty("loginUser", loginUserElem) OrElse payload.TryGetProperty("LoginUser", loginUserElem) Then
                        Dim token As String = GetJsonString(loginUserElem, "sessionToken")
                        Dim userId As String = GetJsonString(loginUserElem, "userId")
                        Dim userName As String = GetJsonString(loginUserElem, "userName")
                        Dim orgCode As String = GetJsonString(loginUserElem, "organizationCode")
                        Dim orgName As String = GetJsonString(loginUserElem, "organizationName")

                        Dim rolesList As New List(Of String)()
                        Dim rolesElem As JsonElement
                        If loginUserElem.TryGetProperty("roles", rolesElem) AndAlso rolesElem.ValueKind = JsonValueKind.Array Then
                            For Each r As JsonElement In rolesElem.EnumerateArray()
                                rolesList.Add(If(r.GetString(), String.Empty))
                            Next
                        End If

                        SessionToken = token
                        AuthenticatedUser = New LoginUser() With {
                            .SessionToken = token,
                            .UserId = userId,
                            .UserName = userName,
                            .OrganizationCode = orgCode,
                            .OrganizationName = orgName,
                            .Roles = rolesList,
                            .IsAuthenticated = True,
                            .SourceChannel = "WinForms WebView2 Login (Common.APP.LoginUser)"
                        }
                        UserSummary = AuthenticatedUser.ToSummaryString()
                    End If

                    Me.DialogResult = DialogResult.OK
                    Me.Close()
                End If
            End Using
        Catch ex As Exception
            lblStatus.Text = "メッセージ解析エラー: " & ex.Message
        End Try
    End Sub

    Private Shared Function GetJsonString(element As JsonElement, propName As String) As String
        Dim val As JsonElement
        If element.TryGetProperty(propName, val) AndAlso val.ValueKind = JsonValueKind.String Then
            Return If(val.GetString(), String.Empty)
        End If
        Return String.Empty
    End Function
End Class
