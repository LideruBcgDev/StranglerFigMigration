using System.Data.Common;
using LegacyMvc.MDL.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModernBff.MDL.Data;

namespace ModernBff.MDL.Commands;

/// <summary>
/// 【モダン側コマンド1: 案件統合管理コマンド (ModernBff.MDL)】
/// モダン側の案件テーブル (PRJ_TBL)・ワークフローテーブル (WF_TBL)・品目テーブル (ITM_TBL) と、
/// レガシー側 (LegacyMvc.MDL) の BOM 承認状態 (BOM_TBL)・図面承認状態 (DRW_TBL) を横断統合して返します。
/// </summary>
public class ProjectIntegrationCommand
{
    public async Task<object> GetIntegratedProjectsAsync(StranglerDbContext db, string? prjId = null)
    {
        var prjQuery = db.PrjTbl.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(prjId))
        {
            string code = prjId.Trim();
            prjQuery = prjQuery.Where(p => p.PrjId.ToLower() == code.ToLower());
        }

        var projects = await prjQuery.OrderBy(p => p.PrjId).ToListAsync();
        var workflows = await db.WfTbl.AsNoTracking().OrderBy(w => w.WfId).ToListAsync();
        var items = await db.ItmTbl.AsNoTracking().ToListAsync();

        var integratedList = projects.Select(p =>
        {
            var item = items.FirstOrDefault(i => i.ItmCd == p.TargetItmCd);
            var wfList = workflows.Where(w => w.PrjId == p.PrjId).ToList();
            var legacyBom = LegacyApprovalStore.GetBom(p.LinkedBomNo);
            var legacyDrawing = LegacyApprovalStore.GetDrawing(p.LinkedDrawingNo);

            return new
            {
                Project = p,
                TargetItem = item,
                Workflows = wfList,
                LegacyBomStatus = legacyBom,
                LegacyDrawingStatus = legacyDrawing
            };
        }).ToList();

        return new
        {
            ExecutedCommand = "ModernBff.MDL.Commands.ProjectIntegrationCommand",
            ProjectCount = integratedList.Count,
            IntegratedProjects = integratedList
        };
    }
}

