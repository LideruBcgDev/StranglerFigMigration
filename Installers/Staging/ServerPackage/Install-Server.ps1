<#
.SYNOPSIS
    Strangler Fig サーバー側一括インストーラー (YarpGateway + ModernBff + LegacyMvc + ファイアウォール設定)
    ※ 管理者権限 (Run as Administrator) で動作します（未昇格時は自動昇格します）。

.DESCRIPTION
    1. C:\StranglerFigServer (デフォルト) に YarpGateway, ModernBff, LegacyMvc, docker-compose.yml を配置
    2. YarpGateway (:5000) と ModernBff (:5002) を Windows サービス (sc.exe) として登録・自動起動設定・開始
    3. IIS (WebAdministration / appcmd) が存在する場合は LegacyMvc (:5001) を IIS アプリケーションプール & サイトとして登録
    4. Windows ファイアウォールでポート 5000 (YarpGateway) を開放
#>
param(
    [string]$InstallDir = "C:\StranglerFigServer",
    [string]$RedisConnection = "localhost:6379,abortConnect=false,connectTimeout=3000",
    [switch]$Silent
)

$ErrorActionPreference = "Stop"

# 管理者権限チェック＆自動昇格
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallDir `"$InstallDir`" -RedisConnection `"$RedisConnection`""
    if ($Silent) { $argList += " -Silent" }
    Start-Process -FilePath "powershell.exe" -ArgumentList $argList -Verb RunAs -Wait
    exit 0
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Install-ServerComponents([string]$targetRoot, [string]$redisConn) {
    $gatewayTarget = Join-Path $targetRoot "YarpGateway"
    $bffTarget     = Join-Path $targetRoot "ModernBff"
    $legacyTarget  = Join-Path $targetRoot "LegacyMvc"

    # 既存サービスが動いていれば一度停止
    foreach ($svc in @("StranglerYarpGateway", "StranglerModernBff")) {
        if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
            Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
        }
    }

    New-Item -ItemType Directory -Path $gatewayTarget -Force | Out-Null
    New-Item -ItemType Directory -Path $bffTarget -Force | Out-Null
    New-Item -ItemType Directory -Path $legacyTarget -Force | Out-Null

    # 1. ファイルコピー
    Copy-Item -Path (Join-Path $PSScriptRoot "YarpGateway\*") -Destination $gatewayTarget -Recurse -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "ModernBff\*")   -Destination $bffTarget -Recurse -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "LegacyMvc\*")   -Destination $legacyTarget -Recurse -Force

    foreach ($extra in @("docker-compose.yml", "Uninstall-Server.ps1")) {
        $extraPath = Join-Path $PSScriptRoot $extra
        if (Test-Path $extraPath) {
            Copy-Item -Path $extraPath -Destination $targetRoot -Force
        }
    }

    # ModernBff の Redis 接続文字列を更新
    $bffSettingsPath = Join-Path $bffTarget "appsettings.json"
    if (Test-Path $bffSettingsPath) {
        $bffJson = Get-Content $bffSettingsPath -Raw | ConvertFrom-Json
        $bffJson.ConnectionStrings.Redis = $redisConn
        $bffJson | ConvertTo-Json -Depth 10 | Set-Content -Path $bffSettingsPath -Encoding UTF8
    }

    # 2. Windows サービスの登録・起動 (YarpGateway: 5000, ModernBff: 5002)
    $gatewayExe = Join-Path $gatewayTarget "YarpGateway.exe"
    $bffExe     = Join-Path $bffTarget "ModernBff.exe"

    Register-WindowsService -serviceName "StranglerYarpGateway" `
        -displayName "Strangler Fig - YARP Reverse Proxy Gateway (:5000)" `
        -binaryPath "`"$gatewayExe`" --contentRoot `"$gatewayTarget`" --urls `"http://0.0.0.0:5000`""

    Register-WindowsService -serviceName "StranglerModernBff" `
        -displayName "Strangler Fig - Modern BFF Web API & MCP (:5002)" `
        -binaryPath "`"$bffExe`" --contentRoot `"$bffTarget`" --urls `"http://localhost:5002`""

    # 3. IIS サイト登録 (LegacyMvc :5001) - IIS (appcmd.exe) が存在する場合に自動構成
    $appCmd = "$env:windir\system32\inetsrv\appcmd.exe"
    $iisStatus = "IIS未検出 (LegacyMvc ファイルは $legacyTarget に配置済み)"
    if (Test-Path $appCmd) {
        & $appCmd add apppool /name:"StranglerLegacyMvcPool" /managedRuntimeVersion:"v4.0" /managedPipelineMode:"Integrated" 2>$null | Out-Null
        & $appCmd delete site "StranglerLegacyMvc" 2>$null | Out-Null
        & $appCmd add site /name:"StranglerLegacyMvc" /bindings:"http/*:5001:" /physicalPath:"$legacyTarget" 2>$null | Out-Null
        & $appCmd set app "StranglerLegacyMvc/" /applicationPool:"StranglerLegacyMvcPool" 2>$null | Out-Null
        $iisStatus = "IIS サイト 'StranglerLegacyMvc' (Port 5001) を登録完了"
    }

    # 4. Windows ファイアウォール受信規則の登録 (Port 5000)
    New-NetFirewallRule -DisplayName "Strangler Fig YarpGateway (TCP 5000)" `
        -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null

    return $iisStatus
}

function Register-WindowsService([string]$serviceName, [string]$displayName, [string]$binaryPath) {
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        & sc.exe stop $serviceName | Out-Null
        Start-Sleep -Seconds 1
        & sc.exe delete $serviceName | Out-Null
        Start-Sleep -Seconds 1
    }
    & sc.exe create $serviceName binPath= $binaryPath start= auto DisplayName= $displayName | Out-Null
    & sc.exe description $serviceName $displayName | Out-Null
    Start-Service -Name $serviceName -ErrorAction SilentlyContinue
}

if ($Silent) {
    $iisMsg = Install-ServerComponents -targetRoot $InstallDir -redisConn $RedisConnection
    Write-Host "[OK] サーバーコンポーネントのインストールが完了しました: $InstallDir ($iisMsg)" -ForegroundColor Green
    exit 0
}

# GUI サーバーインストーラー画面
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object System.Windows.Forms.Form
$form.Text = "Strangler Fig サーバー環境インストーラー (管理者モード)"
$form.Size = New-Object System.Drawing.Size(560, 340)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$lblTitle = New-Object System.Windows.Forms.Label
$lblTitle.Text = "Strangler Fig サーバー一括セットアップ (Gateway / BFF / LegacyMvc)"
$lblTitle.Font = New-Object System.Drawing.Font("Yu Gothic UI", 10.5, [System.Drawing.FontStyle]::Bold)
$lblTitle.Location = New-Object System.Drawing.Point(20, 18)
$lblTitle.Size = New-Object System.Drawing.Size(510, 26)

$lblDir = New-Object System.Windows.Forms.Label
$lblDir.Text = "サーバーインストール先ディレクトリ:"
$lblDir.Location = New-Object System.Drawing.Point(20, 58)
$lblDir.AutoSize = $true

$txtDir = New-Object System.Windows.Forms.TextBox
$txtDir.Text = $InstallDir
$txtDir.Location = New-Object System.Drawing.Point(20, 82)
$txtDir.Size = New-Object System.Drawing.Size(500, 26)

$lblRedis = New-Object System.Windows.Forms.Label
$lblRedis.Text = "共有セッション用 Redis 接続文字列:"
$lblRedis.Location = New-Object System.Drawing.Point(20, 122)
$lblRedis.AutoSize = $true

$txtRedis = New-Object System.Windows.Forms.TextBox
$txtRedis.Text = $RedisConnection
$txtRedis.Location = New-Object System.Drawing.Point(20, 146)
$txtRedis.Size = New-Object System.Drawing.Size(500, 26)

$lblInfo = New-Object System.Windows.Forms.Label
$lblInfo.Text = "・YarpGateway (:5000) および ModernBff (:5002) を Windows サービスとして登録・開始します。`n・IIS が有効な場合は LegacyMvc (:5001) を IIS サイトとして自動構成します。"
$lblInfo.Location = New-Object System.Drawing.Point(20, 185)
$lblInfo.Size = New-Object System.Drawing.Size(500, 42)

