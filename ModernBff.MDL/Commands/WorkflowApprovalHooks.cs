using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LegacyMvc.MDL.Commands;
using LegacyMvc.MDL.Data;
using LegacyMvc.PRM;
using Microsoft.EntityFrameworkCore;
using ModernBff.MDL.Data;
using ModernBff.PRM;

namespace ModernBff.MDL.Commands;

/// <summary>
/// ワークフロー承認 Hook パイプラインで共有されるコンテキスト。
/// WorkflowCompleteCommand 本体は個別の承認対象（品目・BOM・図面・将来の見積等）の具体ロジックを持たず、
/// このコンテキストを登録済みの IWorkflowApprovalHook / IWorkflowPostCommitHook に渡して実行します。
/// </summary>
public class WorkflowApprovalHookContext
{
    public required StranglerDbContext Db { get; init; }
    public required SharedTransactionContext TxContext { get; init; }
    public required WfTblEntity Workflow { get; init; }
    public PrjTblEntity? Project { get; init; }
    public required string TargetType { get; init; }
    public required string TargetId { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string OrgName { get; init; }
    public required string ConfiguredProvider { get; init; }
    public required string ConnectionString { get; init; }
    public bool SimulateDrawingError { get; init; }

    /// <summary>実行された Hook 名のトレース一覧</summary>
    public List<string> ExecutedHooks { get; } = new();

    /// <summary>各ドメイン Hook で承認された対象情報（品目名称 ItmNm、図面名称 Title 等を保持）</summary>
    public List<ApprovedEntityInfo> ApprovedEntities { get; } = new();

    /// <summary>後方互換用：承認された品目オブジェクト一覧</summary>
    public List<object> ApprovedItems { get; } = new();

    /// <summary>後方互換用：承認されたレガシー図面レコード一覧</summary>
    public List<DrawingRecord> ApprovedDrawings { get; } = new();

    /// <summary>後方互換用：承認されたレガシーBOMレコード</summary>
    public BomRecord? ApprovedBom { get; set; }

    /// <summary>完了後 Hook で送信されたモックメール一覧</summary>
    public List<MockEmailRecord> MockEmailsSent { get; } = new();

    /// <summary>完了後 Hook で発行された Windows 通知一覧</summary>
    public List<WindowsNotificationRecord> WindowsNotificationsSent { get; } = new();
}

/// <summary>
/// 【第1フェーズ＆ドメイン別完了後フェーズ：承認ドメイン Hook インターフェース (IWorkflowApprovalHook)】
/// ・新たな承認対象（例：見積承認、発注承認、仕様書承認など）が紐づいた場合も、
///   このインターフェースを実装して WorkflowHookRegistry に登録するだけでよく、
///   WorkflowCompleteCommand 本体のロジック修正は一切不要（Open-Closed Principle）になります。
/// ・また、品目名称 (ItmNm) や図面名称 (Title) など各ドメイン固有の属性を使った完了後メール送信も、
///   各 Hook の OnCommittedAsync 内で完結して呼び出せます。
/// </summary>
public interface IWorkflowApprovalHook
{
    /// <summary>実行順序（小さい順）</summary>
    int Order { get; }

    /// <summary>Hook 識別名</summary>
    string HookName { get; }

    /// <summary>対象ドメイン ("ITEM", "BOM", "DRAWING" 等)</summary>
    string DomainType { get; }

    /// <summary>このワークフロー承認で当 Hook を実行すべきか判定します</summary>
    bool ShouldExecute(WorkflowApprovalHookContext context);

    /// <summary>
    /// 【トランザクション内実行】
    /// SharedTransactionContext 内で DB 承認更新を行います。エラー時は呼び出し元で一括ロールバックされます。
    /// </summary>
    Task ExecuteInTransactionAsync(WorkflowApprovalHookContext context);

