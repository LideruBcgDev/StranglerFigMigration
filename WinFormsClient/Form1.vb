Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text.Json
Imports Common.APP
Imports LegacyMvc.PRM
Imports ModernBff.PRM

Public Class Form1
    Inherits Form

    Private Shared ReadOnly GatewayBaseUrl As String = LoadGatewayBaseUrl()

    ' 共通プロジェクト Common.APP の HttpClient ユーティリティを使用
    Public Shared ReadOnly HttpUtil As New StranglerHttpClientUtility(GatewayBaseUrl)

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

    Private pnlAuth As Panel
    Private lblLoginStatus As Label
    Private WithEvents btnOpenWebLogin As Button
    Private WithEvents btnLogout As Button

    Private pnlScreens As FlowLayoutPanel
    Private WithEvents btnOpenItemWinForms As Button
    Private WithEvents btnExecItmAppCommand As Button
    Private WithEvents btnExecPrjSchCommand As Button
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
        Me.Text = "Strangler Fig 移行検証クライアント (Common.APP + LegacyMvc.PRM 共通参照対応)"
        Me.Size = New Size(940, 650)
        Me.StartPosition = FormStartPosition.CenterScreen

        ' 1. 上段: ログイン状態パネル
        pnlAuth = New Panel() With {
            .Location = New Point(16, 12),
            .Size = New Size(892, 44),
            .BackColor = Color.FromArgb(241, 245, 249),
            .BorderStyle = BorderStyle.FixedSingle,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        lblLoginStatus = New Label() With {
            .Text = "ログイン状態: 未ログイン (Webログインボタンから USR_TBL 認証で Common.APP.LoginUser を取得してください)",
            .Location = New Point(10, 12),
            .Size = New Size(620, 20),
            .ForeColor = Color.FromArgb(51, 65, 85),
            .AutoEllipsis = True,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        btnOpenWebLogin = New Button() With {
            .Text = "Webログイン画面を開く",
            .Location = New Point(640, 7),
            .Size = New Size(155, 28),
            .BackColor = Color.FromArgb(37, 99, 235),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        btnLogout = New Button() With {
            .Text = "ログアウト",
            .Location = New Point(802, 7),
            .Size = New Size(80, 28),
            .Enabled = False,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        pnlAuth.Controls.Add(lblLoginStatus)
        pnlAuth.Controls.Add(btnOpenWebLogin)
        pnlAuth.Controls.Add(btnLogout)

        ' 2. 業務画面ランチャー & コマンド名指定 ("ITM.APP") DirectCast 実行パネル
        pnlScreens = New FlowLayoutPanel() With {
            .Location = New Point(16, 64),
            .Size = New Size(892, 38),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        btnOpenItemWinForms = New Button() With {
            .Text = "[WinForms] 品目(ITM_TBL)",
            .Size = New Size(148, 32),
            .BackColor = Color.FromArgb(15, 118, 110),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnExecItmAppCommand = New Button() With {
            .Text = "[Legacy.PRM: ITM.APP] DirectCast",
            .Size = New Size(175, 32),
            .BackColor = Color.FromArgb(180, 83, 9),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnExecPrjSchCommand = New Button() With {
            .Text = "[Modern.PRM: PRJ.SCH] DirectCast",
            .Size = New Size(178, 32),
            .BackColor = Color.FromArgb(3, 105, 161),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenDrawingWeb = New Button() With {
            .Text = "[Web] 図面(DRW_TBL)",
            .Size = New Size(122, 32),
            .BackColor = Color.FromArgb(71, 85, 105),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenProjectWeb = New Button() With {
            .Text = "[Web] 案件統合",
            .Size = New Size(115, 32),
            .BackColor = Color.FromArgb(29, 78, 216),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenWorkflowWeb = New Button() With {
            .Text = "[Web] WF管理",
            .Size = New Size(115, 32),
            .BackColor = Color.FromArgb(109, 40, 217),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        pnlScreens.Controls.AddRange(New Control() {
            btnOpenItemWinForms,
            btnExecItmAppCommand,
            btnExecPrjSchCommand,
            btnOpenDrawingWeb,
            btnOpenProjectWeb,
            btnOpenWorkflowWeb
        })

        ' 3. Gateway 経由の API 検証エリア
        lblEndpoint = New Label() With {
            .Text = "Gateway 経由エンドポイント:",
            .Location = New Point(16, 116),
            .AutoSize = True
        }

        cmbRoute = New ComboBox() With {
            .Location = New Point(185, 112),
            .Size = New Size(575, 28),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        cmbRoute.Items.AddRange(New Object() {
            "/Client/execute?commandName=ITM.APP",
            "/Client/execute?commandName=PRJ.SCH",
            "/Client/execute?commandName=WF.APR",
            "/Client/commands",
            "/View/execute?commandName=ITM.APP",
            "/View/execute?commandName=PRJ.SCH",
            "/legacy-api/Client/execute?commandName=ITM.APP",
            "/legacy-api/View/execute?commandName=ITM.APP",
            "/Client/items",
            "/Client/items?loginUserId=blocked",
            "/Client/customer/1",
            "/View/drawings",
            "/View/drawings?loginUserId=blocked",
            "/View/projects",
            "/View/workflow/approve?wfId=WF-9001&simulateDrawingError=false",
            "/View/workflow/approve?wfId=WF-9002&simulateDrawingError=true",
            "/oauth/token?client_id=strangler-rest-api-client&client_secret=strangler-rest-api-secret-2026",
            "/api/status",
            "/api/items",
            "/api/drawings",
            "/api/projects",
            "/legacy-api/Client/items",
            "/legacy-api/View/drawings",
            "/legacy-api/LegacyPortal.aspx",
            "/api/bff/auth/providers",
            "/api/bff/auth/users",
            "/api/bff/auth/me",
            "/api/bff/mcp/streamable?tool=get_projects_and_workflows",
            "/api/bff/mcp/sse?oneShot=true&tool=get_projects_and_workflows",
            "/api/bff/mcp/bridge/invoke/get_projects_and_workflows"
        })
        cmbRoute.SelectedIndex = 0

        btnFetchData = New Button() With {
            .Text = "データ取得",
            .Location = New Point(773, 110),
            .Size = New Size(135, 30),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' 4. 下部: JSON 結果表示テキストボックス
        txtResult = New TextBox() With {
            .Location = New Point(16, 150),
            .Size = New Size(892, 445),
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

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        ' クライアント起動時、未ログインであれば強制的に Web ログイン画面 (WebLoginForm) へ遷移
        If String.IsNullOrWhiteSpace(HttpUtil.SessionToken) Then
            EnsureLoggedInOrPrompt()
        End If
    End Sub

    Private Function EnsureLoggedInOrPrompt() As Boolean
        If Not String.IsNullOrWhiteSpace(HttpUtil.SessionToken) Then
            Return True
        End If
        Return ShowWebLoginModal()
    End Function

    Private Function ShowWebLoginModal() As Boolean
        Using loginForm As New WebLoginForm(GatewayBaseUrl)
            If loginForm.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrWhiteSpace(loginForm.SessionToken) Then
                HttpUtil.SetLoginSession(loginForm.SessionToken, loginForm.AuthenticatedUser)

                lblLoginStatus.Text = $"ログイン中: {loginForm.UserSummary}"
                lblLoginStatus.ForeColor = Color.FromArgb(5, 150, 105)
                btnLogout.Enabled = True

                txtResult.Text =
                    "=== Web ログイン成功 (Common.APP.StranglerHttpClientUtility & Common.APP.LoginUser) ===" & Environment.NewLine &
                    $"loginuser : {loginForm.UserSummary}" & Environment.NewLine &
                    $"Token     : {HttpUtil.SessionToken}" & Environment.NewLine &
                    New String("-"c, 75) & Environment.NewLine &
                    loginForm.RawLoginResponseJson
                Return True
            End If
        End Using
        Return False
    End Function

    ''' <summary>
    ''' 【コマンド名指定 ("ITM.APP") + LegacyMvc.PRM.ItemAppOParam への DirectCast 検証】
    ''' &lt;command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1"&gt;
    ''' </summary>
    Private Async Sub btnExecItmAppCommand_Click(sender As Object, e As EventArgs) Handles btnExecItmAppCommand.Click
        If Not EnsureLoggedInOrPrompt() Then Return

        Try
            Dim iParam As New ItemAppIParam() With {
                .CommandId = "ITM.APP",
                .TargetItmCd = "ITM-001",
                .TargetBomNo = "BOM-2026-001",
                .TargetDrawingNo = "drw004",
                .ActionMode = "QUERY_AND_APPROVE"
            }

            ' Common.APP.StranglerHttpClientUtility を使ってコマンド名 "ITM.APP" で送信
            ' 返却された Object (OParamBase) を LegacyMvc.PRM.ItemAppOParam へ DirectCast する
            Dim rawOParam As Object = Await HttpUtil.ExecuteCommandAsync("ITM.APP", iParam, "Client/execute")
            Dim oParam As ItemAppOParam = DirectCast(rawOParam, ItemAppOParam)

            Dim firstBom As BomRecord = oParam.Boms.FirstOrDefault()
            Dim bomSummary As String = If(firstBom IsNot Nothing,
                $"{firstBom.BomNo} (ItmCd={firstBom.ItmCd}) -> Status={firstBom.ApprovalStatus}, By={firstBom.ApprovedBy}",
                "(なし)")

            Dim firstDrw As DrawingRecord = oParam.Drawings.FirstOrDefault()
            Dim drwSummary As String = If(firstDrw IsNot Nothing,
                $"{firstDrw.DrawingNo} ({firstDrw.Title}) -> Status={firstDrw.ApprovalStatus}, By={firstDrw.ApprovedBy}",
                "(なし)")

            txtResult.Text =
                "=== [成功] コマンド名 ""ITM.APP"" 実行 & LegacyMvc.PRM.ItemAppOParam への DirectCast 完了 ===" & Environment.NewLine &
                "Config 定義   : <command name=""ITM.APP"" command=""LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL"" db=""1"">" & Environment.NewLine &
                "通信クラス    : Common.APP.StranglerHttpClientUtility.ExecuteCommandAsync(""ITM.APP"", iParam)" & Environment.NewLine &
                "返却型キャスト: Dim oParam As ItemAppOParam = DirectCast(rawOParam, ItemAppOParam)" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                $"ExecutedCommandName  : {oParam.ExecutedCommandName}" & Environment.NewLine &
                $"DbNumberUsed         : {oParam.DbNumberUsed}" & Environment.NewLine &
                $"PermissionCheck      : {oParam.PermissionCheckResult}" & Environment.NewLine &
                $"SummaryMessage       : {oParam.SummaryMessage}" & Environment.NewLine &
                $"BomRecord            : {bomSummary}" & Environment.NewLine &
                $"DrawingRecord        : {drwSummary}" & Environment.NewLine &
                $"Items.Count          : {oParam.Items.Count} 件" & Environment.NewLine &
                $"Boms.Count           : {oParam.Boms.Count} 件" & Environment.NewLine &
                $"Drawings.Count       : {oParam.Drawings.Count} 件"
        Catch ex As Exception
            txtResult.Text = $"[ITM.APP 実行エラー]{Environment.NewLine}{ex}"
        End Try
    End Sub

    ''' <summary>
    ''' 【モダン側コマンド名指定 ("PRJ.SCH") + ModernBff.PRM.ProjectSearchOParam への DirectCast 検証】
    ''' &lt;CommandDefinition("PRJ.SCH", Db:=1)&gt; ModernBff.MDL.Commands.ProjectSearchCommand
    ''' </summary>
    Private Async Sub btnExecPrjSchCommand_Click(sender As Object, e As EventArgs) Handles btnExecPrjSchCommand.Click
        If Not EnsureLoggedInOrPrompt() Then Return

        Try
            Dim iParam As New ProjectSearchIParam() With {
                .CommandId = "PRJ.SCH",
                .Keyword = ""
            }

            ' Common.APP.StranglerHttpClientUtility を使ってモダンコマンド名 "PRJ.SCH" で送信し、
            ' 返却された Object (OParamBase) を ModernBff.PRM.ProjectSearchOParam へ DirectCast する
            Dim rawOParam As Object = Await HttpUtil.ExecuteCommandAsync("PRJ.SCH", iParam, "Client/execute")
            Dim oParam As ProjectSearchOParam = DirectCast(rawOParam, ProjectSearchOParam)

            Dim firstPrj As IntegratedProjectRecord = oParam.IntegratedProjects.FirstOrDefault()
            Dim prjSummary As String = If(firstPrj IsNot Nothing,
                $"{firstPrj.Project.PrjId} ({firstPrj.Project.PrjNm}) | 品目:{firstPrj.Project.TargetItmCd} | 図面:{firstPrj.Project.LinkedDrawingNo} | 状態:{firstPrj.Project.PrjStatus}",
                "(なし)")

            txtResult.Text =
                "=== [成功] モダンコマンド名 ""PRJ.SCH"" 実行 & ModernBff.PRM.ProjectSearchOParam への DirectCast 完了 ===" & Environment.NewLine &
                "コマンド定義  : <CommandDefinition(""PRJ.SCH"", Db:=1)> ModernBff.MDL.Commands.ProjectSearchCommand" & Environment.NewLine &
                "通信クラス    : Common.APP.StranglerHttpClientUtility.ExecuteCommandAsync(""PRJ.SCH"", iParam)" & Environment.NewLine &
                "返却型キャスト: Dim oParam As ProjectSearchOParam = DirectCast(rawOParam, ProjectSearchOParam)" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                $"ExecutedCommandName  : {oParam.ExecutedCommandName}" & Environment.NewLine &
                $"DbNumberUsed         : {oParam.DbNumberUsed}" & Environment.NewLine &
                $"PermissionCheck      : {oParam.PermissionCheckResult}" & Environment.NewLine &
                $"SummaryMessage       : {oParam.SummaryMessage}" & Environment.NewLine &
                $"先頭案件レコード     : {prjSummary}" & Environment.NewLine &
                $"IntegratedProjects   : {oParam.IntegratedProjects.Count} 件" & Environment.NewLine &
                $"AllWorkflows         : {oParam.AllWorkflows.Count} 件"
        Catch ex As Exception
            txtResult.Text = $"[PRJ.SCH 実行エラー]{Environment.NewLine}{ex}"
        End Try
    End Sub

    Private Sub btnOpenItemWinForms_Click(sender As Object, e As EventArgs) Handles btnOpenItemWinForms.Click
        If Not EnsureLoggedInOrPrompt() Then Return
        Using itemForm As New ItemManagementForm(HttpUtil)
            itemForm.ShowDialog(Me)
        End Using
    End Sub

    Private Sub btnOpenDrawingWeb_Click(sender As Object, e As EventArgs) Handles btnOpenDrawingWeb.Click
        OpenBrowserUrl(GatewayBaseUrl.TrimEnd("/"c) & "/web/view/drawings?drw_no='drw004'")
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
        ShowWebLoginModal()
    End Sub

    Private Async Sub btnLogout_Click(sender As Object, e As EventArgs) Handles btnLogout.Click
        Try
            Await HttpUtil.HttpClient.PostAsync("api/bff/auth/logout", Nothing)
        Catch
        End Try

        HttpUtil.ClearSession()
        lblLoginStatus.Text = "ログイン状態: 未ログイン (ログアウトしました)"
        lblLoginStatus.ForeColor = Color.FromArgb(51, 65, 85)
        btnLogout.Enabled = False
        txtResult.Text = "ログアウトしました。未ログイン状態となったため、ログイン画面に強制遷移します。"
        ShowWebLoginModal()
    End Sub

    Private Async Sub btnFetchData_Click(sender As Object, e As EventArgs) Handles btnFetchData.Click
        If Not EnsureLoggedInOrPrompt() Then Return
        btnFetchData.Enabled = False
        Dim relativePath As String = If(cmbRoute.SelectedItem?.ToString(), "/Client/execute?commandName=ITM.APP")

        txtResult.Text = $"Gateway ({GatewayBaseUrl.TrimEnd("/"c)}{relativePath}) へリクエスト送信中..." & Environment.NewLine

        Try
            Dim response As HttpResponseMessage = Await HttpUtil.GetAsync(relativePath)
            Dim rawJson As String = Await response.Content.ReadAsStringAsync()
            Dim formattedJson As String = FormatJsonSafely(rawJson)

            ' /oauth/token 実行時は返却された access_token を自動的に Common.APP.StranglerHttpClientUtility の Bearer ヘッダーにセット
            If relativePath.StartsWith("/oauth/token", StringComparison.OrdinalIgnoreCase) AndAlso response.IsSuccessStatusCode Then
                Try
                    Using doc As JsonDocument = JsonDocument.Parse(rawJson)
                        Dim tokenElem As JsonElement
                        If doc.RootElement.TryGetProperty("access_token", tokenElem) AndAlso tokenElem.ValueKind = JsonValueKind.String Then
                            Dim bearerToken As String = tokenElem.GetString()
                            If Not String.IsNullOrWhiteSpace(bearerToken) Then
                                HttpUtil.SetBearerAccessToken(bearerToken)
                            End If
                        End If
                    End Using
                Catch
                End Try
            End If

            Dim currentBearer As String = If(String.IsNullOrEmpty(HttpUtil.BearerAccessToken), "(未取得 - /oauth/token で取得可能)", HttpUtil.BearerAccessToken)

            txtResult.Text =
                $"Status : {CInt(response.StatusCode)} ({response.ReasonPhrase})" & Environment.NewLine &
                $"URL    : {New Uri(HttpUtil.HttpClient.BaseAddress, relativePath.TrimStart("/"c))}" & Environment.NewLine &
                $"Token  : {If(String.IsNullOrEmpty(HttpUtil.SessionToken), "(未ログイン)", HttpUtil.SessionToken)}" & Environment.NewLine &
                $"Bearer : {currentBearer}" & Environment.NewLine &
                $"Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
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