$btnInstall = New-Object System.Windows.Forms.Button
$btnInstall.Text = "サーバー構築・開始"
$btnInstall.Location = New-Object System.Drawing.Point(260, 240)
$btnInstall.Size = New-Object System.Drawing.Size(145, 36)
$btnInstall.BackColor = [System.Drawing.Color]::FromArgb(5, 150, 105)
$btnInstall.ForeColor = [System.Drawing.Color]::White
$btnInstall.FlatStyle = "Flat"

$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = "キャンセル"
$btnCancel.Location = New-Object System.Drawing.Point(415, 240)
$btnCancel.Size = New-Object System.Drawing.Size(105, 36)
$btnCancel.Add_Click({ $form.Close() })

$btnInstall.Add_Click({
    try {
        $btnInstall.Enabled = $false
        $btnInstall.Text = "構築中..."
        $form.Refresh()

        $iisResult = Install-ServerComponents -targetRoot $txtDir.Text.Trim() -redisConn $txtRedis.Text.Trim()

        [System.Windows.Forms.MessageBox]::Show(
            "サーバーコンポーネントのインストールとサービス起動が完了しました。`n`n" +
            "・Windows サービス: StranglerYarpGateway (http://localhost:5000)`n" +
            "・Windows サービス: StranglerModernBff (http://localhost:5002)`n" +
            "・LegacyMvc: $iisResult",
            "サーバー構築完了",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null

        $form.Close()
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show(
            "サーバーインストール中にエラーが発生しました:`n" + $_.Exception.Message,
            "エラー",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        $btnInstall.Enabled = $true
        $btnInstall.Text = "サーバー構築・開始"
    }
})

$form.Controls.AddRange(@($lblTitle, $lblDir, $txtDir, $lblRedis, $txtRedis, $lblInfo, $btnInstall, $btnCancel))
[void]$form.ShowDialog()
