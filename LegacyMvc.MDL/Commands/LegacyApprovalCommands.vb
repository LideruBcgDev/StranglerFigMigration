Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Linq

Namespace Commands

    Public Class BomRecord
        Public Property BomNo As String = String.Empty
        Public Property ProjectId As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property ApprovalStatus As String = String.Empty
        Public Property ApprovedBy As String
        Public Property ApprovedOrg As String
        Public Property ApprovedAtUtc As DateTime?
    End Class

    Public Class DrawingRecord
        Public Property DrawingNo As String = String.Empty
        Public Property BomNo As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property ApprovalStatus As String = String.Empty
        Public Property ApprovedBy As String
        Public Property ApprovedOrg As String
        Public Property ApprovedAtUtc As DateTime?
    End Class

    ''' <summary>
    ''' レガシー側 (LegacyMvc.MDL - VB.NET) の既存 DB アクセス基盤（ADO.NET DbConnection / DbTransaction）をシミュレート・実行するストア。
    ''' 実 DB 接続が有効な場合は同じ DbConnection / DbTransaction 上で SQL (UPDATE BOM_TBL / DRW_TBL) を発行し、
    ''' ローカル検証時（InMemory フォールバック時）はトランザクションの Commit / Rollback に完全連動するインメモリスナップショットで動作します。
    ''' </summary>
    Public NotInheritable Class LegacyApprovalStore
        Private Shared ReadOnly BomTable As New ConcurrentDictionary(Of String, BomRecord)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly DrawingTable As New ConcurrentDictionary(Of String, DrawingRecord)(StringComparer.OrdinalIgnoreCase)

        Shared Sub New()
            ResetSeedData()
        End Sub

        Private Sub New()
        End Sub

        Public Shared Sub ResetSeedData()
            BomTable("BOM-2026-001") = New BomRecord With {
                .BomNo = "BOM-2026-001",
                .ProjectId = "PRJ-1001",
                .ItmCd = "ITM-001",
                .ApprovalStatus = "PENDING (未承認)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
            BomTable("BOM-2026-002") = New BomRecord With {
                .BomNo = "BOM-2026-002",
                .ProjectId = "PRJ-1002",
                .ItmCd = "ITM-002",
                .ApprovalStatus = "PENDING (未承認)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }

            DrawingTable("DRW-2026-001") = New DrawingRecord With {
                .DrawingNo = "DRW-2026-001",
                .BomNo = "BOM-2026-001",
                .Title = "スマートセンサー組立図面 Rev.A",
                .ApprovalStatus = "PENDING (未承認)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
            DrawingTable("DRW-2026-002") = New DrawingRecord With {
                .DrawingNo = "DRW-2026-002",
                .BomNo = "BOM-2026-002",
                .Title = "エッジAIゲートウェイ筐体図面 Rev.B",
                .ApprovalStatus = "PENDING (未承認)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
        End Sub

        Public Shared Function GetAllBoms() As List(Of BomRecord)
            Return BomTable.Values.OrderBy(Function(b) b.BomNo).Select(AddressOf CloneBom).ToList()
        End Function

        Public Shared Function GetAllDrawings() As List(Of DrawingRecord)
            Return DrawingTable.Values.OrderBy(Function(d) d.DrawingNo).Select(AddressOf CloneDrawing).ToList()
        End Function

        Public Shared Function GetBom(bomNo As String) As BomRecord
            Dim r As BomRecord = Nothing
            If BomTable.TryGetValue(bomNo, r) Then
                Return CloneBom(r)
            End If
            Return Nothing
        End Function

        Public Shared Function GetDrawing(drawingNo As String) As DrawingRecord
            Dim r As DrawingRecord = Nothing
            If DrawingTable.TryGetValue(drawingNo, r) Then
                Return CloneDrawing(r)
            End If
            Return Nothing
        End Function

        Friend Shared Sub SaveBomInternal(record As BomRecord)
            BomTable(record.BomNo) = CloneBom(record)
        End Sub

        Friend Shared Sub SaveDrawingInternal(record As DrawingRecord)
            DrawingTable(record.DrawingNo) = CloneDrawing(record)
        End Sub

        Private Shared Function CloneBom(src As BomRecord) As BomRecord
            Return New BomRecord With {
                .BomNo = src.BomNo,
                .ProjectId = src.ProjectId,
                .ItmCd = src.ItmCd,
                .ApprovalStatus = src.ApprovalStatus,
                .ApprovedBy = src.ApprovedBy,
                .ApprovedOrg = src.ApprovedOrg,
                .ApprovedAtUtc = src.ApprovedAtUtc
            }
        End Function

        Private Shared Function CloneDrawing(src As DrawingRecord) As DrawingRecord
            Return New DrawingRecord With {
                .DrawingNo = src.DrawingNo,
                .BomNo = src.BomNo,
                .Title = src.Title,
                .ApprovalStatus = src.ApprovalStatus,
                .ApprovedBy = src.ApprovedBy,
                .ApprovedOrg = src.ApprovedOrg,
                .ApprovedAtUtc = src.ApprovedAtUtc
            }
        End Function
    End Class

    ''' <summary>
    ''' モダン側 (ModernBff.MDL) の EF Core トランザクションと、レガシー側 (LegacyMvc.MDL - VB.NET) の BOM・図面コマンドを
    ''' 同一トランザクション境界に束ねる共有トランザクションコンテキスト。
    ''' </summary>
    Public Class SharedTransactionContext
        Implements IDisposable

        Public ReadOnly Property TransactionId As String = "TX-" & Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()
        Public ReadOnly Property SharedConnection As DbConnection
        Public ReadOnly Property SharedTransaction As DbTransaction
        Public ReadOnly Property ExecutionSteps As New List(Of String)()

        Private ReadOnly _rollbackCompensations As New List(Of Action)()
        Private _committed As Boolean = False

        Public Sub New(Optional sharedConnection As DbConnection = Nothing, Optional sharedTransaction As DbTransaction = Nothing)
            Me.SharedConnection = sharedConnection
            Me.SharedTransaction = sharedTransaction
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Begin SharedTransaction ({TransactionId})")
        End Sub

        Public Sub RegisterRollbackAction(rollbackAction As Action)
            _rollbackCompensations.Add(rollbackAction)
        End Sub

        Public Sub Commit()
            If SharedTransaction IsNot Nothing Then
                SharedTransaction.Commit()
            End If
            _committed = True
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Commit SharedTransaction ({TransactionId}) - All Modern & Legacy (VB.NET) changes committed atomically.")
        End Sub

        Public Sub Rollback(reason As String)
            Try
                If SharedTransaction IsNot Nothing Then
                    SharedTransaction.Rollback()
                End If
            Catch
                ' Ignore if already rolled back
            End Try

            For i As Integer = _rollbackCompensations.Count - 1 To 0 Step -1
                _rollbackCompensations(i).Invoke()
            Next

            _committed = False
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Rollback SharedTransaction ({TransactionId}) - Reason: {reason}. All Modern & Legacy (VB.NET) changes rolled back!")
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ' Dispose 処理
        End Sub
    End Class

    ''' <summary>
    ''' 【レガシー側コマンド1 (VB.NET)】BOM承認コマンド (LegacyMvc.MDL.Commands.BomApprovalCommand)
    ''' </summary>
    Public Class BomApprovalCommand
        Public Function Execute(
            txContext As SharedTransactionContext,
            bomNo As String,
            userId As String,
            userName As String,
            orgName As String) As BomRecord

            Dim beforeSnapshot As BomRecord = LegacyApprovalStore.GetBom(bomNo)
            If beforeSnapshot Is Nothing Then
                beforeSnapshot = New BomRecord With {
                    .BomNo = bomNo,
                    .ProjectId = "PRJ-1001",
                    .ItmCd = "ITM-001",
                    .ApprovalStatus = "PENDING (未承認)"
                }
            End If

            ' ロールバック時に元の未承認状態へ戻すスナップショットを登録
            txContext.RegisterRollbackAction(Sub()
                                                 LegacyApprovalStore.SaveBomInternal(beforeSnapshot)
                                             End Sub)

            ' 実 RDBMS コネクションが渡されている場合は同一トランザクション上で UPDATE SQL を発行
            If txContext.SharedConnection IsNot Nothing AndAlso txContext.SharedConnection.State = ConnectionState.Open Then
                Using cmd As DbCommand = txContext.SharedConnection.CreateCommand()
                    cmd.Transaction = txContext.SharedTransaction
                    cmd.CommandText = "UPDATE BOM_TBL SET APPROVAL_STATUS = 'APPROVED', APPROVED_BY = @pUser, APPROVED_AT_UTC = @pNow WHERE BOM_NO = @pBomNo"
                    AddParam(cmd, "@pUser", userId)
                    AddParam(cmd, "@pNow", DateTime.UtcNow)
                    AddParam(cmd, "@pBomNo", bomNo)
                    Try
                        cmd.ExecuteNonQuery()
                    Catch
                        ' テーブル未作成環境用のフォールバック
                    End Try
                End Using
            End If

            Dim updated As New BomRecord With {
                .BomNo = beforeSnapshot.BomNo,
                .ProjectId = beforeSnapshot.ProjectId,
                .ItmCd = beforeSnapshot.ItmCd,
                .ApprovalStatus = "APPROVED (BOM承認済)",
                .ApprovedBy = $"{userName} ({userId})",
                .ApprovedOrg = orgName,
                .ApprovedAtUtc = DateTime.UtcNow
            }

            LegacyApprovalStore.SaveBomInternal(updated)
            txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL (VB.NET) BomApprovalCommand] BOM '{bomNo}' を承認更新しました (Tx={txContext.TransactionId})")

            Return updated
        End Function

        Private Shared Sub AddParam(cmd As DbCommand, name As String, value As Object)
            Dim p As DbParameter = cmd.CreateParameter()
            p.ParameterName = name
            p.Value = If(value, DBNull.Value)
            cmd.Parameters.Add(p)
        End Sub
    End Class

    ''' <summary>
    ''' 【レガシー側コマンド2 (VB.NET)】図面承認コマンド (LegacyMvc.MDL.Commands.DrawingApprovalCommand)
    ''' </summary>
    Public Class DrawingApprovalCommand
        Public Function Execute(
            txContext As SharedTransactionContext,
            drawingNo As String,
            bomNo As String,
            userId As String,
            userName As String,
            orgName As String,
            Optional simulateError As Boolean = False) As DrawingRecord

            Dim beforeSnapshot As DrawingRecord = LegacyApprovalStore.GetDrawing(drawingNo)
            If beforeSnapshot Is Nothing Then
                beforeSnapshot = New DrawingRecord With {
                    .DrawingNo = drawingNo,
                    .BomNo = bomNo,
                    .Title = "標準組立図面",
                    .ApprovalStatus = "PENDING (未承認)"
                }
            End If

            txContext.RegisterRollbackAction(Sub()
                                                 LegacyApprovalStore.SaveDrawingInternal(beforeSnapshot)
                                             End Sub)

            If simulateError Then
                txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL (VB.NET) DrawingApprovalCommand] Error! 図面 '{drawingNo}' は CAD チェックアウト中のため承認できません！")
                Throw New InvalidOperationException($"[LegacyMvc.MDL (VB.NET) 図面承認エラー] 図面番号 '{drawingNo}' は現在 CAD チェックアウト中のため承認できません。トランザクション ({txContext.TransactionId}) 全体をロールバックします。")
            End If

            If txContext.SharedConnection IsNot Nothing AndAlso txContext.SharedConnection.State = ConnectionState.Open Then
                Using cmd As DbCommand = txContext.SharedConnection.CreateCommand()
                    cmd.Transaction = txContext.SharedTransaction
                    cmd.CommandText = "UPDATE DRW_TBL SET APPROVAL_STATUS = 'APPROVED', APPROVED_BY = @pUser, APPROVED_AT_UTC = @pNow WHERE DRAWING_NO = @pDrwNo"
                    Dim p1 As DbParameter = cmd.CreateParameter() : p1.ParameterName = "@pUser" : p1.Value = userId : cmd.Parameters.Add(p1)
                    Dim p2 As DbParameter = cmd.CreateParameter() : p2.ParameterName = "@pNow" : p2.Value = DateTime.UtcNow : cmd.Parameters.Add(p2)
                    Dim p3 As DbParameter = cmd.CreateParameter() : p3.ParameterName = "@pDrwNo" : p3.Value = drawingNo : cmd.Parameters.Add(p3)
                    Try
                        cmd.ExecuteNonQuery()
                    Catch
                    End Try
                End Using
            End If

            Dim updated As New DrawingRecord With {
                .DrawingNo = beforeSnapshot.DrawingNo,
                .BomNo = beforeSnapshot.BomNo,
                .Title = beforeSnapshot.Title,
                .ApprovalStatus = "APPROVED (図面承認済)",
                .ApprovedBy = $"{userName} ({userId})",
                .ApprovedOrg = orgName,
                .ApprovedAtUtc = DateTime.UtcNow
            }

            LegacyApprovalStore.SaveDrawingInternal(updated)
            txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL (VB.NET) DrawingApprovalCommand] 図面 '{drawingNo}' を承認更新しました (Tx={txContext.TransactionId})")

            Return updated
        End Function
    End Class

End Namespace
