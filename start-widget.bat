@echo off
if exist "%~dp0publish_bundle\MiniHardwareWidget.exe" (
    start "" "%~dp0publish_bundle\MiniHardwareWidget.exe"
) else (
    dotnet run --project "%~dp0MiniHardwareWidget.csproj"
)
