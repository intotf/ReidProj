@echo off
chcp 65001 >nul
echo === ReIdFaceBox 单文件发布 ===
dotnet publish -c Release -r win-x64 --self-contained true -o publish /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
echo.
echo 发布完成: publish\ReIdFaceBox.exe
pause
