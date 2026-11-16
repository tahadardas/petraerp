#requires -Version 7.0
[CmdletBinding()]param([string]$OutputDirectory = './nuget-nupkg')
$ErrorActionPreference='Stop'
$items = @(
 @{id='FastReport.OpenSource';version='2026.2.8'},
 @{id='FastReport.OpenSource.Export.PdfSimple';version='2026.2.8'},
 @{id='ClosedXML';version='0.105.1'}
)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results=@()
foreach($item in $items){
 $name=$item.id.ToLowerInvariant(); $version=$item.version.ToLowerInvariant()
 $file="$name.$version.nupkg"
 $url="https://api.nuget.org/v3-flatcontainer/$name/$version/$file"
 $dest=Join-Path $OutputDirectory $file
 Write-Host "Downloading $($item.id) $($item.version) from NuGet.org ..."
 Invoke-WebRequest -Uri $url -OutFile $dest -MaximumRedirection 5 -TimeoutSec 120
 if((Get-Item $dest).Length -lt 1000){ throw "Unexpected small package: $dest" }
 $results+= [pscustomobject]@{id=$item.id;version=$item.version;file=$file;bytes=(Get-Item $dest).Length;sha256=(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash;url=$url}
}
$results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'DOWNLOAD_MANIFEST.json') -Encoding UTF8
Write-Host 'Core three NuGet packages downloaded. Transitive dependencies still require dotnet restore; use Install script with -CreateOfflineFeed.'
