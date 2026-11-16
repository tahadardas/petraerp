param([string]$Project,[string]$Out)
Set-Location D:\PetraERP
$env:PATH = "D:\PetraERP\.tools\dotnet\10.0.402;" + $env:PATH
$env:PETRAERP_SQL_TEST_SERVER = '.\SQLEXPRESS'; $env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE = '1'; $env:PETRAERP_SQL_TEST_EXPECTED_MAJOR = '16'
dotnet build tests\$Project -c Release --no-restore 2>&1 | Select-String -Pattern "error|Error\(s\)|Warning\(s\)" | Select-Object -Last 8 | Out-File $Out -Encoding utf8
dotnet test --project tests\$Project -c Release --no-build --explicit on --filter-class "*Sales*" --filter-class "*Purchase*" --filter-class "*Posted*" 2>&1 | Select-Object -Last 25 | Out-File $Out -Append -Encoding utf8
"DONE" | Out-File $Out -Append
