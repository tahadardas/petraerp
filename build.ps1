$env:DOTNET_ROOT='D:\PetraERP\.tools\dotnet\10.0.402'
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
Set-Location D:\PetraERP
dotnet build PetraERP.slnx -c Release -nologo -v:minimal 2>&1 | Tee-Object D:\PetraERP\build.log | Select-String 'error|Warn|Elapsed' | Select-Object -Unique
"EXIT=$LASTEXITCODE"
