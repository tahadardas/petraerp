param([string]$Project,[string]$Out,[string]$Config = 'Debug')
Set-Location D:\PetraERP
$env:PATH = "D:\PetraERP\.tools\dotnet\10.0.402;" + $env:PATH
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$log = dotnet build $Project -c $Config --nologo -v q 2>&1
$errors = $log | Where-Object { $_ -match ': error ' } | Sort-Object -Unique
$warn = ($log | Where-Object { $_ -match ': warning ' } | Sort-Object -Unique | Measure-Object).Count
$errors | Out-File $Out -Encoding utf8
"WARNINGS_DISTINCT=$warn" | Out-File $Out -Append -Encoding utf8
($log | Select-Object -Last 5) | Out-File $Out -Append -Encoding utf8
"DONE" | Out-File $Out -Append -Encoding utf8
