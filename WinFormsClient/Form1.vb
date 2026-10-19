Imports System.Net
Imports System.Net.Http
Imports System.Text.Json

Public Class Form1
    Inherits Form

    Private Const GatewayBaseUrl As String = "http://localhost:5000/"
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

    Private _currentSessionToken As String = String.Empty

    Private pnlAuth As Panel
    Private lblLoginStatus As Label
    Private WithEvents btnOpenWebLogin As Button
    Private WithEvents btnLogout As Button

    Private lblEndpoint As Label
    Private WithEvents cmbRoute As ComboBox
    Private WithEvents btnFetchData As Button
    Private txtResult As TextBox

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "Strangler Fig 移行検証クライアント (VB.NET WinForms + WebView2 共通ログイン)"
        Me.Size = New Size(840, 580)
        Me.StartPosition = FormStartPosition.CenterScreen

        ' 上部: ログイン状態パネル (loginuser: ユーザ・組織・権限の表示と Webログイン起動ボタン)
        pnlAuth = New Panel() With {
            .Location = New Point(16, 12),
            .Size = New Size(792, 46),
            .BackColor = Color.FromArgb(241, 245, 249),
            .BorderStyle = BorderStyle.FixedSingle,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        lblLoginStatus = New Label() With {
            .Text = "ログイン状態: 未ログイン (Webログインボタンから loginuser を取得してください)",
            .Location = New Point(10, 13),
            .Size = New Size(520, 20),
            .ForeColor = Color.FromArgb(51, 65, 85),
            .AutoEllipsis = True,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        btnOpenWebLogin = New Button() With {
            .Text = "Webログイン画面を開く",
            .Location = New Point(540, 8),
            .Size = New Size(155, 28),
            .BackColor = Color.FromArgb(37, 99, 235),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        btnLogout = New Button() With {
            .Text = "ログアウト",
            .Location = New Point(702, 8),
            .Size = New Size(80, 28),
            .Enabled = False,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        pnlAuth.Controls.Add(lblLoginStatus)
        pnlAuth.Controls.Add(btnOpenWebLogin)
        pnlAuth.Controls.Add(btnLogout)

        ' 中段: Gateway 経由のデータ取得操作エリア
        lblEndpoint = New Label() With {
            .Text = "Gateway 経由エンドポイント:",
            .Location = New Point(16, 74),
            .AutoSize = True
        }

        cmbRoute = New ComboBox() With {
            .Location = New Point(185, 70),
            .Size = New Size(475, 28),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        cmbRoute.Items.AddRange(New Object() {
            "/api/bff/customer/1",
            "/api/bff/customer/1?useLegacyFallback=true",
            "/api/bff/auth/me",
            "/legacy-api/LegacyData/GetCustomer?id=1"
        })
        cmbRoute.SelectedIndex = 0

        btnFetchData = New Button() With {
            .Text = "データ取得",
            .Location = New Point(673, 68),
            .Size = New Size(135, 30),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' 下部: JSON 結果表示テキストボックス
        txtResult = New TextBox() With {
            .Location = New Point(16, 110),
            .Size = New Size(792, 415),
            .Multiline = True,
            .ScrollBars = ScrollBars.Both,
            .ReadOnly = True,
            .Font = New Font("Consolas", 10.0F),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        }

        Me.Controls.Add(pnlAuth)
        Me.Controls.Add(lblEndpoint)
        Me.Controls.Add(cmbRoute)
        Me.Controls.Add(btnFetchData)
        Me.Controls.Add(txtResult)
    End Sub

    ''' <summary>
    ''' WebView2 を使った Web ログイン画面 (/login) をモーダル表示し、
    ''' 認証成功時に loginuser（ユーザ・組織・権限）とセッショントークンを HttpClient に反映します。
    ''' </summary>
    Private Sub btnOpenWebLogin_Click(sender As Object, e As EventArgs) Handles btnOpenWebLogin.Click
        Using loginForm As New WebLoginForm(GatewayBaseUrl)
            If loginForm.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrWhiteSpace(loginForm.SessionToken) Then
                _currentSessionToken = loginForm.SessionToken

                ' HttpClient のヘッダーと CookieContainer の両方にセッショントークンをセット
                SharedHttpClient.DefaultRequestHeaders.Remove(AuthTokenHeaderName)
                SharedHttpClient.DefaultRequestHeaders.Add(AuthTokenHeaderName, _currentSessionToken)
                SharedCookieContainer.Add(New Uri(GatewayBaseUrl), New Cookie(AuthCookieName, _currentSessionToken, "/"))

                lblLoginStatus.Text = $"ログイン中: {loginForm.UserSummary}"
                lblLoginStatus.ForeColor = Color.FromArgb(5, 150, 105)
                btnLogout.Enabled = True

                txtResult.Text =
                    "=== Web ログイン成功 (WebView2 -> ModernBff -> Redis 共有セッション) ===" & Environment.NewLine &
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
            ' ネットワークエラー時もローカル状態はクリアする
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
        Dim relativePath As String = If(cmbRoute.SelectedItem?.ToString(), "/api/bff/customer/1")

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
