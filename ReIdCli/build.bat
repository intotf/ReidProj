@echo off
echo === ReIdCli AOT 发布 ===
dotnet publish -c Release -r win-x64 --self-contained true -o publish /p:PublishAot=true /p:StripSymbols=true /p:OptimizationPreference=Size /p:IlcTrimMetadata=true
echo.
echo 发布完成: publish\ReIdCli.exe
pause
