#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskTools = Join-Path $taskRoot '.tools'
$taskDestination = Join-Path $taskTools "dotnet/$taskVersion"
$taskArchive = Join-Path $taskTools "dotnet-sdk-$taskVersion-win-x64.zip"

if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'This bootstrap supports Windows x64 only.'
}
if ($taskVersion -notmatch '^10\.0\.\d+$') {
    throw 'A stable, pinned .NET 10 SDK is required.'
}
foreach ($taskPath in @($taskTools, (Join-Path $taskTools 'dotnet'), $taskDestination, $taskArchive)) {
    if (Test-Path -LiteralPath $taskPath) {
        if ((Get-Item -LiteralPath $taskPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Linked SDK installation paths are not accepted.'
        }
    }
}
if (Test-Path -LiteralPath $taskDestination) {
    throw "SDK destination already exists. Inspect it before retrying: $taskDestination"
}

$taskMetadataUri = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$taskMetadata = Invoke-RestMethod -Uri $taskMetadataUri -TimeoutSec 60
$taskCandidates = @(
    foreach ($taskRelease in $taskMetadata.releases) {
        foreach ($taskSdk in $taskRelease.sdks) {
            if ($taskSdk.version -eq $taskVersion) {
                $taskSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' }
            }
        }
    }
)
if ($taskCandidates.Count -ne 1) {
    throw 'The official release metadata did not identify exactly one pinned SDK archive.'
}
$taskDownload = $taskCandidates[0]
$taskUri = [Uri]$taskDownload.url
if ($taskUri.Scheme -ne 'https' -or $taskUri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com')) {
    throw 'The SDK URL is not on an approved Microsoft download host.'
}
if ($taskDownload.hash -notmatch '^[0-9A-Fa-f]{128}$') {
    throw 'The official SDK SHA512 is missing or malformed.'
}
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
Invoke-WebRequest -Uri $taskUri -OutFile $taskArchive -TimeoutSec 900
$taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA512).Hash
if ($taskHash -ne $taskDownload.hash) {
    throw 'SDK SHA512 verification failed. The archive was not extracted.'
}
[IO.Compression.ZipFile]::ExtractToDirectory($taskArchive, $taskDestination)
$env:DOTNET_ROOT = $taskDestination
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$taskDotnet = Join-Path $taskDestination 'dotnet.exe'
Push-Location $taskRoot
try {
    & $taskDotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'The installed SDK could not start.' }
    & $taskDotnet build (Join-Path $taskRoot 'PetraERP.slnx') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'The foundation solution build failed.' }
    Write-Output 'PASS: official SDK SHA512 verified and foundation solution built in Release.'
}
finally {
    Pop-Location
}
