param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$UserId = "yamada",
    [string]$Password = "pass123"
)

Write-Host "=== Strangler Fig Decoupled Mode Verification ===" -ForegroundColor Cyan
Write-Host "ModernBff: $BaseUrl"
Write-Host "User: $UserId"

# 1. OAuth2 Password Grant (Pattern A)
Write-Host "`n[1/4] POST /oauth/token (Pattern A: USR_TBL)" -ForegroundColor Yellow
$tokenPayload = @{
    grant_type = "password"
    user_id    = $UserId
    password   = $Password
}

$tokenJson = $tokenPayload | ConvertTo-Json
$tokenRes = Invoke-RestMethod -Uri "$BaseUrl/oauth/token" -Method Post -Body $tokenJson -ContentType "application/json"
Write-Host "Token obtained: $($tokenRes.access_token.Substring(0, 30))..." -ForegroundColor Green
Write-Host "Auth Source: $($tokenRes.auth_source)" -ForegroundColor Green
Write-Host "User: $($tokenRes.user_name) ($($tokenRes.user_id)), Roles: $($tokenRes.roles -join ', ')" -ForegroundColor Green

$headers = @{
    Authorization = "Bearer $($tokenRes.access_token)"
}

# 2. REST API /api/status
Write-Host "`n[2/4] GET /api/status" -ForegroundColor Yellow
$statusRes = Invoke-RestMethod -Uri "$BaseUrl/api/status" -Method Get -Headers $headers
Write-Host "API Status OK: $($statusRes.ApiCategory)" -ForegroundColor Green
Write-Host "Token Source: $($statusRes.TokenLookupSource)" -ForegroundColor Gray

# 3. REST API /api/items
Write-Host "`n[3/4] GET /api/items" -ForegroundColor Yellow
$itemsRes = Invoke-RestMethod -Uri "$BaseUrl/api/items" -Method Get -Headers $headers
Write-Host "Items Count: $($itemsRes.Items.Count)" -ForegroundColor Green
foreach ($item in $itemsRes.Items) {
    Write-Host "  - [$($item.itmCd)] $($item.itmNm)" -ForegroundColor Gray
}

# 4. MCP Streamable JSON-RPC
Write-Host "`n[4/4] POST /api/bff/mcp/streamable (JSON-RPC)" -ForegroundColor Yellow
$mcpBody = @'
{
  "jsonrpc": "2.0",
  "id": "pwsh-1",
  "method": "tools/call",
  "params": {
    "name": "get_projects_and_workflows",
    "arguments": {
      "keyword": ""
    }
  }
}
'@

$mcpRes = Invoke-RestMethod -Uri "$BaseUrl/api/bff/mcp/streamable" -Method Post -Headers $headers -Body $mcpBody -ContentType "application/json"
$mcpData = $mcpRes.result.structuredData
Write-Host "MCP Tool OK: $($mcpData.Tool)" -ForegroundColor Green
Write-Host "Current LoginUser in MCP: $($mcpData.CurrentLoginUser.userName) ($($mcpData.CurrentLoginUser.userId))" -ForegroundColor Green
Write-Host "Lookup Source: $($mcpData.LoginUserLookupSource)" -ForegroundColor Green
Write-Host "Projects Count: $($mcpData.Data.Count)" -ForegroundColor Green
foreach ($p in $mcpData.Data) {
    Write-Host "  - [$($p.Project.PrjId)] $($p.Project.PrjNm)" -ForegroundColor Gray
}

Write-Host "`n=== All tests completed successfully! ===" -ForegroundColor Cyan
