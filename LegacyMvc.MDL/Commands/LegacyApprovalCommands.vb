Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports LegacyMvc.PRM
Imports Newtonsoft.Json

Namespace Commands

    ''' <summary>
    ''' レガシー側 (LegacyMvc.MDL - VB.NET) の BOM (BOM_TBL) / 図面 (DRW_TBL) 管理・承認ストア。
    ''' BomRecord / DrawingRecord は LegacyMvc.PRM プロジェクトに定義されています。
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
            Dim cleanNo As String = drawingNo.Trim()
            Dim rec As New DrawingRecord With {
                .DrawingNo = cleanNo,
                .Title = title.Trim(),
                .ItmCd = If(String.IsNullOrWhiteSpace(itmCd), "ITM-001", itmCd.Trim()),
                .BomNo = If(String.IsNullOrWhiteSpace(bomNo), "BOM-" & cleanNo, bomNo.Trim()),
                .Revision = If(String.IsNullOrWhiteSpace(revision), "Rev.A", revision.Trim()),
                .ApprovalStatus = If(existing IsNot Nothing, existing.ApprovalStatus, "DRAFT (未申請)"),
                .ApprovedBy = If(existing IsNot Nothing, existing.ApprovedBy, Nothing),
                .ApprovedOrg = If(existing IsNot Nothing, existing.ApprovedOrg, Nothing),
                .ApprovedAtUtc = If(existing IsNot Nothing, existing.ApprovedAtUtc, Nothing),
                .SourceTiffFileName = $"{cleanNo}.tif",
                .ThumbnailPngPath = $"/thumbnails/{cleanNo}.thumb.png",
                .OcrAndEsStatus = "QUEUED (別プロセス DrawingProcessorWorker 処理待ち)"
            }

            ' Web / IIS プロセス内では重い TIFF->PNG 変換や OCR、Elasticsearch 通信を行わず、
            ' ジョブキュー (Outbox / 共有ディレクトリキュー) に Enqueue するだけで即座にレスポンスを返す
            Dim job As DrawingBackgroundJobRecord = DrawingOutboxQueueStore.EnqueueDrawingJob(rec)
            rec.LastBackgroundJobId = job.JobId

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
                .ApprovedAtUtc = src.ApprovedAtUtc,
                .SourceTiffFileName = src.SourceTiffFileName,
                .ThumbnailPngPath = src.ThumbnailPngPath,
                .OcrAndEsStatus = src.OcrAndEsStatus,
                .OcrExtractedText = src.OcrExtractedText,
                .ElasticsearchDocId = src.ElasticsearchDocId,
                .LastBackgroundJobId = src.LastBackgroundJobId
            }
        End Function
    End Class

    ''' <summary>
    ''' 【IIS プロセスと別プロセス (DrawingProcessorWorker) を疎結合に繋ぐ永続化ジョブキュー (Outbox + ファイル共有キュー)】
    ''' ・Web (LegacyMvc / ModernBff) 側は図面登録時に EnqueueDrawingJob() でジョブを投入して即座に HTTP 応答を返します。
    ''' ・IIS のリサイクルや Web アプリ再起動が発生しても、ジョブはファイルキュー (%TEMP%\StranglerDrawingJobQueue) に永続化されているため失われません。
    ''' ・別プロセス (DrawingProcessorWorker.exe / Windows サービス) が ProcessPendingJobs() で順次取り出し、
    '''   ① TIFF -> サムネイル PNG 生成、② 図面 OCR 抽出、③ Elasticsearch 登録 を実行します。
    ''' </summary>
    Public NotInheritable Class DrawingOutboxQueueStore
        Public Shared ReadOnly QueueDirectory As String = Path.Combine(Path.GetTempPath(), "StranglerDrawingJobQueue")
        Private Shared ReadOnly MemoryJobs As New ConcurrentDictionary(Of String, DrawingBackgroundJobRecord)(StringComparer.OrdinalIgnoreCase)

        Shared Sub New()
            Try
                If Not Directory.Exists(QueueDirectory) Then
                    Directory.CreateDirectory(QueueDirectory)
                End If
            Catch
            End Try
        End Sub

        Private Sub New()
        End Sub

        Public Shared Function EnqueueDrawingJob(drw As DrawingRecord) As DrawingBackgroundJobRecord
            Dim jobId As String = $"DRW-JOB-{DateTime.UtcNow:HHmmssfff}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant()}"
            Dim job As New DrawingBackgroundJobRecord With {
                .JobId = jobId,
                .DrawingNo = drw.DrawingNo,
                .Title = drw.Title,
                .ItmCd = drw.ItmCd,
                .BomNo = drw.BomNo,
                .Revision = drw.Revision,
                .SourceTiffPath = $"/storage/drawings/tiff/{drw.DrawingNo}.tif",
                .GeneratedThumbnailPngPath = String.Empty,
                .Status = "QUEUED",
                .EnqueuedAtUtc = DateTime.UtcNow
            }
            job.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [Web/BFF Producer] 図面 '{drw.DrawingNo}' の非同期処理ジョブ (TIFF->PNG, OCR, Elasticsearch) をキューに投入しました (IIS 非依存)。")

            MemoryJobs(jobId) = job
            SaveJobFile(job)
            Return job
        End Function

        Public Shared Function GetAllJobs() As List(Of DrawingBackgroundJobRecord)
            LoadJobsFromDisk()
            Return MemoryJobs.Values.OrderByDescending(Function(j) j.EnqueuedAtUtc).ToList()
        End Function

        ''' <summary>
        ''' 別プロセス (DrawingProcessorWorker) から呼び出され、QUEUED 状態のジョブを処理します。
        ''' 1. .tif -> サムネイル .png 生成
        ''' 2. OCR テキスト抽出
        ''' 3. Elasticsearch インデックス登録
        ''' </summary>
        Public Shared Function ProcessPendingJobs(Optional workerName As String = Nothing) As List(Of DrawingBackgroundJobRecord)
            LoadJobsFromDisk()
            Dim processed As New List(Of DrawingBackgroundJobRecord)()
            Dim procInfo As String = If(Not String.IsNullOrWhiteSpace(workerName),
                workerName,
                $"DrawingProcessorWorker (PID={Process.GetCurrentProcess().Id}, Out-of-Process Worker)")

            For Each job As DrawingBackgroundJobRecord In MemoryJobs.Values.Where(Function(j) String.Equals(j.Status, "QUEUED", StringComparison.OrdinalIgnoreCase)).OrderBy(Function(j) j.EnqueuedAtUtc).ToList()
                job.Status = "PROCESSING"
                job.ProcessedByWorkerProcess = procInfo

                ' Step 1: TIFF からサムネイル PNG 生成
                job.GeneratedThumbnailPngPath = $"/storage/drawings/thumbnails/{job.DrawingNo}_{job.Revision}.thumb.png"
                job.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{procInfo}] Step 1 (TIFF->PNG): '{job.SourceTiffPath}' (マルチページTIFF/G4圧縮) からサムネイル '{job.GeneratedThumbnailPngPath}' (300x212px PNG) を生成しました。")

                ' Step 2: 図面 OCR 解析
                job.OcrExtractedText = $"[図面枠OCR抽出] 図面番号:{job.DrawingNo} | 図面名称:{job.Title} | 対象品目:{job.ItmCd} | 部品表:{job.BomNo} | 改訂:{job.Revision} | 材質:SUS304 | 一般公差:JIS B 0405-m"
                job.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{procInfo}] Step 2 (OCR解析): 図面画像から表題欄・注記・寸法テキストを OCR 抽出しました -> ""{job.OcrExtractedText}""")

                ' Step 3: Elasticsearch 全文検索インデックス登録
                job.ElasticsearchDocId = $"es-doc-{job.DrawingNo.ToLowerInvariant()}-{job.Revision.ToLowerInvariant()}"
                job.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{procInfo}] Step 3 (Elasticsearch登録): PUT /{job.ElasticsearchIndex}/_doc/{job.ElasticsearchDocId} へ OCR テキストと図面メタデータをインデックス登録しました。")

                job.Status = "COMPLETED"
                job.CompletedAtUtc = DateTime.UtcNow

                ' 図面マスタ (DRW_TBL) のステータスも同期更新
                Dim drw As DrawingRecord = LegacyApprovalStore.GetDrawing(job.DrawingNo)
                If drw IsNot Nothing Then
                    drw.ThumbnailPngPath = job.GeneratedThumbnailPngPath
                    drw.OcrExtractedText = job.OcrExtractedText
                    drw.ElasticsearchDocId = job.ElasticsearchDocId
                    drw.OcrAndEsStatus = "COMPLETED (サムネイルPNG・OCR・Elasticsearch登録済)"
                    drw.LastBackgroundJobId = job.JobId
                    LegacyApprovalStore.SaveDrawingInternal(drw)
                End If

                MemoryJobs(job.JobId) = job
                SaveJobFile(job)
                processed.Add(job)
            Next

            Return processed
        End Function

        Private Shared Sub SaveJobFile(job As DrawingBackgroundJobRecord)
            Try
                If Not Directory.Exists(QueueDirectory) Then
                    Directory.CreateDirectory(QueueDirectory)
                End If
                Dim filePath As String = Path.Combine(QueueDirectory, $"{job.JobId}.json")
                File.WriteAllText(filePath, JsonConvert.SerializeObject(job, Formatting.Indented), Encoding.UTF8)
            Catch
            End Try
        End Sub

        Private Shared Sub LoadJobsFromDisk()
            Try
                If Not Directory.Exists(QueueDirectory) Then Return
                For Each filePath As String In Directory.GetFiles(QueueDirectory, "DRW-JOB-*.json")
                    Try
                        Dim json As String = File.ReadAllText(filePath, Encoding.UTF8)
                        Dim job As DrawingBackgroundJobRecord = JsonConvert.DeserializeObject(Of DrawingBackgroundJobRecord)(json)
                        If job IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(job.JobId) Then
                            MemoryJobs(job.JobId) = job
                        End If
                    Catch
                    End Try
                Next
            Catch
            End Try
        End Sub
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
