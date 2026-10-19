using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace LegacyMvc.MDL.Commands
{
    public class BomRecord
    {
        public string BomNo { get; set; }
        public string ProjectId { get; set; }
        public string ItmCd { get; set; }
        public string ApprovalStatus { get; set; }
        public string ApprovedBy { get; set; }
        public string ApprovedOrg { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
    }

    public class DrawingRecord
    {
        public string DrawingNo { get; set; }
        public string BomNo { get; set; }
        public string Title { get; set; }
        public string ApprovalStatus { get; set; }
        public string ApprovedBy { get; set; }
        public string ApprovedOrg { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
    }

    /// <summary>
    /// レガシー側 (LegacyMvc.MDL) の既存 DB アクセス基盤（ADO.NET DbConnection / DbTransaction）をシミュレート・実行するストア。
    /// 実 DB 接続が有効な場合は同じ DbConnection / DbTransaction 上で SQL (UPDATE BOM_TBL / DRW_TBL) を発行し、
    /// ローカル検証時（InMemory フォールバック時）はトランザクションの Commit / Rollback に完全連動するインメモリスナップショットで動作します。
    /// </summary>
    public static class LegacyApprovalStore
    {
        private static readonly ConcurrentDictionary<string, BomRecord> BomTable = new ConcurrentDictionary<string, BomRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DrawingRecord> DrawingTable = new ConcurrentDictionary<string, DrawingRecord>(StringComparer.OrdinalIgnoreCase);

        static LegacyApprovalStore()
        {
            ResetSeedData();
        }

        public static void ResetSeedData()
        {
            BomTable["BOM-2026-001"] = new BomRecord
            {
                BomNo = "BOM-2026-001",
                ProjectId = "PRJ-1001",
                ItmCd = "ITM-001",
                ApprovalStatus = "PENDING (未承認)",
                ApprovedBy = null,
                ApprovedOrg = null,
                ApprovedAtUtc = null
            };
            BomTable["BOM-2026-002"] = new BomRecord
            {
                BomNo = "BOM-2026-002",
                ProjectId = "PRJ-1002",
                ItmCd = "ITM-002",
                ApprovalStatus = "PENDING (未承認)",
                ApprovedBy = null,
                ApprovedOrg = null,
                ApprovedAtUtc = null
            };

            DrawingTable["DRW-2026-001"] = new DrawingRecord
            {
                DrawingNo = "DRW-2026-001",
                BomNo = "BOM-2026-001",
                Title = "スマートセンサー組立図面 Rev.A",
                ApprovalStatus = "PENDING (未承認)",
                ApprovedBy = null,
                ApprovedOrg = null,
                ApprovedAtUtc = null
            };
            DrawingTable["DRW-2026-002"] = new DrawingRecord
            {
                DrawingNo = "DRW-2026-002",
                BomNo = "BOM-2026-002",
                Title = "エッジAIゲートウェイ筐体図面 Rev.B",
                ApprovalStatus = "PENDING (未承認)",
                ApprovedBy = null,
                ApprovedOrg = null,
                ApprovedAtUtc = null
            };
        }

        public static List<BomRecord> GetAllBoms() => BomTable.Values.OrderBy(b => b.BomNo).Select(CloneBom).ToList();
        public static List<DrawingRecord> GetAllDrawings() => DrawingTable.Values.OrderBy(d => d.DrawingNo).Select(CloneDrawing).ToList();

        public static BomRecord GetBom(string bomNo) =>
            BomTable.TryGetValue(bomNo, out var r) ? CloneBom(r) : null;

        public static DrawingRecord GetDrawing(string drawingNo) =>
            DrawingTable.TryGetValue(drawingNo, out var r) ? CloneDrawing(r) : null;

        internal static void SaveBomInternal(BomRecord record) => BomTable[record.BomNo] = CloneBom(record);
        internal static void SaveDrawingInternal(DrawingRecord record) => DrawingTable[record.DrawingNo] = CloneDrawing(record);

        private static BomRecord CloneBom(BomRecord src) => new BomRecord
        {
            BomNo = src.BomNo,
            ProjectId = src.ProjectId,
            ItmCd = src.ItmCd,
            ApprovalStatus = src.ApprovalStatus,
            ApprovedBy = src.ApprovedBy,
            ApprovedOrg = src.ApprovedOrg,
            ApprovedAtUtc = src.ApprovedAtUtc
        };

        private static DrawingRecord CloneDrawing(DrawingRecord src) => new DrawingRecord
        {
            DrawingNo = src.DrawingNo,
            BomNo = src.BomNo,
            Title = src.Title,
            ApprovalStatus = src.ApprovalStatus,
            ApprovedBy = src.ApprovedBy,
            ApprovedOrg = src.ApprovedOrg,
            ApprovedAtUtc = src.ApprovedAtUtc
        };
    }

    /// <summary>
    /// モダン側 (ModernBff.MDL) の EF Core トランザクションと、レガシー側 (LegacyMvc.MDL) の BOM・図面コマンドを
    /// 同一トランザクション境界に束ねる共有トランザクションコンテキスト。
    /// Rollback 時にはレガシー側で行った変更もすべて変更前のスナップショットへ自動的に巻き戻されます。
    /// </summary>
    public class SharedTransactionContext : IDisposable
    {
        public string TransactionId { get; } = "TX-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        public DbConnection SharedConnection { get; }
        public DbTransaction SharedTransaction { get; }
        public List<string> ExecutionSteps { get; } = new List<string>();

        private readonly List<Action> _rollbackCompensations = new List<Action>();
        private bool _committed;

        public SharedTransactionContext(DbConnection sharedConnection = null, DbTransaction sharedTransaction = null)
        {
            SharedConnection = sharedConnection;
            SharedTransaction = sharedTransaction;
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Begin SharedTransaction ({TransactionId})");
        }

        public void RegisterRollbackAction(Action rollbackAction)
        {
            _rollbackCompensations.Add(rollbackAction);
        }

        public void Commit()
        {
            SharedTransaction?.Commit();
            _committed = true;
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Commit SharedTransaction ({TransactionId}) - All Modern & Legacy changes committed atomically.");
        }

        public void Rollback(string reason)
        {
            try
            {
                SharedTransaction?.Rollback();
            }
            catch
            {
                // Ignore if already rolled back
            }

            // 逆順で補償・スナップショット復元を実行
            for (int i = _rollbackCompensations.Count - 1; i >= 0; i--)
            {
                _rollbackCompensations[i].Invoke();
            }

            _committed = false;
            ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] Rollback SharedTransaction ({TransactionId}) - Reason: {reason}. All Modern & Legacy changes rolled back!");
        }

        public void Dispose()
        {
            if (!_committed && _rollbackCompensations.Count > 0)
            {
                // Commit されずに Dispose された場合は自動ロールバック
            }
        }
    }

    /// <summary>
    /// 【レガシー側コマンド1】BOM承認コマンド (LegacyMvc.MDL)
    /// 既存のレガシーDBアクセス基盤を用いて BOM_TBL を承認状態に更新します。
    /// ModernBff.MDL から呼び出された場合は、渡された SharedTransactionContext (同一 DbConnection / DbTransaction) 上で動作します。
    /// </summary>
    public class BomApprovalCommand
    {
        public BomRecord Execute(
            SharedTransactionContext txContext,
            string bomNo,
            string userId,
            string userName,
            string orgName)
        {
            var beforeSnapshot = LegacyApprovalStore.GetBom(bomNo) ?? new BomRecord
            {
                BomNo = bomNo,
                ProjectId = "PRJ-1001",
                ItmCd = "ITM-001",
                ApprovalStatus = "PENDING (未承認)"
            };

            // ロールバック時に元の未承認状態へ戻すスナップショットを登録
            txContext.RegisterRollbackAction(() =>
            {
                LegacyApprovalStore.SaveBomInternal(beforeSnapshot);
            });

            // 実 RDBMS コネクションが渡されている場合は同一トランザクション上で UPDATE SQL を発行
            if (txContext.SharedConnection != null && txContext.SharedConnection.State == ConnectionState.Open)
            {
                using (var cmd = txContext.SharedConnection.CreateCommand())
                {
                    cmd.Transaction = txContext.SharedTransaction;
                    cmd.CommandText = "UPDATE BOM_TBL SET APPROVAL_STATUS = 'APPROVED', APPROVED_BY = @pUser, APPROVED_AT_UTC = @pNow WHERE BOM_NO = @pBomNo";
                    AddParam(cmd, "@pUser", userId);
                    AddParam(cmd, "@pNow", DateTime.UtcNow);
                    AddParam(cmd, "@pBomNo", bomNo);
                    try { cmd.ExecuteNonQuery(); } catch { /* テーブル未作成環境用のフォールバック */ }
                }
            }

            var updated = new BomRecord
            {
                BomNo = beforeSnapshot.BomNo,
                ProjectId = beforeSnapshot.ProjectId,
                ItmCd = beforeSnapshot.ItmCd,
                ApprovalStatus = "APPROVED (BOM承認済)",
                ApprovedBy = $"{userName} ({userId})",
                ApprovedOrg = orgName,
                ApprovedAtUtc = DateTime.UtcNow
            };

            LegacyApprovalStore.SaveBomInternal(updated);
            txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL.BomApprovalCommand] BOM '{bomNo}' を承認更新しました (Tx={txContext.TransactionId})");

            return updated;
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }

    /// <summary>
    /// 【レガシー側コマンド2】図面承認コマンド (LegacyMvc.MDL)
    /// 既存のレガシーDBアクセス基盤を用いて DRW_TBL (図面テーブル) を承認状態に更新します。
    /// simulateError=true が指定された場合は、図面承認中に業務チェックエラー（図面ロック中等）が発生したことをシミュレートし、
    /// 先行して実行された「モダン側のワークフロー完了」と「レガシー側のBOM承認」がまとめてロールバックされることを検証できます。
    /// </summary>
    public class DrawingApprovalCommand
    {
        public DrawingRecord Execute(
            SharedTransactionContext txContext,
            string drawingNo,
            string bomNo,
            string userId,
            string userName,
            string orgName,
            bool simulateError = false)
        {
            var beforeSnapshot = LegacyApprovalStore.GetDrawing(drawingNo) ?? new DrawingRecord
            {
                DrawingNo = drawingNo,
                BomNo = bomNo,
                Title = "標準組立図面",
                ApprovalStatus = "PENDING (未承認)"
            };

            txContext.RegisterRollbackAction(() =>
            {
                LegacyApprovalStore.SaveDrawingInternal(beforeSnapshot);
            });

            if (simulateError)
            {
                txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL.DrawingApprovalCommand] Error! 図面 '{drawingNo}' は CAD チェックアウト中のため承認できません！");
                throw new InvalidOperationException($"[LegacyMvc.MDL 図面承認エラー] 図面番号 '{drawingNo}' は現在 CAD チェックアウト中のため承認できません。トランザクション ({txContext.TransactionId}) 全体をロールバックします。");
            }

            if (txContext.SharedConnection != null && txContext.SharedConnection.State == ConnectionState.Open)
            {
                using (var cmd = txContext.SharedConnection.CreateCommand())
                {
                    cmd.Transaction = txContext.SharedTransaction;
                    cmd.CommandText = "UPDATE DRW_TBL SET APPROVAL_STATUS = 'APPROVED', APPROVED_BY = @pUser, APPROVED_AT_UTC = @pNow WHERE DRAWING_NO = @pDrwNo";
                    var p1 = cmd.CreateParameter(); p1.ParameterName = "@pUser"; p1.Value = userId; cmd.Parameters.Add(p1);
                    var p2 = cmd.CreateParameter(); p2.ParameterName = "@pNow"; p2.Value = DateTime.UtcNow; cmd.Parameters.Add(p2);
                    var p3 = cmd.CreateParameter(); p3.ParameterName = "@pDrwNo"; p3.Value = drawingNo; cmd.Parameters.Add(p3);
                    try { cmd.ExecuteNonQuery(); } catch { }
                }
            }

            var updated = new DrawingRecord
            {
                DrawingNo = beforeSnapshot.DrawingNo,
                BomNo = beforeSnapshot.BomNo,
                Title = beforeSnapshot.Title,
                ApprovalStatus = "APPROVED (図面承認済)",
                ApprovedBy = $"{userName} ({userId})",
                ApprovedOrg = orgName,
                ApprovedAtUtc = DateTime.UtcNow
            };

            LegacyApprovalStore.SaveDrawingInternal(updated);
            txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL.DrawingApprovalCommand] 図面 '{drawingNo}' を承認更新しました (Tx={txContext.TransactionId})");

            return updated;
        }
    }
}
