@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title PetraERP Preview Stopper

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Start-LocalPreview.ps1" -Stop %*
if errorlevel 1 (
  echo.
  echo [ERROR] PetraERP failed to stop. Check the output above.
  if /I not "%~1"=="-NonInteractive" pause
  exit /b 1
)
if /I not "%~1"=="-NonInteractive" pause
