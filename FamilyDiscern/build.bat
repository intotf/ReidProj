@echo off
chcp 65001 >nul
setlocal
pushd "%~dp0"

echo === FamilyDiscern Windows x64 自包含单文件发布 ===

dotnet publish "FamilyDiscern.csproj" -c Release -r win-x64 --self-contained true -o "publish" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
set "PUBLISH_EXIT_CODE=%ERRORLEVEL%"

if not "%PUBLISH_EXIT_CODE%"=="0" (
    echo.
    echo 发布失败，请检查上方错误信息。
    popd
    endlocal
    exit /b %PUBLISH_EXIT_CODE%
)

if not exist "publish\FamilyDiscern.exe" (
    echo.
    echo 发布失败：未生成 publish\FamilyDiscern.exe。
    popd
    endlocal
    exit /b 1
)

if not exist "publish\appsettings.json" (
    echo.
    echo 发布失败：未复制 publish\appsettings.json。
    popd
    endlocal
    exit /b 1
)

echo.
echo 发布完成: %~dp0publish\FamilyDiscern.exe
echo 配置文件: %~dp0publish\appsettings.json

popd
endlocal
exit /b 0
