using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Common.APP;
using LegacyMvc.PRM;

namespace ModernBff.PRM;

/// <summary>
/// 【モダン側パラメタ・エンティティ・DTO 定義プロジェクト (ModernBff.PRM - C# / netstandard2.0)】
/// LegacyMvc.PRM と同じ設計思想で、ModernBff / ModernBff.MDL / WinFormsClient 間でやり取りされる
/// 案件統合管理 (PRJ_TBL)・ワークフロー (WF_TBL) のエンティティ、DTO、および IParam / OParam クラスを定義します。
/// WinFormsClient は ModernBff.PRM を参照することで、モダン側コマンド ("PRJ.SCH", "WF.APR" 等) の戻り値も DirectCast 可能です。
/// </summary>
[Table("PRJ_TBL")]
public class PrjTblEntity
{
    [Key]
    [Column("PRJ_ID")]
    [MaxLength(64)]
    public string PrjId { get; set; } = string.Empty;

    [Required]
    [Column("PRJ_NM")]
    [MaxLength(128)]
    public string PrjNm { get; set; } = string.Empty;

    [Column("CUSTOMER_ID")]
    public int CustomerId { get; set; } = 1;

    /// <summary>
    /// 関連付けられた品目コード (カンマ区切り 例: "ITM-001,ITM-002")
    /// </summary>
    [Column("TARGET_ITM_CD")]
    [MaxLength(256)]
    public string TargetItmCd { get; set; } = string.Empty;

    /// <summary>
    /// 関連付けられたBOM番号
    /// </summary>
    [Column("LINKED_BOM_NO")]
    [MaxLength(128)]
    public string LinkedBomNo { get; set; } = string.Empty;

    /// <summary>
    /// 関連付けられた図面番号 (カンマ区切り 例: "DRW-2026-001,DRW-2026-002")
    /// </summary>
    [Column("LINKED_DRAWING_NO")]
    [MaxLength(256)]
    public string LinkedDrawingNo { get; set; } = string.Empty;

    [Column("PRJ_STATUS")]
    [MaxLength(64)]
    public string PrjStatus { get; set; } = "DRAFT (未申請)";

    [Column("APPROVED_BY")]
    [MaxLength(128)]
    public string? ApprovedBy { get; set; }

