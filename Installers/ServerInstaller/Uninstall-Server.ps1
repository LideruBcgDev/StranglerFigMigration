<#
.SYNOPSIS
    Strangler Fig サーバー側アンインストールスクリプト
#>
param(
    [string]$InstallDir = "C:\StranglerFigServer",
    [switch]$Silent
)

$ErrorActionPreference = "Continue"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallDir `"$InstallDir`""
    if ($Silent) { $argList += " -Silent" }
    Start-Process -FilePath "powershell.exe" -ArgumentList $argList -Verb RunAs -Wait
    exit 0
}

# 1. Windows サービスの停止・削除
foreach ($svc in @("StranglerYarpGateway", "StranglerModernBff")) {
    if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        & sc.exe delete $svc | Out-Null
    }
}

# 2. IIS サイト・アプリケーションプールの削除
$appCmd = "$env:windir\system32\inetsrv\appcmd.exe"
if (Test-Path $appCmd) {
    & $appCmd delete site "StranglerLegacyMvc" 2>$null | Out-Null
    & $appCmd delete apppool "StranglerLegacyMvcPool" 2>$null | Out-Null
}

# 3. ファイアウォール規則の削除
Remove-NetFirewallRule -DisplayName "Strangler Fig YarpGateway (TCP 5000)" -ErrorAction SilentlyContinue

# 4. フォルダ削除
if (Test-Path $InstallDir) {
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c timeout /t 2 /nobreak > NUL & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden
}

if (-not $Silent) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "Strangler Fig サーバーサービスおよび IIS サイトのアンインストールが完了しました。",
        "サーバーアンインストール完了",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
