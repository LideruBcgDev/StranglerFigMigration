<#
.SYNOPSIS
    Strangler Fig Client (WinFormsClient) アンインストールスクリプト
#>
param(
    [switch]$Silent
)

$ErrorActionPreference = "Continue"
$AppName = "Strangler Fig 移行検証クライアント"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\StranglerFigClient"

$installDir = "$env:LOCALAPPDATA\Programs\StranglerFigClient"
if (Test-Path $UninstallKey) {
    $regLocation = (Get-ItemProperty -Path $UninstallKey -Name "InstallLocation" -ErrorAction SilentlyContinue).InstallLocation
    if ($regLocation) { $installDir = $regLocation }
}

# 起動中のプロセスがあれば停止
Get-Process -Name "WinFormsClient" -ErrorAction SilentlyContinue | Stop-Process -Force

# ショートカットの削除
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
if (Test-Path $desktopShortcut) { Remove-Item $desktopShortcut -Force }

$startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) "Strangler Fig"
if (Test-Path $startMenuDir) { Remove-Item $startMenuDir -Recurse -Force }

# レジストリ登録の削除
if (Test-Path $UninstallKey) {
    Remove-Item -Path $UninstallKey -Recurse -Force
}

# インストールフォルダの削除 (自身を実行中の場合はバックグラウンドで遅延削除)
if (Test-Path $installDir) {
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c timeout /t 2 /nobreak > NUL & rmdir /s /q `"$installDir`"" -WindowStyle Hidden
}

if (-not $Silent) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "$AppName のアンインストールが完了しました。",
        "アンインストール完了",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