/// <summary>
/// 【モダン側コマンド2: ワークフロー完了 ＆ レガシーBOM・図面一括承認コマンド (ModernBff.MDL -> LegacyMvc.MDL)】
///
/// 1つのデータベーストランザクション (SharedTransactionContext) 内で以下を連続実行します:
///   Step 1: [ModernBff.MDL] ワークフロー (WF_TBL) を APPROVED に更新、案件 (PRJ_TBL) を COMPLETED に更新
///   Step 2: [LegacyMvc.MDL.BomApprovalCommand] レガシー側の BOM (BOM_TBL) を承認更新
///   Step 3: [LegacyMvc.MDL.DrawingApprovalCommand] レガシー側の図面 (DRW_TBL) を承認更新
///
/// ※ Step 3 (図面承認) 等でエラーが発生した場合 (simulateDrawingError=true)、
///   Step 1 (モダン側のワークフロー完了・案件完了) と Step 2 (レガシー側のBOM承認) もすべて一括ロールバックされます！
/// </summary>
public class WorkflowCompleteCommand
{
    public async Task<object> ExecuteAsync(
        StranglerDbContext db,
        string wfId,
        string userId,
        string userName,
        string orgName,
        bool simulateDrawingError = false)
    {
        var wf = await db.WfTbl.FirstOrDefaultAsync(w => w.WfId.ToLower() == wfId.Trim().ToLower());
        if (wf == null)
        {
            throw new KeyNotFoundException($"ワークフロー '{wfId}' が WF_TBL に見つかりません。（登録済: WF-9001, WF-9002）");
        }

        var prj = await db.PrjTbl.FirstOrDefaultAsync(p => p.PrjId == wf.PrjId);
        if (prj == null)
        {
            throw new KeyNotFoundException($"関連案件 '{wf.PrjId}' が PRJ_TBL に見つかりません。");
        }

        // ロールバック検証用に更新前のスナップショットを保持
        string originalWfStatus = wf.WfStatus;
        string? originalWfApprovedBy = wf.ApprovedBy;
        string? originalWfApprovedOrg = wf.ApprovedOrg;
        DateTime? originalWfApprovedAt = wf.ApprovedAtUtc;
        string originalPrjStatus = prj.PrjStatus;

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

        // モダン側 (WF_TBL / PRJ_TBL) のロールバック補償も同一トランザクションコンテキストに登録
        txContext.RegisterRollbackAction(() =>
        {
            wf.WfStatus = originalWfStatus;
            wf.ApprovedBy = originalWfApprovedBy;
            wf.ApprovedOrg = originalWfApprovedOrg;
            wf.ApprovedAtUtc = originalWfApprovedAt;
            prj.PrjStatus = originalPrjStatus;
            db.SaveChanges();
        });

        try
        {
            // =========================================================================
            // Step 1: [ModernBff.MDL] ワークフロー (WF_TBL) と 案件 (PRJ_TBL) を完了更新
            // =========================================================================
            wf.WfStatus = "APPROVED (ワークフロー完了)";
            wf.ApprovedBy = $"{userName} ({userId})";
            wf.ApprovedOrg = orgName;
            wf.ApprovedAtUtc = DateTime.UtcNow;

            prj.PrjStatus = "COMPLETED (BOM・図面承認完了)";
            prj.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ModernBff.MDL.WorkflowCompleteCommand] WF_TBL '{wf.WfId}' および PRJ_TBL '{prj.PrjId}' を承認完了に更新 (Tx={txContext.TransactionId})");

            // =========================================================================
            // Step 2: [LegacyMvc.MDL] BOM承認コマンドを同一トランザクションで呼び出し
            // =========================================================================
            var bomCommand = new BomApprovalCommand();
            var updatedBom = bomCommand.Execute(
                txContext,
                prj.LinkedBomNo,
                userId,
                userName,
                orgName);

            // =========================================================================
            // Step 3: [LegacyMvc.MDL] 図面承認コマンドを同一トランザクションで呼び出し
            // =========================================================================
            var drawingCommand = new DrawingApprovalCommand();
            var updatedDrawing = drawingCommand.Execute(
                txContext,
                prj.LinkedDrawingNo,
                prj.LinkedBomNo,
                userId,
                userName,
                orgName,
                simulateError: simulateDrawingError);

            // =========================================================================
            // Step 4: 全ステップ成功につき一括 Commit
            // =========================================================================
            if (efTx != null)
            {
                await efTx.CommitAsync();
            }
            txContext.Commit();

            return new
            {
                Success = true,
                TransactionResult = "COMMITTED (モダン側ワークフロー完了 + レガシー側BOM承認 + レガシー側図面承認 を単一トランザクションで一括コミットしました)",
                TransactionId = txContext.TransactionId,
                ExecutionLog = txContext.ExecutionSteps,
                AfterState = new
                {
                    ModernWorkflow = wf,
                    ModernProject = prj,
                    LegacyBom = updatedBom,
                    LegacyDrawing = updatedDrawing
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
                TransactionResult = "ROLLED_BACK (図面承認でエラーが発生したため、先行していたモダン側のワークフロー完了とレガシー側のBOM承認もすべて元の未承認状態へ一括ロールバックされました)",
                TransactionId = txContext.TransactionId,
                ErrorMessage = ex.Message,
                ExecutionLog = txContext.ExecutionSteps,
                AfterRollbackState = new
                {
                    ModernWorkflow = wf,
                    ModernProject = prj,
                    LegacyBom = LegacyApprovalStore.GetBom(prj.LinkedBomNo),
                    LegacyDrawing = LegacyApprovalStore.GetDrawing(prj.LinkedDrawingNo)
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
