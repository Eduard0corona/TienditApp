# FoodPark Digital - Development Startup Script
# Runs API backend + React frontend in parallel

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  FoodPark Digital - Development Mode"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Start API backend
Write-Host "[API] Starting .NET backend on http://localhost:5203 ..." -ForegroundColor Yellow
$apiJob = Start-Process -PassThru -NoNewWindow -FilePath "dotnet" -ArgumentList "run", "--project", "$Root\src\FoodParkDigital.WebApi" -WorkingDirectory $Root

Start-Sleep -Seconds 3

# 2. Start React frontend
Write-Host "[WEB] Starting React frontend on http://localhost:5173 ..." -ForegroundColor Green
$webJob = Start-Process -PassThru -NoNewWindow -FilePath "npm" -ArgumentList "run", "dev" -WorkingDirectory "$Root\client"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  API:      http://localhost:5203"
Write-Host "  Frontend: http://localhost:5173"
Write-Host "  Login:    admin@foodparkdigital.com / Admin123!"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press Ctrl+C to stop both servers..." -ForegroundColor DarkGray
Write-Host ""

try {
    # Wait for either process to exit
    while (!$apiJob.HasExited -and !$webJob.HasExited) {
        Start-Sleep -Seconds 1
    }
} finally {
    # Cleanup: kill both processes
    Write-Host ""
    Write-Host "Shutting down..." -ForegroundColor Red
    if (!$apiJob.HasExited) { Stop-Process -Id $apiJob.Id -Force -ErrorAction SilentlyContinue }
    if (!$webJob.HasExited) { Stop-Process -Id $webJob.Id -Force -ErrorAction SilentlyContinue }
    Write-Host "Done." -ForegroundColor Green
}
