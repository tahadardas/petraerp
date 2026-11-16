#requires -Version 5.1
[CmdletBinding()]
param([switch]$NoDesktop, [switch]$Stop, [switch]$Restart, [switch]$ForceRebuild, [switch]$NonInteractive)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# cmd.exe can inherit PowerShell 7 module paths. Load the current host's modules
# explicitly so Windows PowerShell never imports incompatible Core modules.
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath
foreach ($module in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Security')) {
    Import-Module (Join-Path $PSHOME "Modules/$module") -ErrorAction Stop
}
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$script:previousEnvironment = @{}
$taskRoot = Split-Path -Parent $PSScriptRoot
$previewRoot = Join-Path $taskRoot '.artifacts/local-preview'
$statePath = Join-Path $previewRoot 'state.json'
$apiDirectory = Join-Path $previewRoot 'api'
$desktopDirectory = Join-Path $previewRoot 'desktop'
$apiDll = Join-Path $apiDirectory 'PetraERP.Api.dll'
$desktopExe = Join-Path $desktopDirectory 'PetraERP.Desktop.exe'
$baseUri = 'https://localhost:17483/'
$globalJson = Join-Path $taskRoot 'global.json'
$version = if (Test-Path -LiteralPath $globalJson) { (Get-Content -LiteralPath $globalJson -Raw | ConvertFrom-Json).sdk.version } else { '10.0.402' }
$dotnet = Join-Path $taskRoot ".tools/dotnet/$version/dotnet.exe"
if (!(Test-Path -LiteralPath $dotnet)) {
    $toolsDirs = Get-ChildItem -Path (Join-Path $taskRoot '.tools/dotnet') -Directory -ErrorAction SilentlyContinue
    if ($toolsDirs) { $dotnet = Join-Path $toolsDirs[0].FullName 'dotnet.exe' }
    if (!(Test-Path -LiteralPath $dotnet)) {
        $installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($installedDotnet) { $dotnet = $installedDotnet.Source }
    }
}
. (Join-Path $PSScriptRoot 'LocalPreview.Runtime.ps1')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$mutex = [Threading.Mutex]::new($false, "Local\PetraERP.LocalPreview.$identity")
$locked = $false
$fileLock = $null
$component = 'Initialization / التهيئة'
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'Another local-preview launcher is working. Wait for it to finish.' }
    [void](New-Item -ItemType Directory -Path $previewRoot -Force)
    try { $fileLock = [IO.File]::Open((Join-Path $previewRoot 'launcher.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw 'مشغل آخر يحتفظ بقفل هذه المعاينة. / Another Windows session owns this preview launcher lock. Wait for it to finish.' }
    if (Test-Path -LiteralPath $statePath) { $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json }
    elseif ($Stop) { Write-Host 'No local preview has been initialized.'; exit 0 }
    else {
        [void](New-Item -ItemType Directory -Path $previewRoot -Force)
        $installation = [Guid]::NewGuid().ToString('N')
        $state = [pscustomobject]@{
            InstallationId = $installation; Database = "PetraERP_LocalPreview_$($installation.Substring(0,16))"
            UserName = 'admin'; Password = (Protect-PreviewSecret ('Try!' + (New-PreviewRandom 15)))
            SigningKey = (Protect-PreviewSecret (New-PreviewRandom 48)); Initialized = $false
            ApiProcess = $null; DesktopProcess = $null
        }
        Save-PreviewState
    }
    if ($state.Database -notmatch '^PetraERP_LocalPreview_[a-f0-9]{16}$' -or $state.InstallationId -notmatch '^[a-fA-F0-9]{32}$') { throw 'Invalid isolated preview identity. State preserved.' }
    $apiProcess = Get-PreviewProcess $state.ApiProcess $apiDll -Api
    $desktopProcess = Get-PreviewProcess $state.DesktopProcess $desktopExe

    if ($Stop) {
        Stop-OwnedPreviewDesktop $desktopProcess
        Stop-OwnedPreviewApi $apiProcess
        $state.ApiProcess = $null; $state.DesktopProcess = $null; Save-PreviewState
        Write-Host 'تم إيقاف خادم API وتطبيق سطح المكتب بنجاح مع الحفاظ على البيانات. / Local API and Desktop stopped successfully. Data preserved.' -ForegroundColor Green
        exit 0
    }

    if ($Restart) {
        if ($desktopProcess) {
            Stop-OwnedPreviewDesktop $desktopProcess
            $desktopProcess = $null
        }
        $state.DesktopProcess = $null

        Stop-OwnedPreviewApi $apiProcess
        $apiProcess = $null
        $state.ApiProcess = $null

        Save-PreviewState

        # Wait for port 17483 to be released
        $portDeadline = [DateTime]::UtcNow.AddSeconds(5)
        while ([DateTime]::UtcNow -lt $portDeadline) {
            $listener = @(Get-NetTCPConnection -LocalPort 17483 -ErrorAction SilentlyContinue | Where-Object { $_.RemotePort -eq 0 })
            if (!$listener) { break }
            Start-Sleep -Milliseconds 300
        }
    }
    $component = 'Prerequisites / المتطلبات'
    Confirm-PreviewPrerequisites
    $sourceFingerprint = Get-PreviewSourceFingerprint
    $stampPath = Join-Path $previewRoot 'build-stamp.json'
    $current = $false
    if ((Test-Path -LiteralPath $stampPath) -and (Test-Path -LiteralPath $apiDll) -and (Test-Path -LiteralPath $desktopExe)) {
        try {
            $stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
            $current = $stamp.Source -eq $sourceFingerprint -and $stamp.Binaries -eq (Get-PreviewBinaryFingerprint) -and $stamp.Sdk -eq $version
        } catch { $current = $false }
    }
    if (($apiProcess -or $desktopProcess) -and (!$current -or $ForceRebuild)) { throw 'Save work, then use Start-PetraERP.bat -Restart to deploy current source. Running files were not overwritten.' }
    if (!$apiProcess) {
        $listener = @(Get-NetTCPConnection -LocalPort 17483 -ErrorAction SilentlyContinue | Where-Object { $_.RemotePort -eq 0 })
        if ($listener) { throw 'Port 17483 belongs to another process. Nothing was stopped or reused.' }
    }
    if (!$current -or $ForceRebuild) {
        $component = 'Release Build / البناء'
        Write-Host 'Building the current source in Release mode... / جاري بناء أحدث كود مصدري...' -ForegroundColor Cyan
        Push-Location $taskRoot
        try {
            & $dotnet build PetraERP.slnx --configuration Release *> (Join-Path $previewRoot 'build.log')
            if ($LASTEXITCODE -ne 0) {
                Write-Host '--- Build Output (Errors) ---' -ForegroundColor Red
                Get-Content (Join-Path $previewRoot 'build.log') | Where-Object { $_ -match 'error CS|FAILED' } | ForEach-Object { Write-Host $_ -ForegroundColor Red }
                throw "Build failed. See $(Join-Path $previewRoot 'build.log')"
            }
        }
        finally { Pop-Location }
    }
    if (!$apiProcess) {
        if (!$current -or $ForceRebuild) {
            if ($sourceFingerprint -ne (Get-PreviewSourceFingerprint)) { throw 'Source changed during build. Retry after editing finishes.' }
            Copy-PreviewBuild (Join-Path $taskRoot 'src/PetraERP.Api/bin/Release/net10.0') $apiDirectory
            Copy-PreviewBuild (Join-Path $taskRoot 'src/PetraERP.Desktop/bin/Release/net10.0-windows') $desktopDirectory
            [IO.File]::WriteAllText((Join-Path $desktopDirectory 'desktopsettings.json'), (@{ apiBaseUri = $baseUri; requestTimeoutSeconds = 30 } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
            @{Source=$sourceFingerprint;Binaries=(Get-PreviewBinaryFingerprint);Sdk=$version} | ConvertTo-Json | Set-Content -LiteralPath $stampPath -Encoding UTF8
        }
        $component = 'Central database / القاعدة المركزية'
        Confirm-PreviewDatabase
        Set-PreviewEnvironment
        try {
            Invoke-PreviewCli @('migrate', '--confirm', '--environment', 'Development') (Join-Path $previewRoot 'migrate.log')
            if (!$state.Initialized) {
                $completed = Invoke-PreviewSql $state.Database 'SELECT COUNT(*) FROM [security].[BootstrapState];'
                if ($completed -eq 0) {
                    Invoke-PreviewCli @('bootstrap-admin', '--username', $state.UserName, '--display-name-ar', 'مدير الاختبار', '--display-name-en', 'Preview Administrator', '--company-code', 'PREVIEW', '--company-name-ar', 'شركة اختبار PetraERP', '--company-name-en', 'PetraERP Test Company', '--currency', 'USD', '--currency-name-ar', 'الدولار الأمريكي', '--branch-code', 'MAIN', '--branch-name-ar', 'الفرع التجريبي', '--branch-name-en', 'Test Branch', '--password-stdin', '--environment', 'Development') (Join-Path $previewRoot 'bootstrap.log') (Unprotect-PreviewSecret $state.Password)
                }
                $state.Initialized = $true; Save-PreviewState
            }
            $component = 'Annual demo workspace / السنة المالية التجريبية'
            Invoke-PreviewCli @('preview-initialize','--installation-id',$state.InstallationId,'--username',$state.UserName,'--password-stdin','--environment','Development','--result-file',(Join-Path $previewRoot 'workspace.json')) (Join-Path $previewRoot 'demo.log') (Unprotect-PreviewSecret $state.Password)
            Write-Host 'Starting PetraERP API server... / جاري بدء تشغيل خادم API...' -ForegroundColor Cyan
            $apiProcess = Start-Process -FilePath $dotnet -ArgumentList ('"' + $apiDll + '"') -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $previewRoot 'api.stdout.log') -RedirectStandardError (Join-Path $previewRoot 'api.stderr.log')
            $state.ApiProcess = New-PreviewProcessIdentity $apiProcess; Save-PreviewState
        }
        finally { Restore-PreviewEnvironment }
    }
    $component = 'HTTPS readiness / جاهزية الخادم'
    Wait-PreviewReady $apiProcess
    if (!$NoDesktop -and !$desktopProcess) {
        Write-Host 'Deploying updated Desktop binaries and launching GUI window... / جاري نشر شاشات البرنامج وتشغيلها...' -ForegroundColor Cyan
        $priorRuntime = [Environment]::GetEnvironmentVariable('DOTNET_ROOT', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('DOTNET_ROOT', (Split-Path -Parent $dotnet), 'Process')
            $desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory $desktopDirectory -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $previewRoot 'desktop.stdout.log') -RedirectStandardError (Join-Path $previewRoot 'desktop.stderr.log')
        }
        finally { [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $priorRuntime, 'Process') }
        $state.DesktopProcess = New-PreviewProcessIdentity $desktopProcess; Save-PreviewState
        $windowDeadline = [DateTime]::UtcNow.AddSeconds(20)
        while ([DateTime]::UtcNow -lt $windowDeadline) {
            Start-Sleep -Milliseconds 250
            $desktopProcess.Refresh()
            if ($desktopProcess.HasExited) { throw 'Desktop exited during startup. Check the Windows error message.' }
            if ($desktopProcess.MainWindowHandle -ne [IntPtr]::Zero -and $desktopProcess.Responding) { break }
        }
        if ($desktopProcess.MainWindowHandle -eq [IntPtr]::Zero -or !$desktopProcess.Responding) { throw 'Desktop did not expose a responsive window before the startup deadline. Check the Windows error message.' }
    }
    $demoPath = Join-Path $previewRoot 'workspace.json'
    if (!(Test-Path -LiteralPath $demoPath)) { throw 'Demo readiness record missing. Use -Restart to verify annual workspace.' }
    $demo = Get-Content -LiteralPath $demoPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($demo.Status -ne 'Ready') { throw 'Annual workspace is not ready. See demo.log.' }
    Confirm-PreviewApiWorkspace $demo
    if (!$NoDesktop) {
        $desktopProcess.Refresh()
        if ($desktopProcess.HasExited -or $desktopProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Desktop preview has no live window.' }
    }
    @{AtUtc=[DateTime]::UtcNow.ToString('o');Api='Ready';Workspace=$demo;Desktop=$(if($NoDesktop){'Not requested'}else{'Running'});ApiPid=$apiProcess.Id;Database=$state.Database} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $previewRoot 'launch-summary.json') -Encoding UTF8
    $plainPassword = Unprotect-PreviewSecret $state.Password
    $credsContent = @"
PETRA ERP LOCAL PREVIEW CREDENTIALS
------------------------------------
USERNAME: $($state.UserName)
PASSWORD: $plainPassword
COMPANY: $($demo.CompanyCode) - $($demo.CompanyName)
FISCAL_YEAR: $($demo.YearNumber)
BRANCH: $($demo.BranchCode) - $($demo.BranchName)
API_URL: $baseUri
APPLICATION: $(if($NoDesktop){'NOT REQUESTED'}else{$desktopExe})
"@
    [IO.File]::WriteAllText((Join-Path $previewRoot 'credentials.txt'), $credsContent, [Text.UTF8Encoding]::new($false))
    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host '  PetraERP — بيئة المعاينة والاختبار البصري جاهزة بنجاح' -ForegroundColor Green
    Write-Host '  PetraERP Visual Testing Environment is Ready!' -ForegroundColor Green
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host ('  * خادم الـ API        : READY (' + $baseUri + ')') -ForegroundColor White
    Write-Host ('  * قاعدة البيانات     : READY (' + $state.Database + ')') -ForegroundColor White
    Write-Host ('  * الشركة             : ' + $demo.CompanyCode + ' — ' + $demo.CompanyName) -ForegroundColor White
    Write-Host ('  * السنة المالية      : ' + $demo.YearNumber) -ForegroundColor White
    Write-Host ('  * الفرع              : ' + $demo.BranchCode + ' — ' + $demo.BranchName) -ForegroundColor White
    Write-Host ('  * اسم المستخدم       : ' + $state.UserName) -ForegroundColor Cyan
    if (!$NonInteractive) { Write-Host ('  * كلمة المرور        : ' + $plainPassword) -ForegroundColor Yellow }
    Write-Host ('  * ملف بيانات الدخول  : ' + (Join-Path $previewRoot 'credentials.txt')) -ForegroundColor Gray
    Write-Host ('  * تطبيق الواجهة      : ' + $(if($NoDesktop){'NOT REQUESTED'}else{$desktopExe})) -ForegroundColor White
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host 'STOP: Stop-PetraERP.bat'
}
catch {
    $errorPath = Join-Path $previewRoot 'launcher-error.log'
    [IO.File]::WriteAllText($errorPath, ($component + "`r`n" + $_.Exception.GetType().FullName + "`r`n" + $_.ScriptStackTrace), [Text.UTF8Encoding]::new($false))
    Write-Host ("فشل المكوّن / Failed component: $component`n" + 'Startup failed: ' + $_.Exception.Message + "`nالسجل / Log: $errorPath`nLogs: $previewRoot") -ForegroundColor Red
    exit 1
}
finally {
    Restore-PreviewEnvironment
    if ($fileLock) { $fileLock.Dispose() }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
