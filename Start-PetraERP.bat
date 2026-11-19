@echo off
title Petra ERP
cd /d "D:\PetraERP"

:: Check if API server on port 17483 is already listening
netstat -ano | findstr ":17483" | findstr "LISTENING" >nul
if errorlevel 1 (
    echo Starting PetraERP Backend API Server...
    start /min "" powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File "D:\PetraERP\tools\Run-ApiDaemon.ps1"
    timeout /t 3 /nobreak >nul
)

:: Launch the Desktop Client
start "" "D:\PetraERP\frontend\petra-desktop\src-tauri\target\release\petra-desktop.exe"
