#requires -Version 7.0
[CmdletBinding()]
param([string]$Feed = (Join-Path $PSScriptRoot '../artifacts/oss-reporting-offline/feed'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskFeed = (Resolve-Path -LiteralPath $Feed).Path
$taskOutput = Join-Path $PSScriptRoot '../docs/reporting/OSS_DEPENDENCY_EVIDENCE.json'
$taskRows = foreach ($taskPackage in Get-ChildItem -LiteralPath $taskFeed -Filter '*.nupkg' -File | Sort-Object Name) {
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackage.FullName)
    try {
        $taskManifest = @($taskArchive.Entries | Where-Object FullName -like '*.nuspec')
        if ($taskManifest.Count -ne 1) { throw "Ambiguous NuGet metadata: $($taskPackage.Name)" }
        $taskReader = [IO.StreamReader]::new($taskManifest[0].Open())
        try { [xml]$taskXml = $taskReader.ReadToEnd() } finally { $taskReader.Dispose() }
        $taskMetadata = $taskXml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $taskLicense = $taskMetadata.SelectSingleNode('*[local-name()="license"]')
        $taskLicenseUrl = $taskMetadata.SelectSingleNode('*[local-name()="licenseUrl"]')
        $taskProjectUrl = $taskMetadata.SelectSingleNode('*[local-name()="projectUrl"]')
        $taskLicenseHash = $null
        if ($taskLicense -and $taskLicense.GetAttribute('type') -eq 'file') {
            $taskLicenseEntry = $taskArchive.GetEntry($taskLicense.InnerText)
            if (-not $taskLicenseEntry) { throw "Missing declared license file: $($taskPackage.Name)" }
            $taskStream = $taskLicenseEntry.Open()
            try { $taskLicenseHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream)) }
            finally { $taskStream.Dispose() }
        }
        [ordered]@{
            id = $taskMetadata.SelectSingleNode('*[local-name()="id"]').InnerText
            version = $taskMetadata.SelectSingleNode('*[local-name()="version"]').InnerText
            archive = $taskPackage.Name
            sha256 = (Get-FileHash -LiteralPath $taskPackage.FullName -Algorithm SHA256).Hash
            licenseType = $(if ($taskLicense) { $taskLicense.GetAttribute('type') } else { 'legacy-url' })
            license = $(if ($taskLicense) { $taskLicense.InnerText } elseif ($taskLicenseUrl) { $taskLicenseUrl.InnerText } else { 'MISSING' })
            licenseFileSha256 = $taskLicenseHash
            projectUrl = $(if ($taskProjectUrl) { $taskProjectUrl.InnerText } else { $null })
        }
    } finally { $taskArchive.Dispose() }
}
if (@($taskRows).Count -eq 0) { throw 'No restored packages found.' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskOutput) | Out-Null
[ordered]@{
    evidenceDate = '2026-10-22'
    source = 'Restored NuGet package archives; declared licenses, not an assertion about unrelated commercial products.'
    packages = @($taskRows)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskOutput -Encoding utf8
Write-Output "Recorded $(@($taskRows).Count) package hashes and declared licenses."
