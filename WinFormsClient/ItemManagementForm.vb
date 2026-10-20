Imports System
Imports System.Data
Imports System.Drawing
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms
Imports Common.APP
Imports LegacyMvc.PRM

''' <summary>
''' 【レガシー側 Windows Forms 画面】品目マスタ (ITM_TBL) の管理画面
''' ・Common.APP.StranglerHttpClientUtility と Common.APP.LoginUser を使用
''' ・LegacyMvc.PRM プロジェクトのパラメータクラス (ItemSearchOParam / ItemSaveOParam / ItemAppOParam) を DirectCast して利用
''' </summary>
Public Class ItemManagementForm
    Inherits Form

    Private ReadOnly _httpUtil As StranglerHttpClientUtility

    Private txtSearchKeyword As TextBox
    Private WithEvents btnSearch As Button
    Private WithEvents btnShowAll As Button
    Private WithEvents btnExecItmApp As Button
    Private dgvItems As DataGridView

    Private txtItmCd As TextBox
    Private txtItmNm As TextBox
    Private txtCategory As TextBox
    Private txtUnitPrice As TextBox
    Private txtStockQty As TextBox
    Private WithEvents btnSaveItem As Button
    Private WithEvents btnApplyItemWorkflow As Button
    Private lblStatus As Label

    Public Sub New(httpUtil As StranglerHttpClientUtility)
        _httpUtil = httpUtil
        InitializeComponent()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = "[WinForms] 品目マスタ (ITM_TBL) 管理画面 (Common.APP + LegacyMvc.PRM DirectCast 対応)"
        Me.Size = New Size(940, 560)
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
            .Size = New Size(890, 250),
            .ReadOnly = True,
            .AllowUserToAddRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler dgvItems.SelectionChanged, AddressOf OnGridSelectionChanged

        ' 下部: 品目登録・更新・ワークフロー申請パネル
        Dim grpEdit As New GroupBox() With {
            .Text = "品目 (ITM_TBL) の新規登録・更新 (ITM.SAV) ／ 品目からのワークフロー申請",
            .Location = New Point(16, 312),
            .Size = New Size(890, 195),
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        }

        Dim l1 As New Label() With {.Text = "品目コード (ITM_CD)*:", .Location = New Point(16, 30), .AutoSize = True}
        txtItmCd = New TextBox() With {.Text = "ITM-004", .Location = New Point(155, 26), .Size = New Size(150, 25)}

        Dim l2 As New Label() With {.Text = "品目名称 (ITM_NM)*:", .Location = New Point(330, 30), .AutoSize = True}
        txtItmNm = New TextBox() With {.Text = "高耐久サーボモーター Unit-X", .Location = New Point(465, 26), .Size = New Size(390, 25)}

        Dim l3 As New Label() With {.Text = "カテゴリ (CATEGORY):", .Location = New Point(16, 68), .AutoSize = True}
        txtCategory = New TextBox() With {.Text = "駆動ユニット", .Location = New Point(155, 64), .Size = New Size(150, 25)}

        Dim l4 As New Label() With {.Text = "単価 (UNIT_PRICE):", .Location = New Point(330, 68), .AutoSize = True}
        txtUnitPrice = New TextBox() With {.Text = "85000", .Location = New Point(465, 64), .Size = New Size(120, 25)}

        Dim l5 As New Label() With {.Text = "在庫数 (STOCK_QTY):", .Location = New Point(605, 68), .AutoSize = True}
        txtStockQty = New TextBox() With {.Text = "50", .Location = New Point(735, 64), .Size = New Size(90, 25)}

        btnSaveItem = New Button() With {
            .Text = "品目 (ITM_TBL) を登録・更新 (ITM.SAV)",
            .Location = New Point(155, 108),
            .Size = New Size(260, 36),
            .BackColor = Color.FromArgb(37, 99, 235),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        btnApplyItemWorkflow = New Button() With {
            .Text = "選択した品目でワークフロー申請 (Step1: 申請)",
            .Location = New Point(430, 108),
            .Size = New Size(320, 36),
            .BackColor = Color.FromArgb(5, 150, 105),
            .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat
        }

        lblStatus = New Label() With {
            .Text = "準備完了",
            .Location = New Point(16, 156),
            .Size = New Size(850, 24),
            .ForeColor = Color.FromArgb(5, 150, 105)
        }

        grpEdit.Controls.AddRange(New Control() {l1, txtItmCd, l2, txtItmNm, l3, txtCategory, l4, txtUnitPrice, l5, txtStockQty, btnSaveItem, btnApplyItemWorkflow, lblStatus})

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
    ''' Common.APP.StranglerHttpClientUtility でコマンド "ITM.SCH" を呼び出し、
    ''' 返却された OParam を DirectCast(rawResult, ItemSearchOParam) でキャストして表示します。
    ''' </summary>
    Private Async Function LoadItemsAsync(keyword As String) As Task
        Try
            lblStatus.Text = "Common.APP.StranglerHttpClientUtility 経由でコマンド 'ITM.SCH' を実行中..."
            Dim searchIParam As New ItemSearchIParam() With {
                .Keyword = keyword
            }

            Dim rawResult As Object = Await _httpUtil.ExecuteCommandAsync("ITM.SCH", searchIParam, "Client/execute")
            Dim searchOParam As ItemSearchOParam = DirectCast(rawResult, ItemSearchOParam)

            Dim dt As New DataTable()
            dt.Columns.Add("品目コード (ITM_CD)")
            dt.Columns.Add("品目名称 (ITM_NM)")
            dt.Columns.Add("カテゴリ")
            dt.Columns.Add("単価")
            dt.Columns.Add("在庫数")
            dt.Columns.Add("承認ステータス")
            dt.Columns.Add("承認者")

            For Each item As ItmTblEntity In searchOParam.Items
                dt.Rows.Add(
                    item.ItmCd,
                    item.ItmNm,
                    item.Category,
                    item.UnitPrice.ToString("0"),
                    item.StockQty.ToString(),
                    item.ApprovalStatus,
                    If(item.ApprovedBy, String.Empty)
                )
            Next

            dgvItems.DataSource = dt
            lblStatus.Text = $"✓ DirectCast(result, ItemSearchOParam) 成功: {searchOParam.Items.Count} 件の品目を取得しました。"
        Catch ex As Exception
            lblStatus.Text = "検索エラー: " & ex.Message
        End Try
    End Function

    ''' <summary>
    ''' &lt;command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1"&gt;
    ''' コマンド名 "ITM.APP" で通信し、返却された OParam を DirectCast(result, ItemAppOParam) で受け取る処理
    ''' </summary>
    Private Async Sub btnExecItmApp_Click(sender As Object, e As EventArgs) Handles btnExecItmApp.Click
        Try
            Dim code As String = If(String.IsNullOrWhiteSpace(txtItmCd.Text), "ITM-001", txtItmCd.Text.Trim())
            Dim appIParam As New ItemAppIParam() With {
                .TargetItmCd = code,
                .TargetBomNo = "BOM-2026-001",
                .TargetDrawingNo = "drw004",
                .ActionMode = "QUERY_AND_APPROVE"
            }

            Dim rawResult As Object = Await _httpUtil.ExecuteCommandAsync("ITM.APP", appIParam, "Client/execute")
            Dim appOParam As ItemAppOParam = DirectCast(rawResult, ItemAppOParam)

            Await LoadItemsAsync(String.Empty)
            lblStatus.Text = $"✓ DirectCast(result, ItemAppOParam) 成功: {appOParam.SummaryMessage}"
            MessageBox.Show(
                $"【コマンド ""ITM.APP"" 実行 & DirectCast 成功】" & Environment.NewLine &
                $"実行クラス: {appOParam.ExecutedCommandName} (db={appOParam.DbNumberUsed})" & Environment.NewLine &
                $"メッセージ: {appOParam.SummaryMessage}" & Environment.NewLine &
                $"品目件数  : {appOParam.Items.Count} 件 / BOM件数: {appOParam.Boms.Count} 件 / 図面件数: {appOParam.Drawings.Count} 件",
                "ITM.APP DirectCast 完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show("ITM.APP 実行エラー: " & ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub OnGridSelectionChanged(sender As Object, e As EventArgs)
        If dgvItems.SelectedRows.Count > 0 Then
            Dim row As DataGridViewRow = dgvItems.SelectedRows(0)
            txtItmCd.Text = Convert.ToString(row.Cells(0).Value)
            txtItmNm.Text = Convert.ToString(row.Cells(1).Value)
            txtCategory.Text = Convert.ToString(row.Cells(2).Value)
            txtUnitPrice.Text = Convert.ToString(row.Cells(3).Value)
            txtStockQty.Text = Convert.ToString(row.Cells(4).Value)
        End If
    End Sub

    Private Async Sub btnSaveItem_Click(sender As Object, e As EventArgs) Handles btnSaveItem.Click
        Try
            Dim price As Decimal = 0D
            Decimal.TryParse(txtUnitPrice.Text, price)
            Dim qty As Integer = 0
            Integer.TryParse(txtStockQty.Text, qty)

            Dim saveIParam As New ItemSaveIParam() With {
                .ItmCd = txtItmCd.Text.Trim(),
                .ItmNm = txtItmNm.Text.Trim(),
                .Category = txtCategory.Text.Trim(),
                .UnitPrice = price,
                .StockQty = qty
            }

            ' 1. コマンドディスパッチャ ("ITM.SAV") で実行し、ItemSaveOParam に DirectCast
            Dim rawResult As Object = Await _httpUtil.ExecuteCommandAsync("ITM.SAV", saveIParam, "Client/execute")
            Dim saveOParam As ItemSaveOParam = DirectCast(rawResult, ItemSaveOParam)

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
            lblStatus.Text = $"✓ DirectCast(result, ItemSaveOParam) 成功: 品目 '{saveOParam.SavedItem.ItmCd}' を登録・更新しました。"
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
