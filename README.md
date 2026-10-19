# Strangler Fig 移行検証ソリューション 説明書・操作ガイド

本ドキュメントでは、ストラングラー・フィグ（Strangler Fig）パターンの移行検証用 .NET ソリューションの全体構成、共通 Web ログイン（`loginuser` の Redis 共有）、3種類の MCP コントローラー、および Windows 向けインストーラー（サーバー用／クライアント用）の使い方を解説します。

---

## 1. システム全体構成とアーキテクチャ

```mermaid
flowchart TB
    subgraph ClientPC ["Windows クライアント端末"]
        WinForms["WinFormsClient (VB.NET / .NET 8)\n・HttpClient データ取得\n・WebView2 共通ログイン画面"]
        Browser["Web ブラウザ"]
    end

    subgraph Server ["AP サーバー (Windows Server / Localhost)"]
        Gateway["YarpGateway (.NET 8 :5000)\nMicrosoft.ReverseProxy (YARP)"]
        
        subgraph Modern ["ModernBff (.NET 10 / .NET 9 :5002)"]
            Auth["AuthController & /login (Webログイン画面)"]
            BffApi["BffCustomerController (/api/bff/customer)"]
            Mcp1["McpStreamableHttpController (方式1)"]
            Mcp2["McpSseController (方式2)"]
            Mcp3["McpAttributeBridgeController (方式3)"]
        end

        subgraph Legacy ["LegacyMvc (.NET Framework 4.8.1 :5001)"]
            LegacyCtrl["LegacyDataController\n(/LegacyData/GetCustomer)"]
        end

        Redis[("Redis (:6379)\n・loginuser 共有 (ユーザ・組織・権限)\n・分散セッション / KVS")]
    end

    WinForms & Browser -->|http://localhost:5000/*| Gateway
    Gateway -->|/login, /api/bff/*| Modern
    Gateway -->|/legacy-api/* (Prefix除去)| Legacy
    BffApi & Mcp1 -.->|useLegacyFallback=true / source=legacy| LegacyCtrl
    Auth <-->|StranglerSession:LoginUser:{token}| Redis
    BffApi & Mcp1 & Mcp2 & Mcp3 <-->|loginuser 参照| Redis
    LegacyCtrl <-->|同一キーから loginuser 復元| Redis
```

### プロジェクト・ファイル構成

| プロジェクト / フォルダ | ターゲット / 技術要素 | ポート | 役割 |
| :--- | :--- | :--- | :--- |
| **`YarpGateway`** | .NET 8 (`Yarp.ReverseProxy`) | `5000` | 全リクエストを受け付けるリバースプロキシ。`/legacy-api/{**catch-all}` を `LegacyMvc` へ、`/api/bff/{**catch-all}` と `/login` を `ModernBff` へ転送。Windows サービス動作対応。 |
| **`LegacyMvc`** | .NET Framework 4.8.1 (ASP.NET MVC 5) | `5001` | 移行元のレガシー MVC アプリ。`LegacyDataController.GetCustomer` で JSON を返却。`StackExchange.Redis` を用いて `ModernBff` が発行した `loginuser`（ユーザ・組織・権限）を共有参照。 |
| **`ModernBff`** | .NET 10 / .NET 9 Web API | `5002` | 移行先のモダン BFF レイヤー。Web ログイン画面 (`/login`)、`AddStackExchangeRedisCache` による分散セッション、`BffCustomerController`、および 3 方式の MCP コントローラーを提供。Windows サービス動作対応。 |
| **`WinFormsClient`** | VB.NET (.NET 8 Windows Forms + WebView2) | - | クライアント端末用アプリ。`WebView2` で Gateway 経由の Web ログイン画面を表示し、取得した `loginuser` トークンを使って Gateway 経由で各 API / MCP の JSON を取得・表示。 |
| **`Installers`** | PowerShell + C# 自己展開 EXE (`csc.exe`) | - | 有料ツール不要（Windows 標準機能のみ）でサーバー用・クライアント用の単一 `.exe` / `.zip` インストーラーを生成するスクリプト一式。 |
| **`docker-compose.yml`** | Redis 7 Alpine (`redis:7-alpine`) | `6379` | 新旧システム間でセッションおよび `loginuser` を共有するためのローカル検証用 Redis コンテナ定義。 |

---

## 2. 共通 Web ログインと `loginuser`（ユーザ・組織・権限）の共有

認証とセッション発行はモダン側（`ModernBff`）に集約されており、ブラウザからも WinForms (`WebView2`) からも共通の `/login` 画面を使用します。

### 検証用プリセットアカウント（パスワード共通: `pass123`）

