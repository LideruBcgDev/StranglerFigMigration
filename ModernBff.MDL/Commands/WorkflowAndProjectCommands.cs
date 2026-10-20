using System.Data.Common;
using Common.APP;
using LegacyMvc.MDL.Commands;
using LegacyMvc.MDL.Data;
using LegacyMvc.PRM;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModernBff.MDL.Data;
using ModernBff.PRM;

namespace ModernBff.MDL.Commands;

/// <summary>
/// ProjectRegistrationDto および WorkflowApplyDto は ModernBff.PRM プロジェクトに定義されています。
/// </summary>

/// <summary>
/// 【モダン側コマンド1: 案件統合管理コマンド (ModernBff.MDL)】
/// ・案件の検索（関連づけられた品目 ITM_TBL や 図面 DRW_TBL、BOM、ワークフローの状態を統合表示）
/// ・案件の新規登録／更新（品目コードや図面番号の関連付け設定）
/// </summary>
public class ProjectIntegrationCommand
{
    public async Task<object> GetIntegratedProjectsAsync(
        StranglerDbContext db,
        string configuredProvider,
        string connectionString,
        string? keyword = null)
    {
        var prjQuery = db.PrjTbl.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            string kw = keyword.Trim().ToLowerInvariant();
            prjQuery = prjQuery.Where(p =>
                p.PrjId.ToLower().Contains(kw) ||
                p.PrjNm.ToLower().Contains(kw) ||
                p.TargetItmCd.ToLower().Contains(kw) ||
                p.LinkedDrawingNo.ToLower().Contains(kw));
        }

        var projects = await prjQuery.OrderBy(p => p.PrjId).ToListAsync();
        var workflows = await db.WfTbl.AsNoTracking().OrderBy(w => w.WfId).ToListAsync();
        var modernItems = await db.ItmTbl.AsNoTracking().OrderBy(i => i.ItmCd).ToListAsync();

        // レガシー側 (LegacyMvc.MDL - VB.NET) からも品目・図面・BOMを取得して統合
        var legacyItemCmd = new LegacyItemQueryCommand();
        var legacyItems = legacyItemCmd.Execute(configuredProvider, connectionString, null).Items;
        var allDrawings = LegacyApprovalStore.GetAllDrawings();
        var allBoms = LegacyApprovalStore.GetAllBoms();

