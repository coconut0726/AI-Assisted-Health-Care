@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Start-LocalVlm.ps1" %*
set "VLM_EXIT_CODE=%ERRORLEVEL%"
if not "%VLM_EXIT_CODE%"=="0" pause
exit /b %VLM_EXIT_CODE%
