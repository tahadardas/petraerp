#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SqlServer = '.\SQLEXPRESS',
    [ValidateRange(1,4)][int]$SqlMaxThreads = 1
)
# This runner never accepts a database/connection string. SqlTestDatabase creates
# random owned disposable databases and verifies each ownership marker at cleanup.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath
foreach ($module in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Security')) {
    Import-Module (Join-Path $PSHOME "Modules/$module") -ErrorAction Stop
}
$repo = Split-Path -Parent $PSScriptRoot
$results = Join-Path $repo ('.artifacts/phase2-full-verification-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[void](New-Item -ItemType Directory -Path $results)
$started = [DateTimeOffset]::UtcNow
$stages = [System.Collections.Generic.List[object]]::new()
$observations = [System.Collections.Generic.List[object]]::new()
$previousEnvironment = @{}
$failure = $null
$dotnet = $null
$metadata = [ordered]@{ startedUtc=$started.ToString('o'); sqlServer=$SqlServer; expectedSqlMajor=16; sdk=$null; sqlVersion=$null; results=$results; safety='No business database parameter. Tests use SqlTestDatabase randomized names and verified PetraERP.TestRunId ownership markers. No runner cleanup is performed.' }
$exitCode = 1
$databaseBaseline = @()
$databaseInventoryReady = $false
$remainingDatabases = @()

function Get-VerificationDatabaseNames {
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15")
    try {
        $connection.Open(); $command=$connection.CreateCommand()
        $command.CommandText="SELECT name FROM sys.databases WHERE name LIKE N'PetraERP[_]SchemaTest[_]%' OR name LIKE N'PetraERP[_]LocalPreview[_]%'"
        $reader=$command.ExecuteReader()
        try { while ($reader.Read()) { $reader.GetString(0) } } finally { $reader.Dispose(); $command.Dispose() }
    } finally { $connection.Dispose() }
}
[xml]$solution = Get-Content -LiteralPath (Join-Path $repo 'PetraERP.slnx') -Raw
$expectedTestAssemblies = @($solution.SelectNodes('//Project') | Where-Object { $_.Path -like 'tests/*' } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension([string]$_.Path) })