    /// <summary>
    /// 【トランザクション Commit 完了後 Hook】
    /// トランザクションが正常にコミットされた後にのみ呼び出されます。
    /// 品目名称 (ItmNm) や図面名称 (Title) を使ったドメイン別の完了メール送信（モック）等を実行します。
    /// （※ロールバック時には呼ばれないため、誤送信が起きません）
    /// </summary>
    Task OnCommittedAsync(WorkflowApprovalHookContext context);
}

/// <summary>
/// 【第2フェーズ：横断的な完了後 Hook インターフェース (IWorkflowPostCommitHook)】
/// すべての承認ドメイン Hook がコミットされた後に呼び出される横断 Hook（Windows トースト通知、全体完了メール等）です。
/// </summary>
public interface IWorkflowPostCommitHook
{
    int Order { get; }
    string HookName { get; }
    Task OnAfterWorkflowCommittedAsync(WorkflowApprovalHookContext context);
}

// ====================================================================================
// モックメール送信サービス & Windows OS 通知サービス
// ====================================================================================

public static class MockEmailNotificationService
{
    private static readonly ConcurrentQueue<MockEmailRecord> SentHistory = new();

    public static MockEmailRecord SendMockEmail(
        WorkflowApprovalHookContext context,
        string triggeredByHook,
        string domainType,
        string to,
        string subject,
        string body)
    {
        var record = new MockEmailRecord
        {
            EmailId = $"MAIL-{DateTime.UtcNow:HHmmssfff}-{ Random.Shared.Next(100, 999) }",
            TriggeredByHook = triggeredByHook,
            DomainType = domainType,
            To = to,
            Subject = subject,
            Body = body,
            SentAtUtc = DateTime.UtcNow
        };

        SentHistory.Enqueue(record);
        context.MockEmailsSent.Add(record);
        context.TxContext.ExecutionSteps.Add(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] [PostCommit-MockEmail ({triggeredByHook})] To={to} | Subject=\"{subject}\"");

        return record;
    }

    public static List<MockEmailRecord> GetRecentEmails() => SentHistory.Reverse().Take(50).ToList();
}

public static class WindowsOsNotificationService
{
    private static readonly ConcurrentQueue<WindowsNotificationRecord> NotificationHistory = new();

    /// <summary>
    /// Windows OS のトースト通知 / バルーン通知を非同期（ノンブロッキング）で呼び出し、履歴を記録します。
    /// </summary>
    public static WindowsNotificationRecord NotifyWindows(
        WorkflowApprovalHookContext context,
        string triggeredByHook,
        string title,
        string message)
    {
        bool dispatchedToOs = false;
        string method = "Mock / Web Push Notification";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            method = "Windows OS Toast Notification (WinRT ToastNotificationManager / PowerShell)";
            dispatchedToOs = TryShowWindowsToastNonBlocking(title, message);
        }

        var record = new WindowsNotificationRecord
        {
            NotificationId = $"WIN-NOTIFY-{DateTime.UtcNow:HHmmssfff}",
            TriggeredByHook = triggeredByHook,
            Title = title,
            Message = message,
            DeliveryMethod = method,
            DispatchedToWindowsOs = dispatchedToOs,
            NotifiedAtUtc = DateTime.UtcNow
        };

