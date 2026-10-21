# Strangler Fig: IIS (HTTPS 443) 環境構築・運用手順書

本ドキュメントでは、Strangler Fig（絞め殺し）パターンを採用した **【完全2プロセス構成】** を、Windows Server / ローカル IIS 環境で **HTTPS (ポート 443) 公開** するための設定手順を解説します。

---

## 1. システム全体構成（HTTPS 443 終端アーキテクチャ）

外部・社内LANからのアクセス窓口を **ポート 443 (HTTPS)** に集約し、`ModernBff` が SSL/TLS を終端（SSL Termination）します。  
裏側の `LegacyMvc` はサーバー内部通信専用（HTTP: 5001）として稼働させるため、**SSL 証明書の管理は ModernBff（IIS サイト1）の 1 箇所のみ** で完結します。

```mermaid
flowchart TB
    Client["すべてのクライアント (ブラウザ / WinForms / AI / PowerShell)\nhttps://your-domain.com/ (Port 443 HTTPS)"]

    subgraph WindowsServer ["Windows Server (IIS)"]
        subgraph FrontSite ["【表口・IIS サイト1】 ModernBffSite"]
            direction TB
            Ssl["SSL/TLS 終端 (Port 443 HTTPS)\n※既存の SSL 証明書を適用"]
            Ancm["AspNetCoreModuleV2"]
            ModernCore[".NET 10 Web (Embedded YARP)\n・自前: REST API, MCP, OAuth2 (USR_TBL)\n・インメモリトークン管理 (Redis不要)"]
            Ssl --> Ancm --> ModernCore
        end

        subgraph BackSite ["【裏口・IIS サイト2】 LegacyMvcSite"]
            LegacyApp["ASP.NET 4.8.1 (MVC 5 / Web Forms)\nhttp://127.0.0.1:5001/ (内部専用 HTTP)\n※証明書設定・外部公開は一切不要！"]
        end
    end

    Client -->|HTTPS: 443| Ssl
    ModernCore -->|/api/*, /oauth/*, /api/bff/*| ModernCore
    ModernCore -.->|未処理パス / .aspx / /legacy-api/*\n(YARP: 内部 HTTP 転送)| LegacyApp
```

### URL ルーティング対応表（外部からは単一ホストに見えます）

| 利用者のアクセス先 URL | 実際の処理プロセス | 動作内容 |
| :--- | :--- | :--- |
| **`https://your-domain.com/oauth/token`** | **ModernBff (新)** | パターンA: USR_TBL 照合による Bearer トークン発行 |
| **`https://your-domain.com/api/items`** | **ModernBff (新)** | モダン REST API による品目リソース返却 |
| **`https://your-domain.com/api/bff/mcp/*`** | **ModernBff (新)** | AIツール向け MCP (Model Context Protocol) 実行 |
| **`https://your-domain.com/LegacyLogin.aspx`** | **LegacyMvc (旧)** | **YARP が裏の 5001 へ透過転送** して画面を返却 |
| **`https://your-domain.com/LegacyPortal.aspx`** | **LegacyMvc (旧)** | **YARP が裏の 5001 へ透過転送** して画面を返却 |
| **`https://your-domain.com/Client/execute`** | **LegacyMvc (旧)** | **YARP が裏の 5001 へ透過転送** してコマンド実行 |

---

## 2. 前提条件・事前準備

### ① IIS および ASP.NET 4.8 の有効化
PowerShell（管理者）で実行し、必要な IIS 機能をインストールします。

```powershell
# Windows 10 / 11 の場合
Enable-WindowsOptionalFeature -Online -FeatureName `
    IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, `
    IIS-StaticContent, IIS-DefaultDocument, IIS-DirectoryBrowsing, `
    IIS-ApplicationDevelopment, IIS-ASPNET45, IIS-NetFxExtensibility45 -All

# Windows Server の場合
Install-WindowsFeature -Name Web-Server, Web-App-Dev, Web-Asp-Net45, Web-ISAPI-Ext, Web-ISAPI-Filter -IncludeManagementTools
```

### ② .NET Hosting Bundle のインストール（ModernBff 用）
IIS で .NET 10 / 9 アプリケーション（`AspNetCoreModuleV2`）を動かすためのランタイムをインストールします。

1. マイクロソフト公式より **[.NET Hosting Bundle](https://dotnet.microsoft.com/download/dotnet)** をダウンロードしてインストールします。
2. インストール後、管理者 PowerShell で IIS を再起動します：
   ```powershell
   iisreset
   ```

---

## 3. 手順1: アプリケーションの発行（Publish）

### ① ModernBff の発行
```powershell
# 発行先ディレクトリ例: C:\StranglerFigServer\ModernBff
dotnet publish "E:\MyJobs\VisualStudio\StranglerFigMigration\ModernBff\ModernBff.csproj" `
    -c Release -o "C:\StranglerFigServer\ModernBff"
```

