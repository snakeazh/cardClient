@echo off
setlocal
cd /d "%~dp0"

echo Creating Postgres user/database card ...
where psql >nul 2>&1
if errorlevel 1 (
    echo [ERROR] psql not found. Install PostgreSQL and add bin to PATH.
    echo Then run: psql -U postgres -f setup-postgres.sql
    pause
    exit /b 1
)

psql -U postgres -d postgres -v ON_ERROR_STOP=0 -f setup-postgres.sql
echo.
echo If user/database already exist, the errors above can be ignored.
echo Redis: install as a Windows service and listen on 127.0.0.1:6379
echo Then double-click start-server.bat
pause
exit /b 0