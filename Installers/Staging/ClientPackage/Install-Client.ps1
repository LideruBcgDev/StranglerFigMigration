<#
.SYNOPSIS
    Strangler Fig Client (WinFormsClient) セットアップスクリプト
    外部有料ツール不要・Windows 標準機能のみでインストールを行います。

.DESCRIPTION
    1. インストール先フォルダ (%LocalAppData%\Programs\StranglerFigClient) へアプリ一式をコピー
    2. 接続先 YarpGateway URL を client-settings.json に書き込み
    3. デスクトップおよびスタートメニューにショートカット (.lnk) を作成
    4. Windows の「インストールされているアプリ (プログラムの追加と削除)」にアンインストール情報を登録
#>
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\StranglerFigClient",
    [string]$GatewayUrl = "http://localhost:5000/",
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$AppName = "Strangler Fig 移行検証クライアント"
$Publisher = "StranglerFig Migration Lab"
$Version = "1.0.0"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\StranglerFigClient"

function Execute-Install([string]$targetDir, [string]$targetGatewayUrl) {
    $sourceAppDir = Join-Path $PSScriptRoot "App"
    if (-not (Test-Path $sourceAppDir)) {
        throw "配布元アプリケーションフォルダ ($sourceAppDir) が見つかりません。Build-Installers.ps1 を先に実行してください。"
    }

    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    # 1. アプリファイルのコピー
    Copy-Item -Path "$sourceAppDir\*" -Destination $targetDir -Recurse -Force

    # アンインストーラーもインストール先へコピー
    $uninstallScript = Join-Path $PSScriptRoot "Uninstall-Client.ps1"
    if (Test-Path $uninstallScript) {
        Copy-Item -Path $uninstallScript -Destination $targetDir -Force
    }

    # 2. 接続先 GatewayBaseUrl の設定反映
    if (-not $targetGatewayUrl.EndsWith("/")) {
        $targetGatewayUrl = "$targetGatewayUrl/"
    }
    $settingsObj = @{ GatewayBaseUrl = $targetGatewayUrl }
    $settingsJson = $settingsObj | ConvertTo-Json -Depth 3
    Set-Content -Path (Join-Path $targetDir "client-settings.json") -Value $settingsJson -Encoding UTF8

    # 3. ショートカット作成 (デスクトップ & スタートメニュー)
    $exePath = Join-Path $targetDir "WinFormsClient.exe"
    $wsh = New-Object -ComObject WScript.Shell

    $desktopShortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
    $scDesktop = $wsh.CreateShortcut($desktopShortcutPath)
    $scDesktop.TargetPath = $exePath
    $scDesktop.WorkingDirectory = $targetDir
    $scDesktop.Description = $AppName
    $scDesktop.Save()

    $startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) "Strangler Fig"
    if (-not (Test-Path $startMenuDir)) {
        New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
    }
    $startShortcutPath = Join-Path $startMenuDir "$AppName.lnk"
    $scStart = $wsh.CreateShortcut($startShortcutPath)
    $scStart.TargetPath = $exePath
    $scStart.WorkingDirectory = $targetDir
    $scStart.Description = $AppName
    $scStart.Save()

    # 4. Windows「プログラムと機能 / インストールされているアプリ」(HKCU) への登録
    if (-not (Test-Path $UninstallKey)) {
        New-Item -Path $UninstallKey -Force | Out-Null
    }
    $uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$targetDir\Uninstall-Client.ps1`""
    Set-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value $AppName
    Set-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value $Version
    Set-ItemProperty -Path $UninstallKey -Name "Publisher" -Value $Publisher
    Set-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $targetDir
    Set-ItemProperty -Path $UninstallKey -Name "DisplayIcon" -Value $exePath
    Set-ItemProperty -Path $UninstallKey -Name "UninstallString" -Value $uninstallCmd
    Set-ItemProperty -Path $UninstallKey -Name "NoModify" -Value 1 -Type DWord
    Set-ItemProperty -Path $UninstallKey -Name "NoRepair" -Value 1 -Type DWord
}

if ($Silent) {
    Execute-Install -targetDir $InstallDir -targetGatewayUrl $GatewayUrl
    Write-Host "[OK] クライアントのインストールが完了しました: $InstallDir" -ForegroundColor Green
    exit 0
}

# GUI インストーラーウィンドウの表示
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object System.Windows.Forms.Form
$form.Text = "$AppName セットアップ"
$form.Size = New-Object System.Drawing.Size(540, 320)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$lblTitle = New-Object System.Windows.Forms.Label
$lblTitle.Text = "Strangler Fig クライアント (WinForms + WebView2) のインストール"
$lblTitle.Font = New-Object System.Drawing.Font("Yu Gothic UI", 11, [System.Drawing.FontStyle]::Bold)
$lblTitle.Location = New-Object System.Drawing.Point(20, 18)
$lblTitle.Size = New-Object System.Drawing.Size(490, 26)

$lblDir = New-Object System.Windows.Forms.Label
$lblDir.Text = "インストール先フォルダ:"
$lblDir.Location = New-Object System.Drawing.Point(20, 60)
$lblDir.AutoSize = $true

$txtDir = New-Object System.Windows.Forms.TextBox
$txtDir.Text = $InstallDir
$txtDir.Location = New-Object System.Drawing.Point(20, 84)
$txtDir.Size = New-Object System.Drawing.Size(480, 26)

$lblUrl = New-Object System.Windows.Forms.Label
$lblUrl.Text = "接続先 YarpGateway URL (client-settings.json に保存されます):"
$lblUrl.Location = New-Object System.Drawing.Point(20, 126)
$lblUrl.AutoSize = $true

$txtUrl = New-Object System.Windows.Forms.TextBox
$txtUrl.Text = $GatewayUrl
$txtUrl.Location = New-Object System.Drawing.Point(20, 150)
$txtUrl.Size = New-Object System.Drawing.Size(480, 26)

$chkLaunch = New-Object System.Windows.Forms.CheckBox
$chkLaunch.Text = "インストール完了後にクライアントを起動する"
$chkLaunch.Checked = $true
$chkLaunch.Location = New-Object System.Drawing.Point(20, 192)
$chkLaunch.AutoSize = $true

$btnInstall = New-Object System.Windows.Forms.Button
$btnInstall.Text = "インストール実行"
$btnInstall.Location = New-Object System.Drawing.Point(260, 230)
$btnInstall.Size = New-Object System.Drawing.Size(130, 34)
$btnInstall.BackColor = [System.Drawing.Color]::FromArgb(37, 99, 235)
$btnInstall.ForeColor = [System.Drawing.Color]::White
$btnInstall.FlatStyle = "Flat"

$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = "キャンセル"
$btnCancel.Location = New-Object System.Drawing.Point(400, 230)
$btnCancel.Size = New-Object System.Drawing.Size(100, 34)
$btnCancel.Add_Click({ $form.Close() })

$btnInstall.Add_Click({
    try {
        $btnInstall.Enabled = $false
        $btnInstall.Text = "インストール中..."
        $form.Refresh()

        Execute-Install -targetDir $txtDir.Text.Trim() -targetGatewayUrl $txtUrl.Text.Trim()

        [System.Windows.Forms.MessageBox]::Show(
            "インストールが完了しました。`nデスクトップおよびスタートメニューにショートカットを作成しました。",
            "セットアップ完了",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null

        if ($chkLaunch.Checked) {
            $exe = Join-Path $txtDir.Text.Trim() "WinFormsClient.exe"
            if (Test-Path $exe) {
                Start-Process -FilePath $exe -WorkingDirectory $txtDir.Text.Trim()
            }
        }
        $form.Close()
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show(
            "インストール中にエラーが発生しました:`n" + $_.Exception.Message,
            "エラー",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        $btnInstall.Enabled = $true
        $btnInstall.Text = "インストール実行"
    }
})

$form.Controls.AddRange(@($lblTitle, $lblDir, $txtDir, $lblUrl, $txtUrl, $chkLaunch, $btnInstall, $btnCancel))
[void]$form.ShowDialog()
