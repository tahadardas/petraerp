param([string]$Pattern,[int]$Lines=60)
Set-Location D:\PetraERP
$f = git grep -l $Pattern -- '*.cs' | Select-Object -First 15
$f
