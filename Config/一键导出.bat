@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0.."

set "REPO_ROOT=%CD%"
set "CONFIG_DIR=%REPO_ROOT%\Config"

echo ==============================
echo  配置一键导出 (xlsx -^> csv/json/cs/Unity)
echo ==============================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [错误] 未找到 dotnet，请先安装 .NET SDK。
    echo.
    pause
    exit /b 1
)

dotnet run --project "%REPO_ROOT%\Tools\ConfigExporter\ConfigExporter.csproj" -- --config "%CONFIG_DIR%"
set EXIT_CODE=%ERRORLEVEL%

echo.
if %EXIT_CODE% neq 0 (
    echo [失败] 导出未完成，错误码: %EXIT_CODE%
) else (
    echo [成功] CSV   -^> TempConfig\csv
    echo [成功] JSON  -^> TempConfig\json
    echo [成功] C#    -^> Card\Assets\App\Config\Generated
    echo [成功] Unity -^> Card\Assets\Res\Config
)

echo.
pause
exit /b %EXIT_CODE%
