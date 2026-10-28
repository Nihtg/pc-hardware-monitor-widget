@echo off
if exist "%~dp0publish_bundle\MiniHardwareWidget.exe" (
    powershell -Command "Start-Process '%~dp0publish_bundle\MiniHardwareWidget.exe' -WorkingDirectory '%~dp0publish_bundle' -Verb RunAs"
) else (
    powershell -Command "Start-Process 'dotnet' -ArgumentList 'run --project \"%~dp0MiniHardwareWidget.csproj\"' -WorkingDirectory '%~dp0' -Verb RunAs"
)
