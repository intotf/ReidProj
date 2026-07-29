@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set PROJECT_DIR=%~dp0
set PUBLISH_DIR=%PROJECT_DIR%publish

echo === 发布 MediaDownloader（单文件）===
echo 项目路径: %PROJECT_DIR%
echo 发布目录: %PUBLISH_DIR%
echo.

dotnet publish "%PROJECT_DIR%MediaDownloader.csproj" ^
  -c Release ^
  -o "%PUBLISH_DIR%" ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none

if %ERRORLEVEL% equ 0 (
    echo.
    echo ✓ 发布成功
    dir /b "%PUBLISH_DIR%\MediaDownloader.exe" 2>nul
) else (
    echo.
    echo ✗ 发布失败
)

echo.
pause
endlocal
