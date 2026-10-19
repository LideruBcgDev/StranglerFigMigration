Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text.Json

Public Class Form1
    Inherits Form

    Private Shared ReadOnly GatewayBaseUrl As String = LoadGatewayBaseUrl()
    Private Const AuthTokenHeaderName As String = "X-Strangler-AuthToken"
    Private Const AuthCookieName As String = "StranglerAuthToken"

    Private Shared ReadOnly SharedCookieContainer As New CookieContainer()
    Private Shared ReadOnly SharedHttpHandler As New HttpClientHandler() With {
        .CookieContainer = SharedCookieContainer,
        .UseCookies = True
    }
    Private Shared ReadOnly SharedHttpClient As New HttpClient(SharedHttpHandler) With {
        .BaseAddress = New Uri(GatewayBaseUrl),
        .Timeout = TimeSpan.FromSeconds(10)
    }

    Private Shared Function LoadGatewayBaseUrl() As String
        Try
            Dim settingsPath As String = Path.Combine(AppContext.BaseDirectory, "client-settings.json")
            If File.Exists(settingsPath) Then
                Dim json As String = File.ReadAllText(settingsPath)
                Using doc As JsonDocument = JsonDocument.Parse(json)
                    Dim prop As JsonElement
                    If doc.RootElement.TryGetProperty("GatewayBaseUrl", prop) AndAlso prop.ValueKind = JsonValueKind.String Then
                        Dim url As String = prop.GetString()
                        If Not String.IsNullOrWhiteSpace(url) Then
                            Return If(url.EndsWith("/"), url, url & "/")
                        End If
                    End If
                End Using
            End If
        Catch
        End Try
        Return "http://localhost:5000/"
    End Function

    Private _currentSessionToken As String = String.Empty

    Private pnlAuth As Panel
    Private lblLoginStatus As Label
    Private WithEvents btnOpenWebLogin As Button
    Private WithEvents btnLogout As Button

    Private pnlScreens As FlowLayoutPanel
    Private WithEvents btnOpenItemWinForms As Button
    Private WithEvents btnOpenDrawingWeb As Button
    Private WithEvents btnOpenProjectWeb As Button
    Private WithEvents btnOpenWorkflowWeb As Button

    Private lblEndpoint As Label
    Private WithEvents cmbRoute As ComboBox
    Private WithEvents btnFetchData As Button
    Private txtResult As TextBox

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Strangler Fig 移行検証クライアント (VB.NET WinForms + WebView2 共通ログイン)"
        Me.Size = New Size(880, 640)
        Me.StartPosition = FormStartPosition.CenterScreen

        ' 1. 上段: ログイン状態パネル
        pnlAuth = New Panel() With {
            .Location = New Point(16, 12),
            .Size = New Size(832, 44),
            .BackColor = Color.FromArgb(241, 245, 249),
            .BorderStyle = BorderStyle.FixedSingle,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        lblLoginStatus = New Label() With {
            .Text = "ログイン状態: 未ログイン (Webログインボタンから USR_TBL 認証で loginuser を取得してください)",
            .Location = New Point(10, 12),
            .Size = New Size(560, 20),
            .ForeColor = Color.FromArgb(51, 65, 85),
            .AutoEllipsis = True,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        btnOpenWebLogin = New Button() With {
            .Text = "Webログイン画面を開く",
            .Location = New Point(580, 7),
            .Size = New Size(155, 28),
            .BackColor = Color.FromArgb(37, 99, 235),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        btnLogout = New Button() With {
            .Text = "ログアウト",
            .Location = New Point(742, 7),
            .Size = New Size(80, 28),
            .Enabled = False,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        pnlAuth.Controls.Add(lblLoginStatus)
        pnlAuth.Controls.Add(btnOpenWebLogin)
        pnlAuth.Controls.Add(btnLogout)

        ' 2. 業務画面ランチャーパネル (レガシーWinForms品目管理 / レガシーWeb図面管理 / モダンWeb案件統合 / モダンWebワークフロー)
        pnlScreens = New FlowLayoutPanel() With {
            .Location = New Point(16, 64),
            .Size = New Size(832, 38),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        btnOpenItemWinForms = New Button() With {
            .Text = "[レガシー WinForms] 品目(ITM_TBL)管理",
            .Size = New Size(215, 32),
            .BackColor = Color.FromArgb(15, 118, 110),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenDrawingWeb = New Button() With {
            .Text = "[レガシー Web] 図面(DRW_TBL)管理",
            .Size = New Size(195, 32),
            .BackColor = Color.FromArgb(71, 85, 105),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenProjectWeb = New Button() With {
            .Text = "[モダン Web] 案件統合(PRJ_TBL)管理",
            .Size = New Size(200, 32),
            .BackColor = Color.FromArgb(29, 78, 216),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenWorkflowWeb = New Button() With {
            .Text = "[モダン Web] ワークフロー(申請→照査→承認)",
            .Size = New Size(200, 32),
            .BackColor = Color.FromArgb(109, 40, 217),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        pnlScreens.Controls.AddRange(New Control() {btnOpenItemWinForms, btnOpenDrawingWeb, btnOpenProjectWeb, btnOpenWorkflowWeb})

        ' 3. Gateway 経由の API 検証エリア
        lblEndpoint = New Label() With {
            .Text = "Gateway 経由エンドポイント:",
            .Location = New Point(16, 116),
            .AutoSize = True
        }

        cmbRoute = New ComboBox() With {
            .Location = New Point(185, 112),
            .Size = New Size(515, 28),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        cmbRoute.Items.AddRange(New Object() {
            "/api/bff/workflow/projects",
            "/api/bff/workflow/complete?wfId=WF-9001&simulateDrawingError=false",
            "/api/bff/workflow/complete?wfId=WF-9002&simulateDrawingError=true",
            "/api/bff/items",
            "/api/bff/items/drawings",
            "/legacy-api/LegacyData/GetItems",
            "/legacy-api/LegacyData/GetDrawings",
            "/api/bff/auth/users",
            "/api/bff/customer/1",
            "/api/bff/customer/1?useLegacyFallback=true",
            "/api/bff/auth/me",
            "/legacy-api/LegacyData/GetCustomer?id=1",
            "/api/bff/mcp/streamable?tool=get_customer_strangler&source=modern",
            "/api/bff/mcp/sse?oneShot=true&customerId=1",
            "/api/bff/mcp/bridge/compare",
            "/api/bff/mcp/bridge/invoke/check_permission?customerId=1"
        })
        cmbRoute.SelectedIndex = 0

        btnFetchData = New Button() With {
            .Text = "データ取得",
            .Location = New Point(713, 110),
            .Size = New Size(135, 30),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' 4. 下部: JSON 結果表示テキストボックス
        txtResult = New TextBox() With {
            .Location = New Point(16, 150),
            .Size = New Size(832, 435),
            .Multiline = True,
            .ScrollBars = ScrollBars.Both,
            .ReadOnly = True,
            .Font = New Font("Consolas", 10.0F),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        }

        Me.Controls.Add(pnlAuth)
        Me.Controls.Add(pnlScreens)
        Me.Controls.Add(lblEndpoint)
        Me.Controls.Add(cmbRoute)
        Me.Controls.Add(btnFetchData)
        Me.Controls.Add(txtResult)
    End Sub

    Private Sub btnOpenItemWinForms_Click(sender As Object, e As EventArgs) Handles btnOpenItemWinForms.Click
        Using itemForm As New ItemManagementForm(SharedHttpClient)
            itemForm.ShowDialog(Me)
        End Using
    End Sub

    Private Sub btnOpenDrawingWeb_Click(sender As Object, e As EventArgs) Handles btnOpenDrawingWeb.Click
        OpenBrowserUrl(GatewayBaseUrl.TrimEnd("/"c) & "/legacy-drawings")
    End Sub

    Private Sub btnOpenProjectWeb_Click(sender As Object, e As EventArgs) Handles btnOpenProjectWeb.Click
        OpenBrowserUrl(GatewayBaseUrl.TrimEnd("/"c) & "/projects")
    End Sub

    Private Sub btnOpenWorkflowWeb_Click(sender As Object, e As EventArgs) Handles btnOpenWorkflowWeb.Click
        OpenBrowserUrl(GatewayBaseUrl.TrimEnd("/"c) & "/workflows")
    End Sub

    Private Shared Sub OpenBrowserUrl(url As String)
        Try
            Process.Start(New ProcessStartInfo With {
                .FileName = url,
                .UseShellExecute = True
            })
        Catch ex As Exception
            MessageBox.Show("ブラウザの起動に失敗しました: " & ex.Message)
        End Try
    End Sub

    Private Sub btnOpenWebLogin_Click(sender As Object, e As EventArgs) Handles btnOpenWebLogin.Click
        Using loginForm As New WebLoginForm(GatewayBaseUrl)
            If loginForm.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrWhiteSpace(loginForm.SessionToken) Then
                _currentSessionToken = loginForm.SessionToken

                SharedHttpClient.DefaultRequestHeaders.Remove(AuthTokenHeaderName)
                SharedHttpClient.DefaultRequestHeaders.Add(AuthTokenHeaderName, _currentSessionToken)
                SharedCookieContainer.Add(New Uri(GatewayBaseUrl), New Cookie(AuthCookieName, _currentSessionToken, "/"))

                lblLoginStatus.Text = $"ログイン中: {loginForm.UserSummary}"
                lblLoginStatus.ForeColor = Color.FromArgb(5, 150, 105)
                btnLogout.Enabled = True

                txtResult.Text =
                    "=== Web ログイン成功 (WebView2 -> ModernBff USR_TBL 認証 -> Redis 共有セッション) ===" & Environment.NewLine &
                    $"loginuser : {loginForm.UserSummary}" & Environment.NewLine &
                    $"Token     : {_currentSessionToken}" & Environment.NewLine &
                    New String("-"c, 70) & Environment.NewLine &
                    loginForm.RawLoginResponseJson
            End If
        End Using
    End Sub

    Private Async Sub btnLogout_Click(sender As Object, e As EventArgs) Handles btnLogout.Click
        Try
            Await SharedHttpClient.PostAsync("api/bff/auth/logout", Nothing)
        Catch
        End Try

        _currentSessionToken = String.Empty
        SharedHttpClient.DefaultRequestHeaders.Remove(AuthTokenHeaderName)
        lblLoginStatus.Text = "ログイン状態: 未ログイン (ログアウトしました)"
        lblLoginStatus.ForeColor = Color.FromArgb(51, 65, 85)
        btnLogout.Enabled = False
        txtResult.Text = "ログアウトしました。Redis 上の共有 loginuser セッションを破棄しました。"
    End Sub

    Private Async Sub btnFetchData_Click(sender As Object, e As EventArgs) Handles btnFetchData.Click
        btnFetchData.Enabled = False
        Dim relativePath As String = If(cmbRoute.SelectedItem?.ToString(), "/api/bff/workflow/projects")

        txtResult.Text = $"Gateway ({GatewayBaseUrl.TrimEnd("/"c)}{relativePath}) へリクエスト送信中..." & Environment.NewLine

        Try
            Dim response As HttpResponseMessage = Await SharedHttpClient.GetAsync(relativePath.TrimStart("/"c))
            Dim rawJson As String = Await response.Content.ReadAsStringAsync()
            Dim formattedJson As String = FormatJsonSafely(rawJson)

            txtResult.Text =
                $"Status : {CInt(response.StatusCode)} ({response.ReasonPhrase})" & Environment.NewLine &
                $"URL    : {New Uri(SharedHttpClient.BaseAddress, relativePath.TrimStart("/"c))}" & Environment.NewLine &
                $"Token  : {If(String.IsNullOrEmpty(_currentSessionToken), "(未ログイン)", _currentSessionToken)}" & Environment.NewLine &
                $"Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}" & Environment.NewLine &
                New String("-"c, 70) & Environment.NewLine &
                formattedJson
        Catch ex As Exception
            txtResult.Text =
                "[通信エラー]" & Environment.NewLine &
                "YarpGateway (http://localhost:5000) および転送先サービスが起動しているか確認してください。" & Environment.NewLine &
                Environment.NewLine &
                ex.ToString()
        Finally
            btnFetchData.Enabled = True
        End Try
    End Sub

    Private Shared Function FormatJsonSafely(rawJson As String) As String
        Try
            Using doc As JsonDocument = JsonDocument.Parse(rawJson)
                Dim options As New JsonSerializerOptions With {
                    .WriteIndented = True
                }
                Return JsonSerializer.Serialize(doc.RootElement, options)
            End Using
        Catch
            Return rawJson
        End Try
    End Function

    <STAThread>
    Public Shared Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New Form1())
    End Sub
End Class