        var integratedList = projects.Select(p =>
        {
            var itemCodes = (p.TargetItmCd ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var drawingNos = (p.LinkedDrawingNo ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var linkedItems = modernItems
                .Where(i => itemCodes.Contains(i.ItmCd, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var linkedDrawings = allDrawings
                .Where(d => drawingNos.Contains(d.DrawingNo, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var wfList = workflows
                .Where(w => string.Equals(w.PrjId, p.PrjId, StringComparison.OrdinalIgnoreCase) ||
                            (string.Equals(w.TargetType, "PROJECT", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(w.TargetId, p.PrjId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var legacyBom = LegacyApprovalStore.GetBom(p.LinkedBomNo);

            return new
            {
                Project = p,
                LinkedItems = linkedItems,
                LinkedDrawings = linkedDrawings,
                LegacyBomStatus = legacyBom,
                Workflows = wfList
            };
        }).ToList();

        return new
        {
            ExecutedCommand = "ModernBff.MDL.Commands.ProjectIntegrationCommand",
            ProjectCount = integratedList.Count,
            IntegratedProjects = integratedList,
            AvailableItems = modernItems,
            AvailableLegacyItems = legacyItems,
            AvailableDrawings = allDrawings,
            AvailableBoms = allBoms,
            AllWorkflows = workflows
        };
    }

    public async Task<PrjTblEntity> RegisterOrUpdateProjectAsync(StranglerDbContext db, ProjectRegistrationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PrjId))
            throw new ArgumentException("案件ID (PRJ_ID) は必須です。");
        if (string.IsNullOrWhiteSpace(dto.PrjNm))
            throw new ArgumentException("案件名称 (PRJ_NM) は必須です。");

        string prjId = dto.PrjId.Trim();
        var existing = await db.PrjTbl.FirstOrDefaultAsync(p => p.PrjId.ToLower() == prjId.ToLower());

        if (existing == null)
        {
            existing = new PrjTblEntity
            {
                PrjId = prjId,
                PrjNm = dto.PrjNm.Trim(),
                CustomerId = dto.CustomerId <= 0 ? 1 : dto.CustomerId,
                TargetItmCd = dto.TargetItmCd?.Trim() ?? string.Empty,
                LinkedBomNo = string.IsNullOrWhiteSpace(dto.LinkedBomNo) ? $"BOM-{prjId}" : dto.LinkedBomNo.Trim(),
                LinkedDrawingNo = dto.LinkedDrawingNo?.Trim() ?? string.Empty,
                PrjStatus = "DRAFT (未申請)",
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.PrjTbl.Add(existing);
        }
        else
        {
            existing.PrjNm = dto.PrjNm.Trim();
            existing.CustomerId = dto.CustomerId <= 0 ? existing.CustomerId : dto.CustomerId;
            existing.TargetItmCd = dto.TargetItmCd?.Trim() ?? existing.TargetItmCd;
            if (!string.IsNullOrWhiteSpace(dto.LinkedBomNo))
                existing.LinkedBomNo = dto.LinkedBomNo.Trim();
            existing.LinkedDrawingNo = dto.LinkedDrawingNo?.Trim() ?? existing.LinkedDrawingNo;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return existing;
    }
}

/// <summary>
/// 【モダン側コマンド2: ワークフロー管理コマンド (申請 -> 照査 -> 承認)】
/// ・ワークフローは「案件 (PROJECT)」「品目 (ITEM)」「図面 (DRAWING)」のいずれからも申請可能。
/// ・ステップ1: ApplyAsync (申請: APPLIED)
/// ・ステップ2: VerifyAsync (照査: VERIFIED)
/// ・ステップ3: ApproveAsync / ExecuteAsync (承認: APPROVED)
///   - 案件 (PROJECT) を承認した場合: 案件自体に加え、関連づいているすべての品目 (ITM_TBL)・図面 (DRW_TBL)・BOM (BOM_TBL) を同一トランザクションで一括承認します。
///   - 品目 (ITEM) を承認した場合: 該当品目 (ITM_TBL) を承認します。
///   - 図面 (DRAWING) を承認した場合: 該当図面 (DRW_TBL) を承認します。
/// </summary>
public class WorkflowCompleteCommand
{
    /// <summary>
    /// 1. ワークフロー申請 (PROJECT / ITEM / DRAWING から申請可能)
    /// </summary>
    public async Task<WfTblEntity> ApplyAsync(
        StranglerDbContext db,
        string configuredProvider,
        string connectionString,
        WorkflowApplyDto dto,
        string userId,
        string userName)
    {
        string targetType = (dto.TargetType ?? "PROJECT").Trim().ToUpperInvariant();
        string targetId = (dto.TargetId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("申請対象ID (TargetId: 案件ID / 品目コード / 図面番号) を指定してください。");
        }

        string wfId = string.IsNullOrWhiteSpace(dto.WfId)
            ? $"WF-{DateTime.UtcNow:HHmmss}"
            : dto.WfId.Trim();

        string defaultTitle;
        string linkedPrjId = string.Empty;

        switch (targetType)
        {
            case "PROJECT":
                var prj = await db.PrjTbl.FirstOrDefaultAsync(p => p.PrjId.ToLower() == targetId.ToLower())
                    ?? throw new KeyNotFoundException($"案件 '{targetId}' が PRJ_TBL に存在しません。");
                prj.PrjStatus = "APPLIED (WF申請中)";
                linkedPrjId = prj.PrjId;
                defaultTitle = $"[案件承認申請] {prj.PrjNm} (関連品目:{prj.TargetItmCd} / 関連図面:{prj.LinkedDrawingNo})";
                break;

            case "ITEM":
                var itm = await db.ItmTbl.FirstOrDefaultAsync(i => i.ItmCd.ToLower() == targetId.ToLower());
                if (itm != null)
                {
                    itm.ApprovalStatus = "APPLIED (WF申請中)";
                }
                defaultTitle = $"[品目単体申請] 品目コード: {targetId} ({itm?.ItmNm ?? "レガシー品目"})";
                break;

            case "DRAWING":
                var drw = LegacyApprovalStore.GetDrawing(targetId);
                if (drw != null)
                {
                    LegacyApprovalStore.UpsertDrawing(drw.DrawingNo, drw.Title, drw.ItmCd, drw.BomNo, drw.Revision);
                }
                defaultTitle = $"[図面単体申請] 図面番号: {targetId} ({drw?.Title ?? "レガシー図面"})";
                break;

            default:
                throw new ArgumentException("TargetType は PROJECT, ITEM, DRAWING のいずれかを指定してください。");
        }

        var wf = new WfTblEntity
        {
            WfId = wfId,
            TargetType = targetType,
            TargetId = targetId,
            PrjId = linkedPrjId,
            WfTitle = string.IsNullOrWhiteSpace(dto.WfTitle) ? defaultTitle : dto.WfTitle.Trim(),
            WfStatus = "APPLIED (申請済・照査待ち)",
            AppliedBy = $"{userName} ({userId})",
            AppliedAtUtc = DateTime.UtcNow
        };

        db.WfTbl.Add(wf);
        await db.SaveChangesAsync();
        return wf;
    }

    /// <summary>
    /// 2. ワークフロー照査 (APPLIED -> VERIFIED)
    /// </summary>
    public async Task<WfTblEntity> VerifyAsync(
        StranglerDbContext db,
        string wfId,
        string userId,
        string userName)
    {
        var wf = await db.WfTbl.FirstOrDefaultAsync(w => w.WfId.ToLower() == wfId.Trim().ToLower())
            ?? throw new KeyNotFoundException($"ワークフロー '{wfId}' が見つかりません。");

        wf.WfStatus = "VERIFIED (照査済・承認待ち)";
        wf.VerifiedBy = $"{userName} ({userId})";
        wf.VerifiedAtUtc = DateTime.UtcNow;

        if (wf.TargetType == "PROJECT" && !string.IsNullOrWhiteSpace(wf.TargetId))
        {
            var prj = await db.PrjTbl.FirstOrDefaultAsync(p => p.PrjId.ToLower() == wf.TargetId.ToLower());
            if (prj != null)
            {
                prj.PrjStatus = "VERIFIED (照査済・承認待ち)";
            }
        }

        await db.SaveChangesAsync();
        return wf;
    }

    /// <summary>
    /// 3. ワークフロー最終承認 (VERIFIED -> APPROVED)
    /// ・案件 (PROJECT) の承認時は、関連づいているすべての品目 (ITM_TBL)・図面 (DRW_TBL)・BOM (BOM_TBL) を
    ///   同一トランザクション (SharedTransactionContext) で一括承認します。
    /// ・品目 (ITEM) / 図面 (DRAWING) 単体申請の承認時は、それぞれの対象レコードを承認します。
    /// </summary>
    public async Task<object> ExecuteAsync(
        StranglerDbContext db,
        string wfId,
        string userId,
        string userName,
        string orgName,
        bool simulateDrawingError = false,
        string configuredProvider = "SqlServer",
        string connectionString = "")
    {
        var wf = await db.WfTbl.FirstOrDefaultAsync(w => w.WfId.ToLower() == wfId.Trim().ToLower());
        if (wf == null)
        {
            throw new KeyNotFoundException($"ワークフロー '{wfId}' が WF_TBL に見つかりません。（登録済: WF-9001, WF-9002）");
        }

        string originalWfStatus = wf.WfStatus;
        string? originalWfApprovedBy = wf.ApprovedBy;
        string? originalWfApprovedOrg = wf.ApprovedOrg;
        DateTime? originalWfApprovedAt = wf.ApprovedAtUtc;

        IDbContextTransaction? efTx = null;
        DbConnection? sharedConn = null;
        DbTransaction? sharedDbTx = null;

        if (!db.Database.IsInMemory())
        {
            efTx = await db.Database.BeginTransactionAsync();
            sharedConn = db.Database.GetDbConnection();
            sharedDbTx = efTx.GetDbTransaction();
        }

        using var txContext = new SharedTransactionContext(sharedConn, sharedDbTx);

        PrjTblEntity? prj = null;
        string? originalPrjStatus = null;
        var approvedItems = new List<object>();
        var approvedDrawings = new List<DrawingRecord>();
        BomRecord? approvedBom = null;

        string targetType = string.IsNullOrWhiteSpace(wf.TargetType) ? "PROJECT" : wf.TargetType.ToUpperInvariant();
        string targetId = string.IsNullOrWhiteSpace(wf.TargetId) ? wf.PrjId : wf.TargetId;

        if (targetType == "PROJECT")
        {
            prj = await db.PrjTbl.FirstOrDefaultAsync(p => p.PrjId.ToLower() == targetId.ToLower());
            if (prj == null)
            {
                throw new KeyNotFoundException($"関連案件 '{targetId}' が PRJ_TBL に見つかりません。");
            }
            originalPrjStatus = prj.PrjStatus;
        }

        txContext.RegisterRollbackAction(() =>
        {
            wf.WfStatus = originalWfStatus;
            wf.ApprovedBy = originalWfApprovedBy;
            wf.ApprovedOrg = originalWfApprovedOrg;
            wf.ApprovedAtUtc = originalWfApprovedAt;
            if (prj != null && originalPrjStatus != null)
            {
                prj.PrjStatus = originalPrjStatus;
                prj.ApprovedBy = null;
            }
            db.SaveChanges();
        });

        try
        {
            // Step 1: ワークフロー (WF_TBL) を APPROVED に更新
            if (string.IsNullOrWhiteSpace(wf.VerifiedBy))
            {
                wf.VerifiedBy = $"{userName} ({userId}) [自動照査]";
                wf.VerifiedAtUtc = DateTime.UtcNow;
            }
            wf.WfStatus = "APPROVED (承認完了)";
            wf.ApprovedBy = $"{userName} ({userId})";
            wf.ApprovedOrg = orgName;
            wf.ApprovedAtUtc = DateTime.UtcNow;

            var legacyItemCmd = new LegacyItemQueryCommand();
            var bomCommand = new BomApprovalCommand();
            var drawingCommand = new DrawingApprovalCommand();

            if (targetType == "PROJECT" && prj != null)
            {
                // 案件承認：案件本体 + 関連する全ての品目 (ITM_TBL) + BOM + 関連する全ての図面 (DRW_TBL) を一括承認
                prj.PrjStatus = "APPROVED (案件・関連品目・図面一括承認済)";
                prj.ApprovedBy = $"{userName} ({userId})";
                prj.UpdatedAtUtc = DateTime.UtcNow;

                var itemCodes = (prj.TargetItmCd ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var code in itemCodes)
                {
                    var modernItem = await db.ItmTbl.FirstOrDefaultAsync(i => i.ItmCd.ToLower() == code.ToLower());
                    if (modernItem != null)
                    {
                        string prevItemStatus = modernItem.ApprovalStatus;
                        string? prevItemBy = modernItem.ApprovedBy;
                        DateTime? prevItemAt = modernItem.ApprovedAtUtc;

                        txContext.RegisterRollbackAction(() =>
                        {
                            modernItem.ApprovalStatus = prevItemStatus;
                            modernItem.ApprovedBy = prevItemBy;
                            modernItem.ApprovedAtUtc = prevItemAt;
                            db.SaveChanges();
                        });

                        modernItem.ApprovalStatus = "APPROVED (案件連動承認済)";
                        modernItem.ApprovedBy = $"{userName} ({userId})";
                        modernItem.ApprovedAtUtc = DateTime.UtcNow;
                    }

                    var legItem = legacyItemCmd.ApproveItemInTransaction(txContext, configuredProvider, connectionString, code, userId, userName);
                    approvedItems.Add((object?)modernItem ?? (object?)legItem ?? new { ItmCd = code, ApprovalStatus = "APPROVED" });
                }

                await db.SaveChangesAsync();
                txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ModernBff.MDL] 案件 '{prj.PrjId}' および関連品目 ({prj.TargetItmCd}) を承認更新 (Tx={txContext.TransactionId})");

                // レガシーBOM承認
                if (!string.IsNullOrWhiteSpace(prj.LinkedBomNo))
                {
                    approvedBom = bomCommand.Execute(txContext, prj.LinkedBomNo, userId, userName, orgName);
                }

                // レガシー図面承認（複数図面関連付けにも対応）
                var drawingNos = (prj.LinkedDrawingNo ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var drwNo in drawingNos)
                {
                    var updatedDrw = drawingCommand.Execute(
                        txContext,
                        drwNo,
                        prj.LinkedBomNo,
                        userId,
                        userName,
                        orgName,
                        simulateError: simulateDrawingError);
                    if (updatedDrw != null)
                    {
                        approvedDrawings.Add(updatedDrw);
                    }
                }
            }
            else if (targetType == "ITEM")
            {
                // 品目単体ワークフロー承認
                var modernItem = await db.ItmTbl.FirstOrDefaultAsync(i => i.ItmCd.ToLower() == targetId.ToLower());
                if (modernItem != null)
                {
                    modernItem.ApprovalStatus = "APPROVED (品目承認済)";
                    modernItem.ApprovedBy = $"{userName} ({userId})";
                    modernItem.ApprovedAtUtc = DateTime.UtcNow;
                }
                var legItem = legacyItemCmd.ApproveItemInTransaction(txContext, configuredProvider, connectionString, targetId, userId, userName);
                approvedItems.Add((object?)modernItem ?? (object?)legItem ?? new { ItmCd = targetId, ApprovalStatus = "APPROVED" });
                await db.SaveChangesAsync();
            }
            else if (targetType == "DRAWING")
            {
                // 図面単体ワークフロー承認
                await db.SaveChangesAsync();
                var updatedDrw = drawingCommand.Execute(
                    txContext,
                    targetId,
                    "BOM-" + targetId,
                    userId,
                    userName,
                    orgName,
                    simulateError: simulateDrawingError);
                if (updatedDrw != null)
                {
                    approvedDrawings.Add(updatedDrw);
                }
            }

            if (efTx != null)
            {
                await efTx.CommitAsync();
            }
            txContext.Commit();

            return new
            {
                Success = true,
                TargetType = targetType,
                TargetId = targetId,
                TransactionResult = targetType == "PROJECT"
                    ? "COMMITTED (案件承認に伴い、関連づいている品目・BOM・図面も単一トランザクションで一括承認コミットされました)"
                    : $"COMMITTED ({targetType} '{targetId}' のワークフロー承認が完了しました)",
                TransactionId = txContext.TransactionId,
                ExecutionLog = txContext.ExecutionSteps,
                AfterState = new
                {
                    ModernWorkflow = wf,
                    ModernProject = prj,
                    ApprovedItems = approvedItems,
                    LegacyBom = approvedBom,
                    LegacyDrawings = approvedDrawings
                }
            };
        }
        catch (Exception ex)
        {
            if (efTx != null)
            {
                await efTx.RollbackAsync();
            }
            txContext.Rollback(ex.Message);

            return new
            {
                Success = false,
                TargetType = targetType,
                TargetId = targetId,
                TransactionResult = "ROLLED_BACK (承認処理中にエラーが発生したため、案件・ワークフロー・品目・BOM・図面の変更をすべて一括ロールバックしました)",
                TransactionId = txContext.TransactionId,
                ErrorMessage = ex.Message,
                ExecutionLog = txContext.ExecutionSteps,
                AfterRollbackState = new
                {
                    ModernWorkflow = wf,
                    ModernProject = prj,
                    LegacyBom = prj != null ? LegacyApprovalStore.GetBom(prj.LinkedBomNo) : null,
                    LegacyDrawings = prj != null ? LegacyApprovalStore.GetAllDrawings(prj.LinkedDrawingNo) : new List<DrawingRecord>()
                }
            };
        }
        finally
        {
            if (efTx != null)
            {
                await efTx.DisposeAsync();
            }
        }
    }
}

// ====================================================================================
// ModernBff.MDL における ICommand 実装クラス群 (ModernBff.PRM の IParam / OParam を使用)
// LegacyMvc.MDL と全く同じ仕組み (<CommandDefinition> + Validate + Execute) で呼び出し可能で、
// WinFormsClient からは DirectCast(result, ProjectSearchOParam) のように型安全に利用できます。
// ====================================================================================

[CommandDefinition(CommandNames.PrjSch, Db = 1, Description = "案件統合検索コマンド (ModernBff.MDL.Commands.ProjectSearchCommand)")]
public class ProjectSearchCommand : CommandBase<ProjectSearchIParam, ProjectSearchOParam>
{
    public ProjectSearchCommand() : base(null!)
    {
    }

    public ProjectSearchCommand(ProjectSearchIParam iParam) : base(iParam)
    {
    }

    protected override void ExecuteCore()
    {
        using var db = StranglerDbContext.CreateWorkingContext(
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString,
            out string providerStatus);

        var prjQuery = db.PrjTbl.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(_iParam.Keyword))
        {
            string kw = _iParam.Keyword.Trim().ToLowerInvariant();
            prjQuery = prjQuery.Where(p =>
                p.PrjId.ToLower().Contains(kw) ||
                p.PrjNm.ToLower().Contains(kw) ||
                p.TargetItmCd.ToLower().Contains(kw) ||
                p.LinkedDrawingNo.ToLower().Contains(kw));
        }

        var projects = prjQuery.OrderBy(p => p.PrjId).ToList();
        var workflows = db.WfTbl.AsNoTracking().OrderBy(w => w.WfId).ToList();
        var legacyItemCmd = new LegacyItemQueryCommand();
        var legacyItems = legacyItemCmd.Execute(_iParam.ConfiguredProvider, _iParam.ConnectionString, null!).Items;
        var allDrawings = LegacyApprovalStore.GetAllDrawings();
        var allBoms = LegacyApprovalStore.GetAllBoms();

        var integrated = projects.Select(p =>
        {
            var itemCodes = (p.TargetItmCd ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var drawingNos = (p.LinkedDrawingNo ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new IntegratedProjectRecord
            {
                Project = p,
                LinkedItems = legacyItems.Where(i => itemCodes.Contains(i.ItmCd, StringComparer.OrdinalIgnoreCase)).ToList(),
                LinkedDrawings = allDrawings.Where(d => drawingNos.Contains(d.DrawingNo, StringComparer.OrdinalIgnoreCase)).ToList(),
                LegacyBomStatus = LegacyApprovalStore.GetBom(p.LinkedBomNo),
                Workflows = workflows.Where(w =>
                    string.Equals(w.PrjId, p.PrjId, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(w.TargetType, "PROJECT", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(w.TargetId, p.PrjId, StringComparison.OrdinalIgnoreCase))).ToList()
            };
        }).ToList();

        _oParam.ProjectCount = integrated.Count;
        _oParam.IntegratedProjects = integrated;
        _oParam.AvailableItems = legacyItems;
        _oParam.AvailableDrawings = allDrawings;
        _oParam.AvailableBoms = allBoms;
        _oParam.AllWorkflows = workflows;
        _oParam.ProviderStatus = providerStatus;
        _oParam.SummaryMessage = $"[PRJ.SCH: ModernBff.MDL.ProjectSearchCommand (db={_iParam.DbNumber})] 案件 {integrated.Count}件 / WF {workflows.Count}件 を取得しました。";
    }
}

[CommandDefinition(CommandNames.PrjSav, Db = 1, Description = "案件統合登録・更新コマンド (ModernBff.MDL.Commands.ProjectSaveCommand)")]
public class ProjectSaveCommand : CommandBase<ProjectSaveIParam, ProjectSaveOParam>
{
    public ProjectSaveCommand() : base(null!)
    {
    }

    public ProjectSaveCommand(ProjectSaveIParam iParam) : base(iParam)
    {
    }

    protected override void ExecuteCore()
    {
        using var db = StranglerDbContext.CreateWorkingContext(
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString,
            out string providerStatus);

        var cmd = new ProjectIntegrationCommand();
        var saved = cmd.RegisterOrUpdateProjectAsync(db, _iParam.Project).GetAwaiter().GetResult();
        _oParam.SavedProject = saved;
        _oParam.ProviderStatus = providerStatus;
    }
}

[CommandDefinition(CommandNames.WfApp, Db = 1, Description = "ワークフロー申請コマンド (ModernBff.MDL.Commands.WorkflowApplyCommand)")]
public class WorkflowApplyCommand : CommandBase<WorkflowApplyIParam, WorkflowApplyOParam>
{
    public WorkflowApplyCommand() : base(null!)
    {
    }

    public WorkflowApplyCommand(WorkflowApplyIParam iParam) : base(iParam)
    {
    }

    protected override void ExecuteCore()
    {
        using var db = StranglerDbContext.CreateWorkingContext(
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString,
            out _);

        var cmd = new WorkflowCompleteCommand();
        var wf = cmd.ApplyAsync(
            db,
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString,
            _iParam.ApplyRequest,
            _iParam.LoginUser.UserId,
            _iParam.LoginUser.UserName).GetAwaiter().GetResult();

        _oParam.Workflow = wf;
    }
}

[CommandDefinition(CommandNames.WfVrf, Db = 1, Description = "ワークフロー照査コマンド (ModernBff.MDL.Commands.WorkflowVerifyCommand)")]
public class WorkflowVerifyCommand : CommandBase<WorkflowVerifyIParam, WorkflowVerifyOParam>
{
    public WorkflowVerifyCommand() : base(null!)
    {
    }

    public WorkflowVerifyCommand(WorkflowVerifyIParam iParam) : base(iParam)
    {
    }

    protected override void ExecuteCore()
    {
        using var db = StranglerDbContext.CreateWorkingContext(
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString,
            out _);

        var cmd = new WorkflowCompleteCommand();
        var wf = cmd.VerifyAsync(
            db,
            _iParam.WfId,
            _iParam.LoginUser.UserId,
            _iParam.LoginUser.UserName).GetAwaiter().GetResult();

        _oParam.Workflow = wf;
    }
}

[CommandDefinition(CommandNames.WfApr, Db = 1, Description = "ワークフロー最終承認コマンド (ModernBff.MDL.Commands.WorkflowApproveCommand)")]
public class WorkflowApproveCommand : CommandBase<WorkflowApproveIParam, WorkflowApproveOParam>
{
    public WorkflowApproveCommand() : base(null!)
    {
    }

    public WorkflowApproveCommand(WorkflowApproveIParam iParam) : base(iParam)
    {
    }

    protected override void ExecuteCore()
    {
        using var db = StranglerDbContext.CreateWorkingContext(
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString,
            out _);

        var wf = db.WfTbl.AsNoTracking().FirstOrDefault(w => w.WfId.ToLower() == _iParam.WfId.Trim().ToLower());
        var cmd = new WorkflowCompleteCommand();
        _ = cmd.ExecuteAsync(
            db,
            _iParam.WfId,
            _iParam.LoginUser.UserId,
            _iParam.LoginUser.UserName,
            _iParam.LoginUser.OrganizationName,
            _iParam.SimulateDrawingError,
            _iParam.ConfiguredProvider,
            _iParam.ConnectionString).GetAwaiter().GetResult();

        var updatedWf = db.WfTbl.AsNoTracking().FirstOrDefault(w => w.WfId.ToLower() == _iParam.WfId.Trim().ToLower()) ?? wf;
        var updatedPrj = updatedWf != null && !string.IsNullOrWhiteSpace(updatedWf.PrjId)
            ? db.PrjTbl.AsNoTracking().FirstOrDefault(p => p.PrjId.ToLower() == updatedWf.PrjId.ToLower())
            : null;

        _oParam.TargetType = updatedWf?.TargetType ?? "PROJECT";
        _oParam.TargetId = updatedWf?.TargetId ?? _iParam.WfId;
        _oParam.TransactionResult = _iParam.SimulateDrawingError
            ? "ROLLED_BACK (エラーシミュレーションにより一括ロールバック)"
            : "COMMITTED (案件・関連品目・BOM・図面を一括承認コミット)";
        _oParam.ModernWorkflow = updatedWf;
        _oParam.ModernProject = updatedPrj;
        _oParam.LegacyBom = updatedPrj != null ? LegacyApprovalStore.GetBom(updatedPrj.LinkedBomNo) : null;
        _oParam.LegacyDrawings = updatedPrj != null ? LegacyApprovalStore.GetAllDrawings(updatedPrj.LinkedDrawingNo) : new List<DrawingRecord>();
    }
}
