param([string]$Out)
Set-Location D:\PetraERP
$env:DOTNET_ROOT = "D:\PetraERP\.tools\dotnet\10.0.402"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$a = dotnet tool run dotnet-ef migrations add CoreBranchesFifoCurrencyNumbering --project src/PetraERP.Infrastructure --context AccountingDbContext --output-dir Persistence/AccountingMigrations --configuration Release 2>&1 | Select-Object -Last 25
$b = dotnet tool run dotnet-ef migrations add CoreCentralInterCompanyBranches --project src/PetraERP.Infrastructure --context PetraDbContext --output-dir Persistence/Migrations --configuration Release 2>&1 | Select-Object -Last 25
"=== ACCOUNTING ===" | Out-File $Out -Encoding utf8
$a | Out-File $Out -Append -Encoding utf8
"=== PETRA ===" | Out-File $Out -Append -Encoding utf8
$b | Out-File $Out -Append -Encoding utf8
"DONE" | Out-File $Out -Append -Encoding utf8
