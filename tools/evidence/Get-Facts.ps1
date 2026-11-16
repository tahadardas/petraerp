Set-Location D:\PetraERP
$sql = & sqlcmd -S '.\SQLEXPRESS' -E -C -Q 'SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY(''ProductVersion'') AS varchar(40)) + '' | '' + CAST(SERVERPROPERTY(''Edition'') AS varchar(80))' -h -1 -W 2>&1
$dotnet = & '.\.tools\dotnet\10.0.402\dotnet.exe' --version
$srcZip = Get-ChildItem .artifacts -Filter *.zip | ForEach-Object { "$($_.Name) sha256=$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)" }
$csCount = (Get-ChildItem src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | Measure-Object).Count
$testCount = (Get-ChildItem tests -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | Measure-Object).Count
[ordered]@{
  head = (git rev-parse HEAD); lastCheckpointTag = (git rev-parse refs/tags/checkpoint-fix-20261028-191422-p06)
  dirtyFiles = (git status --short | Measure-Object).Count
  sqlServer = ($sql -join ' '); dotnet = $dotnet
  zips = $srcZip; srcCsFiles = $csCount; testCsFiles = $testCount
  srcProjects = ((Get-ChildItem src -Directory).Name -join ',')
  testProjects = ((Get-ChildItem tests -Directory).Name -join ',')
} | ConvertTo-Json | Out-File -Encoding utf8 .artifacts\core00_facts.json
Get-Content .artifacts\core00_facts.json
