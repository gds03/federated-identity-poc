Write-Host "Stopping all Federated Identity services..." -ForegroundColor Yellow

$root = Split-Path -Parent $PSScriptRoot
$pidFile = "$root/scripts/.running-pids"
if (Test-Path $pidFile) { Remove-Item $pidFile -Force -ErrorAction SilentlyContinue }

# Find and stop processes bound to our ports: 5001, 5002, 5003, 5004, 5173
$ports = @(5001, 5002, 5003, 5004, 5173)
foreach ($port in $ports) {
    try {
        $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
        foreach ($conn in $conns) {
            if ($conn.OwningProcess -and $conn.OwningProcess -ne 0) {
                Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
            }
        }
    } catch {}
}

# Stop any lingering Federated dotnet processes
Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object { 
    $_.CommandLine -match "Federated\." 
} | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "All Federated Identity services have been stopped." -ForegroundColor Green
