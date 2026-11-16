#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SqlServer,
    [ValidateSet(16, 17)][int]$ExpectedMajor = 17,
    [switch]$CompatibilityOnly,
    [switch]$SkipRestore,
    [switch]$ContinueSqlAfterUnitFailure,
    [switch]$SqlOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskSdk = Join-Path $taskRoot '.tools/dotnet/10.0.402'
if (-not (Test-Path -LiteralPath (Join-Path $taskSdk 'dotnet.exe'))) { throw 'The pinned .NET SDK 10.0.402 is required.' }
if ($ExpectedMajor -eq 16 -and -not $CompatibilityOnly) { throw 'SQL Server 16 requires explicit compatibility mode.' }
$env:DOTNET_ROOT = $taskSdk
$env:PATH = "$taskSdk;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:PETRAERP_SQL_TEST_SERVER = $SqlServer
$env:PETRAERP_SQL_TEST_EXPECTED_MAJOR = $ExpectedMajor.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:PETRAERP_SQL_COMPATIBILITY_ONLY = if ($CompatibilityOnly) { '1' } else { '0' }
$env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE = '1'
$taskLogRoot = Join-Path $taskRoot ('.artifacts/phase-one/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskLogRoot -Force | Out-Null
$taskResults = [ordered]@{ Git = 'NOT RUN'; Restore = 'NOT RUN'; Build = 'NOT RUN'; Tests = 'NOT RUN'; SqlIntegration = 'NOT RUN'; SqlMajor = $ExpectedMajor; CompatibilityOnly = [bool]$CompatibilityOnly; Logs = $taskLogRoot }

function Invoke-PhaseOneStep {
    param([string]$Name, [string]$Executable, [string[]]$Arguments)
    $script:taskCurrentStep = $Name
    Write-Output "Running $Name"
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $taskLogRoot ($Name + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE. See $taskLogRoot" }
}

Push-Location $taskRoot
try {
    Invoke-PhaseOneStep 'git-diff-check' 'git' @('-c', 'core.safecrlf=false', 'diff', '--check')
    Invoke-PhaseOneStep 'git-status' 'git' @('status', '--short')
    $taskResults.Git = 'PASS'
    if (-not $SqlOnly) {
        if ($SkipRestore) { $taskResults.Restore = 'NOT RUN (prior restore passed)' }
        else {
            Invoke-PhaseOneStep 'restore' 'dotnet' @('restore', 'PetraERP.slnx')
            $taskResults.Restore = 'PASS'
        }
        Invoke-PhaseOneStep 'build' 'dotnet' @('build', 'PetraERP.slnx', '-c', 'Release', '--no-restore')
        $taskResults.Build = 'PASS'
        # The SDK's Microsoft.Testing.Platform runner requires --solution instead of a positional solution.
        try {
            Invoke-PhaseOneStep 'tests' 'dotnet' @('test', '--solution', 'PetraERP.slnx', '-c', 'Release', '--no-build')
            $taskResults.Tests = 'PASS'
        }
        catch {
            $taskResults.Tests = 'FAIL'
            if (-not $ContinueSqlAfterUnitFailure) { throw }
            Write-Output 'Unit suite failed. Keeping that failure recorded while collecting the requested independent SQL integration evidence.'
        }
    }
    $taskSqlClasses = @(
        @('Infrastructure', 'PhaseOneWorkspaceSqlTests'),
        @('Infrastructure', 'PostingSqlTests'),
        @('Infrastructure', 'PostingRequestIdempotencySqlTests'),
        @('Infrastructure', 'InventorySqlTests'),
        # SalesPostingSqlTests is partial and includes the purchase and treasury voucher workflows.
        @('Infrastructure', 'SalesPostingSqlTests'),
        @('Api', 'PhaseOneWorkspaceAuthorizationTests'),
        @('Api', 'WorkspaceListHttpSqlTests')
    )
    foreach ($taskSqlClass in $taskSqlClasses) {
        $taskAssembly = "tests/PetraERP.$($taskSqlClass[0]).Tests/bin/Release/net10.0/PetraERP.$($taskSqlClass[0]).Tests.dll"
        $taskClass = "PetraERP.$($taskSqlClass[0]).Tests.$($taskSqlClass[1])"
        Invoke-PhaseOneStep ('sql-' + $taskSqlClass[1]) 'dotnet' @($taskAssembly, '--explicit', 'only', '--filter-class', $taskClass,
            '--max-threads', '2', '--report-xunit-ctrf', '--report-xunit-ctrf-filename', ($taskSqlClass[1] + '.json'), '--results-directory', $taskLogRoot)
    }
    $taskResults.SqlIntegration = 'PASS'
    if ($taskResults.Tests -eq 'FAIL') {
        $script:taskCurrentStep = 'tests'
        throw 'Unit suite remains FAILED. SQL results do not override that failure.'
    }
    Write-Output 'Phase 1 validation commands passed. FIX-03-D and FIX-04 remain architecturally BLOCKED; these checks do not certify independent financial workspace isolation.'
}
catch {
    switch -Wildcard ($script:taskCurrentStep) {
        'git-*' { $taskResults.Git = 'FAIL' }
        'restore' { $taskResults.Restore = 'FAIL' }
        'build' { $taskResults.Build = 'FAIL' }
        'tests' { $taskResults.Tests = 'FAIL' }
        'sql-*' { $taskResults.SqlIntegration = 'FAIL' }
    }
    $taskResults.Error = $_.Exception.Message
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    $taskResults | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskLogRoot 'result.json') -Encoding utf8
    Write-Output ('Validation result: ' + (Join-Path $taskLogRoot 'result.json'))
    Pop-Location
}
