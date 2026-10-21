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
    Private Shared ReadOnly LegacyBaseUrl As String = LoadLegacyBaseUrl()
    Private Shared ReadOnly LoginMode As String = LoadClientSetting("LoginMode", "Legacy")
    Private Shared ReadOnly LegacyLoginUrl As String = LoadClientSetting("LegacyLoginUrl", "http://localhost:5001/LegacyLogin.aspx?clientWebView=true")

    ' 共通プロジェクト Common.APP の HttpClient ユーティリティを使用
    Public Shared ReadOnly HttpUtil As New StranglerHttpClientUtility(GatewayBaseUrl)

    Private Shared Function LoadClientSetting(propertyName As String, defaultValue As String) As String
        Try
            Dim settingsPath As String = Path.Combine(AppContext.BaseDirectory, "client-settings.json")
            If File.Exists(settingsPath) Then
                Dim json As String = File.ReadAllText(settingsPath)
                Using doc As JsonDocument = JsonDocument.Parse(json)
                    Dim prop As JsonElement
                    If doc.RootElement.TryGetProperty(propertyName, prop) AndAlso prop.ValueKind = JsonValueKind.String Then
                        Dim val As String = prop.GetString()
                        If Not String.IsNullOrWhiteSpace(val) Then
                            Return val
                        End If
                    End If
                End Using
            End If
        Catch
        End Try
        Return defaultValue
    End Function

    Private Shared Function LoadGatewayBaseUrl() As String
        Dim url As String = LoadClientSetting("GatewayBaseUrl", "http://localhost:5000/")
        Return If(url.EndsWith("/"), url, url & "/")
    End Function

    Private Shared Function LoadLegacyBaseUrl() As String
        Dim url As String = LoadClientSetting("LegacyBaseUrl", "http://localhost:5001/")
        Return If(url.EndsWith("/"), url, url & "/")
    End Function

    Private pnlAuth As Panel
    Private lblLoginStatus As Label
    Private WithEvents btnOpenWebLogin As Button
    Private WithEvents btnLogout As Button

    Private pnlScreens As FlowLayoutPanel
    Private WithEvents btnOpenItemWinForms As Button
    Private WithEvents btnExecItmAppCommand As Button
    Private WithEvents btnExecDrwPortalCommand As Button
    Private WithEvents btnExecPrjSchCommand As Button
    Private WithEvents btnExecWfAprCommand As Button
    Private WithEvents btnOpenDrawingWeb As Button
    Private WithEvents btnOpenProjectWeb As Button
    Private WithEvents btnOpenWorkflowWeb As Button
    Private _trayNotifyIcon As NotifyIcon

    Private lblEndpoint As Label
    Private WithEvents cmbRoute As ComboBox
    Private WithEvents btnFetchData As Button
    Private txtResult As TextBox

    Public Sub New()
        LegacyCommands.EnsureRegistered()
        ModernCommands.EnsureRegistered()
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
            .Text = "[WinForms] 品目",
            .Size = New Size(118, 32),
            .BackColor = Color.FromArgb(15, 118, 110),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnExecItmAppCommand = New Button() With {
            .Text = "[Legacy: ITM.APP]",
            .Size = New Size(132, 32),
            .BackColor = Color.FromArgb(180, 83, 9),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnExecDrwPortalCommand = New Button() With {
            .Text = "[Web起動: DRW.PORTAL]",
            .Size = New Size(170, 32),
            .BackColor = Color.FromArgb(217, 119, 6),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnExecPrjSchCommand = New Button() With {
            .Text = "[Modern: PRJ.SCH]",
            .Size = New Size(135, 32),
            .BackColor = Color.FromArgb(3, 105, 161),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnExecWfAprCommand = New Button() With {
            .Text = "[WF.APR: 承認Hook+通知]",
            .Size = New Size(158, 32),
            .BackColor = Color.FromArgb(190, 24, 93),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenDrawingWeb = New Button() With {
            .Text = "[Web] 図面",
            .Size = New Size(102, 32),
            .BackColor = Color.FromArgb(71, 85, 105),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenProjectWeb = New Button() With {
            .Text = "[Web] 案件統合",
            .Size = New Size(108, 32),
            .BackColor = Color.FromArgb(29, 78, 216),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnOpenWorkflowWeb = New Button() With {
            .Text = "[Web] WF管理",
            .Size = New Size(105, 32),
            .BackColor = Color.FromArgb(109, 40, 217),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        pnlScreens.Controls.AddRange(New Control() {
            btnOpenItemWinForms,
            btnExecItmAppCommand,
            btnExecDrwPortalCommand,
            btnExecPrjSchCommand,
            btnExecWfAprCommand,
            btnOpenDrawingWeb,
            btnOpenProjectWeb,
            btnOpenWorkflowWeb
        })

        ' Windows デスクトップ通知用 NotifyIcon (タスクトレイ通知)
        _trayNotifyIcon = New NotifyIcon() With {
            .Icon = SystemIcons.Information,
            .Visible = True,
            .Text = "Strangler Fig ワークフロー通知"
        }

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
            "/Client/execute?commandName=DRW.PORTAL",
            "/Client/execute?commandName=ITM.APP",
            "/Client/execute?commandName=PRJ.SCH",
            "/Client/execute?commandName=WF.APR",
            "/View/drawings/jobs?processNow=true",
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
        Dim isLegacyMode As Boolean = String.Equals(LoginMode, "Legacy", StringComparison.OrdinalIgnoreCase)
        Dim loginDialogTitle As String = If(isLegacyMode,
            "レガシー ログイン画面 (LegacyMvc - USR_TBL 認証)",
            "Web ログイン画面 (WebView2 - Gateway 経由 /login)")

        Using loginForm As WebLoginForm = If(isLegacyMode,
            New WebLoginForm(LegacyLoginUrl, loginDialogTitle),
            New WebLoginForm(GatewayBaseUrl))

            If loginForm.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrWhiteSpace(loginForm.SessionToken) Then
                HttpUtil.SetLoginSession(loginForm.SessionToken, loginForm.AuthenticatedUser)

                lblLoginStatus.Text = $"ログイン中: {loginForm.UserSummary}"
                lblLoginStatus.ForeColor = Color.FromArgb(5, 150, 105)
                btnLogout.Enabled = True

                txtResult.Text =
                    $"=== ログイン成功 ({If(isLegacyMode, "LegacyMvc レガシーログイン画面", "Gateway 共通 Web ログイン")}) ===" & Environment.NewLine &
                    $"LoginMode : {LoginMode}" & Environment.NewLine &
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
    ''' 【レガシーコマンド実行: CommandNames.ItmApp &amp; LegacyCommands.ItmApp による型安全呼び出し】
    ''' ・コマンド名定数 Common.APP.CommandNames.ItmApp ("ITM.APP") を共通使用
    ''' ・New ItemAppIParam() を各画面で手書きせず、LegacyCommands.ItmApp (CommandSpec(Of ItemAppIParam, ItemAppOParam)) から
    '''   CreateIParam() / ExecuteCommandAsync() を呼ぶことで、IParam と OParam の型変換ミスをコンパイル時に100%防止します。
    ''' </summary>
    Private Async Sub btnExecItmAppCommand_Click(sender As Object, e As EventArgs) Handles btnExecItmAppCommand.Click
        If Not EnsureLoggedInOrPrompt() Then Return

        Try
            ' 1. New ItemAppIParam() を直接書かず、LegacyCommands.ItmApp (CommandNames.ItmApp に紐づく定義) から生成＆実行
            '    戻り値も自動的に ItemAppOParam 型として返却されるため、誤った型へのキャストが起きません。
            Dim oParam As ItemAppOParam = Await HttpUtil.ExecuteCommandAsync(
                LegacyCommands.ItmApp,
                Sub(p)
                    p.TargetItmCd = "ITM-001"
                    p.TargetBomNo = "BOM-2026-001"
                    p.TargetDrawingNo = "drw004"
                    p.ActionMode = "QUERY_AND_APPROVE"
                End Sub,
                "Client/execute")

            ' (参考) コマンド名定数 CommandNames.ItmApp から IParam を自動生成して DirectCast する場合も型検証付きで実行可能:
            ' Dim iParam = CommandParamRegistry.CreateIParam(Of ItemAppIParam)(CommandNames.ItmApp)
            ' Dim oParam = DirectCast(Await HttpUtil.ExecuteCommandAsync(CommandNames.ItmApp, iParam), ItemAppOParam)

            Dim firstBom As BomRecord = oParam.Boms.FirstOrDefault()
            Dim bomSummary As String = If(firstBom IsNot Nothing,
                $"{firstBom.BomNo} (ItmCd={firstBom.ItmCd}) -> Status={firstBom.ApprovalStatus}, By={firstBom.ApprovedBy}",
                "(なし)")

            Dim firstDrw As DrawingRecord = oParam.Drawings.FirstOrDefault()
            Dim drwSummary As String = If(firstDrw IsNot Nothing,
                $"{firstDrw.DrawingNo} ({firstDrw.Title}) -> Status={firstDrw.ApprovalStatus}, By={firstDrw.ApprovedBy}",
                "(なし)")

            txtResult.Text =
                $"=== [成功] 定数 CommandNames.ItmApp (""{CommandNames.ItmApp}"") & LegacyCommands.ItmApp 実行完了 ===" & Environment.NewLine &
                $"コマンド名定数 : Common.APP.CommandNames.ItmApp = ""{CommandNames.ItmApp}""" & Environment.NewLine &
                $"型付きカタログ : LegacyMvc.PRM.LegacyCommands.ItmApp (IParam={LegacyCommands.ItmApp.IParamType.Name}, OParam={LegacyCommands.ItmApp.OParamType.Name})" & Environment.NewLine &
                $"型安全呼び出し : Await HttpUtil.ExecuteCommandAsync(LegacyCommands.ItmApp, Sub(p) ...)" & Environment.NewLine &
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
            txtResult.Text = $"[{CommandNames.ItmApp} 実行エラー]{Environment.NewLine}{ex}"
        End Try
    End Sub

    ''' <summary>
    ''' 【Web 画面 (.aspx) 起動コマンド実行: CommandNames.DrwPortal &amp; RedirectException による自動ブラウザ起動】
    ''' ・サーバー側 CommandDefinitionAttribute に Aspx:="LegacyPortal.aspx" が設定されているコマンド
    ''' ・StranglerHttpClientUtility がレスポンスのリダイレクト指定を検知し、独自の RedirectException を発生させる
    ''' ・WinForms 側で RedirectException を catch し、Process.Start(ex.RedirectUrl) でブラウザを自動起動する
    ''' ・URL の req_code により、RequestManager を通じてブラウザ画面にもログインユーザー情報が引き継がれる
    ''' </summary>
    Private Async Sub btnExecDrwPortalCommand_Click(sender As Object, e As EventArgs) Handles btnExecDrwPortalCommand.Click
        If Not EnsureLoggedInOrPrompt() Then Return

        Try
            txtResult.Text = $"コマンド '{CommandNames.DrwPortal}' を実行中..." & Environment.NewLine

            ' LegacyCommands.DrwPortal ("DRW.PORTAL") を呼び出し
            Dim oParam As DrawingSearchOParam = Await HttpUtil.ExecuteCommandAsync(
                LegacyCommands.DrwPortal,
                Sub(p)
                    p.Keyword = ""
                End Sub,
                "Client/execute")

            txtResult.Text = $"コマンド実行完了: {oParam.SummaryMessage}"

        Catch ex As RedirectException
            ' 【既存仕様の完全再現】
            ' 独自の RedirectException を catch してブラウザを自動起動
            txtResult.Text =
                "=== [検知成功] RedirectException による Web 画面自動起動 ===" & Environment.NewLine &
                $"CommandName  : {ex.CommandName}" & Environment.NewLine &
                $"AspxPage     : {ex.AspxPage}" & Environment.NewLine &
                $"ReqCode      : {ex.ReqCode} (RequestManager 管理)" & Environment.NewLine &
                $"RedirectUrl  : {ex.RedirectUrl}" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                "Process.Start(ex.RedirectUrl) でブラウザを自動起動しました。" & Environment.NewLine &
                "ブラウザ側 (LegacyPortal.aspx) で req_code からログインユーザー・セッションが復元されます。"

            OpenBrowserUrl(ex.RedirectUrl)

        Catch ex As Exception
            txtResult.Text = $"[{CommandNames.DrwPortal} 実行エラー]{Environment.NewLine}{ex}"
        End Try
    End Sub

    ''' <summary>
    ''' 【モダンコマンド実行: CommandNames.PrjSch &amp; ModernCommands.PrjSch による型安全呼び出し】
    ''' ・コマンド名定数 Common.APP.CommandNames.PrjSch ("PRJ.SCH") を共通使用
    ''' ・ModernCommands.PrjSch (CommandSpec(Of ProjectSearchIParam, ProjectSearchOParam)) により
    '''   New ProjectSearchIParam() の手書きや型変換ミスを防止します。
    ''' </summary>
    Private Async Sub btnExecPrjSchCommand_Click(sender As Object, e As EventArgs) Handles btnExecPrjSchCommand.Click
        If Not EnsureLoggedInOrPrompt() Then Return

        Try
            Dim oParam As ProjectSearchOParam = Await HttpUtil.ExecuteCommandAsync(
                ModernCommands.PrjSch,
                Sub(p)
                    p.Keyword = ""
                End Sub,
                "Client/execute")

            Dim firstPrj As IntegratedProjectRecord = oParam.IntegratedProjects.FirstOrDefault()
            Dim prjSummary As String = If(firstPrj IsNot Nothing,
                $"{firstPrj.Project.PrjId} ({firstPrj.Project.PrjNm}) | 品目:{firstPrj.Project.TargetItmCd} | 図面:{firstPrj.Project.LinkedDrawingNo} | 状態:{firstPrj.Project.PrjStatus}",
                "(なし)")

            txtResult.Text =
                $"=== [成功] 定数 CommandNames.PrjSch (""{CommandNames.PrjSch}"") & ModernCommands.PrjSch 実行完了 ===" & Environment.NewLine &
                $"コマンド名定数 : Common.APP.CommandNames.PrjSch = ""{CommandNames.PrjSch}""" & Environment.NewLine &
                $"型付きカタログ : ModernBff.PRM.ModernCommands.PrjSch (IParam={ModernCommands.PrjSch.IParamType.Name}, OParam={ModernCommands.PrjSch.OParamType.Name})" & Environment.NewLine &
                $"型安全呼び出し : Await HttpUtil.ExecuteCommandAsync(ModernCommands.PrjSch, Sub(p) ...)" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                $"ExecutedCommandName  : {oParam.ExecutedCommandName}" & Environment.NewLine &
                $"DbNumberUsed         : {oParam.DbNumberUsed}" & Environment.NewLine &
                $"PermissionCheck      : {oParam.PermissionCheckResult}" & Environment.NewLine &
                $"SummaryMessage       : {oParam.SummaryMessage}" & Environment.NewLine &
                $"先頭案件レコード     : {prjSummary}" & Environment.NewLine &
                $"IntegratedProjects   : {oParam.IntegratedProjects.Count} 件" & Environment.NewLine &
                $"AllWorkflows         : {oParam.AllWorkflows.Count} 件"
        Catch ex As Exception
            txtResult.Text = $"[{CommandNames.PrjSch} 実行エラー]{Environment.NewLine}{ex}"
        End Try
    End Sub

    ''' <summary>
    ''' 【疎結合 Hook 方式のワークフロー最終承認 (CommandNames.WfApr = "WF.APR")】
    ''' 1. トランザクション内 Hook: ItemApprovalHook / BomApprovalHook / DrawingApprovalHook を疎結合に実行
    ''' 2. 完了後 Hook (メールモック): 各ドメイン Hook の OnCommittedAsync で「品目名称 (ItmNm)」「図面名称 (Title)」を使ったモックメールを送信
    ''' 3. 完了後 Hook (Windows 通知): サーバー側の Windows トースト通知に加え、クライアント側でも NotifyIcon で Windows デスクトップ通知を表示
    ''' </summary>
    Private Async Sub btnExecWfAprCommand_Click(sender As Object, e As EventArgs) Handles btnExecWfAprCommand.Click
        If Not EnsureLoggedInOrPrompt() Then Return

        Try
            Dim oParam As WorkflowApproveOParam = Await HttpUtil.ExecuteCommandAsync(
                ModernCommands.WfApr,
                Sub(p)
                    p.WfId = "WF-9001"
                    p.SimulateDrawingError = False
                End Sub,
                "Client/execute")

            ' Windows 通知の呼び出し (NotifyIcon バルーン通知)
            Dim winNotify As WindowsNotificationRecord = oParam.WindowsNotificationsSent.FirstOrDefault()
            If winNotify IsNot Nothing AndAlso _trayNotifyIcon IsNot Nothing Then
                _trayNotifyIcon.ShowBalloonTip(5000, winNotify.Title, winNotify.Message, ToolTipIcon.Info)
            End If

            Dim hooksText As String = String.Join(Environment.NewLine & "  - ", oParam.ExecutedHooks)
            Dim mailLines As New List(Of String)()
            For Each m As MockEmailRecord In oParam.MockEmailsSent
                mailLines.Add($"[{m.TriggeredByHook}] To={m.To} | Subject={m.Subject}")
            Next

            Dim notifyLines As New List(Of String)()
            For Each n As WindowsNotificationRecord In oParam.WindowsNotificationsSent
                notifyLines.Add($"[{n.TriggeredByHook}] {n.Title} : {n.Message} ({n.DeliveryMethod})")
            Next

            txtResult.Text =
                $"=== [成功] 疎結合 Hook ワークフロー最終承認 ({CommandNames.WfApr}) & メール/Windows通知 完了 ===" & Environment.NewLine &
                $"TransactionResult : {oParam.TransactionResult}" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                $"【実行された Hook 一覧 ({oParam.ExecutedHooks.Count} 件)】" & Environment.NewLine &
                $"  - {hooksText}" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                $"【完了後 Hook: モックメール送信 ({oParam.MockEmailsSent.Count} 通 - 品目名称/図面名称を使用)】" & Environment.NewLine &
                $"  {String.Join(Environment.NewLine & "  ", mailLines)}" & Environment.NewLine &
                New String("-"c, 75) & Environment.NewLine &
                $"【完了後 Hook: Windows 通知 ({oParam.WindowsNotificationsSent.Count} 件)】" & Environment.NewLine &
                $"  {String.Join(Environment.NewLine & "  ", notifyLines)}"
        Catch ex As Exception
            txtResult.Text = $"[{CommandNames.WfApr} 実行エラー]{Environment.NewLine}{ex}"
        End Try
    End Sub

    Private Sub btnOpenItemWinForms_Click(sender As Object, e As EventArgs) Handles btnOpenItemWinForms.Click
        If Not EnsureLoggedInOrPrompt() Then Return
        Using itemForm As New ItemManagementForm(HttpUtil)
            itemForm.ShowDialog(Me)
        End Using
    End Sub

    Private Sub btnOpenDrawingWeb_Click(sender As Object, e As EventArgs) Handles btnOpenDrawingWeb.Click
        If String.Equals(LoginMode, "Legacy", StringComparison.OrdinalIgnoreCase) Then
            OpenBrowserUrl(LegacyBaseUrl.TrimEnd("/"c) & "/LegacyPortal.aspx")
        Else
            OpenBrowserUrl(GatewayBaseUrl.TrimEnd("/"c) & "/web/view/drawings?drw_no='drw004'")
        End If
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

            ' レスポンスに isRedirect が含まれていた場合はブラウザを自動起動 (RedirectException 再現)
            Try
                Using doc As JsonDocument = JsonDocument.Parse(rawJson)
                    Dim isRedirElem As JsonElement
                    If doc.RootElement.TryGetProperty("isRedirect", isRedirElem) AndAlso isRedirElem.GetBoolean() Then
                        Dim redirUrl As String = doc.RootElement.GetProperty("redirectUrl").GetString()
                        If Not String.IsNullOrWhiteSpace(redirUrl) Then
                            OpenBrowserUrl(redirUrl)
                        End If
                    End If
                End Using
            Catch
            End Try

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
