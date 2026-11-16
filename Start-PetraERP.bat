@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title PetraERP Preview Launcher

rem Set local .NET 10 environment
set "DOTNET_DIR=%~dp0.tools\dotnet\10.0.402"
if exist "%DOTNET_DIR%\dotnet.exe" (
  set "DOTNET_ROOT=%DOTNET_DIR%"
  set "PATH=%DOTNET_DIR%;%PATH%"
)

set "Scripts=%~dp0tools"
set "PS=powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File"

set "STOPPING="
set "NODESKTOP="
set "NONINTERACTIVE="
if not "%~1"=="" for %%A in (%*) do (
  if /I "%%~A"=="-Stop" set "STOPPING=1"
  if /I "%%~A"=="-NoDesktop" set "NODESKTOP=1"
  if /I "%%~A"=="-NonInteractive" set "NONINTERACTIVE=1"
)

rem -Stop is a deliberate graceful stop, so nothing is force-closed before it.
if defined STOPPING goto start

echo.
echo [CLEANUP] Closing open PetraERP instances that block startup...
%PS% "%Scripts%\Close-StalePreview.ps1"
if errorlevel 2 goto portbusy
if errorlevel 1 goto closefailed

:start
%PS% "%Scripts%\Start-LocalPreview.ps1" %*
if errorlevel 1 goto failed

echo.
if defined STOPPING (
  %PS% "%Scripts%\Test-LocalPreviewInstance.ps1" -ExpectStopped
) else if defined NODESKTOP (
  %PS% "%Scripts%\Test-LocalPreviewInstance.ps1" -ExpectApiOnly
) else (
  %PS% "%Scripts%\Test-LocalPreviewInstance.ps1" -ExpectDesktop
)
if errorlevel 1 goto notrunning
goto done

:portbusy
echo.
echo [ERROR] Port 17483 is held by a program outside PetraERP; nothing was stopped.
echo [ERROR] المنفذ 17483 محجوز ببرنامج خارج PetraERP ولم يغلق.
goto fail

:closefailed
echo.
echo [ERROR] An open PetraERP instance could not be closed. Save your work, close it manually, then retry.
echo [ERROR] تعذر اغلاق نسخة مفتوحة. احفظ عملك واغلقها يدويا ثم اعد المحاولة.
goto fail

:notrunning
echo.
echo [ERROR] PetraERP is not open after startup although the commands succeeded. Check the log above.
echo [ERROR] لم تفتح نسخة البرنامج بعد التشغيل. راجع السجل اعلاه.
goto fail

:failed
echo.
echo [ERROR] PetraERP failed to start. Check the output above.
echo [ERROR] فشل تشغيل البرنامج. راجع السجل اعلاه.
:fail
if not defined NONINTERACTIVE pause
exit /b 1

:done
if not defined NONINTERACTIVE pause
exit /b 0