| ユーザID | 氏名 | 組織コード / 組織名 | 付与されている権限 (`roles`) |
| :--- | :--- | :--- | :--- |
| **`yamada`** | 山田 太郎 | `[ORG-SALES-01]` 第一営業本部 エンタープライズ営業部 | `Admin`, `SalesManager`, `CustomerRead`, `CustomerWrite`, `LegacyAccess` |
| **`suzuki`** | 鈴木 花子 | `[ORG-CS-02]` カスタマーサクセス推進室 | `Operator`, `CustomerRead` |
| **`sato`** | 佐藤 健一 | `[ORG-AUDIT-99]` 内部監査室 | `Auditor`, `CustomerRead`, `LegacyAccess` |

### `loginuser` 共有の流れ
1. `/login` 画面でログインすると、`ModernBff` の `AuthController` が `LoginUser` オブジェクトを生成し、Redis の `StranglerSession:LoginUser:{token}` キーに JSON 保存します（Redis 未起動時は自動的にインメモリフォールバックで動作継続します）。
2. クライアントには Cookie (`StranglerAuthToken`) とヘッダー (`X-Strangler-AuthToken`) でトークンが返却されます。
3. 以降、`ModernBff` でも `LegacyMvc`（`SharedLoginUser.ResolveFromRedis`）でも、同じトークンから同一の `loginuser`（ユーザ・組織・権限）を復元して認可判定やレスポンス生成を行います。

---

## 3. 開発環境での起動・動作確認手順

### 手順1: Redis コンテナの起動
```powershell
docker compose up -d
```
※ Docker が起動していない環境でも、各コントローラーのフォールバック処理によりエラーで停止せず動作確認が可能です。

### 手順2: サーバープロジェクトの起動
別々のターミナル（または Visual Studio のマルチスタートアップ）で起動します。
```powershell
# 1. ModernBff (:5002) の起動
dotnet run --project ModernBff/ModernBff.csproj

# 2. YarpGateway (:5000) の起動
dotnet run --project YarpGateway/YarpGateway.csproj
```
※ `LegacyMvc` (:5001) は Visual Studio (IIS Express) または後述のサーバーインストーラー (IIS) で起動します。未起動時でも `ModernBff` が自動的にグレースフル・フォールバック応答を返します。

### 手順3: クライアント (`WinFormsClient`) の起動と操作
```powershell
dotnet run --project WinFormsClient/WinFormsClient.vbproj
```
1. 画面右上の **「Webログイン画面を開く」** ボタンをクリックします。
2. `WebView2` ダイアログで `/login` が開くので、プリセット（例：`yamada`）を選択して **「ログインして loginuser を共有」** をクリックします。
3. ダイアログが自動的に閉じ、WinForms 上部にログイン中のユーザ・組織・権限が表示されます。
4. コンボボックスから確認したいエンドポイントを選択し、**「データ取得」** ボタンをクリックすると、整形された JSON がテキストボックスに表示されます。

### 確認できるエンドポイント一覧（Gateway `:5000` 経由）

| エンドポイント | 転送先・動作内容 |
| :--- | :--- |
| `/api/bff/customer/1` | `ModernBff` の新ロジックで顧客情報と `loginuser` の組織・編集権限判定を返却 |
| `/api/bff/customer/1?useLegacyFallback=true` | `ModernBff` から内部的に `LegacyMvc` を呼び出し、新旧統合レスポンスを返却 |
| `/api/bff/auth/me` | 現在ログイン中の `loginuser`（ユーザ・組織・権限）と取得元（Redis / Fallback）を確認 |
| `/legacy-api/LegacyData/GetCustomer?id=1` | `YarpGateway` から `LegacyMvc` へ直接ルーティングし、レガシー側で Redis から復元した `loginuser` と顧客 JSON を返却 |
| `/api/bff/mcp/streamable?tool=get_customer_strangler&source=modern` | **MCP 方式1 (Streamable HTTP JSON-RPC)** の実行結果を表示 |
| `/api/bff/mcp/sse?oneShot=true&customerId=1` | **MCP 方式2 (HTTP + SSE)** の `text/event-stream` イベント出力を表示 |
| `/api/bff/mcp/bridge/compare` | **MCP 方式3 (Attribute / Reflection)** の自動検出ツール一覧と3方式の比較表を表示 |
| `/api/bff/mcp/bridge/invoke/check_permission?customerId=1` | **MCP 方式3** の `[McpTool("check_permission")]` を実行し、`loginuser` のロール権限マトリクスを表示 |

※ 各 MCP コントローラー方式の詳細な比較については [MCP_CONTROLLERS_GUIDE.md](MCP_CONTROLLERS_GUIDE.md) を参照してください。

---

