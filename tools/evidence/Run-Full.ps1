param([string]$Project,[string]$Out)
Set-Location D:\PetraERP
$env:PATH = "D:\PetraERP\.tools\dotnet\10.0.402;" + $env:PATH
$env:PETRAERP_SQL_TEST_SERVER = '.\SQLEXPRESS'; $env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE = '1'; $env:PETRAERP_SQL_TEST_EXPECTED_MAJOR = '16'
dotnet test --project tests\$Project -c Release --no-build --explicit on 2>&1 | Select-Object -Last 40 | Out-File $Out -Encoding utf8
"DONE" | Out-File $Out -Append
