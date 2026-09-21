@echo off
setlocal
cd /d "%~dp0"
set PORT=5254
set Persistence__Provider=Postgres
set ConnectionStrings__Redis=127.0.0.1:6379
set GuestAuth__Enabled=true

echo Starting Card.Server with Postgres + Redis on port %PORT% ...
echo URL: http://0.0.0.0:%PORT% (LAN accessible)
echo Close this window or Ctrl+C to stop.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet not found. Install .NET SDK first.
    pause
    exit /b 1
)

netstat -ano | findstr ":%PORT%" | findstr LISTENING >nul 2>&1
if not errorlevel 1 (
    echo Port %PORT% is already in use.
    echo Run stop-server.bat first.
    pause
    exit /b 1
)

dotnet run --project src/Card.Server --launch-profile postgres
echo.
echo Server exited.
pause
exit /b %ERRORLEVEL%