    [Column("UPDATED_AT_UTC")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// ワークフロー管理テーブル (WF_TBL) - モダン側 (ModernBff.PRM) で定義
/// </summary>
[Table("WF_TBL")]
public class WfTblEntity
{
    [Key]
    [Column("WF_ID")]
    [MaxLength(64)]
    public string WfId { get; set; } = string.Empty;

    /// <summary>
    /// 申請対象種別: "PROJECT" (案件) | "ITEM" (品目) | "DRAWING" (図面)
    /// </summary>
    [Column("TARGET_TYPE")]
    [MaxLength(32)]
    public string TargetType { get; set; } = "PROJECT";

    /// <summary>
    /// 申請対象のキー (PRJ-1001 / ITM-001 / DRW-2026-001)
    /// </summary>
    [Column("TARGET_ID")]
    [MaxLength(64)]
    public string TargetId { get; set; } = string.Empty;

    [Column("PRJ_ID")]
    [MaxLength(64)]
    public string PrjId { get; set; } = string.Empty;

    [Column("WF_TITLE")]
    [MaxLength(256)]
    public string WfTitle { get; set; } = string.Empty;

    [Column("WF_STATUS")]
    [MaxLength(64)]
    public string WfStatus { get; set; } = "APPLIED (申請済・照査待ち)";

    [Column("APPLIED_BY")]
    [MaxLength(128)]
    public string? AppliedBy { get; set; }

    [Column("APPLIED_AT_UTC")]
    public DateTime? AppliedAtUtc { get; set; }

    [Column("VERIFIED_BY")]
    [MaxLength(128)]
    public string? VerifiedBy { get; set; }

    [Column("VERIFIED_AT_UTC")]
    public DateTime? VerifiedAtUtc { get; set; }

    [Column("APPROVED_BY")]
    [MaxLength(128)]
    public string? ApprovedBy { get; set; }

    [Column("APPROVED_ORG")]
    [MaxLength(128)]
    public string? ApprovedOrg { get; set; }

    [Column("APPROVED_AT_UTC")]
    public DateTime? ApprovedAtUtc { get; set; }
}

/// <summary>
/// 案件統合レコード（案件本体＋関連品目・関連図面・レガシーBOM・関連ワークフロー）
/// WinFormsClient 等で DirectCast して強い型付けで参照できます。
/// </summary>
public class IntegratedProjectRecord
{
    public PrjTblEntity Project { get; set; } = new();
    public List<ItmTblEntity> LinkedItems { get; set; } = new();
    public List<DrawingRecord> LinkedDrawings { get; set; } = new();
    public BomRecord? LegacyBomStatus { get; set; }
    public List<WfTblEntity> Workflows { get; set; } = new();
}

// ====================================================================================
// リクエスト DTO 定義 (Controller / BFF / WinFormsClient 共通)
// ====================================================================================

public class ProjectRegistrationDto
{
    public string PrjId { get; set; } = string.Empty;
    public string PrjNm { get; set; } = string.Empty;
    public int CustomerId { get; set; } = 1;
    public string TargetItmCd { get; set; } = string.Empty;
    public string LinkedBomNo { get; set; } = string.Empty;
    public string LinkedDrawingNo { get; set; } = string.Empty;
}

public class WorkflowApplyDto
{
    public string? WfId { get; set; }
    public string TargetType { get; set; } = "PROJECT";
    public string TargetId { get; set; } = string.Empty;
    public string? WfTitle { get; set; }
}

public class ItemRegistrationRequest
{
    public string ItmCd { get; set; } = string.Empty;
    public string ItmNm { get; set; } = string.Empty;
    public string Category { get; set; } = "一般品目";
    public decimal UnitPrice { get; set; }
    public int StockQty { get; set; }
}

public class DrawingRegistrationRequest
{
    public string DrawingNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ItmCd { get; set; } = "ITM-001";
    public string BomNo { get; set; } = string.Empty;
    public string Revision { get; set; } = "Rev.A";
}

// ====================================================================================
// モダン側コマンド用 IParam / OParam クラス群 (LegacyMvc.PRM と同じ仕組みで DirectCast 可能)
// ====================================================================================

/// <summary>
/// 1. "PRJ.SCH" (案件統合検索コマンド: ProjectSearchCommand) 用 IParam / OParam
/// </summary>
public class ProjectSearchIParam : IParamBase
{
    public string ConfiguredProvider { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
}

public class ProjectSearchOParam : OParamBase
{
    public int ProjectCount { get; set; }
    public List<IntegratedProjectRecord> IntegratedProjects { get; set; } = new();
    public List<ItmTblEntity> AvailableItems { get; set; } = new();
    public List<DrawingRecord> AvailableDrawings { get; set; } = new();
    public List<BomRecord> AvailableBoms { get; set; } = new();
    public List<WfTblEntity> AllWorkflows { get; set; } = new();
    public string ProviderStatus { get; set; } = string.Empty;
    public string SummaryMessage { get; set; } = string.Empty;
}

/// <summary>
/// 2. "PRJ.SAV" (案件統合登録・更新コマンド: ProjectSaveCommand) 用 IParam / OParam
/// </summary>
public class ProjectSaveIParam : IParamBase
{
    public string ConfiguredProvider { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = string.Empty;
    public ProjectRegistrationDto Project { get; set; } = new();
}

public class ProjectSaveOParam : OParamBase
{
    public PrjTblEntity SavedProject { get; set; } = new();
    public string ProviderStatus { get; set; } = string.Empty;
}

/// <summary>
/// 3. "WF.APP" (ワークフロー申請コマンド: WorkflowApplyCommand) 用 IParam / OParam
/// </summary>
public class WorkflowApplyIParam : IParamBase
{
    public string ConfiguredProvider { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = string.Empty;
    public WorkflowApplyDto ApplyRequest { get; set; } = new();
}

public class WorkflowApplyOParam : OParamBase
{
    public WfTblEntity Workflow { get; set; } = new();
}

/// <summary>
/// 4. "WF.VRF" (ワークフロー照査コマンド: WorkflowVerifyCommand) 用 IParam / OParam
/// </summary>
public class WorkflowVerifyIParam : IParamBase
{
    public string ConfiguredProvider { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = string.Empty;
    public string WfId { get; set; } = "WF-9002";
}

public class WorkflowVerifyOParam : OParamBase
{
    public WfTblEntity Workflow { get; set; } = new();
}

/// <summary>
/// 5. "WF.APR" (ワークフロー最終承認コマンド: WorkflowApproveCommand) 用 IParam / OParam
/// </summary>
public class WorkflowApproveIParam : IParamBase
{
    public string ConfiguredProvider { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = string.Empty;
    public string WfId { get; set; } = "WF-9001";
    public bool SimulateDrawingError { get; set; } = false;
}

public class WorkflowApproveOParam : OParamBase
{
    public string TargetType { get; set; } = "PROJECT";
    public string TargetId { get; set; } = string.Empty;
    public string TransactionResult { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public List<string> ExecutionLog { get; set; } = new();
    public WfTblEntity? ModernWorkflow { get; set; }
    public PrjTblEntity? ModernProject { get; set; }
    public BomRecord? LegacyBom { get; set; }
    public List<DrawingRecord> LegacyDrawings { get; set; } = new();
}

// ====================================================================================
// 【モダン側コマンド型付きカタログ (ModernBff.PRM.ModernCommands)】
// コマンド名定数 (CommandNames.XXX) が決まれば IParam 型と OParam 型が一意に決まるため、
// ここで CommandSpec<TIParam, TOParam> として一元定義します。
// ・ModernCommands.PrjSch.CreateIParam() で new ProjectSearchIParam() を手書きせずに生成可能
// ・HttpUtil.ExecuteCommandAsync(ModernCommands.PrjSch, p => ...) で型変換ミスを100%防止可能
// ====================================================================================
public static class ModernCommands
{
    public static readonly CommandSpec<ProjectSearchIParam, ProjectSearchOParam> PrjSch = new(CommandNames.PrjSch);
    public static readonly CommandSpec<ProjectSaveIParam, ProjectSaveOParam> PrjSav = new(CommandNames.PrjSav);
    public static readonly CommandSpec<WorkflowApplyIParam, WorkflowApplyOParam> WfApp = new(CommandNames.WfApp);
    public static readonly CommandSpec<WorkflowVerifyIParam, WorkflowVerifyOParam> WfVrf = new(CommandNames.WfVrf);
    public static readonly CommandSpec<WorkflowApproveIParam, WorkflowApproveOParam> WfApr = new(CommandNames.WfApr);

    public static void EnsureRegistered()
    {
        _ = PrjSch.Name;
    }
}
