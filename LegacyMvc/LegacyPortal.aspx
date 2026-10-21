<%@ Page Language="VB" AutoEventWireup="false" CodeBehind="LegacyPortal.aspx.vb" Inherits="LegacyMvc.LegacyPortal" ResponseEncoding="utf-8" %>
<!DOCTYPE html>
<html lang="ja">
<head runat="server">
    <meta charset="utf-8" />
    <title>LegacyPortal.aspx - レガシー Web Forms 図面・品目ポータル</title>
    <style>
        body { margin: 0; font-family: "Meiryo", "Yu Gothic UI", sans-serif; background: #e2e8f0; color: #1e293b; }
        .legacy-banner { background: #334155; color: #f8fafc; padding: 12px 24px; border-bottom: 4px solid #f59e0b; display: flex; justify-content: space-between; align-items: center; }
        .legacy-banner h1 { margin: 0; font-size: 18px; }
        .container { max-width: 1200px; margin: 20px auto; padding: 0 16px; }
        .box { background: white; border-radius: 8px; padding: 20px; margin-bottom: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.05); }
        .box h2 { margin-top: 0; color: #0f172a; border-bottom: 2px solid #e2e8f0; padding-bottom: 8px; font-size: 16px; }
        .info-panel { background: #eff6ff; border-left: 4px solid #3b82f6; padding: 12px; margin-bottom: 16px; font-size: 13px; line-height: 1.6; }
        .legacy-grid { width: 100%; border-collapse: collapse; margin-top: 12px; font-size: 13px; }
        .legacy-grid th, .legacy-grid td { border: 1px solid #cbd5e1; padding: 8px; text-align: left; }
        .legacy-grid th { background: #f8fafc; color: #334155; }
        .btn-postback { background: #2563eb; color: white; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; font-weight: bold; }
        .btn-postback:hover { background: #1d4ed8; }
        .form-row { margin-bottom: 10px; }
        .form-row label { display: block; font-size: 12px; font-weight: bold; margin-bottom: 4px; }
        .form-control { width: 95%; padding: 6px; border: 1px solid #cbd5e1; border-radius: 4px; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="legacy-banner">
            <div>
                <h1>[Legacy] ASP.NET 4.8.1 Web Forms ポータル (LegacyPortal.aspx)</h1>
                <small>クライアント起動 / RequestManager 連携 / YarpGateway (ModernBff) 透過リダイレクト</small>
            </div>
            <div>
                サーバー時刻: <asp:Label ID="lblServerTime" runat="server" Text="" />
            </div>
        </div>

        <div class="container">
            <!-- 復元されたログインユーザー & RequestManager 情報 -->
            <div class="box">
                <h2>クライアント認証連携情報 (RequestManager / req_code)</h2>
                <div class="info-panel">
                    <asp:Label ID="lblLoginUserInfo" runat="server" Text="未ログイン" Font-Bold="true" ForeColor="#1e3a8a" /><br />
                    データベース接続: <asp:Label ID="lblDbProvider" runat="server" Text="SqlServer" />
                </div>
            </div>

            <div style="display:grid; grid-template-columns: 1fr 1fr; gap: 16px;">
                <!-- 左ペイン: 新規図面登録フォーム -->
                <div class="box">
                    <h2>図面 (DRW_TBL) 新規登録 - LegacyMvc (PostBack)</h2>
                    <div class="form-row">
                        <label>図面番号:</label>
                        <asp:TextBox ID="txtDrawingNo" runat="server" CssClass="form-control" Text="drw-web-001" />
                    </div>
                    <div class="form-row">
                        <label>図面名称:</label>
                        <asp:TextBox ID="txtTitle" runat="server" CssClass="form-control" Text="新規設計ユニット組立図" />
                    </div>
                    <div class="form-row">
                        <label>紐付け品目コード:</label>
                        <asp:TextBox ID="txtItmCd" runat="server" CssClass="form-control" Text="ITM-001" />
                    </div>
                    <div class="form-row">
                        <label>BOM番号:</label>
                        <asp:TextBox ID="txtBomNo" runat="server" CssClass="form-control" Text="BOM-2026-001" />
                    </div>
                    <div class="form-row">
                        <label>リビジョン:</label>
                        <asp:TextBox ID="txtRevision" runat="server" CssClass="form-control" Text="Rev.A" />
                    </div>
                    <asp:Button ID="btnRegisterDrawing" runat="server" Text="図面登録実行 (PostBack)" CssClass="btn-postback" />
                    <div style="margin-top:10px;">
                        <asp:Label ID="lblPostBackMessage" runat="server" ForeColor="#16a34a" Font-Bold="true" />
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