## 4. Windows 向けインストーラーのビルドと使い方

有料のサードパーティ製インストーラー（Inno Setup 等）を使用せず、Windows 標準機能と .NET SDK のみで **クライアント用** と **サーバー用** の2種類のインストーラーを生成・実行できます。

### 4.1 インストーラーの再ビルド方法

ソースコードを変更した後にインストーラーを作り直す場合は、ソリューションルートで以下を実行します。

```powershell
# 1. 軽量版（フレームワーク依存）で .exe と .zip を一括生成
powershell -NoProfile -ExecutionPolicy Bypass -File Installers/Build-Installers.ps1

# 2. .NET ランタイム未導入の PC / Server 向けにランタイム同梱 (Self-Contained) で生成する場合
powershell -NoProfile -ExecutionPolicy Bypass -File Installers/Build-Installers.ps1 -SelfContained $true
```

実行すると `Installers/Output/` フォルダに以下の4ファイルが出力されます。
* `StranglerFig-Client-Setup.exe` / `StranglerFig-Client-Setup.zip`
* `StranglerFig-Server-Setup.exe` / `StranglerFig-Server-Setup.zip`

---

### 4.2 クライアント側インストーラーの使い方 (`StranglerFig-Client-Setup.exe`)

業務担当者の Windows 端末に `WinFormsClient` を導入するためのインストーラーです（管理者権限不要、一般ユーザー権限で動作します）。

1. **`StranglerFig-Client-Setup.exe` をダブルクリック**して起動します（または ZIP 展開後の `Setup-Client.cmd` を実行）。
2. セットアップ画面が表示されるので、以下を確認・入力して **「インストール実行」** をクリックします。
   * **インストール先フォルダ**: デフォルトは `%LocalAppData%\Programs\StranglerFigClient`
   * **接続先 YarpGateway URL**: サーバーの URL（例: `http://localhost:5000/` または `http://192.168.1.100:5000/`）
3. 自動的にアプリが配置され、入力した URL が `client-settings.json` に保存されます。また、**デスクトップ**と**スタートメニュー**に「Strangler Fig 移行検証クライアント」のショートカットが作成されます。

#### サイレントインストール（コマンドライン配布時）
```powershell
.\StranglerFig-Client-Setup.exe -Silent -GatewayUrl "http://192.168.1.100:5000/"
```

#### クライアントのアンインストール方法
* Windows の「設定」$\rightarrow$「アプリ」$\rightarrow$「インストールされているアプリ（またはプログラムと機能）」から **「Strangler Fig 移行検証クライアント」** を選択してアンインストールできます。
* またはインストール先フォルダ内の `Uninstall-Client.ps1` を実行します。

---

### 4.3 サーバー側インストーラーの使い方 (`StranglerFig-Server-Setup.exe`)

Windows Server（または検証用ホスト PC）に `YarpGateway`, `ModernBff`, `LegacyMvc` を一括配置し、サービス登録を行うインストーラーです。

1. **`StranglerFig-Server-Setup.exe` を実行**します（自動的に管理者権限 UAC 昇格ダイアログが表示されます）。
2. セットアップ画面で以下を確認・入力して **「サーバー構築・開始」** をクリックします。
   * **サーバーインストール先ディレクトリ**: デフォルトは `C:\StranglerFigServer`
   * **共有セッション用 Redis 接続文字列**: デフォルトは `localhost:6379,abortConnect=false,connectTimeout=3000`
3. インストーラーが以下を自動実行します。
   * `C:\StranglerFigServer` 配下に `YarpGateway`, `ModernBff`, `LegacyMvc`, `docker-compose.yml` を配置
   * Windows サービス **`StranglerYarpGateway`** (Port `5000`) を自動起動登録＆開始
   * Windows サービス **`StranglerModernBff`** (Port `5002`) を自動起動登録＆開始
   * IIS (`appcmd.exe`) が有効な環境では、アプリケーションプール **`StranglerLegacyMvcPool`** と IIS サイト **`StranglerLegacyMvc`** (Port `5001`) を自動作成
   * Windows ファイアウォールで受信ポート `5000` (TCP) を開放

#### サイレントインストール（自動化・CI/CD 配布時）
```powershell
.\StranglerFig-Server-Setup.exe -Silent -InstallDir "C:\StranglerFigServer" -RedisConnection "localhost:6379,abortConnect=false"
```

#### サーバーのアンインストール方法
サーバーのサービス停止・登録解除・IIS サイト削除・フォルダ削除を行う場合は、インストール先に配置された `Uninstall-Server.ps1` を実行します。
```powershell
powershell -ExecutionPolicy Bypass -File "C:\StranglerFigServer\Uninstall-Server.ps1"
```
