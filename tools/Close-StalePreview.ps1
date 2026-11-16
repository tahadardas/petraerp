#requires -Version 5.1
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# Only processes whose command line lives under this repository may be closed. An editor, a browser or an
# unrelated dotnet app that merely mentions the path must never be shut down.
$taskRoot = Split-Path -Parent $PSScriptRoot
$rootPrefix = $taskRoot.TrimEnd('\') + '\'
$previewRoot = Join-Path $taskRoot '.artifacts/local-preview'
$port = 17483

function Write-Note([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

# The API is usually a dotnet host whose exe lives under .tools, so ownership follows the command line
# as well as the executable path; a system dotnet must not make an owned listener look foreign.
function Get-OwnerInfo([int]$OwnerId) {
    $process = $null
    try { $process = Get-CimInstance Win32_Process -Filter "ProcessId=$OwnerId" -ErrorAction Stop } catch { }
    $path = ''
    $command = ''
    $name = 'unknown'
    if ($process) {
        $path = [string]$process.ExecutablePath
        $command = [string]$process.CommandLine
        $name = [string]$process.Name
    }
    if ([string]::IsNullOrEmpty($path)) {
        $live = Get-Process -Id $OwnerId -ErrorAction SilentlyContinue
        if ($live) {
            try { $path = [string]$live.Path } catch { }
            if ($name -eq 'unknown') { $name = $live.ProcessName }
        }
    }
    $owned = ($path -and $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) -or
        ($command.IndexOf($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $command -match 'PetraERP\.(Api\.dll|Desktop\.exe)')
    return [pscustomobject]@{ Path = $path; Name = $name; Owned = $owned }
}

function Get-RepoProcesses {
    $all = @(Get-CimInstance Win32_Process -ErrorAction Stop)
    $byId = @{}
    foreach ($process in $all) { $byId[[int]$process.ProcessId] = $process }

    $protected = New-Object 'System.Collections.Generic.HashSet[int]'
    $cursor = $PID
    while ($byId.ContainsKey($cursor)) {
        $parent = [int]$byId[$cursor].ParentProcessId
        if ($parent -le 0 -or !$protected.Add($parent)) { break }
        $cursor = $parent
    }

    $found = New-Object 'System.Collections.Generic.List[object]'
    foreach ($process in $all) {
        $id = [int]$process.ProcessId
        if ($id -eq $PID -or $protected.Contains($id)) { continue }
        $command = [string]$process.CommandLine
        if ([string]::IsNullOrWhiteSpace($command)) { continue }
        if ($command.IndexOf($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
        $name = [string]$process.Name
        $kind = $null
        if ($name -ieq 'PetraERP.Desktop.exe') { $kind = 'Desktop' }
        elseif ($name -ieq 'PetraERP.Api.exe' -or ($name -ieq 'dotnet.exe' -and $command -match 'PetraERP\.Api\.dll')) { $kind = 'API' }
        elseif (($name -ieq 'powershell.exe' -or $name -ieq 'pwsh.exe') -and $command -match 'Start-LocalPreview\.ps1') { $kind = 'Launcher' }
        if ($kind) { $found.Add([pscustomobject]@{ Id = $id; Kind = $kind; Name = $name }) }
    }
    return $found.ToArray()
}

function Stop-RepoProcess($Entry) {
    $process = Get-Process -Id $Entry.Id -ErrorAction SilentlyContinue
    if (!$process) {
        Write-Note ("  - [{0}] PID {1}: لم يعد قائماً / already exited" -f $Entry.Kind, $Entry.Id)
        return $true
    }

    $closed = $false
    if ($Entry.Kind -eq 'Desktop') {
        try { $closed = [bool]$process.CloseMainWindow() } catch { $closed = $false }
        if ($closed) { $closed = $process.WaitForExit(10000) }
        if (!$closed) {
            Write-Note ("  - [{0}] PID {1}: لم تستجب النافذة، يُغلق قسراً — المسودات غير المحفوظة في تلك النافذة تُفقد / window did not close; forcing, unsaved drafts in that window are lost" -f $Entry.Kind, $Entry.Id) 'Yellow'
        }
    }
    elseif ($Entry.Kind -eq 'Launcher') {
        Write-Note ("  - [{0}] PID {1}: إغلاق مشغّل سابق — إن كان يبني، سيُعاد البناء في هذا التشغيل / stopping an earlier launcher; any abandoned build is rebuilt now" -f $Entry.Kind, $Entry.Id) 'Yellow'
    }

    if (!$closed) {
        Stop-Process -Id $Entry.Id -ErrorAction Stop
        [void]$process.WaitForExit(10000)
    }
    if (!$process.HasExited) {
        Write-Note ("  - [{0}] PID {1}: ما زال يعمل / still running" -f $Entry.Kind, $Entry.Id) 'Red'
        return $false
    }
    $how = if ($closed) { 'بإغلاق مهذب / gracefully' } else { 'بقوة / forced' }
    Write-Note ("  - [{0}] PID {1}: أُغلق {2}" -f $Entry.Kind, $Entry.Id, $how) 'Yellow'
    return $true
}

Write-Note '[1/3] إغلاق النسخ المفتوحة التي تحتفظ بملفات المعاينة / Closing open preview processes...' 'Cyan'
$targets = @(Get-RepoProcesses)
if ($targets.Count -eq 0) {
    Write-Note '  - لا توجد نسخ PetraERP مفتوحة تلغي العمل. / No open PetraERP preview process found.'
}
else {
    $rank = @{ Launcher = 0; Desktop = 1; API = 2 }
    $allStopped = $true
    foreach ($entry in ($targets | Sort-Object { $rank[$_.Kind] })) {
        if (!(Stop-RepoProcess $entry)) { $allStopped = $false }
    }
    if (!$allStopped) {
        Write-Note '[ERROR] تعذّر إغلاق كل عمليات المعاينة. / Some preview processes refused to stop.' 'Red'
        exit 1
    }
}

Write-Note ("[2/3] التحقق من المنفذ {0} / Checking port {0}..." -f $port) 'Cyan'
$deadline = [DateTime]::UtcNow.AddSeconds(10)
$listener = @()
while ([DateTime]::UtcNow -lt $deadline) {
    $listener = @(Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Where-Object { $_.RemotePort -eq 0 })
    if ($listener.Count -eq 0) { break }
    Start-Sleep -Milliseconds 300
}
if ($listener.Count -gt 0) {
    $ownerId = [int]$listener[0].OwningProcess
    $owner = Get-OwnerInfo $ownerId
    if (!$owner.Owned) {
        Write-Note ("[ERROR] المنفذ {0} يشغله برنامج آخر ({1}, PID {2}) ولم يُغلق لأنه لا ينتمي إلى PetraERP. / Port {0} is held by {1} (PID {2}) outside this repository; nothing was stopped." -f $port, $owner.Name, $ownerId) 'Red'
        exit 2
    }
    Write-Note ("[ERROR] المنفذ {0} ما زال محتجزاً من عملية PetraERP (PID {1}). / Port {0} is still held by PetraERP PID {1}." -f $port, $ownerId) 'Red'
    exit 1
}
Write-Note '  - المنفذ حر. / Port is free.'
Write-Note '[3/3] جاهز للتشغيل. / Ready to start.' 'Green'
exit 0
