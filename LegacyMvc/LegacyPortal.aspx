<%@ Page Language="VB" AutoEventWireup="false" CodeBehind="LegacyPortal.aspx.vb" Inherits="LegacyMvc.LegacyPortal" %>
<!DOCTYPE html>
<html lang="ja">
<head runat="server">
    <meta charset="utf-8" />
    <title>LegacyPortal.aspx - レガシー Web Forms 図面(DRW_TBL)・品目(ITM_TBL) 管理画面</title>
    <style>
        body { margin: 0; font-family: "Meiryo", "Yu Gothic UI", sans-serif; background: #e2e8f0; color: #1e293b; }
        .legacy-banner { background: #334155; color: #f8fafc; padding: 12px 24px; border-bottom: 4px solid #f59e0b; display: flex; justify-content: space-between; align-items: center; }
        .legacy-banner h1 { margin: 0; font-size: 18px; }
        .legacy-banner .tag { background: #f59e0b; color: #0f172a; font-weight: bold; font-size: 12px; padding: 3px 10px; border-radius: 4px; }
        .container { max-width: 1120px; margin: 18px auto; padding: 0 16px; }
        .session-panel { background: #fffbeb; border: 1px solid #f59e0b; border-radius: 6px; padding: 12px 16px; margin-bottom: 16px; font-size: 13px; }
        .grid-layout { display: grid; grid-template-columns: 360px 1fr; gap: 18px; }
        .box { background: #ffffff; border: 1px solid #94a3b8; border-radius: 6px; padding: 16px; box-shadow: 0 2px 4px rgba(0,0,0,0.06); }
        .box h2 { margin: 0 0 12px 0; font-size: 15px; background: #f1f5f9; padding: 8px 10px; border-left: 4px solid #334155; }
        .field { margin-bottom: 10px; }
        .field label { display: block; font-size: 12px; font-weight: bold; margin-bottom: 4px; }
        .field input[type="text"] { width: 100%; padding: 6px 8px; border: 1px solid #64748b; border-radius: 4px; box-sizing: border-box; }
        .btn-postback { background: #1d4ed8; color: #fff; border: none; padding: 8px 16px; border-radius: 4px; font-weight: bold; cursor: pointer; }
        .btn-postback:hover { background: #1e40af; }
        table.legacy-grid { width: 100%; border-collapse: collapse; font-size: 12.5px; margin-top: 10px; }
        table.legacy-grid th { background: #334155; color: #fff; padding: 7px 8px; text-align: left; }
        table.legacy-grid td { border-bottom: 1px solid #cbd5e1; padding: 7px 8px; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="legacy-banner">
            <div>
                <span class="tag">ASP.NET Web Forms (.aspx / VB.NET)</span>
                <h1 style="display:inline;margin-left:10px;">LegacyPortal.aspx - レガシー図面(DRW_TBL)・品目(ITM_TBL) ポータル</h1>
            </div>
            <div style="font-size:12px;">
                <a href="/login" style="color:#fde68a;margin-right:12px;">共通ログイン(/login)</a>
                <a href="/projects" style="color:#fde68a;margin-right:12px;">モダン案件統合(/projects)</a>
                <a href="/workflows" style="color:#fde68a;">モダンWF(/workflows)</a>
            </div>
        </div>

        <div class="container">
            <!-- Redis 共有 loginuser 表示パネル -->
            <div class="session-panel">
                <strong>【Redis 共有セッション (loginuser) 復元状態 - LegacyPortal.aspx.vb】</strong><br />
                <asp:Label ID="lblLoginUserInfo" runat="server" Text="読み込み中..." />
                <br />
                <span style="color:#64748b;font-size:12px;">
                    DBプロバイダー設定 (Web.config): <asp:Label ID="lblDbProvider" runat="server" /> |
                    サーバー時刻: <asp:Label ID="lblServerTime" runat="server" />
                </span>
            </div>

            <div class="grid-layout">
                <!-- 左ペイン: Web Forms ポストバックによる図面 (DRW_TBL) 登録 -->
                <div class="box">
                    <h2>図面 (DRW_TBL) 登録 [Web Forms PostBack]</h2>
                    <div class="field">
                        <label>図面番号 (DRAWING_NO):</label>
                        <asp:TextBox ID="txtDrawingNo" runat="server" Text="DRW-2026-005" />
                    </div>
                    <div class="field">
                        <label>図面名称 (TITLE):</label>
                        <asp:TextBox ID="txtTitle" runat="server" Text="レガシー制御盤 回路図面 (.aspx登録)" />
                    </div>
                    <div class="field">
                        <label>対象品目コード (ITM_CD):</label>
                        <asp:TextBox ID="txtItmCd" runat="server" Text="ITM-001" />
                    </div>
                    <div class="field">
                        <label>関連 BOM 番号 (BOM_NO):</label>
                        <asp:TextBox ID="txtBomNo" runat="server" Text="BOM-2026-001" />
                    </div>
                    <div class="field">
                        <label>改訂番号 (REVISION):</label>
                        <asp:TextBox ID="txtRevision" runat="server" Text="Rev.A" />
                    </div>
                    <asp:Button ID="btnRegisterDrawing" runat="server" Text="DRW_TBL に登録 (PostBack)" CssClass="btn-postback" />
                    <div style="margin-top:10px;color:#047857;font-weight:bold;font-size:12.5px;">
                        <asp:Label ID="lblPostBackMessage" runat="server" />
                    </div>
                </div>

                <!-- 右ペイン: 図面 (DRW_TBL) & 品目 (ITM_TBL) GridView 表示 -->
                <div class="box">
                    <h2>レガシー図面一覧 (DRW_TBL) - LegacyMvc.MDL (VB.NET)</h2>
                    <div style="display:flex;gap:8px;margin-bottom:8px;">
                        <asp:TextBox ID="txtSearchKeyword" runat="server" placeholder="図面番号・名称・品目コードで検索..." style="flex:1;padding:6px;" />
                        <asp:Button ID="btnSearchDrawing" runat="server" Text="検索" CssClass="btn-postback" />
                    </div>
                    <asp:GridView ID="gvDrawings" runat="server" CssClass="legacy-grid" AutoGenerateColumns="true" EmptyDataText="図面データがありません。" />

                    <h2 style="margin-top:20px;">レガシー品目一覧 (ITM_TBL) - EF Core 参照</h2>
                    <asp:GridView ID="gvItems" runat="server" CssClass="legacy-grid" AutoGenerateColumns="true" EmptyDataText="品目データがありません。" />
                </div>
            </div>
        </div>
    </form>
</body>
</html>
