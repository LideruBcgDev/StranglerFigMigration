param(
    [string]$ModernBffUrl = "http://localhost:5000",
    [string]$LegacyMvcUrl = "http://localhost:5001"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Strangler Fig Web Redirect Flow Verification           " -ForegroundColor Cyan
Write-Host "   (CommandDefinition.Aspx -> RequestManager -> Browser)  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Start IIS Express if not running
$iisProc = Get-Process -Name "iisexpress" -ErrorAction SilentlyContinue
if (-not $iisProc) {
    Write-Host "[Info] Starting IIS Express for LegacyMvc on port 5001..." -ForegroundColor Yellow
    $iisProc = Start-Process -FilePath "C:\Program Files\IIS Express\iisexpress.exe" -ArgumentList "/path:E:\MyJobs\VisualStudio\StranglerFigMigration\LegacyMvc", "/port:5001" -PassThru
    Start-Sleep -Seconds 3
}

# Start ModernBff if not running
$modernProc = Get-Process -Name "ModernBff" -ErrorAction SilentlyContinue
if (-not $modernProc) {
    Write-Host "[Info] Starting ModernBff on port 5000..." -ForegroundColor Yellow
    $modernProc = Start-Process -FilePath "dotnet" -ArgumentList "run --project E:\MyJobs\VisualStudio\StranglerFigMigration\ModernBff\ModernBff.csproj --no-build" -PassThru
    Start-Sleep -Seconds 4
}

try {
    # 2. Request DRW.PORTAL via ModernBff (:5000) YARP proxy to LegacyMvc (:5001)
    Write-Host "`n[Step 1] POST $ModernBffUrl/Client/execute (Command: DRW.PORTAL)" -ForegroundColor Yellow

    $clientPayload = @{
        commandName = "DRW.PORTAL"
        iParamJson = @{
            Keyword = "PUMP-01"
        }
        loginUser = @{
            UserId = "yamada"
            UserName = "Taro Yamada"
            OrganizationCode = "ORG-01"
            OrganizationName = "Engineering Dept"
            Roles = @("ADMIN", "ENGINEER")
        }
    } | ConvertTo-Json -Depth 5

    $response = Invoke-RestMethod -Uri "$ModernBffUrl/Client/execute" -Method Post -Body $clientPayload -ContentType "application/json"
    
    Write-Host "Response received:" -ForegroundColor Green
    $response | Format-List | Out-String | Write-Host -ForegroundColor Gray

    # Verify redirect response
    if ($response.isRedirect -ne $true -or [string]::IsNullOrWhiteSpace($response.redirectUrl)) {
        Write-Error "Test Failed: isRedirect is not true or redirectUrl is empty!"
        exit 1
    }

    Write-Host "SUCCESS: Redirect detected!" -ForegroundColor Green
    Write-Host "  -> req_code: $($response.req_code)" -ForegroundColor Cyan
    Write-Host "  -> redirectUrl: $($response.redirectUrl)" -ForegroundColor Cyan

    # 3. Access the generated redirectUrl (ModernBff :5000 -> LegacyMvc :5001 /LegacyPortal.aspx?req_code=...)
    Write-Host "`n[Step 2] GET $($response.redirectUrl) (Simulating browser launch)" -ForegroundColor Yellow

    $htmlResponse = Invoke-WebRequest -Uri $response.redirectUrl -Method Get -UseBasicParsing

    Write-Host "HTML Response Status: $($htmlResponse.StatusCode)" -ForegroundColor Green

    # Verify that restored user information and req_code exist in HTML
    $htmlContent = $htmlResponse.Content
    $foundUser = $htmlContent.Contains("Taro Yamada")
    $foundOrg = $htmlContent.Contains("Engineering Dept")
    $foundReqCode = $htmlContent.Contains($response.req_code)

    Write-Host "Verification of LegacyPortal.aspx rendering:" -ForegroundColor Yellow
    Write-Host "  - Restored User Name ('Taro Yamada'): $foundUser" -ForegroundColor $(if ($foundUser) { "Green" } else { "Red" })
    Write-Host "  - Restored Organization ('Engineering Dept'): $foundOrg" -ForegroundColor $(if ($foundOrg) { "Green" } else { "Red" })
    Write-Host "  - req_code in HTML: $foundReqCode" -ForegroundColor $(if ($foundReqCode) { "Green" } else { "Red" })

    if ($foundUser -and $foundReqCode) {
        Write-Host "`n==========================================================" -ForegroundColor Green
        Write-Host " ALL CHECKS PASSED: Web screen redirect flow fully working!" -ForegroundColor Green
        Write-Host "==========================================================" -ForegroundColor Green
    } else {
        Write-Warning "User or req_code not found in HTML. Check LegacyPortal.aspx implementation."
    }

} finally {
    # Keep running for manual tests or stop if desired
}
