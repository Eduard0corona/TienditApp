@echo off
title FoodPark Digital - Dev

echo.
echo ========================================
echo   FoodPark Digital - Development Mode
echo ========================================
echo.
echo Starting API backend + React frontend...
echo.
echo   API:      http://localhost:5203
echo   Frontend: http://localhost:5173
echo   Login:    admin@foodparkdigital.com (password: Seed:SuperAdminPassword en user-secrets)
echo.

start "FoodPark API" cmd /k "cd /d %~dp0 && dotnet run --project src\FoodParkDigital.WebApi"

timeout /t 3 /nobreak >nul

start "FoodPark Web" cmd /k "cd /d %~dp0\client && npm run dev"

echo Both servers started in separate windows.
echo Close those windows to stop the servers.
pause
