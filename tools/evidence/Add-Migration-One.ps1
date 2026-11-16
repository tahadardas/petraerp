param([string]$Name,[string]$Context,[string]$OutputDir,[string]$Out)
Set-Location D:\PetraERP
$env:DOTNET_ROOT = "D:\PetraERP\.tools\dotnet\10.0.402"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$r = dotnet tool run dotnet-ef migrations add $Name --project src/PetraERP.Infrastructure --context $Context --output-dir $OutputDir --configuration Release 2>&1 | Select-Object -Last 40
$r | Out-File $Out -Encoding utf8
"DONE" | Out-File $Out -Append -Encoding utf8