        NotificationHistory.Enqueue(record);
        context.WindowsNotificationsSent.Add(record);
        context.TxContext.ExecutionSteps.Add(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] [PostCommit-WindowsNotification ({triggeredByHook})] Title=\"{title}\" | Message=\"{message}\" (OSDispatched={dispatchedToOs})");

        return record;
    }

    private static bool TryShowWindowsToastNonBlocking(string title, string message)
    {
        try
        {
            string safeTitle = (title ?? "ワークフロー承認完了").Replace("'", "''").Replace("\r", " ").Replace("\n", " ");
            string safeMsg = (message ?? string.Empty).Replace("'", "''").Replace("\r", " ").Replace("\n", " ");

            string psScript = $@"
try {{
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml('<toast><visual><binding template=""ToastGeneric""><text>{safeTitle}</text><text>{safeMsg}</text></binding></visual></toast>')
    $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Windows PowerShell').Show($toast)
}} catch {{ }}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{psScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static List<WindowsNotificationRecord> GetRecentNotifications() => NotificationHistory.Reverse().Take(50).ToList();
}

// ====================================================================================
// 具体ドメイン承認 Hook 実装（1. 品目承認 Hook / 2. BOM承認 Hook / 3. 図面承認 Hook）
// ====================================================================================

/// <summary>
/// 【ドメイン Hook 1: 品目 (ITM_TBL) 承認 Hook】
/// ・案件 (PROJECT) に紐づく複数品目 (TargetItmCd)、または品目単体申請 (ITEM) を自動判定して承認します。
/// ・完了後 (OnCommittedAsync) には、承認した各品目の「品目名称 (ItmNm)」を使って品目承認完了メール（モック）を送信します。
/// </summary>
public class ItemApprovalHook : IWorkflowApprovalHook
{
    public int Order => 10;
    public string HookName => nameof(ItemApprovalHook);
    public string DomainType => "ITEM";

    public bool ShouldExecute(WorkflowApprovalHookContext context)
    {
        if (context.TargetType == "ITEM" && !string.IsNullOrWhiteSpace(context.TargetId))
            return true;
        if (context.TargetType == "PROJECT" && !string.IsNullOrWhiteSpace(context.Project?.TargetItmCd))
            return true;
        return false;
    }

    public async Task ExecuteInTransactionAsync(WorkflowApprovalHookContext context)
    {
        var itemCodes = context.TargetType == "ITEM"
            ? new[] { context.TargetId.Trim() }
            : (context.Project?.TargetItmCd ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var legacyItemCmd = new LegacyItemQueryCommand();

        foreach (var code in itemCodes)
        {
            string itemName = code;
            string newStatus = context.TargetType == "PROJECT"
                ? "APPROVED (案件連動Hook承認済)"
                : "APPROVED (品目Hook承認済)";

            var modernItem = await context.Db.ItmTbl.FirstOrDefaultAsync(i => i.ItmCd.ToLower() == code.ToLower());
            if (modernItem != null)
            {
                string prevStatus = modernItem.ApprovalStatus;
                string? prevBy = modernItem.ApprovedBy;
                DateTime? prevAt = modernItem.ApprovedAtUtc;

                context.TxContext.RegisterRollbackAction(() =>
                {
                    modernItem.ApprovalStatus = prevStatus;
                    modernItem.ApprovedBy = prevBy;
                    modernItem.ApprovedAtUtc = prevAt;
                    context.Db.SaveChanges();
                });

                modernItem.ApprovalStatus = newStatus;
                modernItem.ApprovedBy = $"{context.UserName} ({context.UserId})";
                modernItem.ApprovedAtUtc = DateTime.UtcNow;
                itemName = modernItem.ItmNm;
            }

            var legItem = legacyItemCmd.ApproveItemInTransaction(
                context.TxContext,
                context.ConfiguredProvider,
                context.ConnectionString,
                code,
                context.UserId,
                context.UserName);

            if (string.IsNullOrWhiteSpace(itemName) && legItem != null)
            {
                itemName = legItem.ItmNm;
            }

            var snapshot = (object?)modernItem ?? (object?)legItem ?? new { ItmCd = code, ItmNm = itemName, ApprovalStatus = newStatus };
            context.ApprovedItems.Add(snapshot);
            context.ApprovedEntities.Add(new ApprovedEntityInfo
            {
                HookName = HookName,
                DomainType = DomainType,
                TargetCode = code,
                TargetName = itemName,
                ApprovalStatus = newStatus,
                EntitySnapshot = snapshot
            });
        }

        await context.Db.SaveChangesAsync();
        context.ExecutedHooks.Add($"{HookName} (InTransaction: {string.Join(", ", itemCodes)})");
        context.TxContext.ExecutionSteps.Add(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] [Hook: {HookName}] 品目 ({string.Join(", ", itemCodes)}) を承認更新しました (Tx={context.TxContext.TransactionId})");
    }

    public Task OnCommittedAsync(WorkflowApprovalHookContext context)
    {
        var myItems = context.ApprovedEntities.Where(e => e.HookName == HookName).ToList();
        foreach (var item in myItems)
        {
            // 品目名称 (TargetName = ItmNm) を使った品目固有の完了メール送信（モック）
            MockEmailNotificationService.SendMockEmail(
                context,
                triggeredByHook: $"{HookName}.OnCommittedAsync",
                domainType: DomainType,
                to: "item-master-notify@example.local",
                subject: $"【品目承認完了通知】品目「{item.TargetName}」({item.TargetCode}) が承認されました",
                body: $"ワークフローID: {context.Workflow.WfId}\n" +
                      $"承認された品目コード: {item.TargetCode}\n" +
                      $"承認された品目名称  : {item.TargetName}\n" +
                      $"承認ステータス      : {item.ApprovalStatus}\n" +
                      $"承認者              : {context.UserName} ({context.UserId}) / {context.OrgName}");
        }

        context.ExecutedHooks.Add($"{HookName}.OnCommittedAsync (MockEmail: {myItems.Count}件)");
        return Task.CompletedTask;
    }
}

/// <summary>
/// 【ドメイン Hook 2: BOM (部品表) 承認 Hook】
/// ・案件 (PROJECT) に紐づく LinkedBomNo を自動判定して承認します。
/// </summary>
public class BomApprovalHook : IWorkflowApprovalHook
{
    public int Order => 20;
    public string HookName => nameof(BomApprovalHook);
    public string DomainType => "BOM";

    public bool ShouldExecute(WorkflowApprovalHookContext context)
    {
        return context.TargetType == "PROJECT" && !string.IsNullOrWhiteSpace(context.Project?.LinkedBomNo);
    }

    public Task ExecuteInTransactionAsync(WorkflowApprovalHookContext context)
    {
        string bomNo = context.Project!.LinkedBomNo;
        var bomCommand = new BomApprovalCommand();
        var approvedBom = bomCommand.Execute(
            context.TxContext,
            bomNo,
            context.UserId,
            context.UserName,
            context.OrgName);

        context.ApprovedBom = approvedBom;
        if (approvedBom != null)
        {
            context.ApprovedEntities.Add(new ApprovedEntityInfo
            {
                HookName = HookName,
                DomainType = DomainType,
                TargetCode = approvedBom.BomNo,
                TargetName = $"部品表 {approvedBom.BomNo} (対象品目: {approvedBom.ItmCd})",
                ApprovalStatus = approvedBom.ApprovalStatus,
                EntitySnapshot = approvedBom
            });
        }

        context.ExecutedHooks.Add($"{HookName} (InTransaction: {bomNo})");
        return Task.CompletedTask;
    }

    public Task OnCommittedAsync(WorkflowApprovalHookContext context)
    {
        var myBoms = context.ApprovedEntities.Where(e => e.HookName == HookName).ToList();
        foreach (var bom in myBoms)
        {
            MockEmailNotificationService.SendMockEmail(
                context,
                triggeredByHook: $"{HookName}.OnCommittedAsync",
                domainType: DomainType,
                to: "bom-engineering@example.local",
                subject: $"【BOM承認完了通知】{bom.TargetName} が承認されました",
                body: $"ワークフローID: {context.Workflow.WfId}\n" +
                      $"BOM番号: {bom.TargetCode}\n" +
                      $"名称   : {bom.TargetName}\n" +
                      $"承認者 : {context.UserName} ({context.UserId})");
        }

        context.ExecutedHooks.Add($"{HookName}.OnCommittedAsync (MockEmail: {myBoms.Count}件)");
        return Task.CompletedTask;
    }
}

/// <summary>
/// 【ドメイン Hook 3: 図面 (DRW_TBL) 承認 Hook】
/// ・案件 (PROJECT) に紐づく複数図面 (LinkedDrawingNo)、または図面単体申請 (DRAWING) を自動判定して承認します。
/// ・完了後 (OnCommittedAsync) には、承認した各図面の「図面名称 (Title)」を使って図面承認完了メール（モック）を送信します。
/// </summary>
public class DrawingApprovalHook : IWorkflowApprovalHook
{
    public int Order => 30;
    public string HookName => nameof(DrawingApprovalHook);
    public string DomainType => "DRAWING";

    public bool ShouldExecute(WorkflowApprovalHookContext context)
    {
        if (context.TargetType == "DRAWING" && !string.IsNullOrWhiteSpace(context.TargetId))
            return true;
        if (context.TargetType == "PROJECT" && !string.IsNullOrWhiteSpace(context.Project?.LinkedDrawingNo))
            return true;
        return false;
    }

    public Task ExecuteInTransactionAsync(WorkflowApprovalHookContext context)
    {
        var drawingNos = context.TargetType == "DRAWING"
            ? new[] { context.TargetId.Trim() }
            : (context.Project?.LinkedDrawingNo ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string linkedBomNo = context.Project?.LinkedBomNo ?? $"BOM-{context.TargetId}";
        var drawingCommand = new DrawingApprovalCommand();

        foreach (var drwNo in drawingNos)
        {
            var updatedDrw = drawingCommand.Execute(
                context.TxContext,
                drwNo,
                linkedBomNo,
                context.UserId,
                context.UserName,
                context.OrgName,
                simulateError: context.SimulateDrawingError);

            if (updatedDrw != null)
            {
                context.ApprovedDrawings.Add(updatedDrw);
                context.ApprovedEntities.Add(new ApprovedEntityInfo
                {
                    HookName = HookName,
                    DomainType = DomainType,
                    TargetCode = updatedDrw.DrawingNo,
                    TargetName = updatedDrw.Title, // 図面名称
                    ApprovalStatus = updatedDrw.ApprovalStatus,
                    EntitySnapshot = updatedDrw
                });
            }
        }

        context.ExecutedHooks.Add($"{HookName} (InTransaction: {string.Join(", ", drawingNos)})");
        return Task.CompletedTask;
    }

    public Task OnCommittedAsync(WorkflowApprovalHookContext context)
    {
        var myDrawings = context.ApprovedEntities.Where(e => e.HookName == HookName).ToList();
        foreach (var drw in myDrawings)
        {
            // 図面名称 (TargetName = Title) を使った図面固有の完了メール送信（モック）
            MockEmailNotificationService.SendMockEmail(
                context,
                triggeredByHook: $"{HookName}.OnCommittedAsync",
                domainType: DomainType,
                to: "drawing-cad-notify@example.local",
                subject: $"【図面承認完了通知】図面「{drw.TargetName}」({drw.TargetCode}) が承認されました",
                body: $"ワークフローID: {context.Workflow.WfId}\n" +
                      $"承認された図面番号: {drw.TargetCode}\n" +
                      $"承認された図面名称: {drw.TargetName}\n" +
                      $"承認ステータス    : {drw.ApprovalStatus}\n" +
                      $"承認者            : {context.UserName} ({context.UserId}) / {context.OrgName}");
        }

        context.ExecutedHooks.Add($"{HookName}.OnCommittedAsync (MockEmail: {myDrawings.Count}件)");
        return Task.CompletedTask;
    }
}

// ====================================================================================
// 第2フェーズ：横断的な完了後 Hook 実装（Windows OS 通知 Hook）
// ====================================================================================

/// <summary>
/// 【完了後横断 Hook: Windows デスクトップ通知 (トースト通知) Hook】
/// 承認完了後、承認された品目名称・図面名称をまとめたメッセージで Windows OS の通知機能を呼び出します。
/// </summary>
public class WindowsNotificationPostCommitHook : IWorkflowPostCommitHook
{
    public int Order => 100;
    public string HookName => nameof(WindowsNotificationPostCommitHook);

    public Task OnAfterWorkflowCommittedAsync(WorkflowApprovalHookContext context)
    {
        var itemNames = context.ApprovedEntities
            .Where(e => e.DomainType == "ITEM")
            .Select(e => $"{e.TargetName}({e.TargetCode})")
            .ToList();

        var drawingNames = context.ApprovedEntities
            .Where(e => e.DomainType == "DRAWING")
            .Select(e => $"{e.TargetName}({e.TargetCode})")
            .ToList();

        var summaryParts = new List<string>();
        if (context.Project != null)
            summaryParts.Add($"案件: {context.Project.PrjNm} ({context.Project.PrjId})");
        if (itemNames.Count > 0)
            summaryParts.Add($"品目: {string.Join(", ", itemNames)}");
        if (drawingNames.Count > 0)
            summaryParts.Add($"図面: {string.Join(", ", drawingNames)}");

        string title = $"ワークフロー最終承認完了 ({context.Workflow.WfId})";
        string message = summaryParts.Count > 0
            ? string.Join(" / ", summaryParts) + $" [承認者: {context.UserName}]"
            : $"{context.TargetType} ({context.TargetId}) が承認されました [承認者: {context.UserName}]";

        WindowsOsNotificationService.NotifyWindows(
            context,
            triggeredByHook: HookName,
            title: title,
            message: message);

        context.ExecutedHooks.Add($"{HookName} (Windows OS Toast Dispatched)");
        return Task.CompletedTask;
    }
}

// ====================================================================================
// ワークフロー Hook レジストリ (WorkflowHookRegistry)
// 新たな承認対象（見積承認・購買承認など）が増えても、ここに RegisterApprovalHook するだけで
// WorkflowCompleteCommand 本体のコード修正は一切不要になります。
// ====================================================================================

public static class WorkflowHookRegistry
{
    private static readonly object SyncLock = new();

    private static readonly List<IWorkflowApprovalHook> ApprovalHooks = new()
    {
        new ItemApprovalHook(),
        new BomApprovalHook(),
        new DrawingApprovalHook()
    };

    private static readonly List<IWorkflowPostCommitHook> PostCommitHooks = new()
    {
        new WindowsNotificationPostCommitHook()
    };

    public static IReadOnlyList<IWorkflowApprovalHook> GetApprovalHooks()
    {
        lock (SyncLock)
        {
            return ApprovalHooks.OrderBy(h => h.Order).ToList();
        }
    }

    public static IReadOnlyList<IWorkflowPostCommitHook> GetPostCommitHooks()
    {
        lock (SyncLock)
        {
            return PostCommitHooks.OrderBy(h => h.Order).ToList();
        }
    }

    /// <summary>
    /// 新たな承認ドメイン Hook を追加登録します（既存の同名 Hook は置換）。
    /// </summary>
    public static void RegisterApprovalHook(IWorkflowApprovalHook hook)
    {
        if (hook == null) return;
        lock (SyncLock)
        {
            ApprovalHooks.RemoveAll(h => string.Equals(h.HookName, hook.HookName, StringComparison.OrdinalIgnoreCase));
            ApprovalHooks.Add(hook);
        }
    }

    /// <summary>
    /// 新たな完了後横断 Hook を追加登録します（既存の同名 Hook は置換）。
    /// </summary>
    public static void RegisterPostCommitHook(IWorkflowPostCommitHook hook)
    {
        if (hook == null) return;
        lock (SyncLock)
        {
            PostCommitHooks.RemoveAll(h => string.Equals(h.HookName, hook.HookName, StringComparison.OrdinalIgnoreCase));
            PostCommitHooks.Add(hook);
        }
    }

    /// <summary>
    /// 【フェーズ1】トランザクション内で対象となるすべての承認ドメイン Hook を順次実行します。
    /// </summary>
    public static async Task<List<IWorkflowApprovalHook>> ExecuteInTransactionHooksAsync(WorkflowApprovalHookContext context)
    {
        var executed = new List<IWorkflowApprovalHook>();
        foreach (var hook in GetApprovalHooks())
        {
            if (hook.ShouldExecute(context))
            {
                await hook.ExecuteInTransactionAsync(context);
                executed.Add(hook);
            }
        }
        return executed;
    }

    /// <summary>
    /// 【フェーズ2】トランザクション Commit 成功後に、各ドメイン Hook の OnCommittedAsync（品目名称・図面名称を使ったモックメール送信）
    /// および横断 PostCommitHook（Windows OS 通知）を実行します。
    /// </summary>
    public static async Task ExecutePostCommitHooksAsync(
        WorkflowApprovalHookContext context,
        IEnumerable<IWorkflowApprovalHook> executedDomainHooks)
    {
        // 1. 各承認ドメイン固有の完了後 Hook（品目名称メール・図面名称メール等）
        foreach (var domainHook in executedDomainHooks)
        {
            await domainHook.OnCommittedAsync(context);
        }

        // 2. 横断的な完了後 Hook（Windows トースト通知等）
        foreach (var postHook in GetPostCommitHooks())
        {
            await postHook.OnAfterWorkflowCommittedAsync(context);
        }
    }
}
