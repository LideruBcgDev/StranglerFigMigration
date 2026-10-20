Imports System
Imports System.Collections.Generic
Imports System.ComponentModel.DataAnnotations
Imports System.ComponentModel.DataAnnotations.Schema
Imports Common.APP

''' <summary>
''' 【パラメタ・レコード定義プロジェクト (LegacyMvc.PRM - VB.NET)】
''' Controller・LegacyMvc.MDL・ModernBff・WinFormsClient 間でやり取りされる
''' BomRecord / DrawingRecord / ItmTblEntity および ICommand / IParamBase / OParamBase 派生クラスを定義します。
''' WinFormsClient はこの LegacyMvc.PRM を参照することで、サーバーから返却された OParam を DirectCast して型安全に利用できます。
''' </summary>
Namespace Global.LegacyMvc.PRM

    ''' <summary>
    ''' 【共通コマンドインターフェース (ICommand)】
    ''' </summary>
    Public Interface ICommand
        ReadOnly Property IParam() As IParamBase
        ReadOnly Property OParam() As OParamBase
        Property ResponseType() As String
        Sub Execute()
        Sub Validate()
    End Interface

    ''' <summary>
    ''' コマンド定義属性 (別案: Config の文字列手書きに代わる、属性＋DI/自動登録方式用)
    ''' 例: &lt;CommandDefinition("ITM.APP", Db:=1)&gt;
    ''' </summary>
    <AttributeUsage(AttributeTargets.Class, AllowMultiple:=True)>
    Public Class CommandDefinitionAttribute
        Inherits Attribute

        Public ReadOnly Property Name As String
        Public Property Db As Integer = 1
        Public Property Description As String = String.Empty

        Public Sub New(name As String, Optional db As Integer = 1, Optional description As String = "")
            Me.Name = name
            Me.Db = db
            Me.Description = description
        End Sub
    End Class

    ''' <summary>
    ''' CommandLoginUser は Common.APP.LoginUser を継承（既存コードとの完全互換用）
    ''' </summary>
    Public Class CommandLoginUser
        Inherits LoginUser
    End Class

    ''' <summary>
    ''' 【入力パラメータ基底クラス (IParamBase)】
    ''' Session 情報、DB番号、および Common.APP.LoginUser 情報を保持します。
    ''' </summary>
    Public Class IParamBase
        Public Property SessionId As String = String.Empty
        Public Property SessionData As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Public Property LoginUser As New LoginUser()
        Public Property CommandId As String = String.Empty
        Public Property DbNumber As Integer = 1
    End Class

    ''' <summary>
    ''' 【出力パラメータ基底クラス (OParamBase)】
    ''' WinFormsClient 側で DirectCast 可能な出力パラメータの基底クラスです。
    ''' </summary>
    Public Class OParamBase
        Public Property IsSuccess As Boolean = False
        Public Property ValidationPassed As Boolean = False
        Public Property ExecutedCommandKey As String = String.Empty
        Public Property ExecutedCommandName As String = String.Empty
        Public Property DbNumberUsed As Integer = 1
        Public Property AuthorizedUserId As String = String.Empty
        Public Property AuthorizedUserName As String = String.Empty
        Public Property LoginUserSource As String = String.Empty
        Public Property SessionIdUsed As String = String.Empty
        Public Property PermissionCheckResult As String = String.Empty
        Public Property ErrorMessage As String = String.Empty
        Public Property ExecutedAtUtc As DateTime = DateTime.UtcNow
    End Class

    ' ====================================================================================
    ' レコード・エンティティ定義 (BomRecord / DrawingRecord / ItmTblEntity / UsrTblEntity)
    ' ====================================================================================
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
        ' --- 別プロセス (ProcessorWorker) で非同期処理される TIFF変換・OCR・Elasticsearch 連携情報 ---
        Public Property SourceTiffFileName As String = String.Empty
        Public Property ThumbnailPngPath As String = String.Empty
        Public Property OcrAndEsStatus As String = "INDEXED (完了)"
        Public Property OcrExtractedText As String = String.Empty
        Public Property ElasticsearchDocId As String = String.Empty
        Public Property LastBackgroundJobId As String = String.Empty
    End Class

    ''' <summary>
    ''' 【IIS とは別プロセス (ProcessorWorker) で実行される非同期ジョブ】
    ''' 1. TIFF ファイルからサムネイル用 PNG ファイルの生成
    ''' 2. 図面ファイルの OCR テキスト抽出
    ''' 3. Elasticsearch への全文検索インデックス登録
    ''' </summary>
    Public Class DrawingBackgroundJobRecord
        Public Property JobId As String = String.Empty
        Public Property DrawingNo As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property BomNo As String = String.Empty
        Public Property Revision As String = "Rev.A"
        Public Property SourceTiffPath As String = String.Empty
        Public Property GeneratedThumbnailPngPath As String = String.Empty
        Public Property OcrExtractedText As String = String.Empty
        Public Property ElasticsearchIndex As String = "strangler-drawings-v1"
        Public Property ElasticsearchDocId As String = String.Empty
        ''' <summary>"QUEUED" -> "PROCESSING" -> "COMPLETED" | "FAILED_RETRYING"</summary>
        Public Property Status As String = "QUEUED"
        Public Property ProcessedByWorkerProcess As String = String.Empty
        Public Property EnqueuedAtUtc As DateTime = DateTime.UtcNow
        Public Property CompletedAtUtc As DateTime?
        Public Property ExecutionSteps As New List(Of String)()
    End Class

    ''' <summary>
    ''' 【図面表示時 オンデマンド・スタンプ焼き込み要求モデル】
    ''' IIS (ModernBff / LegacyMvc) から別プロセス ProcessorWorker の同期エンドポイント
    ''' (名前付きパイプ / 内部 HTTP / gRPC相当) へ送信されるリクエストです。
    ''' 「承認情報」だけでなく、動的な「閲覧日時」「閲覧ログインユーザ名（社外秘透かし等）」を含みます。
    ''' </summary>
    Public Class DrawingStampRequest
        Public Property DrawingNo As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property Revision As String = "Rev.A"
        Public Property ItmCd As String = String.Empty
        Public Property BomNo As String = String.Empty
        Public Property SourceTiffPath As String = String.Empty

        ' --- 承認スタンプ情報 (オンデマンド焼き込み) ---
        Public Property ApprovalStatus As String = "PENDING (未承認)"
        Public Property ApprovedBy As String = String.Empty
        Public Property ApprovedOrg As String = String.Empty
        Public Property ApprovedAtUtc As DateTime?

        ' --- 閲覧者・閲覧日時 動的透かし (Watermark) 情報 ---
        Public Property ViewerUserId As String = String.Empty
        Public Property ViewerUserName As String = String.Empty
        Public Property ViewerOrgName As String = String.Empty
        Public Property ViewedAtLocalText As String = String.Empty
        Public Property ConfidentialLabel As String = "社外秘 / CONFIDENTIAL - DO NOT COPY"
        Public Property PreferredTransport As String = "AUTO" ' "AUTO", "NamedPipe", "InternalHttp"
    End Class

    ''' <summary>
    ''' 【図面表示時 オンデマンド・スタンプ焼き込み応答モデル (戻り値)】
    ''' ProcessorWorker が元図面に承認スタンプ＋動的透かしを焼き込み、IIS プロセスへ同期返却するレスポンスです。
    ''' </summary>
    Public Class DrawingStampResponse
        Public Property Success As Boolean = True
        Public Property DrawingNo As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property Revision As String = String.Empty
        Public Property ContentType As String = "image/svg+xml"
        Public Property StampedImageBase64 As String = String.Empty
        Public Property StampedImageDataUrl As String = String.Empty
        Public Property StampedSvgMarkup As String = String.Empty
        Public Property ApprovalStampSummary As String = String.Empty
        Public Property DynamicWatermarkSummary As String = String.Empty
        Public Property TransportUsed As String = String.Empty
        Public Property WorkerEndpoint As String = String.Empty
        Public Property ProcessedByWorkerProcess As String = String.Empty
        Public Property ProcessedAtUtc As DateTime = DateTime.UtcNow
    End Class

    <Table("USR_TBL")>
    Public Class UsrTblEntity
        <Key>
        <Column("USR_ID")>
        <MaxLength(64)>
        Public Property UsrId As String = String.Empty

        <Required>
        <Column("PASSWORD")>
        <MaxLength(128)>
        Public Property Password As String = String.Empty

        <Required>
        <Column("USR_NM")>
        <MaxLength(128)>
        Public Property UsrNm As String = String.Empty

        <Column("ORG_CD")>
        <MaxLength(64)>
        Public Property OrgCd As String = String.Empty

        <Column("ORG_NM")>
        <MaxLength(128)>
        Public Property OrgNm As String = String.Empty

        <Column("ROLES_CSV")>
        <MaxLength(512)>
        Public Property RolesCsv As String = String.Empty

        <Column("IS_ACTIVE")>
        Public Property IsActive As Boolean = True
    End Class

    <Table("ITM_TBL")>
    Public Class ItmTblEntity
        <Key>
        <Column("ITM_CD")>
        <MaxLength(64)>
        Public Property ItmCd As String = String.Empty

        <Required>
        <Column("ITM_NM")>
        <MaxLength(128)>
        Public Property ItmNm As String = String.Empty

        <Column("CATEGORY")>
        <MaxLength(64)>
        Public Property Category As String = String.Empty

        <Column("UNIT_PRICE")>
        Public Property UnitPrice As Decimal

        <Column("STOCK_QTY")>
        Public Property StockQty As Integer

        <Column("APPROVAL_STATUS")>
        <MaxLength(64)>
        Public Property ApprovalStatus As String = "DRAFT (未申請)"

        <Column("APPROVED_BY")>
        <MaxLength(128)>
        Public Property ApprovedBy As String

        <Column("APPROVED_AT_UTC")>
        Public Property ApprovedAtUtc As DateTime?

        <Column("UPDATED_AT_UTC")>
        Public Property UpdatedAtUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class LegacyItemQueryResult
        Public Property Items As List(Of ItmTblEntity) = New List(Of ItmTblEntity)()
        Public Property ProviderStatus As String = String.Empty
    End Class

    ' ====================================================================================
    ' 具体コマンド用 IParam / OParam クラス群 (WinFormsClient で DirectCast して利用可能)
    ' ====================================================================================

    ''' <summary>
    ''' 0. "ITM.APP" コマンド用 IParam / OParam
    ''' (&lt;command name="ITM.APP" command="LegacyMvc.MDL.Commands.LegacyApprovalStore,LegacyMvc.MDL" db="1" /&gt;)
    ''' </summary>
    Public Class ItemAppIParam
        Inherits IParamBase

        Public Property ConfiguredProvider As String = "SqlServer"
        Public Property ConnectionString As String = String.Empty
        Public Property TargetItmCd As String = "ITM-001"
        Public Property TargetBomNo As String = "BOM-2026-001"
        Public Property TargetDrawingNo As String = "drw004"
        Public Property ActionMode As String = "QUERY_AND_APPROVE"
    End Class

    Public Class ItemAppOParam
        Inherits OParamBase

        Public Property Items As New List(Of ItmTblEntity)()
        Public Property Boms As New List(Of BomRecord)()
        Public Property Drawings As New List(Of DrawingRecord)()
        Public Property ProviderStatus As String = String.Empty
        Public Property SummaryMessage As String = String.Empty
    End Class

    ''' <summary>
    ''' 1. "ITM.SCH" (品目検索) 用 IParam / OParam
    ''' </summary>
    Public Class ItemSearchIParam
        Inherits IParamBase

        Public Property ConfiguredProvider As String = "SqlServer"
        Public Property ConnectionString As String = String.Empty
        Public Property Keyword As String = String.Empty
    End Class

    Public Class ItemSearchOParam
        Inherits OParamBase

        Public Property Items As New List(Of ItmTblEntity)()
        Public Property ProviderStatus As String = String.Empty
    End Class

    ''' <summary>
    ''' 2. "ITM.SAV" (品目登録・更新) 用 IParam / OParam
    ''' </summary>
    Public Class ItemSaveIParam
        Inherits IParamBase

        Public Property ConfiguredProvider As String = "SqlServer"
        Public Property ConnectionString As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property ItmNm As String = String.Empty
        Public Property Category As String = String.Empty
        Public Property UnitPrice As Decimal = 0D
        Public Property StockQty As Integer = 0
    End Class

    Public Class ItemSaveOParam
        Inherits OParamBase

        Public Property SavedItem As ItmTblEntity
    End Class

    ''' <summary>
    ''' 3. "DRW.SCH" (図面検索) 用 IParam / OParam
    ''' </summary>
    Public Class DrawingSearchIParam
        Inherits IParamBase

        Public Property Keyword As String = String.Empty
    End Class

    Public Class DrawingSearchOParam
        Inherits OParamBase

        Public Property Drawings As New List(Of DrawingRecord)()
    End Class

    ''' <summary>
    ''' 4. "DRW.SAV" (図面登録・更新) 用 IParam / OParam
    ''' </summary>
    Public Class DrawingSaveIParam
        Inherits IParamBase

        Public Property DrawingNo As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property BomNo As String = String.Empty
        Public Property Revision As String = "Rev.A"
    End Class

    Public Class DrawingSaveOParam
        Inherits OParamBase

        Public Property SavedDrawing As DrawingRecord
    End Class

    ''' <summary>
    ''' 5. "CUS.SCH" (顧客照会) 用 IParam / OParam
    ''' </summary>
    Public Class CustomerQueryIParam
        Inherits IParamBase

        Public Property CustomerId As Integer = 1
    End Class

    Public Class CustomerQueryOParam
        Inherits OParamBase

        Public Property CustomerId As Integer
        Public Property CustomerName As String = String.Empty
    End Class

    ' ====================================================================================
    ' 【レガシーコマンド型付きカタログ (LegacyMvc.PRM.LegacyCommands)】
    ' コマンド名定数 (CommandNames.XXX) が決まれば IParam 型と OParam 型が一意に決まるため、
    ' ここで CommandSpec(Of TIParam, TOParam) として一元定義します。
    ' ・LegacyCommands.ItmApp.CreateIParam() で New ItemAppIParam() を手書きせずに生成可能
    ' ・HttpUtil.ExecuteCommandAsync(LegacyCommands.ItmApp, Sub(p) ...) で型変換ミスを100%防止可能
    ' ====================================================================================
    Public NotInheritable Class LegacyCommands
        Public Shared ReadOnly ItmApp As New CommandSpec(Of ItemAppIParam, ItemAppOParam)(CommandNames.ItmApp)
        Public Shared ReadOnly ItmSch As New CommandSpec(Of ItemSearchIParam, ItemSearchOParam)(CommandNames.ItmSch)
        Public Shared ReadOnly ItmSav As New CommandSpec(Of ItemSaveIParam, ItemSaveOParam)(CommandNames.ItmSav)
        Public Shared ReadOnly DrwSch As New CommandSpec(Of DrawingSearchIParam, DrawingSearchOParam)(CommandNames.DrwSch)
        Public Shared ReadOnly DrwSav As New CommandSpec(Of DrawingSaveIParam, DrawingSaveOParam)(CommandNames.DrwSav)
        Public Shared ReadOnly CusSch As New CommandSpec(Of CustomerQueryIParam, CustomerQueryOParam)(CommandNames.CusSch)

        Private Sub New()
        End Sub

        Public Shared Sub EnsureRegistered()
            ' 静的フィールド初期化を確実にトリガーして CommandParamRegistry へ全ペアを登録
            Dim _dummy As String = ItmApp.Name
        End Sub
    End Class

End Namespace
