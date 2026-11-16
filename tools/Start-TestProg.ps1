#requires -Version 5.1
# TESTPROG: isolated test copy loaded with Ameen "Dardas 2026" master data and posted journals.
# TEST DATA ONLY. Separate database (TESTPROG), port (17484) and folder (.artifacts/testprog); legacy-data is read only.
[CmdletBinding()]
param([switch]$Stop, [switch]$NoDesktop)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:previousEnvironment = @{}
$taskRoot = Split-Path -Parent $PSScriptRoot
$previewRoot = Join-Path $taskRoot '.artifacts/testprog'
$statePath = Join-Path $previewRoot 'state.json'
$apiDirectory = Join-Path $previewRoot 'api'
$desktopDirectory = Join-Path $previewRoot 'desktop'
$apiDll = Join-Path $apiDirectory 'PetraERP.Api.dll'
$desktopExe = Join-Path $desktopDirectory 'PetraERP.Desktop.exe'
$baseUri = 'https://localhost:17484/'
$legacy = Join-Path $taskRoot 'legacy-data/DB_2026'
$version = (Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$dotnet = Join-Path $taskRoot ".tools/dotnet/$version/dotnet.exe"
. (Join-Path $PSScriptRoot 'LocalPreview.Runtime.ps1')
# BOM-less: a UTF-8 preamble would otherwise be piped into --password-stdin as part of the password.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    [void](New-Item -ItemType Directory -Path $previewRoot -Force)
    if (Test-Path -LiteralPath $statePath) { $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json }
    else {
        $state = [pscustomobject]@{
            InstallationId = [Guid]::NewGuid().ToString('N'); Database = 'TESTPROG'; UserName = 'admin'
            Password = (Protect-PreviewSecret ('Try!' + (New-PreviewRandom 15))); SigningKey = (Protect-PreviewSecret (New-PreviewRandom 48))
            Initialized = $false; Imported = $false; ApiProcess = $null; DesktopProcess = $null
        }
        Save-PreviewState
    }
    $apiProcess = Get-PreviewProcess $state.ApiProcess $apiDll -Api
    $desktopProcess = Get-PreviewProcess $state.DesktopProcess $desktopExe
    if (!$Stop -and ($apiProcess -or $desktopProcess)) {
        # Each launch deploys the latest build; database TESTPROG and the import are kept.
        if ($desktopProcess) { Stop-Process -Id $desktopProcess.Id -Force -ErrorAction SilentlyContinue }
        if ($apiProcess) { Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue; $apiProcess.WaitForExit(5000) | Out-Null }
        $apiProcess = $null; $desktopProcess = $null; $state.ApiProcess = $null; $state.DesktopProcess = $null; Save-PreviewState
    }
    if ($Stop) {
        if ($desktopProcess) { Stop-Process -Id $desktopProcess.Id -Force -ErrorAction SilentlyContinue }
        if ($apiProcess) { Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue }
        $state.ApiProcess = $null; $state.DesktopProcess = $null; Save-PreviewState
        Write-Host 'TESTPROG stopped. Data preserved.' -ForegroundColor Green; exit 0
    }
    if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw 'Pinned .NET SDK is missing. Run tools/Initialize-DotNet.ps1 first.' }
    if (!(Test-Path -LiteralPath $legacy -PathType Container)) { throw "Ameen 2026 export not found: $legacy" }
    & $dotnet dev-certs https --check --trust *> (Join-Path $previewRoot 'certificate.log')
    if ($LASTEXITCODE -ne 0) { & $dotnet dev-certs https --trust; if ($LASTEXITCODE -ne 0) { throw 'Trust the local HTTPS development certificate, then try again.' } }
    if (!$apiProcess) {
        Write-Host 'Building PetraERP and the TESTPROG importer (Release)...' -ForegroundColor Cyan
        Push-Location $taskRoot
        try {
            & $dotnet build PetraERP.slnx --configuration Release *> (Join-Path $previewRoot 'build.log')
            if ($LASTEXITCODE -ne 0) { throw "Build failed. See $(Join-Path $previewRoot 'build.log')" }
        }
        finally { Pop-Location }
        Copy-PreviewBuild (Join-Path $taskRoot 'src/PetraERP.Api/bin/Release/net10.0') $apiDirectory
        Confirm-PreviewDatabase
        Set-PreviewEnvironment
        try {
            Write-Host 'Applying migrations to database TESTPROG...' -ForegroundColor Cyan
            Invoke-PreviewCli @('migrate', '--confirm', '--environment', 'Development') (Join-Path $previewRoot 'migrate.log')
            if (!$state.Initialized) {
                $completed = Invoke-PreviewSql $state.Database 'SELECT COUNT(*) FROM [security].[BootstrapState];'
                if ($completed -eq 0) {
                    Invoke-PreviewCli @('bootstrap-admin', '--username', $state.UserName, '--display-name-ar', 'مدير الاختبار', '--display-name-en', 'Test Administrator', '--company-code', 'DARDAS', '--company-name-ar', 'دردس 2026 (نسخة تجريبية)', '--company-name-en', 'Dardas 2026 (test copy)', '--currency', 'SYP', '--currency-name-ar', 'الليرة السورية', '--branch-code', 'MAIN', '--branch-name-ar', 'الفرع الرئيسي', '--branch-name-en', 'Main Branch', '--password-stdin', '--environment', 'Development') (Join-Path $previewRoot 'bootstrap.log') (Unprotect-PreviewSecret $state.Password)
                }
                $state.Initialized = $true; Save-PreviewState
            }
            # Local test instance only: the import sends ~26k requests, above the default 600/min per-IP API limit.
            [Environment]::SetEnvironmentVariable('Petra__Security__RateLimits__ApiPermitsPerMinute', '100000', 'Process')
            $apiProcess = Start-Process -FilePath $dotnet -ArgumentList ('"' + $apiDll + '"') -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $previewRoot 'api.stdout.log') -RedirectStandardError (Join-Path $previewRoot 'api.stderr.log')
            $state.ApiProcess = New-PreviewProcessIdentity $apiProcess; Save-PreviewState
        }
        finally { Restore-PreviewEnvironment }
    }
    Wait-PreviewReady $apiProcess
    if (!$state.Imported) {
        Write-Host 'Importing Dardas 2026 data through the API (first run only, several minutes)...' -ForegroundColor Cyan
        & $dotnet build (Join-Path $taskRoot 'tools/PetraERP.TestDataImport') --configuration Release *> (Join-Path $previewRoot 'importer-build.log')
        if ($LASTEXITCODE -ne 0) { throw "Importer build failed. See $(Join-Path $previewRoot 'importer-build.log')" }
        $importer = Join-Path $taskRoot 'tools/PetraERP.TestDataImport/bin/Release/net10.0/PetraERP.TestDataImport.dll'
        $priorRuntime = [Environment]::GetEnvironmentVariable('DOTNET_ROOT', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('DOTNET_ROOT', (Split-Path -Parent $dotnet), 'Process')
            (Unprotect-PreviewSecret $state.Password) | & $dotnet $importer --legacy $legacy --api $baseUri --user $state.UserName --company DARDAS --state $previewRoot
            $importExit = $LASTEXITCODE
        }
        finally { [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $priorRuntime, 'Process') }
        if ($importExit -eq 1) { throw "Import stopped. Run TESTPROG.bat again to resume. Report: $(Join-Path $previewRoot 'import-report.md')" }
        if ($importExit -eq 2) { Write-Host 'Some records were rejected (see import-report.md). The next start retries only those.' -ForegroundColor Yellow }
        else { $state.Imported = $true; Save-PreviewState }
    }
    if (!$NoDesktop -and !$desktopProcess) {
        Copy-PreviewBuild (Join-Path $taskRoot 'src/PetraERP.Desktop/bin/Release/net10.0-windows') $desktopDirectory
        [IO.File]::WriteAllText((Join-Path $desktopDirectory 'desktopsettings.json'), (@{ apiBaseUri = $baseUri; requestTimeoutSeconds = 30 } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        $priorRuntime = [Environment]::GetEnvironmentVariable('DOTNET_ROOT', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('DOTNET_ROOT', (Split-Path -Parent $dotnet), 'Process')
            $desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory $desktopDirectory -WindowStyle Normal -PassThru
        }
        finally { [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $priorRuntime, 'Process') }
        $state.DesktopProcess = New-PreviewProcessIdentity $desktopProcess; Save-PreviewState
    }
    Write-Host ''
    Write-Host '==================== TESTPROG ====================' -ForegroundColor Cyan
    Write-Host '  نسخة تجريبية من بيانات دردس 2026 / Dardas 2026 test copy' -ForegroundColor Green
    Write-Host ('  Username : ' + $state.UserName) -ForegroundColor White
    Write-Host ('  Password : ' + (Unprotect-PreviewSecret $state.Password)) -ForegroundColor White
    Write-Host '  Company  : DARDAS · Branch: MAIN · Fiscal year: 2026 · Database: TESTPROG' -ForegroundColor Gray
    Write-Host ('  Import report: ' + (Join-Path $previewRoot 'import-report.md')) -ForegroundColor Gray
    Write-Host '  Stop with: TESTPROG.bat stop' -ForegroundColor Gray
    Write-Host '==================================================' -ForegroundColor Cyan
}
catch { Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
finally { Restore-PreviewEnvironment }