function Write-Json([object]$Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding UTF8
}
function Invoke-VerificationStage([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $begin = [DateTimeOffset]::UtcNow
    $log = Join-Path $results "$Name.log"
    Write-Host "Running $Name; log: $log"
    $ErrorActionPreference = 'Continue'
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $entry = [ordered]@{ name=$Name; executable=$Executable; arguments=$Arguments; startedUtc=$begin.ToString('o'); finishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); exitCode=$code; log=$log }
    $stages.Add([pscustomobject]$entry)
    Write-Json $entry (Join-Path $results "$Name.exit.json")
    return $code
}
function Import-ResultFiles([string]$Stage, [string]$Directory, [string]$OutputName) {
    $files = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object { $_.Extension -in @('.json','.ctrf') })
    if (!$files.Count) { throw "$Stage produced no CTRF evidence: $Directory" }
    $allTests = [System.Collections.Generic.List[object]]::new()
    $reportedAssemblies = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        $report = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if (!$report.PSObject.Properties['results'] -or !$report.results.PSObject.Properties['tests']) { continue }
        $suites = @($report.results.extra.suites)
        if ($suites.Count -ne 1) { throw "Expected one assembly per original CTRF report: $($file.FullName)" }
        $assembly = [IO.Path]::GetFileNameWithoutExtension([string]$suites[0].filePath)
        $reportedAssemblies.Add($assembly)
        foreach ($test in $report.results.tests) {
            $category = ''
            if ($test.PSObject.Properties['labels'] -and $test.labels.PSObject.Properties['Category']) { $category = [string]$test.labels.Category }
            $message = if ($test.PSObject.Properties['message']) { [string]$test.message } else { '' }
            $classification = if ($category -eq 'SqlServer') { 'Explicit ' + $assembly.Replace('PetraERP.','').Replace('.Tests','') + ' SQL' } else { 'Regular' }
            if (!$test.PSObject.Properties['extra'] -or !$test.extra.PSObject.Properties['id'] -or ![string]$test.extra.id) { throw "Missing stable xUnit test ID in $($file.FullName). Truncated display names cannot safely identify parameterized cases." }
            $id = $assembly + '::' + [string]$test.extra.id
            $observations.Add([pscustomobject]@{ Identity=$id; Assembly=$assembly; Name=[string]$test.name; Classification=$classification; Stage=$Stage; Status=[string]$test.status; Message=$message; OriginalReport=$file.FullName })
            $allTests.Add($test)
        }
    }
    if (!$allTests.Count) { throw "$Stage has no test observations in its CTRF evidence." }
    if ($Stage -eq 'regular') {
        $missing = @($expectedTestAssemblies | Where-Object { $_ -notin $reportedAssemblies })
        if ($missing.Count) { throw "Regular test discovery has missing assembly evidence: $($missing -join ', ')" }
    } elseif (!@($allTests | Where-Object { $_.status -in @('passed','failed','skipped') }).Count) {
        throw "$Stage executed no explicit tests. A successful empty runner is insufficient."
    }
    # Aggregate only original generated observations; retain original files unchanged.
    $counts = @{}
    foreach ($status in @('passed','failed','skipped','pending','other')) { $counts[$status] = @($allTests | Where-Object status -eq $status).Count }
    Write-Json ([ordered]@{
        reportFormat='CTRF'; specVersion='0.0.0'; generatedBy='PetraERP original CTRF evidence aggregation';
        results=[ordered]@{ tool=@{ name='xUnit.net v3 via Microsoft.Testing.Platform' }; summary=@{ tests=$allTests.Count; passed=$counts.passed; failed=$counts.failed; skipped=$counts.skipped; pending=$counts.pending; other=$counts.other }; tests=@($allTests.ToArray()); extra=@{ originalReports=@($files.FullName); stage=$Stage } }
    }) (Join-Path $results $OutputName)
}

