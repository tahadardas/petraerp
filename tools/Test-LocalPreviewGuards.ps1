#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$ResultsDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$previewRoot = Join-Path $taskRoot '.artifacts/local-preview'
$dotnet = Join-Path $taskRoot '.tools/dotnet/10.0.402/dotnet.exe'
. (Join-Path $PSScriptRoot 'LocalPreview.Runtime.ps1')
[void](New-Item -ItemType Directory -Path $ResultsDirectory -Force)
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) {
    $checks.Add([pscustomobject]@{name=$Name;passed=$Passed})
    if (!$Passed) { throw "Launcher guard failed: $Name" }
}
try {
    $hashVector=Join-Path $ResultsDirectory 'sha256-known-vector.txt'
    [IO.File]::WriteAllBytes($hashVector,[Text.Encoding]::ASCII.GetBytes('abc'))
    Check 'Native SHA-256 matches the published abc test vector' ((Get-PreviewFileHash $hashVector) -ceq 'BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD')
    $self = Get-Process -Id $PID
    $wrong = @{Id=$PID;StartTicks=($self.StartTime.ToUniversalTime().Ticks + 1).ToString()}
    Check 'Reused PID with different start time is not adopted' ($null -eq (Get-PreviewProcess $wrong 'unrelated.exe'))
    $identity = New-PreviewProcessIdentity $self
    $rejected = $false
    try { Get-PreviewProcess $identity (Join-Path $previewRoot 'not-this-executable.exe') | Out-Null } catch { $rejected=$true }
    Check 'Unrelated executable is rejected and left running' ($rejected -and !(Get-Process -Id $PID).HasExited)
    $rejected = $false
    try { Assert-PreviewDatabaseName 'TESTPROG' } catch { $rejected=$true }
    Check 'Production database-name guard rejects TESTPROG without connecting to SQL' $rejected
    $secret = 'Local-guard-' + [Guid]::NewGuid().ToString('N')
    $protected = Protect-PreviewSecret $secret
    Check 'DPAPI round-trip remains protected for this Windows user' ($protected -ne $secret -and (Unprotect-PreviewSecret $protected) -ceq $secret)
    $secret=$null; $protected=$null
    # Exercise the actual launcher in an isolated installation-shaped directory.
    # It must stop at the file lock before parsing state or connecting to SQL.
    $guardRoot = Join-Path $ResultsDirectory 'launcher-guard-installation'
    $guardTools = Join-Path $guardRoot 'tools'
    $guardPreview = Join-Path $guardRoot '.artifacts/local-preview'
    [void](New-Item -ItemType Directory -Path $guardTools,$guardPreview -Force)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-LocalPreview.ps1'),(Join-Path $PSScriptRoot 'LocalPreview.Runtime.ps1') -Destination $guardTools
    Copy-Item -LiteralPath (Join-Path $taskRoot 'global.json') -Destination $guardRoot
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Start-PetraERP.bat') -Destination $guardRoot
    $stateFile = Join-Path $guardPreview 'state.json'
    '{"testSentinel":"must never be parsed under another launcher lock"}' | Set-Content -LiteralPath $stateFile -Encoding UTF8
    $before = if (Test-Path -LiteralPath $stateFile) { Get-PreviewFileHash $stateFile } else { '' }
    $lock = [IO.File]::Open((Join-Path $guardPreview 'launcher.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $rejectionLog = Join-Path $ResultsDirectory 'launcher-lock-rejection.log'
        & cmd.exe /d /c (Join-Path $guardRoot 'Start-PetraERP.bat') -NonInteractive -NoDesktop *> $rejectionLog
        $code = $LASTEXITCODE
        $after = if (Test-Path -LiteralPath $stateFile) { Get-PreviewFileHash $stateFile } else { '' }
        $correctRejection = Select-String -LiteralPath $rejectionLog -SimpleMatch 'Another Windows session owns this preview launcher lock' -Quiet
        Check 'Second launcher fails at exclusive file lock without altering installation state' ($code -ne 0 -and $before -eq $after -and $correctRejection)
    } finally { $lock.Dispose() }
} finally {
    $checks.ToArray() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'launcher-guard-tests.json') -Encoding UTF8
}
Write-Host 'PASS: preview process identity, data-target, DPAPI and cross-session file-lock guards.'
