<#
.SYNOPSIS  TestEvidence runner: restore (locked), Release build, every test project (Explicit SQL/HTTP included) -> one JSON.
.PARAMETER Out   Output JSON path.   .PARAMETER Phase  Label stored in the JSON.
Status vocabulary: PASS / FAIL / NOT_RUN / BLOCKED. A project with skipped tests is never reported as full PASS.
#>
param([string]$Out = '.artifacts\evidence\TestEvidence.json', [string]$Phase = 'adhoc', [switch]$SkipExplicit)
$ErrorActionPreference = 'Continue'
Set-Location (Split-Path (Split-Path $PSScriptRoot))
$dn = '.\.tools\dotnet\10.0.402\dotnet.exe'
$env:PETRAERP_SQL_TEST_SERVER = '.\SQLEXPRESS'; $env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE = '1'; $env:PETRAERP_SQL_TEST_EXPECTED_MAJOR = '16'
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$logDir = Join-Path (Split-Path $Out) ("logs_" + $Phase); New-Item -ItemType Directory -Force $logDir | Out-Null
$sqlVersion = (& sqlcmd -S '.\SQLEXPRESS' -E -C -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(40))+' | '+CAST(SERVERPROPERTY('Edition') AS varchar(80))" -h -1 -W 2>&1) -join ' '
$result = [ordered]@{ phase = $Phase; startedUtc = (Get-Date).ToUniversalTime().ToString('o'); revision = (git rev-parse HEAD); dirtyFiles = (git status --short | Measure-Object).Count
  sqlServer = $sqlVersion; dotnet = (& $dn --version); sqlServerInstance = '.\SQLEXPRESS'; database = 'disposable per test (PetraERP_SchemaTest_*)'
  seeds = 'fixed Random seeds inside fixtures (20261028, 5, 42, ...); Guid.NewGuid ids are random by design'; steps = @(); projects = @() }
function Step($name, $exe, $argList, $log) {
  & $exe @argList *> $log; $rc = $LASTEXITCODE
  $script:result.steps += [ordered]@{ name = $name; rc = $rc; status = $(if ($rc -eq 0) { 'PASS' } else { 'FAIL' }); log = $log }
  return $rc
}
$rcRestore = Step 'restore --locked-mode' $dn @('restore', 'PetraERP.slnx', '--locked-mode') "$logDir\restore.log"
$rcBuild = Step 'build -c Release' $dn @('build', 'PetraERP.slnx', '-c', 'Release', '--no-restore', '--nologo', '-v', 'q') "$logDir\build.log"
if ($rcBuild -eq 0) {
  foreach ($d in Get-ChildItem tests -Directory) {
    $proj = Join-Path $d.FullName ($d.Name + '.csproj'); if (-not (Test-Path $proj)) { continue }
    $log = "$logDir\$($d.Name).log"
    $targs = @('test', '--project', $proj, '-c', 'Release', '--no-build'); if (-not $SkipExplicit) { $targs += @('--explicit', 'on') }
    $sw = [Diagnostics.Stopwatch]::StartNew(); & $dn @targs *> $log; $rc = $LASTEXITCODE; $sw.Stop()
    $text = Get-Content $log -Raw
    $m = @{}; foreach ($k in 'total', 'failed', 'succeeded', 'skipped') { if ($text -match "(?m)^\s*$($k):\s*(\d+)") { $m[$k] = [int]$Matches[1] } else { $m[$k] = $null } }
    $failedNames = @([regex]::Matches($text, '(?m)^failed (\S+)') | ForEach-Object { $_.Groups[1].Value })
    $status = if ($null -eq $m.total) { 'FAIL' } elseif ($m.failed -gt 0 -or $rc -ne 0) { 'FAIL' } elseif ($m.skipped -gt 0) { 'PASS_WITH_SKIPS' } else { 'PASS' }
    $result.projects += [ordered]@{ name = $d.Name; status = $status; rc = $rc; total = $m.total; succeeded = $m.succeeded; failed = $m.failed; skipped = $m.skipped; failedTests = $failedNames; seconds = [int]$sw.Elapsed.TotalSeconds; log = $log }
  }
} else { $result.projects += [ordered]@{ name = 'ALL'; status = 'BLOCKED'; reason = 'build failed' } }
$result.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
$result.overall = if (($result.steps | Where-Object status -ne 'PASS') -or ($result.projects | Where-Object { $_.status -in 'FAIL', 'BLOCKED' })) { 'FAIL' } else { 'PASS' }
$result | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 $Out
"DONE overall=$($result.overall)"
