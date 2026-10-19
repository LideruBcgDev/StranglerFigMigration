param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $false
)

$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $PSScriptRoot
$StagingRoot  = Join-Path $PSScriptRoot "Staging"
$OutputRoot   = Join-Path $PSScriptRoot "Output"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Building Strangler Fig Client & Server Installers" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (Test-Path $StagingRoot) { Remove-Item $StagingRoot -Recurse -Force }
if (Test-Path $OutputRoot)  { Remove-Item $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$scFlag = if ($SelfContained) { "true" } else { "false" }

# 1. Publish WinFormsClient (Client Installer Package)
Write-Host "`n[1/4] Publishing WinFormsClient..." -ForegroundColor Yellow
$clientStageDir = Join-Path $StagingRoot "ClientPackage"
$clientAppDir   = Join-Path $clientStageDir "App"
New-Item -ItemType Directory -Path $clientAppDir -Force | Out-Null

dotnet publish (Join-Path $SolutionRoot "WinFormsClient\WinFormsClient.vbproj") -c $Configuration -r $Runtime --self-contained $scFlag -o $clientAppDir

Copy-Item (Join-Path $PSScriptRoot "ClientInstaller\Install-Client.ps1")   -Destination $clientStageDir -Force
Copy-Item (Join-Path $PSScriptRoot "ClientInstaller\Uninstall-Client.ps1") -Destination $clientStageDir -Force

$clientCmdContent = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-Client.ps1`" %*`r`n"
Set-Content -Path (Join-Path $clientStageDir "Setup-Client.cmd") -Value $clientCmdContent -Encoding ASCII

$clientZipPath = Join-Path $OutputRoot "StranglerFig-Client-Setup.zip"
Compress-Archive -Path "$clientStageDir\*" -DestinationPath $clientZipPath -Force
Write-Host "  -> Created Client ZIP: $clientZipPath" -ForegroundColor Green

# 2. Publish Server Projects (YarpGateway + ModernBff + LegacyMvc)
Write-Host "`n[2/4] Publishing Server Projects (YarpGateway / ModernBff / LegacyMvc)..." -ForegroundColor Yellow
$serverStageDir = Join-Path $StagingRoot "ServerPackage"
$gwOutDir       = Join-Path $serverStageDir "YarpGateway"
$bffOutDir      = Join-Path $serverStageDir "ModernBff"
$legacyOutDir   = Join-Path $serverStageDir "LegacyMvc"

New-Item -ItemType Directory -Path $gwOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $bffOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $legacyOutDir -Force | Out-Null

dotnet publish (Join-Path $SolutionRoot "YarpGateway\YarpGateway.csproj") -c $Configuration -r $Runtime --self-contained $scFlag -o $gwOutDir
dotnet publish (Join-Path $SolutionRoot "ModernBff\ModernBff.csproj")     -c $Configuration -r $Runtime --self-contained $scFlag -o $bffOutDir
dotnet build   (Join-Path $SolutionRoot "LegacyMvc\LegacyMvc.vbproj")     -c $Configuration

$legacySrcDir = Join-Path $SolutionRoot "LegacyMvc"
New-Item -ItemType Directory -Path (Join-Path $legacyOutDir "bin") -Force | Out-Null
Copy-Item (Join-Path $legacySrcDir "bin\*")             -Destination (Join-Path $legacyOutDir "bin") -Recurse -Force
Copy-Item (Join-Path $legacySrcDir "Global.asax")        -Destination $legacyOutDir -Force
Copy-Item (Join-Path $legacySrcDir "LegacyPortal.aspx")  -Destination $legacyOutDir -Force
Copy-Item (Join-Path $legacySrcDir "Web.config")         -Destination $legacyOutDir -Force

Copy-Item (Join-Path $SolutionRoot "docker-compose.yml")                   -Destination $serverStageDir -Force
Copy-Item (Join-Path $PSScriptRoot "ServerInstaller\Install-Server.ps1")   -Destination $serverStageDir -Force
Copy-Item (Join-Path $PSScriptRoot "ServerInstaller\Uninstall-Server.ps1") -Destination $serverStageDir -Force

$serverCmdContent = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-Server.ps1`" %*`r`n"
Set-Content -Path (Join-Path $serverStageDir "Setup-Server.cmd") -Value $serverCmdContent -Encoding ASCII

$serverZipPath = Join-Path $OutputRoot "StranglerFig-Server-Setup.zip"
Compress-Archive -Path "$serverStageDir\*" -DestinationPath $serverZipPath -Force
Write-Host "  -> Created Server ZIP: $serverZipPath" -ForegroundColor Green

# 3. Compile Self-Extracting .exe Installers using built-in Windows csc.exe
Write-Host "`n[3/4] Compiling standalone .exe Self-Extracting Installers..." -ForegroundColor Yellow
$cscPath = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$launcherCs = Join-Path $PSScriptRoot "SfxLauncher.cs"

if (Test-Path $cscPath) {
    $clientExePath = Join-Path $OutputRoot "StranglerFig-Client-Setup.exe"
    & $cscPath /nologo /target:winexe /out:"$clientExePath" /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /resource:"$clientZipPath,payload.zip" "$launcherCs"
    Write-Host "  -> Created Client EXE Installer: $clientExePath" -ForegroundColor Green

    $serverExePath = Join-Path $OutputRoot "StranglerFig-Server-Setup.exe"
    & $cscPath /nologo /target:winexe /out:"$serverExePath" /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /resource:"$serverZipPath,payload.zip" "$launcherCs"
    Write-Host "  -> Created Server EXE Installer: $serverExePath" -ForegroundColor Green
} else {
    Write-Warning "csc.exe not found; skipped .exe wrapper generation."
}

Write-Host "`n[4/4] Installer build completed successfully!" -ForegroundColor Cyan
Get-ChildItem $OutputRoot | Select-Object Name, Length | Format-Table -AutoSize
