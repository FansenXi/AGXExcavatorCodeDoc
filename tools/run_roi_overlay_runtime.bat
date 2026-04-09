@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0run_roi_overlay_runtime.ps1" %*
exit /b %ERRORLEVEL%
