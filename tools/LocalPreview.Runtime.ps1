# Local preview mechanics only. No accounting records or credentials are shipped here.
Set-StrictMode -Version Latest

function Protect-PreviewSecret([string]$Value) {
    ConvertFrom-SecureString (ConvertTo-SecureString $Value -AsPlainText -Force)
}

function Unprotect-PreviewSecret([string]$Value) {
    try {
        $secret = ConvertTo-SecureString $Value -ErrorAction Stop
        return ([System.Net.NetworkCredential]::new('', $secret)).Password
    } catch {
        throw 'تعذر فك حماية بيانات الدخول لهذا مستخدم Windows. / DPAPI secret cannot be decrypted; use the original Windows account. State was preserved.'
    }
}

function New-PreviewRandom([int]$Length) {
    $bytes = New-Object byte[] $Length
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    [Convert]::ToBase64String($bytes)
}

function Save-PreviewState {
    $temporary = Join-Path $previewRoot 'state.tmp'
    [IO.File]::WriteAllText($temporary, ($state | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $statePath -Force
}

function Get-PreviewProcess($Identity, [string]$ExpectedPath, [switch]$Api) {
    if ($null -eq $Identity) { return $null }
    $process = Get-Process -Id $Identity.Id -ErrorAction SilentlyContinue
    if ($null -eq $process -or $process.StartTime.ToUniversalTime().Ticks.ToString() -ne $Identity.StartTicks) { return $null }
    if ($Api) {
        $command = (Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" -ErrorAction Stop).CommandLine
        $expectedCommand = '^(?:"[^"]+"|\S+)\s+"?' + [regex]::Escape($ExpectedPath) + '"?(?:\s|$)'
        if (!$command -or $command -notmatch $expectedCommand -or ![string]::Equals($process.Path, $dotnet, [StringComparison]::OrdinalIgnoreCase)) { throw 'Recorded API process identity does not match. Nothing was stopped.' }
    }
    elseif (![string]::Equals($process.Path, $ExpectedPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Recorded Desktop process identity does not match. Nothing was stopped.' }
    return $process
}

function New-PreviewProcessIdentity($Process) {
    @{ Id = $Process.Id; StartTicks = $Process.StartTime.ToUniversalTime().Ticks.ToString() }
}

function Invoke-PreviewSql([string]$Database, [string]$Sql, [hashtable]$Parameters = @{}) {
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = '.\SQLEXPRESS'; $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true; $builder['Encrypt'] = $true; $builder['TrustServerCertificate'] = $true
    $builder['Connect Timeout'] = 15; $builder['Application Name'] = 'PetraERP Local Preview Setup'
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open(); $command = $connection.CreateCommand(); $command.CommandTimeout = 60; $command.CommandText = $Sql
        foreach ($name in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($name, $Parameters[$name]) }
        try { $command.ExecuteScalar() } finally { $command.Dispose() }
    }
    finally { $connection.Dispose() }
}

function Assert-PreviewDatabaseName([string]$Name) {
    if ($Name -notmatch '^PetraERP_LocalPreview_[a-f0-9]{16}$') { throw 'Invalid isolated preview database name.' }
}

function Confirm-PreviewDatabase {
    Assert-PreviewDatabaseName $state.Database
    $exists = Invoke-PreviewSql 'master' 'SELECT DB_ID(@name);' @{ '@name' = $state.Database }
    if ($exists -is [DBNull] -or $null -eq $exists) {
        [void](Invoke-PreviewSql 'master' "CREATE DATABASE [$($state.Database)];")
        [void](Invoke-PreviewSql $state.Database "EXEC sys.sp_addextendedproperty @name=N'PetraERP.LocalPreview.Owner', @value=@owner;" @{ '@owner' = $state.InstallationId })
    }
    $owner = Invoke-PreviewSql $state.Database "SELECT CONVERT(nvarchar(128), value) FROM sys.extended_properties WHERE class=0 AND name=N'PetraERP.LocalPreview.Owner';"
    if ($owner -ne $state.InstallationId) { throw 'Database ownership marker mismatch. No migration or bootstrap was attempted.' }
}

function Set-PreviewEnvironment {
    $values = @{
        DOTNET_ROOT = (Split-Path -Parent $dotnet)
        ASPNETCORE_ENVIRONMENT = 'Development'; DOTNET_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = $baseUri.TrimEnd('/')
        ConnectionStrings__PetraERP = "Server=.\SQLEXPRESS;Database=$($state.Database);Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=PetraERP Local Preview"
        Petra__Database__CompatibilityLevel = '160'
        Petra__Security__Tokens__SigningKey = (Unprotect-PreviewSecret $state.SigningKey)
        Petra__Security__Tokens__Issuer = "PetraERP.LocalPreview.$($state.InstallationId)"
        Petra__Security__Tokens__Audience = 'PetraERP.LocalPreview.Clients'
        Petra__Api__RequireHttps = 'true'; Petra__Api__ExposeOpenApi = 'false'
        AllowedHosts = 'localhost'
    }
    foreach ($name in $values.Keys) {
        $script:previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $values[$name], 'Process')
    }
}

function Restore-PreviewEnvironment {
    foreach ($name in $script:previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $script:previousEnvironment[$name], 'Process') }
    $script:previousEnvironment.Clear()
}

function Invoke-PreviewCli([string[]]$Arguments, [string]$Log, [string]$Password) {
    $priorNativeEncoding = $OutputEncoding
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    Push-Location $apiDirectory
    try {
        if ($Password) { $Password | & $dotnet $apiDll @Arguments *> $Log }
        else { & $dotnet $apiDll @Arguments *> $Log }
        if ($LASTEXITCODE -ne 0) { throw "Server setup failed. See $Log" }
    }
    finally { Pop-Location; $OutputEncoding = $priorNativeEncoding }
}

function Copy-PreviewBuild([string]$Source, [string]$Destination) {
    [void](New-Item -ItemType Directory -Path $Destination -Force)
    Get-ChildItem -LiteralPath $Source | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force }
}

function Wait-PreviewReady($Process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($Process.HasExited) { throw "Local API exited. See $(Join-Path $previewRoot 'api.stderr.log') and api.stdout.log." }
        try {
            $response = Invoke-WebRequest ($baseUri + 'health/ready') -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return }
        }
        catch { if ([DateTime]::UtcNow -ge $deadline) { throw 'Local HTTPS API readiness failed. Check certificate trust and API logs.' } }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Local API readiness timed out.'
}

