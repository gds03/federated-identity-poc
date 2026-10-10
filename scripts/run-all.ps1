Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "   STARTING ALL FEDERATED IDENTITY SERVICES & ADMIN UI            " -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

$root = Split-Path -Parent $PSScriptRoot

# 1. Build Solution
Write-Host "`n[1/5] Building .NET Solution..." -ForegroundColor Yellow
dotnet build "$root/Federated.slnx"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Please resolve errors." -ForegroundColor Red
    exit 1
}

# Ensure logs directory exists
$logDir = "$root/logs"
if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

# 2. Start AuthService
Write-Host "`n[2/5] Starting AuthService on http://localhost:5001..." -ForegroundColor Yellow
$authDir = "$root/src/AuthService/Federated.AuthService"
Start-Process cmd.exe -ArgumentList "/c title AuthService (5001) && dotnet run --no-build --urls `"http://localhost:5001`"" -WorkingDirectory $authDir

# 3. Start Test APIs
Write-Host "`n[3/5] Starting Test APIs (Orders: 5002, Customers: 5003, Inventory: 5004)..." -ForegroundColor Yellow
$ordersDir = "$root/src/TestApis/Federated.OrdersApi"
$customersDir = "$root/src/TestApis/Federated.CustomersApi"
$inventoryDir = "$root/src/TestApis/Federated.InventoryApi"

Start-Process cmd.exe -ArgumentList "/c title OrdersApi (5002) && dotnet run --no-build --urls `"http://localhost:5002`"" -WorkingDirectory $ordersDir
Start-Process cmd.exe -ArgumentList "/c title CustomersApi (5003) && dotnet run --no-build --urls `"http://localhost:5003`"" -WorkingDirectory $customersDir
Start-Process cmd.exe -ArgumentList "/c title InventoryApi (5004) && dotnet run --no-build --urls `"http://localhost:5004`"" -WorkingDirectory $inventoryDir

# 4. Start AdminUI
Write-Host "`n[4/5] Starting Admin UI on http://localhost:5173..." -ForegroundColor Yellow
$uiDir = "$root/src/AdminUI"
Start-Process cmd.exe -ArgumentList "/c title AdminUI (5173) && npm.cmd run dev" -WorkingDirectory $uiDir

Start-Sleep -Seconds 3

Write-Host "`n==================================================================" -ForegroundColor Green
Write-Host "   ALL SERVICES ARE LIVE AND LISTENING!                           " -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Green
Write-Host "👉 Admin UI:      http://localhost:5173" -ForegroundColor White
Write-Host "👉 AuthService:   http://localhost:5001" -ForegroundColor White
Write-Host "👉 OrdersApi:     http://localhost:5002" -ForegroundColor White
Write-Host "👉 CustomersApi:  http://localhost:5003" -ForegroundColor White
Write-Host "👉 InventoryApi:  http://localhost:5004" -ForegroundColor White
Write-Host "`nTo run the API A simulation client anytime, execute:" -ForegroundColor Cyan
Write-Host "   dotnet run --project src/TestClient/Federated.TestClient/Federated.TestClient.csproj" -ForegroundColor White
Write-Host "`nTo stop all services, run: ./scripts/stop-all.ps1`n" -ForegroundColor DarkGray
