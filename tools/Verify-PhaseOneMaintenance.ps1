param(
    [ValidateSet('All','Build','Regular','Infrastructure','Api','Desktop','Fx')][string]$Stage = 'All',
    [string]$ResultsDirectory = '.artifacts/phase1-maintenance-20261025',
    [string]$SqlServer = '.\SQLEXPRESS',
    [int]$SqlMajor = 16,
    [ValidateRange(1,4)][int]$SqlMaxThreads = 1
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$dotnet = Join-Path $repo '.tools/dotnet/10.0.402/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PETRAERP_SQL_TEST_SERVER = $SqlServer
$env:PETRAERP_SQL_TEST_EXPECTED_MAJOR = [string]$SqlMajor
$env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE = '1'
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$results = (Resolve-Path -LiteralPath $ResultsDirectory).Path
function Run-Stage([string]$name, [string[]]$arguments) {
    $start = [DateTimeOffset]::UtcNow
    $log = Join-Path $results "$name.log"
    "START $name $start" | Write-Output
    & $dotnet @arguments 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    [ordered]@{stage=$name;started=$start.ToString('o');finished=[DateTimeOffset]::UtcNow.ToString('o');exitCode=$code;arguments=$arguments} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results "$name.exit.json") -Encoding utf8
    "END $name exit=$code" | Write-Output
    if ($code -ne 0) { throw "$name failed with exit code $code; see $log" }
}
if ($Stage -in @('All','Build')) {
    git diff --check 2>&1 | Tee-Object -FilePath (Join-Path $results 'diff-check.log')
    if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed' }
    git status --short | Set-Content -LiteralPath (Join-Path $results 'git-status.txt') -Encoding utf8
    git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $results 'git-revision.txt')
    Run-Stage 'restore' @('restore','PetraERP.slnx')
    Run-Stage 'build' @('build','PetraERP.slnx','-c','Release','--no-restore')
}
if ($Stage -in @('All','Regular')) {
    Run-Stage 'regular' @('test','--solution','PetraERP.slnx','-c','Release','--no-build','--','--report-xunit-ctrf','--results-directory', (Join-Path $results 'regular'))
}
foreach ($suite in @('Infrastructure','Api','Desktop')) {
    if ($Stage -notin @('All',$suite)) { continue }
    $tfm = if ($suite -eq 'Desktop') { 'net10.0-windows' } else { 'net10.0' }
    $dll = "tests/PetraERP.$suite.Tests/bin/Release/$tfm/PetraERP.$suite.Tests.dll"
    $threads = if ($suite -eq 'Desktop') { '1' } else { [string]$SqlMaxThreads }
    Run-Stage ($suite.ToLowerInvariant() + '-sql') @($dll,'--explicit','only','--max-threads',$threads,'--report-xunit-ctrf','--report-xunit-ctrf-filename',"$suite-sql.json",'--results-directory', (Join-Path $results $suite.ToLowerInvariant()))
}
if ($Stage -in @('All','Fx')) {
    Run-Stage 'fx-sql' @('tests/PetraERP.Api.Tests/bin/Release/net10.0/PetraERP.Api.Tests.dll','--explicit','only','--max-threads','1','--filter-class','*FxRevaluation*','--report-xunit-ctrf','--report-xunit-ctrf-filename','fx-sql.json','--results-directory', (Join-Path $results 'fx'))
}