# Content fingerprints deliberately exclude outputs and preview secrets. A successful stamp
# is valid only while both the sources and the actual staged binaries still match.
function Get-PreviewFileHash([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    $stream = $null
    try {
        $stream = [IO.File]::OpenRead($Path)
        return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','')
    } finally {
        if ($stream) { $stream.Dispose() }
        $hash.Dispose()
    }
}
function Get-PreviewSourceFingerprint {
    $paths = @('global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','NuGet.Config','PetraERP.slnx') | ForEach-Object { Join-Path $taskRoot $_ } | Where-Object { Test-Path -LiteralPath $_ }
    $paths += Get-ChildItem (Join-Path $taskRoot 'src') -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object FullName
    $digest = [Security.Cryptography.SHA256]::Create()
    try {
        $manifest = ($paths | Sort-Object | ForEach-Object { $_.Substring($taskRoot.Length) + ':' + (Get-PreviewFileHash $_) }) -join "`n"
        return [BitConverter]::ToString($digest.ComputeHash([Text.Encoding]::UTF8.GetBytes($manifest))).Replace('-','')
    } finally { $digest.Dispose() }
}
function Get-PreviewBinaryFingerprint {
    $paths = @($apiDirectory,$desktopDirectory) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File } | Where-Object { $_.Extension -in @('.dll','.exe','.json','.config') }
    if (!$paths) { return '' }
    $digest = [Security.Cryptography.SHA256]::Create()
    try {
        $manifest = ($paths | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($previewRoot.Length) + ':' + (Get-PreviewFileHash $_.FullName) }) -join "`n"
        return [BitConverter]::ToString($digest.ComputeHash([Text.Encoding]::UTF8.GetBytes($manifest))).Replace('-','')
    } finally { $digest.Dispose() }
}
function Stop-OwnedPreviewDesktop($Process) {
    if (!$Process) { return }
    $owned = Get-PreviewProcess $state.DesktopProcess $desktopExe
    if (!$owned) { return }
    [void]$owned.CloseMainWindow()
    if (!$owned.WaitForExit(8000)) {
        throw 'احفظ العمل وأغلق نافذة Petra التجريبية ثم أعد المحاولة. / Desktop has not closed; save pending edits and close its window. API and data were preserved.'
    }
}
function Stop-OwnedPreviewApi($Process) {
    if (!$Process) { return }
    $owned = Get-PreviewProcess $state.ApiProcess $apiDll -Api
    if ($owned) {
        Stop-Process -Id $owned.Id -ErrorAction Stop
        if (!$owned.WaitForExit(8000)) { throw 'API shutdown did not complete. Process identity was preserved; retry Stop after checking the owned process.' }
    }
}
function Confirm-PreviewPrerequisites {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'يتطلب التشغيل Windows. / Windows is required.' }
    if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) {
        $installed = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($installed) {
            $candidate = $installed.Source
            $sdks = & $candidate --list-sdks
            if ($sdks -match ('^' + [regex]::Escape($version) + ' ')) { $script:dotnet = $candidate }
        }
        if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw 'حزمة .NET المثبتة في global.json غير متاحة. / Install the pinned SDK with tools/Initialize-DotNet.ps1.' }
    }
    $service = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
    Push-Location $taskRoot
    try { $actualSdk = (& $dotnet --version | Out-String).Trim() }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $version) { throw 'حزمة SDK لا تطابق global.json. / The SDK must match global.json; run tools/Initialize-DotNet.ps1.' }
    if (!$service) { throw 'SQL Server Express غير مثبت. / Install SQL Server 2022 Express; SQL Server 2025 is not required.' }
    if ($service.Status -ne 'Running') { throw 'خدمة SQL Server متوقفة. / Start SQL Server (SQLEXPRESS) from Services.' }
    try { $major = Invoke-PreviewSql 'master' "SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion'));" }
    catch { throw 'فشل اتصال SQL باستخدام هوية Windows الحالية. / SQL connectivity/authentication failed. Check SQLEXPRESS and Windows login access.' }
    if ($major -ne 16) { throw 'تتطلب المعاينة SQL Server 2022 (16). / This preview requires SQL Server major version 16.' }
    & $dotnet dev-certs https --check --trust *> (Join-Path $previewRoot 'certificate.log')
    if ($LASTEXITCODE -ne 0) { throw 'شهادة HTTPS غير موثوقة. / Run dotnet dev-certs https --trust under this Windows account, then retry. See certificate.log.' }
}

