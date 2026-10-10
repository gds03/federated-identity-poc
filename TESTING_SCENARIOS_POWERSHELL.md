# Testing Federated Identity with PowerShell: Complete Scenario Guide

This research and testing guide provides ready-to-run PowerShell scripts to test every authentication and authorization scenario in the system. It covers token acquisition, valid/invalid scopes, missing audiences, correlation tracing, and instant revocation under both **Decentralized JWT** and **CentralCheck** modes.

---

## Prerequisites & Service Ports

Ensure all services are running:
```powershell
./scripts/run-all.ps1
```

- **AuthService**: `http://localhost:5001`
- **OrdersApi**: `http://localhost:5002` (Configured with `AuthMode: CentralCheck`)
- **CustomersApi**: `http://localhost:5003` (Configured with `AuthMode: Jwt`)
- **InventoryApi**: `http://localhost:5004` (Configured with `AuthMode: Jwt`)
- **Admin UI**: `http://localhost:5173`

### Pre-Seeded Test Clients & Secrets

| Client ID | Raw Secret | Grants Configured |
|---|---|---|
| `api-a` | `secret-api-a-123` | `orders.read`, `inventory.read` |
| `api-b` | `secret-api-b-456` | `orders.read`, `orders.write`, `customers.read` |
| `api-no-access`| `secret-none-789` | *None* |

---

## Scenario 1: Acquire an OAuth 2.0 Access Token

Calls `AuthService` using the standard **Client Credentials Grant** (RFC 6749 §4.4).

```powershell
# 1. Request token for api-a
$tokenResponse = Invoke-RestMethod -Method Post -Uri "http://localhost:5001/oauth/token" `
  -Headers @{ "X-Correlation-Id" = "corr-test-01" } `
  -ContentType "application/json" `
  -Body (@{
    grant_type    = "client_credentials"
    client_id     = "api-a"
    client_secret = "secret-api-a-123"
  } | ConvertTo-Json)

$tokenA = $tokenResponse.access_token

Write-Host "✅ Token Received!" -ForegroundColor Green
Write-Host "• Token Type: $($tokenResponse.token_type)"
Write-Host "• Expires In: $($tokenResponse.expires_in)s"
Write-Host "• Granted Scopes: $($tokenResponse.scope)"
```

### Inspect the JWT Claims:
To decode and view the claims inside `$tokenA` without external tools:
```powershell
$payloadBase64 = $tokenA.Split('.')[1]
$payloadBase64 = $payloadBase64.PadRight($payloadBase64.Length + (4 - $payloadBase64.Length % 4) % 4, '=')
$claimsJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($payloadBase64))
$claimsJson | ConvertFrom-Json | Format-List
```
Notice:
- `iss`: `http://localhost:5001`
- `aud`: `["https://orders.internal", "https://inventory.internal"]` *(CustomersApi is absent because api-a has no grants on it!)*
- `scope`: `orders.read inventory.read`

---

## Scenario 2: Allowed Call (`200 OK`)

`api-a` calls `OrdersApi` (`GET /orders`). Because `api-a` has the `orders.read` grant, the call succeeds.

```powershell
$response = Invoke-RestMethod -Method Get -Uri "http://localhost:5002/orders" `
  -Headers @{ 
    "Authorization"    = "Bearer $tokenA"
    "X-Correlation-Id" = "corr-allowed-read"
  }

