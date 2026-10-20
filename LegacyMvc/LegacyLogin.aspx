<%@ Page Language="VB" AutoEventWireup="false" CodeBehind="LegacyLogin.aspx.vb" Inherits="LegacyMvc.LegacyLogin" %>
<!DOCTYPE html>
<html lang="ja">
<head runat="server">
    <meta charset="utf-8" />
    <title>レガシー ログイン画面 (LegacyMvc - ASP.NET Web Forms)</title>
    <style>
        body { margin: 0; font-family: "Meiryo", "Yu Gothic UI", sans-serif; background: #0f172a; color: #f8fafc; display: flex; justify-content: center; align-items: center; min-height: 100vh; }
        .login-card { background: #1e293b; border: 1px solid #334155; border-radius: 8px; width: 440px; padding: 28px; box-shadow: 0 10px 25px rgba(0,0,0,0.4); }
        .badge { background: #d97706; color: #fff; font-size: 11px; font-weight: bold; padding: 3px 8px; border-radius: 4px; display: inline-block; margin-bottom: 8px; }
        h1 { margin: 0 0 16px 0; font-size: 20px; color: #f8fafc; }
        .field { margin-bottom: 14px; }
        .field label { display: block; font-size: 13px; color: #cbd5e1; margin-bottom: 5px; font-weight: bold; }
        .field input[type="text"], .field input[type="password"] { width: 100%; padding: 8px 10px; background: #0f172a; border: 1px solid #475569; border-radius: 4px; color: #f8fafc; font-size: 14px; box-sizing: border-box; }
        .field input:focus { border-color: #38bdf8; outline: none; }
        .btn-submit { width: 100%; background: #2563eb; color: #fff; border: none; padding: 10px; font-size: 14px; font-weight: bold; border-radius: 4px; cursor: pointer; margin-top: 8px; }
        .btn-submit:hover { background: #1d4ed8; }
        .preset-area { margin-top: 18px; padding-top: 14px; border-top: 1px solid #334155; font-size: 12px; color: #94a3b8; }
        .preset-btn { background: #334155; color: #f1f5f9; border: 1px solid #475569; padding: 4px 10px; border-radius: 4px; cursor: pointer; margin-right: 6px; font-size: 12px; margin-top: 6px; }
        .preset-btn:hover { background: #475569; }
        .msg-error { color: #f87171; font-size: 13px; margin-top: 10px; font-weight: bold; }
        .msg-success { color: #34d399; font-size: 13px; margin-top: 10px; font-weight: bold; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="login-card">
            <span class="badge">ASP.NET Web Forms (.NET Framework 4.8.1)</span>
            <h1>LegacyMvc 業務ポータル ログイン</h1>
            <p style="font-size:12.5px;color:#94a3b8;margin-top:0;margin-bottom:16px;">
                レガシー側専用ログイン画面です。USR_TBL による直接認証を行い、セッションを確立します。
            </p>

            <div class="field">
                <label>ユーザID (USR_ID):</label>
                <asp:TextBox ID="txtUserId" runat="server" Text="yamada" />
            </div>

            <div class="field">
                <label>パスワード (PASSWORD):</label>
                <asp:TextBox ID="txtPassword" runat="server" TextMode="Password" Text="pass123" />
            </div>

            <asp:Button ID="btnLogin" runat="server" Text="ログイン (レガシー認証)" CssClass="btn-submit" />

            <asp:Label ID="lblMessage" runat="server" CssClass="msg-error" EnableViewState="false" />

            <div class="preset-area">
                <strong>検証用アカウント (パスワード共通: pass123):</strong><br />
                <asp:Button ID="btnYamada" runat="server" Text="yamada (管理者)" CssClass="preset-btn" UseSubmitBehavior="false" />
                <asp:Button ID="btnSuzuki" runat="server" Text="suzuki (一般)" CssClass="preset-btn" UseSubmitBehavior="false" />
                <asp:Button ID="btnSato" runat="server" Text="sato (監査)" CssClass="preset-btn" UseSubmitBehavior="false" />
            </div>
        </div>
    </form>
</body>
</html>