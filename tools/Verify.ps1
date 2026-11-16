#requires -Version 5.1
[CmdletBinding()]
param([switch]$IncludeSqlTests, [string]$ChecklistReportDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskDotnet = Join-Path $taskRoot ".tools/dotnet/$taskVersion/dotnet.exe"
if (-not (Test-Path -LiteralPath $taskDotnet -PathType Leaf)) {
    throw 'Initialize the pinned local SDK before running repository verification.'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROOT = Split-Path -Parent $taskDotnet
Push-Location $taskRoot
try {
    & python -m unittest discover -s tests/tooling -v
    if ($LASTEXITCODE -ne 0) { throw 'Repository tooling/architecture tests failed.' }
    & $taskDotnet build PetraERP.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    if ($ChecklistReportDirectory) {
        $taskReportRoot = [IO.Path]::GetFullPath($ChecklistReportDirectory)
        $taskReportRun = Join-Path $taskReportRoot ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $taskReportRun -Force | Out-Null
        $taskRunStart = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        foreach ($taskTestProject in Get-ChildItem -Path (Join-Path $taskRoot 'tests/PetraERP.*.Tests/*.csproj')) {
            $taskTestArguments = @('--report-xunit-ctrf', '--report-xunit-ctrf-filename', ($taskTestProject.BaseName + '.json'), '--results-directory', $taskReportRun)
            if ($IncludeSqlTests) { $taskTestArguments += @('--explicit', 'on') }
            $taskAssemblyCandidates = @(Get-ChildItem -Path (Join-Path $taskTestProject.Directory.FullName "bin/Release/*/$($taskTestProject.BaseName).dll"))
            if ($taskAssemblyCandidates.Count -ne 1) { throw "Expected one built test assembly for $($taskTestProject.BaseName)." }
            $taskTestAssembly = $taskAssemblyCandidates[0].FullName
            & $taskDotnet $taskTestAssembly @taskTestArguments
            if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($taskTestProject.BaseName). Results retained in $taskReportRun" }
        }
        & python (Join-Path $PSScriptRoot 'checklist_verification.py') --reports $taskReportRun --not-before-unix $taskRunStart
        if ($LASTEXITCODE -ne 0) { throw 'Checklist executed-evidence reconciliation failed.' }
        Write-Output "Checklist reports: $taskReportRun"
    }
    elseif ($IncludeSqlTests) {
        Write-Output 'Running explicit SQL tests against the configured engine. Major 16 compatibility runs do not certify the SQL Server 2025 target.'
        & $taskDotnet test --solution PetraERP.slnx --configuration Release --no-build -- --explicit on
    }
    else {
        & $taskDotnet test --solution PetraERP.slnx --configuration Release --no-build
    }
    if ($LASTEXITCODE -ne 0) { throw 'Application/domain/infrastructure tests failed.' }
    & (Join-Path $PSScriptRoot 'Update-ApiContract.ps1') -Check
    Write-Output 'PASS: selected checks. Posting transactions, authorization, complete persistence and API workflows remain separate acceptance gates.'
}
finally {
    Pop-Location
}