発行先フォルダに以下の `web.config` が生成されていることを確認します：
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet"
                  arguments=".\ModernBff.dll"
                  stdoutLogEnabled="false"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

### ② LegacyMvc の配置
`LegacyMvc` はプロジェクトフォルダ（例: `E:\MyJobs\VisualStudio\StranglerFigMigration\LegacyMvc`）をそのまま参照するか、運用用フォルダ（例: `C:\StranglerFigServer\LegacyMvc`）へコピーします。

---

## 4. 手順2: IIS サイト・アプリケーションプールの構築

### 【方法A】 PowerShell コマンドで一発構築（推奨）

管理者権限の PowerShell で以下を実行します。

```powershell
$appCmd = "$env:windir\system32\inetsrv\appcmd.exe"
$modernPath = "C:\StranglerFigServer\ModernBff"
$legacyPath = "E:\MyJobs\VisualStudio\StranglerFigMigration\LegacyMvc"

# =========================================================================
# 1. 【表口】 ModernBffSite の作成 (HTTPS: 443 / マネージドコードなし)
# =========================================================================
& $appCmd add apppool /name:"ModernBffAppPool" /managedRuntimeVersion:"" /managedPipelineMode:"Integrated"
& $appCmd delete site "ModernBffSite" 2>$null

# サイトの作成 (HTTPS 443)
& $appCmd add site /name:"ModernBffSite" /bindings:"https/*:443:" /physicalPath:"$modernPath"
& $appCmd set app "ModernBffSite/" /applicationPool:"ModernBffAppPool"
icacls "$modernPath" /grant "IIS_IUSRS:(OI)(CI)(RX)" /T

# =========================================================================
# 2. 【裏口】 LegacyMvcSite の作成 (HTTP: 127.0.0.1:5001 / .NET 4.0 統合)
# =========================================================================
& $appCmd add apppool /name:"LegacyMvcAppPool" /managedRuntimeVersion:"v4.0" /managedPipelineMode:"Integrated"
& $appCmd delete site "LegacyMvcSite" 2>$null

# 内部専用バインディング (127.0.0.1:5001 のみ。外部からの直接アクセスを防止)
& $appCmd add site /name:"LegacyMvcSite" /bindings:"http/127.0.0.1:5001:" /physicalPath:"$legacyPath"
& $appCmd set app "LegacyMvcSite/" /applicationPool:"LegacyMvcAppPool"
icacls "$legacyPath" /grant "IIS_IUSRS:(OI)(CI)(RX)" /T

Write-Host "✓ IIS サイトの構成が完了しました。" -ForegroundColor Green
```

---

### 【方法B】 IIS マネージャー (GUI) で手動設定する場合

#### 1. 【表口】 ModernBff の設定
1. `inetmgr`（IIS マネージャー）を開きます。
2. **「アプリケーション プール」** を右クリック → **「アプリケーション プールの追加」**:
   - 名前: `ModernBffAppPool`
   - .NET CLR バージョン: **「マネージド コードなし」**（.NET Core 用）
   - パイプライン モード: **「統合」**
3. **「サイト」** を右クリック → **「Web サイトの追加」**:
   - サイト名: `ModernBffSite`
   - アプリケーション プール: `ModernBffAppPool`
   - 物理パス: `C:\StranglerFigServer\ModernBff`
   - 種類: **`https`**
   - ポート: **`443`**
   - **SSL 証明書:** ドロップダウンから既存の証明書を選択

#### 2. 【裏口】 LegacyMvc の設定
1. **「アプリケーション プール」** を右クリック → **「アプリケーション プールの追加」**:
   - 名前: `LegacyMvcAppPool`
   - .NET CLR バージョン: **`.NET CLR バージョン v4.0.30319`**
   - パイプライン モード: **「統合」**
2. **「サイト」** を右クリック → **「Web サイトの追加」**:
   - サイト名: `LegacyMvcSite`
   - アプリケーション プール: `LegacyMvcAppPool`
   - 物理パス: `E:\MyJobs\VisualStudio\StranglerFigMigration\LegacyMvc`
   - 種類: **`http`**
   - IP アドレス: **`127.0.0.1`**（外部から直接叩かれないよう限定）
   - ポート: **`5001`**