function Confirm-PreviewApiWorkspace($Demo) {
    # Credentials and tokens remain in memory. This proves normal authorization, not
    # merely an open listener or an old workspace.json file. Never log the responses.
    $login = $null; $headers = $null; $body = $null
    try {
        $body = @{userName=$state.UserName; password=(Unprotect-PreviewSecret $state.Password); clientKind='Desktop'; deviceName='Local Preview Readiness'} | ConvertTo-Json
        $login = Invoke-RestMethod ($baseUri + 'api/v1/auth/login') -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 30
        if ($login.mustChangePassword) { throw 'Credential maintenance required.' }
        $headers = @{Authorization=('Bearer ' + $login.accessToken)}
        $workspaces = Invoke-RestMethod ($baseUri + 'api/v1/me/workspaces') -Headers $headers -TimeoutSec 30
        $matches = @($workspaces.items | Where-Object { $_.workspaceId -eq $Demo.WorkspaceId -and $_.companyId -eq $Demo.CompanyId -and $_.fiscalYearId -eq $Demo.FiscalYearId -and $_.state -eq 'Active' })
        if ($matches.Count -ne 1) { throw 'Authorized annual workspace was not returned.' }
        $headers['X-Petra-Workspace'] = [string]$Demo.WorkspaceId
        # A normal financial read forces live annual schema and branch authorization;
        # a cached readiness JSON or central registry row alone cannot satisfy startup.
        Invoke-RestMethod ($baseUri + "api/v1/companies/$($Demo.CompanyId)/branches/$($Demo.BranchId)/ledger/chart/accounts") -Headers $headers -TimeoutSec 30 | Out-Null
    } catch {
        throw 'فشل تسجيل الدخول أو اختيار السنة الفعلية. / Demo login/workspace verification failed. Check administrator access and demo.log; no READY status was issued.'
    } finally {
        if ($login -and $headers) {
            try { Invoke-RestMethod ($baseUri + 'api/v1/auth/logout') -Method Post -Headers $headers -TimeoutSec 10 | Out-Null } catch { }
        }
        $body = $null; $login = $null; $headers = $null
    }
}
