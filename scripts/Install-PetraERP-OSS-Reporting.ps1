#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PetraRoot,
    [switch]$SkipBuild,
    [switch]$CreateOfflineFeed,
    [switch]$ForceReplaceAddon
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $PetraRoot).Path
$pack = Split-Path -Parent $PSScriptRoot
$src = Join-Path $pack 'addons/PetraERP.Reporting.OpenSource'
$dst = Join-Path $root 'src/PetraERP.Reporting.OpenSource'
$props = Join-Path $root 'Directory.Packages.props'
$sln = Join-Path $root 'PetraERP.slnx'
if (-not (Test-Path -LiteralPath $props -PathType Leaf) -or -not (Test-Path -LiteralPath $sln -PathType Leaf)) { throw 'PetraERP root is invalid: missing Directory.Packages.props or PetraERP.slnx.' }
if (-not (Test-Path -LiteralPath $src -PathType Container)) { throw 'OSS addon source missing from package.' }
$dotnet = Get-Command dotnet -ErrorAction Stop
Write-Host 'dotnet SDK:'
& $dotnet.Source --version
if ($LASTEXITCODE -ne 0) { throw '.NET SDK unavailable.' }
$backupDir = Join-Path $root ('artifacts/oss-reporting-backup-'+ (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
Copy-Item -LiteralPath $props -Destination (Join-Path $backupDir 'Directory.Packages.props')
Copy-Item -LiteralPath $sln -Destination (Join-Path $backupDir 'PetraERP.slnx')
if (Test-Path -LiteralPath $dst) {
    if (-not $ForceReplaceAddon) { throw "Addon folder already exists: $dst. Inspect it or rerun explicitly with -ForceReplaceAddon." }
    Copy-Item -LiteralPath $dst -Destination (Join-Path $backupDir 'ExistingAddon') -Recurse
    Remove-Item -LiteralPath $dst -Recurse -Force
}
Copy-Item -LiteralPath $src -Destination $dst -Recurse
# Merge NuGet Central Package Management declarations, never replace the project's versions.
[xml]$xml = Get-Content -LiteralPath $props -Raw -Encoding UTF8
$itemGroup = @($xml.Project.ItemGroup | Where-Object { $_ -and $_.GetAttribute('Condition') -eq '' }) | Select-Object -First 1
if (-not $itemGroup) { $itemGroup = $xml.CreateElement('ItemGroup'); [void]$xml.Project.AppendChild($itemGroup) }
$versions = [ordered]@{
  'FastReport.OpenSource'='2026.2.8';
  'FastReport.OpenSource.Export.PdfSimple'='2026.2.8';
  'ClosedXML'='0.105.1';
  'System.Drawing.Common'='10.0.13'
}
foreach ($name in $versions.Keys) {
  $matching = @($xml.SelectNodes("//*[local-name()='PackageVersion' and @Include='$name']"))
  if ($matching.Count -gt 1) { throw "Duplicate PackageVersion declarations for $name; inspect first." }
  if ($matching.Count -eq 1) {
    $old=$matching[0].GetAttribute('Version'); $desired='['+$versions[$name]+']'
    if ($old -ne $desired -and $old -ne $versions[$name]) { throw "Conflicting existing $name version $old (expected $desired). Nothing overwritten; rollback via $backupDir." }
    Write-Host "Reuse $name $old"
  } else {
    $node = $xml.CreateElement('PackageVersion')
    $node.SetAttribute('Include',$name)
    $node.SetAttribute('Version','['+$versions[$name]+']')
    [void]$itemGroup.AppendChild($node)
    Write-Host "Add $name $($versions[$name])"
  }
}
$xml.Save($props)
$project = Join-Path $dst 'PetraERP.Reporting.OpenSource.csproj'
$existing = Get-Content -LiteralPath $sln -Raw -Encoding UTF8
if ($existing -notmatch [regex]::Escape('src/PetraERP.Reporting.OpenSource/PetraERP.Reporting.OpenSource.csproj')) {
  & $dotnet.Source sln $sln add $project
  if ($LASTEXITCODE -ne 0) { throw 'Failed to add reporting project to solution; original slnx is backed up.' }
}
Write-Host 'Restore NuGet packages for addon project...'
$restoreArgs=@('restore',$project,'--force-evaluate')
if ($CreateOfflineFeed) {
  $cache = Join-Path $root 'artifacts/oss-reporting-offline/nuget-packages'
  New-Item -ItemType Directory -Force -Path $cache | Out-Null
  $restoreArgs+=@('--packages',$cache)
}
& $dotnet.Source @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed; check internet and dotnet --info. Nothing should be declared validated.' }
if ($CreateOfflineFeed) {
  $feed=Join-Path $root 'artifacts/oss-reporting-offline/feed'
  New-Item -ItemType Directory -Force -Path $feed | Out-Null
  Get-ChildItem -LiteralPath $cache -Recurse -Filter '*.nupkg' -File | ForEach-Object {
    $candidate=Join-Path $feed $_.Name
    if (-not (Test-Path $candidate)) { Copy-Item -LiteralPath $_.FullName -Destination $candidate }
  }
  $manifest=Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File | ForEach-Object {
    [pscustomobject]@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
  }
  $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path (Split-Path $feed) 'SHA256.json') -Encoding UTF8
  if (@(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File).Count -eq 0) { Write-Warning 'NuGet package archives not present in cache: make sure dependencies are in global cache and materialize an offline feed separately.' }
  Write-Host "Offline feed directory: $feed"
}
if (-not $SkipBuild) {
  & $dotnet.Source build $project -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw 'Build failed: review compiler errors, patch requires correction; no runtime verification claimed.' }
}
Write-Host 'Reporting OSS package setup completed for project references. Not yet integrated with financial API or WPF.'
Write-Host "Backup of changed config: $backupDir"
