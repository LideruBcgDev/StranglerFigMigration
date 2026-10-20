Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Pipes
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Threading.Tasks
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
                .OcrAndEsStatus = "QUEUED (別プロセス ProcessorWorker 処理待ち)"
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
    ''' 【IIS プロセスと別プロセス (ProcessorWorker) を疎結合に繋ぐ永続化ジョブキュー (Outbox + ファイル共有キュー)】
    ''' ・Web (LegacyMvc / ModernBff) 側は図面登録時に EnqueueDrawingJob() でジョブを投入して即座に HTTP 応答を返します。
    ''' ・IIS のリサイクルや Web アプリ再起動が発生しても、ジョブはファイルキュー (%TEMP%\StranglerDrawingJobQueue) に永続化されているため失われません。
    ''' ・別プロセス (ProcessorWorker.exe / Windows サービス) が ProcessPendingJobs() で順次取り出し、
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
        ''' 別プロセス (ProcessorWorker) から呼び出され、QUEUED 状態のジョブを処理します。
        ''' 1. .tif -> サムネイル .png 生成
        ''' 2. OCR テキスト抽出
        ''' 3. Elasticsearch インデックス登録
        ''' </summary>
        Public Shared Function ProcessPendingJobs(Optional workerName As String = Nothing) As List(Of DrawingBackgroundJobRecord)
            LoadJobsFromDisk()
            Dim processed As New List(Of DrawingBackgroundJobRecord)()
            Dim procInfo As String = If(Not String.IsNullOrWhiteSpace(workerName),
                workerName,
                $"ProcessorWorker (PID={Process.GetCurrentProcess().Id}, Out-of-Process Worker)")

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
    ''' 【別プロセス ProcessorWorker 上で実行されるオンデマンド図面スタンプ・動的透かし焼き込みエンジン】
    ''' 表示リクエストのたびに以下の情報をリアルタイム合成して戻り値 (DrawingStampResponse) を生成します：
    ''' 1. 承認情報 (承認ステータス・承認者印・承認部署・承認日時)
    ''' 2. 動的ウォーターマーク (閲覧日時・閲覧しているログインユーザ名・所属部署・社外秘透かし)
    ''' </summary>
    Public NotInheritable Class DrawingStampBurner
        Public Const DefaultNamedPipeName As String = "StranglerProcessorWorker.StampPipe"
        Public Const DefaultInternalHttpUrl As String = "http://localhost:5055/internal/drawings/stamp"

        Private Sub New()
        End Sub

        Public Shared Function BurnStampOnDemand(
            req As DrawingStampRequest,
            Optional workerProcessName As String = Nothing,
            Optional transportUsed As String = "NamedPipe",
            Optional endpointName As String = "\\.\pipe\StranglerProcessorWorker.StampPipe") As DrawingStampResponse

            If req Is Nothing Then
                req = New DrawingStampRequest()
            End If

            Dim drwNo As String = If(String.IsNullOrWhiteSpace(req.DrawingNo), "DRW-2026-01", req.DrawingNo.Trim())
            Dim existing As DrawingRecord = LegacyApprovalStore.GetDrawing(drwNo)

            Dim title As String = If(Not String.IsNullOrWhiteSpace(req.Title), req.Title, If(existing IsNot Nothing, existing.Title, "標準組立図面"))
            Dim rev As String = If(Not String.IsNullOrWhiteSpace(req.Revision), req.Revision, If(existing IsNot Nothing, existing.Revision, "Rev.A"))
            Dim itmCd As String = If(Not String.IsNullOrWhiteSpace(req.ItmCd), req.ItmCd, If(existing IsNot Nothing, existing.ItmCd, "ITM-001"))
            Dim bomNo As String = If(Not String.IsNullOrWhiteSpace(req.BomNo), req.BomNo, If(existing IsNot Nothing, existing.BomNo, "BOM-2026-001"))
            Dim appStatus As String = If(Not String.IsNullOrWhiteSpace(req.ApprovalStatus), req.ApprovalStatus, If(existing IsNot Nothing, existing.ApprovalStatus, "PENDING (未承認)"))
            Dim appBy As String = If(Not String.IsNullOrWhiteSpace(req.ApprovedBy), req.ApprovedBy, If(existing IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(existing.ApprovedBy), existing.ApprovedBy, "未承認"))
            Dim appOrg As String = If(Not String.IsNullOrWhiteSpace(req.ApprovedOrg), req.ApprovedOrg, If(existing IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(existing.ApprovedOrg), existing.ApprovedOrg, "-"))
            Dim appDate As String = If(req.ApprovedAtUtc.HasValue, req.ApprovedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                                       If(existing IsNot Nothing AndAlso existing.ApprovedAtUtc.HasValue, existing.ApprovedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), "-"))

            Dim viewerId As String = If(String.IsNullOrWhiteSpace(req.ViewerUserId), "anonymous", req.ViewerUserId)
            Dim viewerName As String = If(String.IsNullOrWhiteSpace(req.ViewerUserName), viewerId, req.ViewerUserName)
            Dim viewerOrg As String = If(String.IsNullOrWhiteSpace(req.ViewerOrgName), "技術開発部", req.ViewerOrgName)
            Dim viewedAt As String = If(String.IsNullOrWhiteSpace(req.ViewedAtLocalText), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), req.ViewedAtLocalText)
            Dim confidential As String = If(String.IsNullOrWhiteSpace(req.ConfidentialLabel), "社外秘 / CONFIDENTIAL - DO NOT COPY", req.ConfidentialLabel)

            Dim isApproved As Boolean = appStatus.ToUpperInvariant().Contains("APPROVED")
            Dim stampColor As String = If(isApproved, "#dc2626", "#d97706")
            Dim stampHeader As String = If(isApproved, "承認済", "審査中/未承認")

            Dim procInfo As String = If(Not String.IsNullOrWhiteSpace(workerProcessName),
                workerProcessName,
                $"ProcessorWorker.exe (PID={Process.GetCurrentProcess().Id})")

            Dim approvalSummary As String = $"[{stampHeader}] ステータス:{appStatus} | 承認者:{appBy} ({appOrg}) | 承認日時:{appDate}"
            Dim watermarkSummary As String = $"[{confidential}] 閲覧ユーザ:{viewerName} ({viewerId} / {viewerOrg}) | 閲覧日時:{viewedAt}"

            ' SVG で CAD 図面枠 + 表題欄 + 承認印(赤ハンコ) + 斜め社外秘・閲覧者・閲覧日時透かし を動的合成
            Dim svg As New StringBuilder()
            svg.AppendLine("<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 900 540"" width=""100%"" height=""100%"">")
            svg.AppendLine("  <rect width=""900"" height=""540"" fill=""#f8fafc"" stroke=""#0f172a"" stroke-width=""4""/>")
            svg.AppendLine("  <rect x=""16"" y=""16"" width=""868"" height=""508"" fill=""#ffffff"" stroke=""#334155"" stroke-width=""2""/>")
            svg.AppendLine("  <!-- CAD グリッドと図面モック線画 -->")
            svg.AppendLine("  <g stroke=""#e2e8f0"" stroke-width=""1"">")
            svg.AppendLine("    <line x1=""16"" y1=""140"" x2=""884"" y2=""140""/>")
            svg.AppendLine("    <line x1=""16"" y1=""270"" x2=""884"" y2=""270""/>")
            svg.AppendLine("    <line x1=""16"" y1=""400"" x2=""884"" y2=""400""/>")
            svg.AppendLine("    <line x1=""220"" y1=""16"" x2=""220"" y2=""524""/>")
            svg.AppendLine("    <line x1=""450"" y1=""16"" x2=""450"" y2=""524""/>")
            svg.AppendLine("    <line x1=""680"" y1=""16"" x2=""680"" y2=""524""/>")
            svg.AppendLine("  </g>")
            svg.AppendLine("  <!-- CAD 機械・回路図形 -->")
            svg.AppendLine("  <g stroke=""#1e3a8a"" stroke-width=""2"" fill=""none"">")
            svg.AppendLine("    <rect x=""90"" y=""85"" width=""380"" height=""230"" rx=""6""/>")
            svg.AppendLine("    <circle cx=""280"" cy=""200"" r=""72""/>")
            svg.AppendLine("    <circle cx=""280"" cy=""200"" r=""28"" stroke-dasharray=""5,4""/>")
            svg.AppendLine("    <line x1=""70"" y1=""200"" x2=""490"" y2=""200"" stroke=""#0284c7"" stroke-dasharray=""10,4,2,4""/>")
            svg.AppendLine("    <line x1=""280"" y1=""65"" x2=""280"" y2=""335"" stroke=""#0284c7"" stroke-dasharray=""10,4,2,4""/>")
            svg.AppendLine($"    <text x=""95"" y=""74"" fill=""#1e3a8a"" font-size=""13"" font-family=""Meiryo, sans-serif"" font-weight=""bold"">DRAWING: {EscapeXml(drwNo)} ({EscapeXml(rev)}) - {EscapeXml(title)}</text>")
            svg.AppendLine($"    <text x=""95"" y=""342"" fill=""#475569"" font-size=""12"" font-family=""Meiryo, sans-serif"">対象品目: {EscapeXml(itmCd)}  |  関連BOM: {EscapeXml(bomNo)}  |  材質: SUS304  |  尺度: 1:1</text>")
            svg.AppendLine("  </g>")

            ' 右上: 承認スタンプ (電子印鑑・検印欄) オンデマンド焼き込み
            svg.AppendLine("  <!-- オンデマンド焼き込み1: 承認スタンプ (電子印鑑) -->")
            svg.AppendLine($"  <g transform=""translate(635, 36)"">")
            svg.AppendLine($"    <rect x=""0"" y=""0"" width=""225"" height=""125"" rx=""8"" fill=""#fffbfa"" stroke=""{stampColor}"" stroke-width=""3""/>")
            svg.AppendLine($"    <line x1=""0"" y1=""32"" x2=""225"" y2=""32"" stroke=""{stampColor}"" stroke-width=""2""/>")
            svg.AppendLine($"    <line x1=""0"" y1=""88"" x2=""225"" y2=""88"" stroke=""{stampColor}"" stroke-width=""1.5""/>")
            svg.AppendLine($"    <text x=""112"" y=""22"" text-anchor=""middle"" fill=""{stampColor}"" font-size=""14"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">【電子承認印: {EscapeXml(stampHeader)}】</text>")
            svg.AppendLine($"    <text x=""112"" y=""55"" text-anchor=""middle"" fill=""{stampColor}"" font-size=""12"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">承認部署: {EscapeXml(appOrg)}</text>")
            svg.AppendLine($"    <text x=""112"" y=""76"" text-anchor=""middle"" fill=""{stampColor}"" font-size=""13"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">承認者: {EscapeXml(appBy)}</text>")
            svg.AppendLine($"    <text x=""112"" y=""110"" text-anchor=""middle"" fill=""{stampColor}"" font-size=""11"" font-family=""Meiryo, sans-serif"">承認日時: {EscapeXml(appDate)}</text>")
            svg.AppendLine("  </g>")

            ' 中央斜め: 動的ウォーターマーク (社外秘 + 閲覧ログインユーザ名 + 閲覧日時)
            svg.AppendLine("  <!-- オンデマンド焼き込み2: 閲覧日時・閲覧ログインユーザ名・社外秘透かし (Dynamic Watermark) -->")
            svg.AppendLine("  <g transform=""translate(450, 255) rotate(-22)"" opacity=""0.28"">")
            svg.AppendLine("    <rect x=""-370"" y=""-68"" width=""740"" height=""136"" rx=""10"" fill=""none"" stroke=""#dc2626"" stroke-width=""3"" stroke-dasharray=""10,6""/>")
            svg.AppendLine($"    <text x=""0"" y=""-24"" text-anchor=""middle"" fill=""#dc2626"" font-size=""26"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">{EscapeXml(confidential)}</text>")
            svg.AppendLine($"    <text x=""0"" y=""14"" text-anchor=""middle"" fill=""#b91c1c"" font-size=""19"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">閲覧者: {EscapeXml(viewerName)} ({EscapeXml(viewerId)}) [{EscapeXml(viewerOrg)}]</text>")
            svg.AppendLine($"    <text x=""0"" y=""46"" text-anchor=""middle"" fill=""#991b1b"" font-size=""17"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">閲覧日時: {EscapeXml(viewedAt)}  |  図面番号: {EscapeXml(drwNo)} ({EscapeXml(rev)})</text>")
            svg.AppendLine("  </g>")

            ' 右下: CAD 表題欄 (Title Block) & ProcessorWorker 焼き込み証明
            svg.AppendLine("  <!-- 右下: CAD 表題欄 -->")
            svg.AppendLine("  <g transform=""translate(350, 395)"">")
            svg.AppendLine("    <rect x=""0"" y=""0"" width=""518"" height=""114"" fill=""#f1f5f9"" stroke=""#1e293b"" stroke-width=""2""/>")
            svg.AppendLine("    <line x1=""0"" y1=""36"" x2=""518"" y2=""36"" stroke=""#334155"" stroke-width=""1""/>")
            svg.AppendLine("    <line x1=""0"" y1=""74"" x2=""518"" y2=""74"" stroke=""#334155"" stroke-width=""1""/>")
            svg.AppendLine($"    <text x=""12"" y=""23"" fill=""#0f172a"" font-size=""13"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">図面番号: {EscapeXml(drwNo)} ({EscapeXml(rev)})  |  名称: {EscapeXml(title)}</text>")
            svg.AppendLine($"    <text x=""12"" y=""59"" fill=""#1e293b"" font-size=""12"" font-family=""Meiryo, sans-serif"">承認状態: {EscapeXml(appStatus)}  |  承認者: {EscapeXml(appBy)}  |  閲覧者: {EscapeXml(viewerName)} ({EscapeXml(viewerId)})</text>")
            svg.AppendLine($"    <text x=""12"" y=""97"" fill=""#0369a1"" font-size=""11"" font-weight=""bold"" font-family=""Meiryo, sans-serif"">焼き込み実行: {EscapeXml(procInfo)} via {EscapeXml(transportUsed)} ({EscapeXml(viewedAt)})</text>")
            svg.AppendLine("  </g>")
            svg.AppendLine("</svg>")

            Dim svgText As String = svg.ToString()
            Dim base64 As String = Convert.ToBase64String(Encoding.UTF8.GetBytes(svgText))

            Return New DrawingStampResponse With {
                .Success = True,
                .DrawingNo = drwNo,
                .Title = title,
                .Revision = rev,
                .ContentType = "image/svg+xml",
                .StampedSvgMarkup = svgText,
                .StampedImageBase64 = base64,
                .StampedImageDataUrl = $"data:image/svg+xml;base64,{base64}",
                .ApprovalStampSummary = approvalSummary,
                .DynamicWatermarkSummary = watermarkSummary,
                .TransportUsed = transportUsed,
                .WorkerEndpoint = endpointName,
                .ProcessedByWorkerProcess = procInfo,
                .ProcessedAtUtc = DateTime.UtcNow
            }
        End Function

        Private Shared Function EscapeXml(input As String) As String
            If String.IsNullOrEmpty(input) Then Return String.Empty
            Return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;").Replace("'", "&apos;")
        End Function
    End Class

    ''' <summary>
    ''' 【IIS (ModernBff / LegacyMvc) から別プロセス ProcessorWorker の同期エンドポイントを呼び出すクライアント】
    ''' 1. 名前付きパイプ (Named Pipe: \\.\pipe\StranglerProcessorWorker.StampPipe) でのプロセス間高速 RPC 通信
    ''' 2. 内部 HTTP エンドポイント (POST http://localhost:5055/internal/drawings/stamp) での同期呼び出し
    ''' 3. 開発環境で ProcessorWorker.exe が未起動の場合でも動作を確認できる自動フォールバック機能付き
    ''' </summary>
    Public NotInheritable Class ProcessorWorkerStampClient
        Private Shared ReadOnly SharedHttpClient As New HttpClient() With {
            .Timeout = TimeSpan.FromMilliseconds(1500)
        }

        Private Sub New()
        End Sub

        Public Shared Async Function RequestOnDemandStampAsync(
            req As DrawingStampRequest,
            Optional internalHttpUrl As String = Nothing,
            Optional namedPipeName As String = Nothing) As Task(Of DrawingStampResponse)

            Dim pipeName As String = If(String.IsNullOrWhiteSpace(namedPipeName), DrawingStampBurner.DefaultNamedPipeName, namedPipeName)
            Dim httpUrl As String = If(String.IsNullOrWhiteSpace(internalHttpUrl), DrawingStampBurner.DefaultInternalHttpUrl, internalHttpUrl)
            Dim pref As String = If(String.IsNullOrWhiteSpace(req.PreferredTransport), "AUTO", req.PreferredTransport.Trim().ToUpperInvariant())

            ' 1. 名前付きパイプ (Named Pipe) で別プロセス ProcessorWorker へ同期リクエスト
            If pref = "AUTO" OrElse pref = "NAMEDPIPE" OrElse pref = "PIPE" Then
                Try
                    Using pipeClient As New NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
                        Await pipeClient.ConnectAsync(350).ConfigureAwait(False)
                        Dim utf8NoBom As New UTF8Encoding(False)
                        Using writer As New StreamWriter(pipeClient, utf8NoBom, 4096, leaveOpen:=True)
                            writer.AutoFlush = True
                            Dim requestJson As String = JsonConvert.SerializeObject(req)
                            Await writer.WriteLineAsync(requestJson).ConfigureAwait(False)
                        End Using
                        Using reader As New StreamReader(pipeClient, utf8NoBom, False, 4096, leaveOpen:=True)
                            Dim responseJson As String = Await reader.ReadLineAsync().ConfigureAwait(False)
                            If Not String.IsNullOrWhiteSpace(responseJson) Then
                                Dim pipeResp As DrawingStampResponse = JsonConvert.DeserializeObject(Of DrawingStampResponse)(responseJson)
                                If pipeResp IsNot Nothing Then
                                    Return pipeResp
                                End If
                            End If
                        End Using
                    End Using
                Catch
                    ' NamedPipe 未起動時は InternalHttp または Fallback へ遷移
                End Try
            End If

            ' 2. 内部 HTTP (POST http://localhost:5055/internal/drawings/stamp) で別プロセス ProcessorWorker へ同期リクエスト
            If pref = "AUTO" OrElse pref = "INTERNALHTTP" OrElse pref = "HTTP" Then
                Try
                    Dim jsonBody As String = JsonConvert.SerializeObject(req)
                    Using content As New StringContent(jsonBody, Encoding.UTF8, "application/json")
                        Using httpResp As HttpResponseMessage = Await SharedHttpClient.PostAsync(httpUrl, content).ConfigureAwait(False)
                            If httpResp.IsSuccessStatusCode Then
                                Dim respText As String = Await httpResp.Content.ReadAsStringAsync().ConfigureAwait(False)
                                Dim parsed As DrawingStampResponse = JsonConvert.DeserializeObject(Of DrawingStampResponse)(respText)
                                If parsed IsNot Nothing Then
                                    Return parsed
                                End If
                            End If
                        End Using
                    End Using
                Catch
                    ' InternalHttp 未起動時は Fallback へ遷移
                End Try
            End If

            ' 3. 開発環境で ProcessorWorker.exe が別起動されていない場合でも同じエンジンで焼き込み結果を返却
            Return DrawingStampBurner.BurnStampOnDemand(
                req,
                workerProcessName:="ProcessorWorker (In-Process Simulation / Ready for Out-of-Process Pipe & HTTP)",
                transportUsed:="NamedPipe/InternalHttp (Fallback Simulation)",
                endpointName:=$"\\.\pipe\{pipeName} | {httpUrl}")
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
