#requires -Version 7.0
[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$PetraRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PetraRoot).Path
$project=Join-Path $root 'src/PetraERP.Reporting.OpenSource/PetraERP.Reporting.OpenSource.csproj'
if(-not(Test-Path $project)){ throw "Not installed: $project" }
Write-Host 'Checking NuGet restore and compilation (does not verify Arabic PDF output or WPF UI).'
dotnet restore $project
if($LASTEXITCODE -ne 0){ throw 'Restore failed' }
dotnet build $project -c Release --no-restore
if($LASTEXITCODE -ne 0){ throw 'Build failed' }
Write-Host 'Addon builds. End-to-end report generation, database accuracy, and WPF integration require separate tests.'
