Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms
Imports Common.APP
Imports LegacyMvc.PRM

''' <summary>
''' 【レガシー側 Windows Forms 画面】品目マスタ (ITM_TBL) の管理画面
''' ・Common.APP.StranglerHttpClientUtility と Common.APP.LoginUser を使用
''' ・LegacyMvc.PRM プロジェクトのパラメータクラス (ItemSearchOParam / ItemSaveOParam / ItemDuplicateOParam / ItemReviseOParam) を型安全に利用
''' ・選択した品目の「複製 (ITM.CPY)」と「改訂 (ITM.REV)」で引き継ぐ項目・初期化する項目の違いを検証可能
''' </summary>
Public Class ItemManagementForm
    Inherits Form

    Private ReadOnly _httpUtil As StranglerHttpClientUtility
    Private _loadedItems As New List(Of ItmTblEntity)()

    Private txtSearchKeyword As TextBox
    Private WithEvents btnSearch As Button
    Private WithEvents btnShowAll As Button
    Private WithEvents btnExecItmApp As Button
    Private dgvItems As DataGridView

    Private txtItmCd As TextBox
    Private txtVer As TextBox
    Private txtRev As TextBox
    Private txtItmNm As TextBox
    Private txtCategory As TextBox
    Private txtUnitPrice As TextBox
    Private txtStockQty As TextBox
    Private WithEvents btnSaveItem As Button
    Private WithEvents btnDuplicateItem As Button
    Private WithEvents btnReviseItem As Button
    Private WithEvents btnApplyItemWorkflow As Button
    Private lblStatus As Label

    Public Sub New(httpUtil As StranglerHttpClientUtility)
        _httpUtil = httpUtil
        InitializeComponent()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "[WinForms] 品目マスタ (ITM_TBL: Ver/Rev・複製・改訂対応) 管理画面"
        Me.Size = New Size(1020, 600)
        Me.StartPosition = FormStartPosition.CenterParent

        ' 上部: 検索バー & ITM.APP 実行ボタン
        Dim lblSearch As New Label() With {.Text = "品目検索 (コード/名称/カテゴリ):", .Location = New Point(16, 18), .AutoSize = True}
        txtSearchKeyword = New TextBox() With {.Location = New Point(210, 14), .Size = New Size(220, 26)}
        btnSearch = New Button() With {.Text = "検索 (ITM.SCH)", .Location = New Point(440, 12), .Size = New Size(120, 28)}
        btnShowAll = New Button() With {.Text = "全件表示", .Location = New Point(568, 12), .Size = New Size(90, 28)}
        btnExecItmApp = New Button() With {
            .Text = "コマンド ""ITM.APP"" 実行 (DirectCast)",
            .Location = New Point(668, 12),
            .Size = New Size(235, 28),
            .BackColor = Color.FromArgb(180, 83, 9),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        ' 中央: 品目一覧 DataGridView
        dgvItems = New DataGridView() With {
            .Location = New Point(16, 50),
            .Size = New Size(970, 250),
            .ReadOnly = True,
            .AllowUserToAddRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler dgvItems.SelectionChanged, AddressOf OnGridSelectionChanged

        ' 下部: 品目登録・更新・複製・改訂・ワークフロー申請パネル
        Dim grpEdit As New GroupBox() With {
            .Text = "品目 (ITM_TBL) の新規登録・更新 (ITM.SAV) ／ 選択品目の複製 (ITM.CPY)・改訂 (ITM.REV)・WF申請",
            .Location = New Point(16, 312),
            .Size = New Size(970, 230),
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        }

        Dim l1 As New Label() With {.Text = "品目コード (ITM_CD)*:", .Location = New Point(16, 30), .AutoSize = True}
        txtItmCd = New TextBox() With {.Text = "ITM-004", .Location = New Point(155, 26), .Size = New Size(135, 25)}

        Dim lVer As New Label() With {.Text = "Ver:", .Location = New Point(305, 30), .AutoSize = True}
        txtVer = New TextBox() With {.Text = "1", .Location = New Point(340, 26), .Size = New Size(45, 25)}

        Dim lRev As New Label() With {.Text = "Rev:", .Location = New Point(395, 30), .AutoSize = True}
        txtRev = New TextBox() With {.Text = "A", .Location = New Point(430, 26), .Size = New Size(50, 25)}

        Dim l2 As New Label() With {.Text = "品目名称 (ITM_NM)*:", .Location = New Point(495, 30), .AutoSize = True}
        txtItmNm = New TextBox() With {.Text = "高耐久サーボモーター Unit-X", .Location = New Point(625, 26), .Size = New Size(325, 25)}

        Dim l3 As New Label() With {.Text = "カテゴリ (CATEGORY):", .Location = New Point(16, 68), .AutoSize = True}
        txtCategory = New TextBox() With {.Text = "駆動ユニット", .Location = New Point(155, 64), .Size = New Size(150, 25)}

        Dim l4 As New Label() With {.Text = "単価 (UNIT_PRICE):", .Location = New Point(330, 68), .AutoSize = True}
        txtUnitPrice = New TextBox() With {.Text = "85000", .Location = New Point(465, 64), .Size = New Size(120, 25)}

        Dim l5 As New Label() With {.Text = "在庫数 (STOCK_QTY):", .Location = New Point(605, 68), .AutoSize = True}
        txtStockQty = New TextBox() With {.Text = "50", .Location = New Point(735, 64), .Size = New Size(90, 25)}

        btnSaveItem = New Button() With {
            .Text = "登録・更新 (ITM.SAV)",
            .Location = New Point(16, 108),
            .Size = New Size(205, 38),
            .BackColor = Color.FromArgb(37, 99, 235),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnDuplicateItem = New Button() With {
            .Text = "選択品目を複製 (ITM.CPY)" & Environment.NewLine & "[Ver=1, Rev=A, 在庫=0 初期化]",
            .Location = New Point(230, 104),
            .Size = New Size(235, 44),
            .BackColor = Color.FromArgb(124, 58, 237),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnReviseItem = New Button() With {
            .Text = "選択品目を改訂 (ITM.REV)" & Environment.NewLine & "[Ver+1, Rev次記号, 在庫引継]",
            .Location = New Point(474, 104),
            .Size = New Size(235, 44),
            .BackColor = Color.FromArgb(217, 119, 6),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnApplyItemWorkflow = New Button() With {
            .Text = "選択品目でWF申請 (Step1)",
            .Location = New Point(718, 108),
            .Size = New Size(235, 38),
            .BackColor = Color.FromArgb(5, 150, 105),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        lblStatus = New Label() With {
            .Text = "準備完了: 一覧から品目を選択して「複製 (ITM.CPY)」または「改訂 (ITM.REV)」を実行できます。",
            .Location = New Point(16, 162),
            .Size = New Size(935, 52),
            .ForeColor = Color.FromArgb(5, 150, 105)
        }

        grpEdit.Controls.AddRange(New Control() {
            l1, txtItmCd, lVer, txtVer, lRev, txtRev, l2, txtItmNm,
            l3, txtCategory, l4, txtUnitPrice, l5, txtStockQty,
            btnSaveItem, btnDuplicateItem, btnReviseItem, btnApplyItemWorkflow, lblStatus
        })

        Me.Controls.AddRange(New Control() {lblSearch, txtSearchKeyword, btnSearch, btnShowAll, btnExecItmApp, dgvItems, grpEdit})
    End Sub

    Protected Overrides Async Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Await LoadItemsAsync(String.Empty)
    End Sub

    Private Async Sub btnSearch_Click(sender As Object, e As EventArgs) Handles btnSearch.Click
        Await LoadItemsAsync(txtSearchKeyword.Text.Trim())
    End Sub

    Private Async Sub btnShowAll_Click(sender As Object, e As EventArgs) Handles btnShowAll.Click
        txtSearchKeyword.Text = String.Empty
        Await LoadItemsAsync(String.Empty)
    End Sub

    ''' <summary>
    ''' LegacyCommands.ItmSch (CommandNames.ItmSch = "ITM.SCH") を使って型安全に品目検索を実行します。
    ''' </summary>
    Private Async Function LoadItemsAsync(keyword As String) As Task
        Try
            lblStatus.Text = $"Common.APP.StranglerHttpClientUtility 経由でコマンド '{CommandNames.ItmSch}' を実行中..."

            Dim searchOParam As ItemSearchOParam = Await _httpUtil.ExecuteCommandAsync(
                LegacyCommands.ItmSch,
                Sub(p)
                    p.Keyword = keyword
                End Sub,
                "Client/execute")

            _loadedItems = If(searchOParam.Items, New List(Of ItmTblEntity)())

            Dim dt As New DataTable()
            dt.Columns.Add("品目コード (ITM_CD)")
            dt.Columns.Add("Ver")
            dt.Columns.Add("Rev")
            dt.Columns.Add("品目名称 (ITM_NM)")
            dt.Columns.Add("カテゴリ")
            dt.Columns.Add("単価")
            dt.Columns.Add("在庫数")
            dt.Columns.Add("承認ステータス")
            dt.Columns.Add("承認者")

            For Each item As ItmTblEntity In _loadedItems
                dt.Rows.Add(
                    item.ItmCd,
                    item.Ver.ToString(),
                    item.Rev,
                    item.ItmNm,
                    item.Category,
                    item.UnitPrice.ToString("0"),
                    item.StockQty.ToString(),
                    item.ApprovalStatus,
                    If(item.ApprovedBy, String.Empty)
                )
            Next

            dgvItems.DataSource = dt
            lblStatus.Text = $"✓ LegacyCommands.ItmSch ({CommandNames.ItmSch}) 成功: {_loadedItems.Count} 件の品目を取得しました。"
        Catch ex As Exception
            lblStatus.Text = "検索エラー: " & ex.Message
        End Try
    End Function

    Private Function GetSelectedEntity() As ItmTblEntity
        Dim targetCd As String = txtItmCd.Text.Trim()
        If dgvItems.SelectedRows.Count > 0 Then
            targetCd = Convert.ToString(dgvItems.SelectedRows(0).Cells(0).Value)
        End If
        Return _loadedItems.FirstOrDefault(Function(x) String.Equals(x.ItmCd, targetCd, StringComparison.OrdinalIgnoreCase))
    End Function

    Private Sub PopulateFormFromEntity(entity As ItmTblEntity)
        If entity Is Nothing Then Return
        txtItmCd.Text = entity.ItmCd
        txtVer.Text = entity.Ver.ToString()
        txtRev.Text = entity.Rev
        txtItmNm.Text = entity.ItmNm
        txtCategory.Text = entity.Category
        txtUnitPrice.Text = entity.UnitPrice.ToString("0")
        txtStockQty.Text = entity.StockQty.ToString()
    End Sub

    ''' <summary>
    ''' 【品目複製 (ITM.CPY)】
    ''' 選択された品目の ItmTblEntity.CreateDuplicateRecord メソッド＆サーバーコマンド LegacyCommands.ItmCpy を実行します。
    ''' ・引き継ぐ項目: 品目名称 (+ "(複製)"), カテゴリ, 単価
    ''' ・初期化する項目: 品目コード (新規採番), Ver (= 1), Rev (= "A"), 在庫数 (= 0), 承認ステータス (= "DRAFT"), 承認者 (= Nothing)
    ''' </summary>
    Private Async Sub btnDuplicateItem_Click(sender As Object, e As EventArgs) Handles btnDuplicateItem.Click
        Try
            Dim selected As ItmTblEntity = GetSelectedEntity()
            Dim srcCd As String = If(selected IsNot Nothing, selected.ItmCd, txtItmCd.Text.Trim())
            If String.IsNullOrWhiteSpace(srcCd) Then
                MessageBox.Show("一覧から複製元の品目を選択してください。", "品目複製", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            ' 1. クライアント側でも ItmTblEntity.CreateDuplicateRecord() を直接呼んで複製後レコードを取得可能
            If selected IsNot Nothing Then
                Dim localPreview As ItmTblEntity = selected.CreateDuplicateRecord($"{selected.ItmCd}-CPY")
                PopulateFormFromEntity(localPreview)
            End If

            ' 2. サーバー側コマンド LegacyCommands.ItmCpy ("ITM.CPY") を型安全に実行し DB へ複製保存
            Dim cpyOParam As ItemDuplicateOParam = Await _httpUtil.ExecuteCommandAsync(
                LegacyCommands.ItmCpy,
                Sub(p)
                    p.SourceItmCd = srcCd
                End Sub,
                "Client/execute")

            Await LoadItemsAsync(String.Empty)
            PopulateFormFromEntity(cpyOParam.DuplicatedItem)
            lblStatus.Text = $"✓ {cpyOParam.FieldMappingSummary}"

            MessageBox.Show(
                $"【品目複製コマンド ({CommandNames.ItmCpy}) 完了】" & Environment.NewLine & Environment.NewLine &
                cpyOParam.FieldMappingSummary,
                "品目複製 (ITM.CPY) 完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show("品目複製エラー: " & ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' 【品目改訂 (ITM.REV)】
    ''' 選択された品目の ItmTblEntity.CreateRevisedRecord メソッド＆サーバーコマンド LegacyCommands.ItmRev を実行します。
    ''' ・更新する項目: Ver (+1 カウントアップ), Rev ("A" -> "B" 等の次リビジョン記号)
    ''' ・引き継ぐ項目: 品目コード, 品目名称, カテゴリ, 単価, 在庫数 (StockQty)
    ''' ・初期化する項目: 承認ステータス (= "DRAFT (改訂・未申請)"), 承認者 (= Nothing), 承認日時 (= Nothing)
    ''' </summary>
    Private Async Sub btnReviseItem_Click(sender As Object, e As EventArgs) Handles btnReviseItem.Click
        Try
            Dim selected As ItmTblEntity = GetSelectedEntity()
            Dim targetCd As String = If(selected IsNot Nothing, selected.ItmCd, txtItmCd.Text.Trim())
            If String.IsNullOrWhiteSpace(targetCd) Then
                MessageBox.Show("一覧から改訂対象の品目を選択してください。", "品目改訂", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            ' サーバー側コマンド LegacyCommands.ItmRev ("ITM.REV") を型安全に実行
            Dim revOParam As ItemReviseOParam = Await _httpUtil.ExecuteCommandAsync(
                LegacyCommands.ItmRev,
                Sub(p)
                    p.TargetItmCd = targetCd
                End Sub,
                "Client/execute")

            Await LoadItemsAsync(String.Empty)
            PopulateFormFromEntity(revOParam.RevisedItem)
            lblStatus.Text = $"✓ {revOParam.FieldMappingSummary}"

            MessageBox.Show(
                $"【品目改訂コマンド ({CommandNames.ItmRev}) 完了】" & Environment.NewLine & Environment.NewLine &
                revOParam.FieldMappingSummary,
                "品目改訂 (ITM.REV) 完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show("品目改訂エラー: " & ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' LegacyCommands.ItmApp (CommandNames.ItmApp = "ITM.APP") による型安全実行
    ''' </summary>
    Private Async Sub btnExecItmApp_Click(sender As Object, e As EventArgs) Handles btnExecItmApp.Click
        Try
            Dim code As String = If(String.IsNullOrWhiteSpace(txtItmCd.Text), "ITM-001", txtItmCd.Text.Trim())

            Dim appOParam As ItemAppOParam = Await _httpUtil.ExecuteCommandAsync(
                LegacyCommands.ItmApp,
                Sub(p)
                    p.TargetItmCd = code
                    p.TargetBomNo = "BOM-2026-001"
                    p.TargetDrawingNo = "drw004"
                    p.ActionMode = "QUERY_AND_APPROVE"
                End Sub,
                "Client/execute")

            Await LoadItemsAsync(String.Empty)
            lblStatus.Text = $"✓ LegacyCommands.ItmApp ({CommandNames.ItmApp}) 成功: {appOParam.SummaryMessage}"
            MessageBox.Show(
                $"【コマンド定数 CommandNames.ItmApp (""{CommandNames.ItmApp}"") 実行完了】" & Environment.NewLine &
                $"実行クラス: {appOParam.ExecutedCommandName} (db={appOParam.DbNumberUsed})" & Environment.NewLine &
                $"メッセージ: {appOParam.SummaryMessage}" & Environment.NewLine &
                $"品目件数  : {appOParam.Items.Count} 件 / BOM件数: {appOParam.Boms.Count} 件 / 図面件数: {appOParam.Drawings.Count} 件",
                "ITM.APP 実行完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show($"{CommandNames.ItmApp} 実行エラー: " & ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub OnGridSelectionChanged(sender As Object, e As EventArgs)
        If dgvItems.SelectedRows.Count > 0 Then
            Dim row As DataGridViewRow = dgvItems.SelectedRows(0)
            txtItmCd.Text = Convert.ToString(row.Cells(0).Value)
            txtVer.Text = Convert.ToString(row.Cells(1).Value)
            txtRev.Text = Convert.ToString(row.Cells(2).Value)
            txtItmNm.Text = Convert.ToString(row.Cells(3).Value)
            txtCategory.Text = Convert.ToString(row.Cells(4).Value)
            txtUnitPrice.Text = Convert.ToString(row.Cells(5).Value)
            txtStockQty.Text = Convert.ToString(row.Cells(6).Value)
        End If
    End Sub

    Private Async Sub btnSaveItem_Click(sender As Object, e As EventArgs) Handles btnSaveItem.Click
        Try
            Dim verNum As Integer = 1
            Integer.TryParse(txtVer.Text, verNum)
            If verNum < 1 Then verNum = 1
            Dim revStr As String = If(String.IsNullOrWhiteSpace(txtRev.Text), "A", txtRev.Text.Trim())

            Dim price As Decimal = 0D
            Decimal.TryParse(txtUnitPrice.Text, price)
            Dim qty As Integer = 0
            Integer.TryParse(txtStockQty.Text, qty)

            ' New ItemSaveIParam() を直接書かず、LegacyCommands.ItmSav.CreateIParam で生成
            Dim saveIParam As ItemSaveIParam = LegacyCommands.ItmSav.CreateIParam(
                Sub(p)
                    p.ItmCd = txtItmCd.Text.Trim()
                    p.Ver = verNum
                    p.Rev = revStr
                    p.ItmNm = txtItmNm.Text.Trim()
                    p.Category = txtCategory.Text.Trim()
                    p.UnitPrice = price
                    p.StockQty = qty
                End Sub)

            ' 1. LegacyCommands.ItmSav (CommandNames.ItmSav = "ITM.SAV") で型安全に実行
            Dim saveOParam As ItemSaveOParam = Await _httpUtil.ExecuteCommandAsync(LegacyCommands.ItmSav, saveIParam, "Client/execute")

            ' 2. ModernBff の EF Core テーブルとも同期
            Dim syncBody As String = JsonSerializer.Serialize(New With {
                .iParam = saveIParam,
                .itmCd = saveIParam.ItmCd,
                .itmNm = saveIParam.ItmNm,
                .category = saveIParam.Category,
                .unitPrice = saveIParam.UnitPrice,
                .stockQty = saveIParam.StockQty
            })
            Await _httpUtil.HttpClient.PostAsync("Client/items", New StringContent(syncBody, Encoding.UTF8, "application/json"))

            Await LoadItemsAsync(String.Empty)
            lblStatus.Text = $"✓ DirectCast(result, ItemSaveOParam) 成功: 品目 '{saveOParam.SavedItem.ItmCd}' (Ver={saveOParam.SavedItem.Ver}, Rev={saveOParam.SavedItem.Rev}) を登録・更新しました。"
        Catch ex As Exception
            MessageBox.Show("品目登録エラー: " & ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Async Sub btnApplyItemWorkflow_Click(sender As Object, e As EventArgs) Handles btnApplyItemWorkflow.Click
        Try
            Dim code As String = txtItmCd.Text.Trim()
            Dim name As String = txtItmNm.Text.Trim()
            If String.IsNullOrWhiteSpace(code) Then Return

            Dim payloadObj = New With {
                .targetType = "ITEM",
                .targetId = code,
                .wfTitle = $"[品目単体申請 (WinForms -> Client)] {code}: {name}"
            }
            Dim content As New StringContent(JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json")
            Dim resp As HttpResponseMessage = Await _httpUtil.HttpClient.PostAsync("Client/workflow/apply", content)
            Dim respJson As String = Await resp.Content.ReadAsStringAsync()

            Await LoadItemsAsync(String.Empty)
            MessageBox.Show(
                $"ClientController (Client/workflow/apply) 経由で品目 '{code} ({name})' のワークフロー申請を完了しました。" & Environment.NewLine &
                "モダン側のワークフロー管理画面 (/workflows) で照査・承認を行えます。",
                "ワークフロー申請完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show("ワークフロー申請エラー: " & ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
End Class
