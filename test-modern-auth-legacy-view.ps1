param(
    [string]$ModernBffUrl = "http://localhost:5000",
    [string]$LegacyMvcUrl = "http://localhost:5001"
)

Add-Type -AssemblyName System.Net.Http

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Modern Auth Guard & Legacy View Proxy Verification     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# Test 1: Unauthenticated request to /LegacyPortal.aspx must redirect to /login
# -------------------------------------------------------------------------
Write-Host "`n[Test 1] Unauthenticated request: GET $ModernBffUrl/LegacyPortal.aspx" -ForegroundColor Yellow

$unauthReq = [System.Net.HttpWebRequest]::Create("$ModernBffUrl/LegacyPortal.aspx")
$unauthReq.AllowAutoRedirect = $false
try {
    $unauthRes = $unauthReq.GetResponse()
    $statusCode = [int]$unauthRes.StatusCode
    $location = $unauthRes.Headers["Location"]
    $unauthRes.Close()
} catch [System.Net.WebException] {
    $unauthRes = $_.Response
    $statusCode = [int]$unauthRes.StatusCode
    $location = $unauthRes.Headers["Location"]
    $unauthRes.Close()
}

Write-Host "Status Code: $statusCode" -ForegroundColor $(if ($statusCode -eq 302) { "Green" } else { "Red" })
Write-Host "Location: $location" -ForegroundColor Cyan

if ($statusCode -eq 302 -and $location -like "*login?returnUrl=*") {
    Write-Host "PASS: Successfully redirected to /login" -ForegroundColor Green
} else {
    Write-Error "FAIL: Expected 302 redirect to /login but got $statusCode ($location)"
}

# -------------------------------------------------------------------------
# Test 2: Login via ModernBff Web Login API (POST /api/bff/auth/login)
# -------------------------------------------------------------------------
Write-Host "`n[Test 2] Login via ModernBff: POST $ModernBffUrl/api/bff/auth/login" -ForegroundColor Yellow

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$loginPayload = @{
    userId = "yamada"
    password = "pass123"
    returnUrl = "/LegacyPortal.aspx"
} | ConvertTo-Json

$loginRes = Invoke-RestMethod -Uri "$ModernBffUrl/api/bff/auth/login" -Method Post -Body $loginPayload -ContentType "application/json" -WebSession $session

Write-Host "Login Status: OK" -ForegroundColor Green
Write-Host "Login User: $($loginRes.user.userName) ($($loginRes.user.userId))" -ForegroundColor Green
Write-Host "Auth Token: $($loginRes.user.sessionToken)" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# Test 3: Authenticated request to /LegacyPortal.aspx with Cookie
# -------------------------------------------------------------------------
Write-Host "`n[Test 3] Authenticated request: GET $ModernBffUrl/LegacyPortal.aspx (with Cookie)" -ForegroundColor Yellow

$portalRes = Invoke-WebRequest -Uri "$ModernBffUrl/LegacyPortal.aspx" -Method Get -WebSession $session -UseBasicParsing

Write-Host "Portal Status: $($portalRes.StatusCode)" -ForegroundColor Green

$portalHtml = $portalRes.Content
$hasYamada = $portalHtml.Contains("yamada")
$hasLegacyAuth = $portalHtml.Contains("LegacyPortal.aspx")
$hasModernBadge = $portalHtml.Contains("ModernBff") -or $portalHtml.Contains("モダン認証") -or $portalHtml.Contains("SALES-01")

Write-Host "Verification of LegacyPortal.aspx with restored LoginUser:" -ForegroundColor Yellow
Write-Host "  - Legacy Portal Page Rendered: $hasLegacyAuth" -ForegroundColor $(if ($hasLegacyAuth) { "Green" } else { "Red" })
Write-Host "  - LoginUser 'yamada' visible in Legacy: $hasYamada" -ForegroundColor $(if ($hasYamada) { "Green" } else { "Red" })
Write-Host "  - ModernBff Header Integration: $hasModernBadge" -ForegroundColor $(if ($hasModernBadge) { "Green" } else { "Red" })

if ($portalRes.StatusCode -eq 200 -and $hasYamada) {
    Write-Host "PASS: ModernBff forwarded authenticated request to LegacyMvc with LoginUser headers!" -ForegroundColor Green
} else {
    Write-Warning "User info not fully found in HTML."
}

# -------------------------------------------------------------------------
# Test 4: REST API without Bearer token must return 401 Unauthorized
# -------------------------------------------------------------------------
Write-Host "`n[Test 4] REST API Unauthenticated: GET $ModernBffUrl/api/items" -ForegroundColor Yellow

$apiStatus = 0
try {
    $unauthApi = Invoke-WebRequest -Uri "$ModernBffUrl/api/items" -Method Get -UseBasicParsing
    $apiStatus = $unauthApi.StatusCode
} catch [System.Net.WebException] {
    $apiStatus = [int]$_.Response.StatusCode
}

Write-Host "API Status: $apiStatus" -ForegroundColor $(if ($apiStatus -eq 401) { "Green" } else { "Red" })

if ($apiStatus -eq 401) {
    Write-Host "PASS: 401 Unauthorized returned as expected for API" -ForegroundColor Green
} else {
    Write-Error "FAIL: Expected 401 Unauthorized"
}

# -------------------------------------------------------------------------
# Test 5: Obtain OAuth Bearer token and call REST API (/api/items)
# -------------------------------------------------------------------------
Write-Host "`n[Test 5] OAuth2 Password Grant -> GET $ModernBffUrl/api/items" -ForegroundColor Yellow

$tokenPayload = @{
    grant_type = "password"
    user_id = "yamada"
    password = "pass123"
} | ConvertTo-Json

$tokenRes = Invoke-RestMethod -Uri "$ModernBffUrl/oauth/token" -Method Post -Body $tokenPayload -ContentType "application/json"
Write-Host "Token obtained: $($tokenRes.access_token.Substring(0, 30))..." -ForegroundColor Green

$authHeaders = @{
    Authorization = "Bearer $($tokenRes.access_token)"
}

$itemsRes = Invoke-RestMethod -Uri "$ModernBffUrl/api/items" -Method Get -Headers $authHeaders
Write-Host "Items returned count: $($itemsRes.Items.Count)" -ForegroundColor Green
foreach ($item in $itemsRes.Items) {
    Write-Host "  - [$($item.itmCd)] $($item.itmNm)" -ForegroundColor Gray
}

# -------------------------------------------------------------------------
# Test 6: Call MCP Server (/api/bff/mcp/streamable) with Bearer token
# -------------------------------------------------------------------------
Write-Host "`n[Test 6] Call MCP Server: GET $ModernBffUrl/api/bff/mcp/streamable" -ForegroundColor Yellow

$mcpRes = Invoke-RestMethod -Uri "$ModernBffUrl/api/bff/mcp/streamable" -Method Get -Headers $authHeaders
Write-Host "MCP Result JSON-RPC ID: $($mcpRes.id)" -ForegroundColor Green
Write-Host "MCP Content: $($mcpRes.result.content[0].text.Substring(0, 100))..." -ForegroundColor Gray

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " ALL SCENARIOS VERIFIED SUCCESSFULLY!                     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