Write-Host "✅ Status: 200 OK" -ForegroundColor Green
$response | Format-Table -AutoSize
```

---

## Scenario 3: Missing Token / Unauthenticated (`401 Unauthorized`)

Calling a protected route without an `Authorization` header.

```powershell
try {
  Invoke-RestMethod -Method Get -Uri "http://localhost:5002/orders" `
    -Headers @{ "X-Correlation-Id" = "corr-no-token" }
} catch {
  $statusCode = [int]$_.Exception.Response.StatusCode
  Write-Host "❌ Status: $statusCode (Expected 401 Unauthorized)" -ForegroundColor Yellow
  $stream = $_.Exception.Response.GetResponseStream()
  $reader = New-Object System.IO.StreamReader($stream)
  Write-Host "Response Body: $($reader.ReadToEnd())" -ForegroundColor DarkGray
}
```

---

## Scenario 4: Insufficient Scope (`403 Forbidden`)

`api-a` attempts to call `POST /orders`, which requires `orders.write`.
`api-a` is authenticated, but **lacks the write scope**.

```powershell
$orderPayload = @{
  customerName = "ACME Corp"
  amount       = 450.00
} | ConvertTo-Json

try {
  Invoke-RestMethod -Method Post -Uri "http://localhost:5002/orders" `
    -Headers @{ 
      "Authorization"    = "Bearer $tokenA"
      "X-Correlation-Id" = "corr-scope-denied"
    } `
    -ContentType "application/json" `
    -Body $orderPayload
} catch {
  $statusCode = [int]$_.Exception.Response.StatusCode
  Write-Host "🚫 Status: $statusCode (Expected 403 Forbidden)" -ForegroundColor Red
  $stream = $_.Exception.Response.GetResponseStream()
  $reader = New-Object System.IO.StreamReader($stream)
  Write-Host "Response Body: $($reader.ReadToEnd())" -ForegroundColor DarkGray
}
```

---

## Scenario 5: Missing Audience (`401 Unauthorized`)

`api-a` attempts to call `CustomersApi` (`GET /customers`).
`CustomersApi` expects audience `https://customers.internal`. Because `api-a` has no grants on `CustomersApi`, `AuthService` omitted that audience from `tokenA`.

```powershell
try {
  Invoke-RestMethod -Method Get -Uri "http://localhost:5003/customers" `
    -Headers @{ 
      "Authorization"    = "Bearer $tokenA"
      "X-Correlation-Id" = "corr-aud-missing"
    }
} catch {
  $statusCode = [int]$_.Exception.Response.StatusCode
  Write-Host "🔒 Status: $statusCode (Expected 401 Unauthorized - Audience Mismatch)" -ForegroundColor Magenta
  Write-Host "Reason: Token audience does not contain https://customers.internal"
}
```

Now test with `api-b` (which **does** have `customers.read`):
```powershell
$tokenB = (Invoke-RestMethod -Method Post -Uri "http://localhost:5001/oauth/token" `
  -ContentType "application/json" `
  -Body '{"grant_type":"client_credentials","client_id":"api-b","client_secret":"secret-api-b-456"}').access_token

$customers = Invoke-RestMethod -Method Get -Uri "http://localhost:5003/customers" `
  -Headers @{ "Authorization" = "Bearer $tokenB" }

Write-Host "✅ api-b call to CustomersApi succeeded:" -ForegroundColor Green
$customers | Format-Table -AutoSize
```

---

## Scenario 6: Read vs. Write Protection on InventoryApi

`api-a` has `inventory.read` but lacks `inventory.write`.

### Step 6A: Read inventory (Allowed: 200 OK)
```powershell
$items = Invoke-RestMethod -Method Get -Uri "http://localhost:5004/inventory" `
  -Headers @{ "Authorization" = "Bearer $tokenA" }