Push-Location $repo
try {
    git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $results 'git-revision.txt') -Encoding UTF8
    git status --short | Set-Content -LiteralPath (Join-Path $results 'git-status.txt') -Encoding UTF8
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows is required for the full WPF verification suite.' }
    if ($SqlServer -notmatch '^(\.|localhost|127\.0\.0\.1|\(local\))(\\[A-Za-z0-9_-]+)?$') { throw 'Only an explicitly local SQL instance name is accepted. Database names, connection strings and remote targets are refused.' }
    $version = (Get-Content -LiteralPath global.json -Raw | ConvertFrom-Json).sdk.version
    $dotnet = Join-Path $repo ".tools/dotnet/$version/dotnet.exe"
    if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    foreach ($name in @('DOTNET_ROOT','PATH','DOTNET_NOLOGO','DOTNET_CLI_TELEMETRY_OPTOUT','PETRAERP_SQL_TEST_SERVER','PETRAERP_SQL_TEST_EXPECTED_MAJOR','PETRAERP_SQL_TEST_TRUST_CERTIFICATE','PETRAERP_SQL_COMPATIBILITY_ONLY')) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    $env:DOTNET_NOLOGO='1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $metadata.sdk = (& $dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $metadata.sdk -ne $version) { throw "Pinned SDK $version is required; found $($metadata.sdk)." }
    $env:PETRAERP_SQL_TEST_SERVER=$SqlServer
    $env:PETRAERP_SQL_TEST_EXPECTED_MAJOR='16'
    $env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE='1'
    $env:PETRAERP_SQL_COMPATIBILITY_ONLY='1'
    Add-Type -AssemblyName System.Data
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=PetraERP Full Verification Prerequisites")
    try {
        $connection.Open()
        $command=$connection.CreateCommand()
        $command.CommandText="SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int), CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion')), CONVERT(nvarchar(128),SERVERPROPERTY('Edition'));"
        $reader=$command.ExecuteReader()
        try { if (!$reader.Read()) { throw 'SQL version query returned no row.' }; $major=$reader.GetInt32(0); $metadata.sqlVersion=$reader.GetString(1); $metadata.sqlEdition=$reader.GetString(2) } finally { $reader.Dispose(); $command.Dispose() }
        if ($major -ne 16) { throw "SQL Server 2022 (major 16) is required; found $major. No test database was created." }
    } finally { $connection.Dispose() }
    $metadata.sqlTestDatabaseHelperSha256=(Get-FileHash -LiteralPath 'tests/PetraERP.Infrastructure.Tests/SqlTestDatabase.cs' -Algorithm SHA256).Hash
    $databaseBaseline=@(Get-VerificationDatabaseNames)
    $databaseInventoryReady=$true
    Write-Json $databaseBaseline (Join-Path $results 'sql-database-baseline.json')
    Write-Json $metadata (Join-Path $results 'environment.json')
    if ((Invoke-VerificationStage 'diff-check' 'git' @('diff','--check')) -ne 0) { throw 'Static diff check failed.' }
    if ((Invoke-VerificationStage 'restore' $dotnet @('restore','PetraERP.slnx')) -ne 0) { throw 'Restore failed; tests were not run.' }
    if ((Invoke-VerificationStage 'build' $dotnet @('build','PetraERP.slnx','-c','Release','--no-restore')) -ne 0) { throw 'Release build failed; tests were not run.' }
    $binaries = @('Infrastructure','Api','Desktop') | ForEach-Object {
        $tfm = if ($_ -eq 'Desktop') { 'net10.0-windows' } else { 'net10.0' }
        Get-FileHash -LiteralPath (Join-Path $repo "tests/PetraERP.$_.Tests/bin/Release/$tfm/PetraERP.$_.Tests.dll") -Algorithm SHA256
    }
    Write-Json @($binaries | Select-Object Path,Hash,Algorithm) (Join-Path $results 'test-binaries.json')
    if ((Invoke-VerificationStage 'client-contract' 'powershell.exe' @('-NoProfile','-ExecutionPolicy','Bypass','-File','tools/Update-ApiContract.ps1','-Check')) -ne 0) { throw 'Generated API client drift; tests were not run.' }
    if ((Invoke-VerificationStage 'launcher-guards' 'powershell.exe' @('-NoProfile','-ExecutionPolicy','Bypass','-File','tools/Test-LocalPreviewGuards.ps1','-ResultsDirectory',$results)) -ne 0) { throw 'Launcher safety guard checks failed; tests were not run.' }
    $regular = Join-Path $results 'regular'
    [void](Invoke-VerificationStage 'regular' $dotnet @('test','--solution','PetraERP.slnx','-c','Release','--no-build','--','--report-xunit-ctrf','--results-directory',$regular))
    try { Import-ResultFiles 'regular' $regular 'regular-tests.json' } catch { $failure = $_.Exception.Message; Write-Warning $failure }
    foreach ($suite in @('Infrastructure','Api','Desktop')) {
        $stage=$suite.ToLowerInvariant()+'-sql'
        $tfm=if ($suite -eq 'Desktop') { 'net10.0-windows' } else { 'net10.0' }
        $dll="tests/PetraERP.$suite.Tests/bin/Release/$tfm/PetraERP.$suite.Tests.dll"
        $threads=if ($suite -eq 'Desktop') { '1' } else { [string]$SqlMaxThreads }
        $directory=Join-Path $results $stage
        [void](Invoke-VerificationStage $stage $dotnet @($dll,'--explicit','only','--max-threads',$threads,'--report-xunit-ctrf','--report-xunit-ctrf-filename',"$stage.json",'--results-directory',$directory))
        try { Import-ResultFiles $stage $directory "$stage.json" } catch { $failure = $_.Exception.Message; Write-Warning $failure }
    }
} catch { $failure=$_.Exception.Message; Write-Warning $failure }
finally {
    if ($databaseInventoryReady) {
        try {
            $databaseAfter=@(Get-VerificationDatabaseNames)
            $remainingDatabases=@($databaseAfter | Where-Object { $_ -notin $databaseBaseline })
            Write-Json @{before=$databaseBaseline;after=$databaseAfter;newUncleaned=$remainingDatabases;note='Metadata only; no database is adopted or deleted by this runner.'} (Join-Path $results 'sql-cleanup-inventory.json')
            if ($remainingDatabases.Count) { $failure='New test-like databases remain; ownership review is required. Nothing was deleted. See sql-cleanup-inventory.json.' }
        } catch { $failure='SQL cleanup inventory could not be completed: '+$_.Exception.Message }
    }
    $inventory = @($observations | Group-Object Identity | ForEach-Object {
        $items=@($_.Group)
        $actual=@($items | Where-Object { $_.Status -in @('passed','failed','skipped') })
        $status=if (@($actual | Where-Object Status -eq 'failed').Count) { 'failed' } elseif (@($actual | Where-Object Status -eq 'passed').Count) { 'passed' } elseif (@($actual | Where-Object Status -eq 'skipped').Count) { 'skipped' } else { 'not-run' }
        [pscustomobject]@{ Identity=$_.Name; Assembly=$items[0].Assembly; Name=$items[0].Name; Classification=$items[0].Classification; Status=$status; ExecutedStages=(@($actual | ForEach-Object Stage | Select-Object -Unique) -join ';'); FilteredObservations=@($items | Where-Object Status -eq 'pending').Count; OriginalReports=(@($items.OriginalReport | Select-Object -Unique) -join ';'); FailureMessage=(@($items | Where-Object Status -eq 'failed' | ForEach-Object Message) -join ' | ') }
    } | Sort-Object Identity)
    # Headers without fabricated test rows are legitimate evidence when a build failed.
    if ($inventory.Count) { $inventory | Export-Csv -LiteralPath (Join-Path $results 'test-inventory.csv') -NoTypeInformation -Encoding UTF8 }
    else { '"Identity","Assembly","Name","Classification","Status","ExecutedStages","FilteredObservations","OriginalReports","FailureMessage"' | Set-Content -LiteralPath (Join-Path $results 'test-inventory.csv') -Encoding UTF8 }
    $failed=@($inventory | Where-Object Status -eq 'failed')
    if ($failed.Count) { $failed | Export-Csv -LiteralPath (Join-Path $results 'failed-tests.csv') -NoTypeInformation -Encoding UTF8 }
    else { '"Identity","Assembly","Name","Classification","Status","ExecutedStages","FilteredObservations","OriginalReports","FailureMessage"' | Set-Content -LiteralPath (Join-Path $results 'failed-tests.csv') -Encoding UTF8 }
    $stageSummary=@($stages | ForEach-Object {
        $stageName=$_.name
        $items=@($observations | Where-Object Stage -eq $stageName)
        [pscustomobject]@{ stage=$stageName; exitCode=$_.exitCode; passed=@($items | Where-Object Status -eq 'passed').Count; failed=@($items | Where-Object Status -eq 'failed').Count; skipped=@($items | Where-Object Status -eq 'skipped').Count; filtered=@($items | Where-Object Status -eq 'pending').Count; log=$_.log }
    })
    $passed=@($inventory | Where-Object Status -eq 'passed').Count
    $skipped=@($inventory | Where-Object Status -eq 'skipped').Count
    $notRun=@($inventory | Where-Object Status -eq 'not-run').Count
    $requiredStages=@('diff-check','restore','build','client-contract','launcher-guards','regular','infrastructure-sql','api-sql','desktop-sql')
    $complete=@($stages | Where-Object { $_.exitCode -eq 0 -and $_.name -in $requiredStages }).Count -eq $requiredStages.Count
    if ($complete -and !$failure -and $inventory.Count -gt 0 -and !$failed.Count -and !$skipped -and !$notRun) { $exitCode=0 }
    $coverage=[ordered]@{}
    foreach ($entry in @(
        @('Financial regression','Posting|Journal|Sales|Purchase|Treasury|Voucher|Inventory|Period|Weighted|Reversal|Receivable|Payable'),
        @('Phase2 administration','CompanyAdministration|CompanyOptions|MasterCopy|ChartTemplate|Provisioning|LocalPreviewInitialization|InstallationDatabaseManager'),
        @('Numbering','Numbering|NumberAllocation|NumberReservation|NumberLifecycle|SalesNumberSurvives'),
        @('FX','FxRevaluation'),
        @('Isolation and security','Workspace|Security|Authorization|Permission|Actor'),
        @('Desktop/reporting','Desktop|Reporting|Report|NumberFormat')
    )) {
        $matches=@($inventory | Where-Object { $_.Name -match $entry[1] })
        $coverage[$entry[0]]=@{ discovered=$matches.Count; passed=@($matches | Where-Object Status -eq 'passed').Count; failed=@($matches | Where-Object Status -eq 'failed').Count; notRun=@($matches | Where-Object Status -eq 'not-run').Count; note='Derived subsets of the complete runs; never added to unique totals. Name grouping is an index, not a scenario coverage proof.' }
    }
    $summary=[ordered]@{ status=$(if ($exitCode -eq 0) { 'VERIFIED_AUTOMATED' } else { 'FAILED_OR_INCOMPLETE' }); startedUtc=$started.ToString('o'); finishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); uniqueDiscovered=$inventory.Count; passed=$passed; failed=$failed.Count; skipped=$skipped; notRun=$notRun; unenumerated=$(if (!$inventory.Count) { 'Inventory unavailable: prerequisites/build failed before test discovery.' } else { $null }); exitCode=$exitCode; failure=$failure; stages=$stageSummary; coverageIndex=$coverage; filteredObservations=@($observations | Where-Object Status -eq 'pending').Count; guiAcceptance='Separate launcher/Windows visual acceptance required. This runner does not certify GUI use.'; environment=$metadata }
    Write-Json $summary (Join-Path $results 'test-summary.json')
    Write-Json @($stages.ToArray()) (Join-Path $results 'commands.json')
    Write-Json $metadata (Join-Path $results 'environment.json')
    $lines=@('# PetraERP full automated verification', '', "Status: $($summary.status)", "SDK: $($metadata.sdk); SQL Server: $($metadata.sqlVersion) (required major 16).", "Unique discovered: $($inventory.Count); passed: $passed; failed: $($failed.Count); skipped: $skipped; not run: $notRun.", '', '| Stage | Exit | Passed | Failed | Skipped | Filtered |', '|---|---:|---:|---:|---:|---:|')
    foreach ($row in $stageSummary) { $lines+='| '+$row.stage+' | '+$row.exitCode+' | '+$row.passed+' | '+$row.failed+' | '+$row.skipped+' | '+$row.filtered+' |' }
    $lines+=@('', 'Filtered pending observations are reconciled by assembly + stable xUnit extra.id across regular and explicit runs. Display names can be truncated and are never used as identity. FX and security subsets are included in API/Infrastructure SQL totals, never counted twice.', 'Original xUnit CTRF reports and command exit records are preserved in the per-stage folders. The root JSON reports aggregate their original observations.', 'No SQL tests are filtered down to selected classes. Source method counts are not execution evidence.', "Failure/blocker: $failure", 'Windows GUI/manual acceptance is recorded separately; no GUI PASS is implied by these results.')
    $lines | Set-Content -LiteralPath (Join-Path $results 'verification-report.md') -Encoding UTF8
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name],'Process') }
    Pop-Location
    Write-Host "Verification result: $results"
    Write-Host "Unique: $($inventory.Count); passed=$passed failed=$($failed.Count) skipped=$skipped not-run=$notRun; exit=$exitCode"
}
exit $exitCode

