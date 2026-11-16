# Petra ERP / AlAmeen10 reference downloader.
# Optional. No application source or database changes.
$ErrorActionPreference = 'Stop'
$folder = Split-Path -Parent $MyInvocation.MyCommand.Path
$listPath = Join-Path $folder 'ALAMEEN10_OFFICIAL_IMAGE_LINKS.json'
$imageFolder = Join-Path $folder 'images'
if (!(Test-Path -LiteralPath $imageFolder)) { New-Item -ItemType Directory -Path $imageFolder | Out-Null }
$refs = @(Get-Content -Raw -Encoding UTF8 -LiteralPath $listPath | ConvertFrom-Json)
$log = New-Object System.Collections.Generic.List[string]
foreach ($r in $refs) {
  $id = [string]$r.id
  $uri = [string]$r.image
  $localFile = [IO.Path]::GetFileName([string]$r.local_file)
  $target = Join-Path $imageFolder $localFile
  if ($uri -notmatch '^https://(s3\.alameensoft\.com|docs\.alameensoft\.com)/') {
    $log.Add("REJECTED $id : domain not official")
    continue
  }
  try {
    Invoke-WebRequest -Uri $uri -OutFile $target -TimeoutSec 45 -ErrorAction Stop
    $bytes = [IO.File]::ReadAllBytes($target)
    $validPng = $bytes.Length -ge 8 -and $bytes[0] -eq 137 -and $bytes[1] -eq 80 -and $bytes[2] -eq 78 -and $bytes[3] -eq 71
    $validJpg = $bytes.Length -ge 3 -and $bytes[0] -eq 255 -and $bytes[1] -eq 216 -and $bytes[2] -eq 255
    if (!$validPng -and !$validJpg) {
      Remove-Item $target -Force
      $log.Add("INVALID_IMAGE $id : invalid binary signature")
    } else {
      $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash
      $log.Add("OK $id : $localFile [$($bytes.Length) bytes] SHA256=$hash")
    }
  } catch {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    $log.Add("ERROR $id : $($_.Exception.Message)")
  }
}
$log | Set-Content -LiteralPath (Join-Path $folder 'IMAGE_DOWNLOAD_RESULTS.txt') -Encoding UTF8
$log | ForEach-Object { Write-Host $_ }
Write-Host "Reference download completed. No Petra ERP application files were changed."
