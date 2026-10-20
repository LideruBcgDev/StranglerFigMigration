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
        Public Property ApprovalStatus As String = "DRAFT (未申請)"
        Public Property ApprovedBy As String
        Public Property ApprovedOrg As String
        Public Property ApprovedAtUtc As DateTime?
    End Class

    Public Class DrawingRecord
        Public Property DrawingNo As String = String.Empty
        Public Property BomNo As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property Revision As String = "Rev.A"
        Public Property ApprovalStatus As String = "DRAFT (未申請)"
        Public Property ApprovedBy As String
        Public Property ApprovedOrg As String
        Public Property ApprovedAtUtc As DateTime?
    End Class

    ''' <summary>
    ''' レガシー側 (LegacyMvc.MDL - VB.NET) の BOM (BOM_TBL) / 図面 (DRW_TBL) 管理・承認ストア。
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
                .ApprovalStatus = "PENDING (承認待ち)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
            BomTable("BOM-2026-002") = New BomRecord With {
                .BomNo = "BOM-2026-002",
                .ProjectId = "PRJ-1002",
                .ItmCd = "ITM-002",
                .ApprovalStatus = "PENDING (承認待ち)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }

            DrawingTable("DRW-2026-001") = New DrawingRecord With {
                .DrawingNo = "DRW-2026-001",
                .BomNo = "BOM-2026-001",
                .ItmCd = "ITM-001",
                .Title = "スマートセンサー組立図面",
                .Revision = "Rev.A",
                .ApprovalStatus = "PENDING (承認待ち)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
            DrawingTable("DRW-2026-002") = New DrawingRecord With {
                .DrawingNo = "DRW-2026-002",
                .BomNo = "BOM-2026-002",
                .ItmCd = "ITM-002",
                .Title = "エッジAIゲートウェイ筐体図面",
                .Revision = "Rev.B",
                .ApprovalStatus = "PENDING (承認待ち)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
            DrawingTable("DRW-2026-003") = New DrawingRecord With {
                .DrawingNo = "DRW-2026-003",
                .BomNo = "BOM-2026-003",
                .ItmCd = "ITM-003",
                .Title = "クラウド監視エージェント構成図",
                .Revision = "Rev.A",
                .ApprovalStatus = "DRAFT (未申請)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
            DrawingTable("drw004") = New DrawingRecord With {
                .DrawingNo = "drw004",
                .BomNo = "BOM-2026-001",
                .ItmCd = "ITM-001",
                .Title = "第4世代 制御ユニット回路図 (DeepLink検証用 drw004)",
                .Revision = "Rev.C",
                .ApprovalStatus = "PENDING (承認待ち)",
                .ApprovedBy = Nothing,
                .ApprovedOrg = Nothing,
                .ApprovedAtUtc = Nothing
            }
        End Sub

        Public Shared Function GetAllBoms() As List(Of BomRecord)
            Return BomTable.Values.OrderBy(Function(b) b.BomNo).Select(AddressOf CloneBom).ToList()
        End Function

        Public Shared Function GetAllDrawings(Optional keyword As String = Nothing) As List(Of DrawingRecord)
            Dim list = DrawingTable.Values.Select(AddressOf CloneDrawing)
            If Not String.IsNullOrWhiteSpace(keyword) Then
                Dim kw As String = keyword.Trim().Trim("'"c, """"c).ToLowerInvariant()
                If Not String.IsNullOrWhiteSpace(kw) Then
                    list = list.Where(Function(d) _
                        (d.DrawingNo IsNot Nothing AndAlso d.DrawingNo.ToLowerInvariant().Contains(kw)) OrElse
                        (d.Title IsNot Nothing AndAlso d.Title.ToLowerInvariant().Contains(kw)) OrElse
                        (d.ItmCd IsNot Nothing AndAlso d.ItmCd.ToLowerInvariant().Contains(kw)) OrElse
                        (d.BomNo IsNot Nothing AndAlso d.BomNo.ToLowerInvariant().Contains(kw)))
                End If
            End If
            Return list.OrderBy(Function(d) d.DrawingNo).ToList()
        End Function

        Public Shared Function GetBom(bomNo As String) As BomRecord
            If String.IsNullOrWhiteSpace(bomNo) Then Return Nothing
            Dim r As BomRecord = Nothing
            If BomTable.TryGetValue(bomNo, r) Then
                Return CloneBom(r)
            End If
            Return Nothing
        End Function

        Public Shared Function GetDrawing(drawingNo As String) As DrawingRecord
            If String.IsNullOrWhiteSpace(drawingNo) Then Return Nothing
            Dim r As DrawingRecord = Nothing
            If DrawingTable.TryGetValue(drawingNo, r) Then
                Return CloneDrawing(r)
            End If
            Return Nothing
        End Function

        Public Shared Function UpsertDrawing(drawingNo As String, title As String, itmCd As String, bomNo As String, revision As String) As DrawingRecord
            Dim existing As DrawingRecord = GetDrawing(drawingNo)
            Dim rec As New DrawingRecord With {
                .DrawingNo = drawingNo.Trim(),
                .Title = title.Trim(),
                .ItmCd = If(String.IsNullOrWhiteSpace(itmCd), "ITM-001", itmCd.Trim()),
                .BomNo = If(String.IsNullOrWhiteSpace(bomNo), "BOM-" & drawingNo.Trim(), bomNo.Trim()),
                .Revision = If(String.IsNullOrWhiteSpace(revision), "Rev.A", revision.Trim()),
                .ApprovalStatus = If(existing IsNot Nothing, existing.ApprovalStatus, "DRAFT (未申請)"),
                .ApprovedBy = If(existing IsNot Nothing, existing.ApprovedBy, Nothing),
                .ApprovedOrg = If(existing IsNot Nothing, existing.ApprovedOrg, Nothing),
                .ApprovedAtUtc = If(existing IsNot Nothing, existing.ApprovedAtUtc, Nothing)
            }
            DrawingTable(rec.DrawingNo) = CloneDrawing(rec)
            Return rec
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
                .ItmCd = src.ItmCd,
                .Title = src.Title,
                .Revision = src.Revision,
                .ApprovalStatus = src.ApprovalStatus,
                .ApprovedBy = src.ApprovedBy,
                .ApprovedOrg = src.ApprovedOrg,
                .ApprovedAtUtc = src.ApprovedAtUtc
            }
        End Function
    End Class

    ''' <summary>
    ''' 【レガシー側 図面(DRW_TBL)管理コマンド (VB.NET)】検索・登録処理
    ''' </summary>
    Public Class LegacyDrawingCrudCommand
        Public Function Search(Optional keyword As String = Nothing) As List(Of DrawingRecord)
            Return LegacyApprovalStore.GetAllDrawings(keyword)
        End Function

        Public Function RegisterOrUpdate(drawingNo As String, title As String, itmCd As String, bomNo As String, revision As String) As DrawingRecord
            If String.IsNullOrWhiteSpace(drawingNo) Then
                Throw New ArgumentException("図面番号 (DRAWING_NO) は必須です。")
            End If
            If String.IsNullOrWhiteSpace(title) Then
                Throw New ArgumentException("図面名称 (TITLE) は必須です。")
            End If
            Return LegacyApprovalStore.UpsertDrawing(drawingNo, title, itmCd, bomNo, revision)
        End Function
    End Class

    ''' <summary>
    ''' モダン側 (ModernBff.MDL) の EF Core トランザクションと、レガシー側 (LegacyMvc.MDL - VB.NET) の品目・BOM・図面コマンドを
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
            End Try

            For i As Integer = _rollbackCompensations.Count - 1 To 0 Step -1
                _rollbackCompensations(i).Invoke()
            Next

            _committed = False
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Rollback SharedTransaction ({TransactionId}) - Reason: {reason}. All Modern & Legacy (VB.NET) changes rolled back!")
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
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

            If String.IsNullOrWhiteSpace(bomNo) Then Return Nothing

            Dim beforeSnapshot As BomRecord = LegacyApprovalStore.GetBom(bomNo)
            If beforeSnapshot Is Nothing Then
                beforeSnapshot = New BomRecord With {
                    .BomNo = bomNo,
                    .ProjectId = "PRJ-1001",
                    .ItmCd = "ITM-001",
                    .ApprovalStatus = "PENDING (未承認)"
                }
            End If

            txContext.RegisterRollbackAction(Sub()
                                                 LegacyApprovalStore.SaveBomInternal(beforeSnapshot)
                                             End Sub)

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

            If String.IsNullOrWhiteSpace(drawingNo) Then Return Nothing

            Dim beforeSnapshot As DrawingRecord = LegacyApprovalStore.GetDrawing(drawingNo)
            If beforeSnapshot Is Nothing Then
                beforeSnapshot = New DrawingRecord With {
                    .DrawingNo = drawingNo,
                    .BomNo = bomNo,
                    .ItmCd = "ITM-001",
                    .Title = "標準組立図面",
                    .Revision = "Rev.A",
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
                .ItmCd = beforeSnapshot.ItmCd,
                .Title = beforeSnapshot.Title,
                .Revision = beforeSnapshot.Revision,
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
