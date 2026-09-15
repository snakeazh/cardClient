@echo off
setlocal
set PORT=5254
set FOUND=0

echo Stopping Card.Server on port %PORT% ...

for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":%PORT%" ^| findstr LISTENING') do (
    echo Killing PID %%a
    taskkill /F /PID %%a >nul 2>&1
    set FOUND=1
)

taskkill /F /IM Card.Server.exe >nul 2>&1
if not errorlevel 1 set FOUND=1

if "%FOUND%"=="0" (
    echo No running Server found.
) else (
    echo Done.
)

pause
exit /b 0