Write-Host "✅ Read Inventory (200 OK):" -ForegroundColor Green
$items | Format-Table -AutoSize
```

### Step 6B: Update stock level (Denied: 403 Forbidden)
```powershell
try {
  Invoke-RestMethod -Method Put -Uri "http://localhost:5004/inventory/1" `
    -Headers @{ 
      "Authorization"    = "Bearer $tokenA"
      "X-Correlation-Id" = "corr-inventory-update"
    } `
    -ContentType "application/json" `
    -Body '{"stockLevel": 999}'
} catch {
  $statusCode = [int]$_.Exception.Response.StatusCode
  Write-Host "🚫 Update Stock Denied: $statusCode (Expected 403 Forbidden)" -ForegroundColor Red
}
```

---

## Scenario 7: Instant Revocation with CentralCheck Mode

`OrdersApi` is configured with `AuthMode: CentralCheck`. Every request triggers a live policy lookup in `AuthService`.

### Step 7A: Verify call works initially (200 OK)
```powershell
$resp = Invoke-RestMethod -Method Get -Uri "http://localhost:5002/orders" `
  -Headers @{ "Authorization" = "Bearer $tokenA" }
Write-Host "✅ Initial call allowed: $($resp.Count) orders returned." -ForegroundColor Green
```

### Step 7B: Revoke `orders.read` from `api-a`
You can do this either in the **Admin UI** (`http://localhost:5173` → Access Matrix) OR directly via PowerShell calling the Admin API:

```powershell
# Find permission ID for orders.read
$matrix = Invoke-RestMethod -Method Get -Uri "http://localhost:5001/api/admin/matrix"
$permId = ($matrix.permissions | Where-Object { $_.scope -eq "orders.read" }).id

# Toggle grant OFF for api-a
$revokeResult = Invoke-RestMethod -Method Post -Uri "http://localhost:5001/api/admin/matrix/toggle" `
  -ContentType "application/json" `
  -Body (@{
    clientId     = "api-a"
    permissionId = $permId
    enable       = $false
    actor        = "powershell.tester@internal"
  } | ConvertTo-Json)

Write-Host "⚡ Grant Revoked: $($revokeResult.diff)" -ForegroundColor Yellow
```

### Step 7C: Re-run with the SAME token (Immediately 403 Forbidden!)
```powershell
try {
  Invoke-RestMethod -Method Get -Uri "http://localhost:5002/orders" `
    -Headers @{ "Authorization" = "Bearer $tokenA" }
} catch {
  $statusCode = [int]$_.Exception.Response.StatusCode
  Write-Host "🛑 Status: $statusCode (Expected 403 Forbidden)" -ForegroundColor Red
  $stream = $_.Exception.Response.GetResponseStream()
  $reader = New-Object System.IO.StreamReader($stream)
  Write-Host "Instant Revocation Proof: $($reader.ReadToEnd())" -ForegroundColor Yellow
}
```

### Step 7D: Re-grant access
```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5001/api/admin/matrix/toggle" `
  -ContentType "application/json" `
  -Body (@{
    clientId     = "api-a"
    permissionId = $permId
    enable       = $true
    actor        = "powershell.tester@internal"
  } | ConvertTo-Json) | Out-Null

$respAgain = Invoke-RestMethod -Method Get -Uri "http://localhost:5002/orders" `
  -Headers @{ "Authorization" = "Bearer $tokenA" }

Write-Host "✅ Access Restored: 200 OK without re-issuing token!" -ForegroundColor Green
```

---

## Scenario 8: Trace Requests with `X-Correlation-Id`

Every request echoes its correlation ID in response headers:

```powershell
$corrId = "corr-my-trace-9999"

$webReq = [System.Net.WebRequest]::Create("http://localhost:5002/orders")
$webReq.Headers.Add("Authorization", "Bearer $tokenA")
$webReq.Headers.Add("X-Correlation-Id", $corrId)
$webResp = $webReq.GetResponse()

$returnedCorr = $webResp.Headers["X-Correlation-Id"]
Write-Host "Sent Correlation ID:     $corrId" -ForegroundColor Cyan
Write-Host "Returned Correlation ID: $returnedCorr" -ForegroundColor Green

if ($corrId -eq $returnedCorr) {
  Write-Host "✅ End-to-end trace correlation verified!" -ForegroundColor Green
}
$webResp.Close()
```

If you look at the `OrdersApi (5002)` and `AuthService (5001)` console windows, both will display logs containing:
`[CorrelationId: corr-my-trace-9999]`
