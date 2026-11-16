Set-Location D:\PetraERP
$env:PATH = "D:\PetraERP\.tools\dotnet\10.0.402;" + $env:PATH
dotnet build tests\PetraERP.Domain.Tests -c Release --no-restore 2>&1 | Select-Object -Last 8
dotnet test --project tests\PetraERP.Domain.Tests -c Release --no-build --filter-class "*DocumentLifecycleTests" 2>&1 | Select-Object -Last 15
