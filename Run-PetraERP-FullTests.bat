@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title PetraERP Full Verification
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Verify-PetraERP-Full.ps1" %*
set "PETRA_VERIFY_EXIT=%ERRORLEVEL%"
echo.
if not "%PETRA_VERIFY_EXIT%"=="0" echo فشل التحقق الكامل. راجع مسار النتائج المعروض أعلاه.
if /i not "%PETRAERP_NO_PAUSE%"=="1" pause
exit /b %PETRA_VERIFY_EXIT%
