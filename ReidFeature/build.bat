@echo off
chcp 65001 >nul
echo === ReidFeature AOT 发布 ===
dotnet publish -c Release -r win-x64 --self-contained true -o publish /p:PublishAot=true /p:StripSymbols=true /p:OptimizationPreference=Size /p:IlcTrimMetadata=true
echo.
echo 发布完成: publish\ReidFeature.exe
pause