---

## 5. 手順3: SSL 証明書のバインド確認

IIS マネージャーで、`ModernBffSite` に SSL 証明書が正常に割り当てられているか確認します。

1. IIS マネージャーで **`ModernBffSite`** を選択。
2. 右側メニューの **「バインド...」** をクリック。
3. `https (443)` の行を選択し、**「編集」** をクリック。
4. **「SSL 証明書」** に既存の有効なサーバー証明書が選択されていることを確認して「OK」をクリック。

---

## 6. 手順4: Windows ファイアウォールの設定

外部・社内LAN端末に対しては、**HTTPS (443) のみ** を開放します。裏側の 5001 は開放しません。

```powershell
# ポート 443 (HTTPS) の受信を許可
New-NetFirewallRule -DisplayName "Strangler Fig ModernBff (HTTPS 443)" `
    -Direction Inbound -LocalPort 443 -Protocol TCP -Action Allow
```

---

## 7. 手順5: クライアント側の接続設定

クライアント（WinForms やスクリプト）は、すべて **`https://your-domain.com/`** に接続します。

### `WinFormsClient/client-settings.json` の設定
```json
{
  "GatewayBaseUrl": "https://your-domain.com/",
  "LegacyBaseUrl": "http://localhost:5001/",
  "ModernApiBaseUrl": "https://your-domain.com/",
  "LoginMode": "Legacy",
  "LegacyLoginUrl": "https://your-domain.com/LegacyLogin.aspx?clientWebView=true",
  "Comment": "【推奨2プロセス構成】ModernBff が HTTPS (443) で YARP を内蔵し一括受付"
}
```

---

## 8. 動作確認チェックリスト

ブラウザおよび PowerShell から、以下の確認を行います。

### ① レガシー画面の HTTPS 透過確認 (YARP 転送)
ブラウザで以下にアクセス：
- **`https://your-domain.com/LegacyLogin.aspx`**
  - ModernBff (443) 経由で、裏の LegacyMvc (:5001) のログイン画面が表示されれば成功です。
- **`https://your-domain.com/LegacyPortal.aspx`**
  - レガシー業務ポータル画面が表示されれば成功です。

### ② モダン REST API / MCP の直接処理確認
PowerShell から実行：
```powershell
# 1. パターンA: USR_TBL 認証による Bearer トークン取得
$token = Invoke-RestMethod -Uri "https://your-domain.com/oauth/token" -Method Post -Body (@{
    grant_type = "password"
    user_id    = "yamada"
    password   = "pass123"
} | ConvertTo-Json) -ContentType "application/json"

# 2. REST API 呼び出し
Invoke-RestMethod -Uri "https://your-domain.com/api/items" -Headers @{
    Authorization = "Bearer $($token.access_token)"
}

# 3. MCP ツール呼び出し
Invoke-RestMethod -Uri "https://your-domain.com/api/bff/mcp/streamable" -Method Post -Headers @{
    Authorization = "Bearer $($token.access_token)"
} -Body '{"jsonrpc":"2.0","id":"1","method":"tools/call","params":{"name":"get_projects_and_workflows"}}' -ContentType "application/json"
```

---

## 9. トラブルシューティング

| 現象 | 主な原因 | 対処方法 |
| :--- | :--- | :--- |
| **HTTP 502.5 / 500.30**<br>(ANCM Error) | .NET Hosting Bundle 未導入、またはアーキテクチャ不一致 | .NET Hosting Bundle を再インストールし、`iisreset` を実行。`web.config` の `stdoutLogEnabled="true"` にして `.\logs` 内のエラー詳細を確認。 |
| **HTTP 502 Bad Gateway**<br>(YARP Error) | 裏側の `LegacyMvcSite` が未起動、またはポート不一致 | IIS マネージャーで `LegacyMvcSite` が「開始」状態か確認。ブラウザからサーバー上で直接 `http://127.0.0.1:5001/` が開けるか確認。 |
| **ポート 443 のバインド競合** | `Default Web Site` 等、既存サイトが 443 を既に使用中 | 既存の競合サイトを停止するか、既存サイトの 443 バインディングを削除してから `ModernBffSite` に割り当て。 |
| **403.14 Forbidden** | 物理パスのアクセス権不足 | `icacls <発行先フォルダ> /grant "IIS_IUSRS:(OI)(CI)(RX)" /T` を実行。 |
