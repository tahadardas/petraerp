#requires -Version 5.1
[CmdletBinding()]
param([switch]$ExpectDesktop, [switch]$ExpectApiOnly, [switch]$ExpectStopped)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$taskRoot = Split-Path -Parent $PSScriptRoot
$previewPrefix = (Join-Path $taskRoot '.artifacts/local-preview').TrimEnd('\') + '\'
$statePath = Join-Path $taskRoot '.artifacts/local-preview/state.json'
$port = 17483

function Write-Note([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

$desktopCandidate = @(Get-CimInstance Win32_Process -Filter "Name='PetraERP.Desktop.exe'" -ErrorAction SilentlyContinue) |
    Where-Object { ([string]$_.CommandLine).IndexOf($previewPrefix, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1
$desktop = if ($desktopCandidate) { Get-Process -Id ([int]$desktopCandidate.ProcessId) -ErrorAction SilentlyContinue } else { $null }
$desktopWindow = $desktop -ne $null -and $desktop.MainWindowHandle -ne [IntPtr]::Zero

# The API can need a few seconds after the launcher reports it ready before the socket is visible, and Get-NetTCPConnection
# sometimes answers empty while the machine is busy. Look again (and through .NET) before calling it closed.
$listener = @()
for ($attempt = 0; $attempt -lt 8 -and $listener.Count -eq 0; $attempt++) {
    if ($attempt -gt 0) { Start-Sleep -Milliseconds 1500 }
    $listener = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    # On some Windows builds the -State filter matches nothing although the socket is listening; a listener is a socket with no remote end.
    if ($listener.Count -eq 0) { $listener = @(Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Where-Object { $_.RemotePort -eq 0 }) }
    if ($listener.Count -eq 0 -and $ExpectStopped) { break }
}
$apiId = if ($listener.Count -gt 0) { [int]$listener[0].OwningProcess } else { $null }
$health = 'stopped'
if ($null -ne $apiId) {
    $health = 'listening, health not answering'
    try {
        $response = Invoke-WebRequest "https://localhost:$port/health/ready" -UseBasicParsing -TimeoutSec 5
        $health = if ($response.StatusCode -eq 200) { 'ready' } else { "listening (HTTP $($response.StatusCode))" }
    }
    catch { }
}

$recordedDesktop = $null
$recordedApi = $null
if (Test-Path -LiteralPath $statePath) {
    try {
        $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($state.DesktopProcess) { $recordedDesktop = [int]$state.DesktopProcess.Id }
        if ($state.ApiProcess) { $recordedApi = [int]$state.ApiProcess.Id }
    }
    catch { }
}

$desktopText = if ($desktop) {
    "PID $($desktop.Id) — window: $(if ($desktopWindow) { 'yes' } else { 'no' }) — responding: $(if ($desktop.Responding) { 'yes' } else { 'no' })"
} else { 'لا يعمل / not running' }
$apiText = if ($null -ne $apiId) { "PID $apiId — HTTPS $port : $health" } else { "مغلق / not listening on $port" }

Write-Note ''
Write-Note 'نسخة المعاينة / Preview instance' 'Cyan'
Write-Note ("  Desktop: {0}" -f $desktopText)
Write-Note ("  API:     {0}" -f $apiText)
if ($null -ne $recordedDesktop -or $null -ne $recordedApi) {
    Write-Note ("  state.json سجّل / recorded: Desktop={0} API={1}" -f $(if ($null -eq $recordedDesktop) { '-' } else { $recordedDesktop }), $(if ($null -eq $recordedApi) { '-' } else { $recordedApi }))
    $mismatch = ($null -ne $recordedDesktop -and $null -ne $desktop -and $recordedDesktop -ne $desktop.Id) -or
        ($null -ne $recordedApi -and $null -ne $apiId -and $recordedApi -ne $apiId)
    if ($mismatch) { Write-Note '  تنبيه: النسخة العاملة لا تطابق ما سجّله المشغّل. / Warning: running instance does not match the recorded identity.' 'Yellow' }
}

if ($ExpectStopped) {
    if ($null -eq $desktop -and $null -eq $apiId) {
        Write-Note 'INSTANCE: NOT RUNNING — لا توجد نسخة مفتوحة. / No preview instance is open.' 'Green'
        exit 0
    }
    Write-Note 'INSTANCE: OPEN — نسخة ما زالت مفتوحة رغم طلب الإيقاف. / An instance is still open after the stop request.' 'Red'
    exit 1
}

if ($ExpectApiOnly) {
    if ($null -ne $apiId -and $null -eq $desktop) {
        Write-Note 'INSTANCE: OPEN (API only) — الخادم يعمل كما طُلب بدون واجهة. / API is running as requested, no desktop.' 'Green'
        exit 0
    }
    if ($null -ne $desktop) {
        Write-Note 'INSTANCE: OPEN — واجهة سطح المكتب تعمل رغم طلب -NoDesktop. / A desktop window is open although -NoDesktop was requested.' 'Red'
        exit 1
    }
    Write-Note 'INSTANCE: NOT RUNNING — لم يبدأ الخادم. / The API is not running.' 'Red'
    exit 1
}

if ($ExpectDesktop) {
    if ($desktopWindow -and $null -ne $apiId) {
        Write-Note 'INSTANCE: OPEN — نسخة مفتوحة وجاهزة. / Preview is open and ready.' 'Green'
        exit 0
    }
    Write-Note 'INSTANCE: NOT READY — لا توجد نسخة مفتوحة بنافذة وخادم. / No open preview with both a window and the API.' 'Red'
    exit 1
}

if ($null -ne $desktop -or $null -ne $apiId) {
    Write-Note 'INSTANCE: OPEN — نسخة مفتوحة. / Preview is open.' 'Green'
}
else {
    Write-Note 'INSTANCE: NOT RUNNING — لا توجد نسخة مفتوحة. / No preview instance is open.'
}
exit 0
