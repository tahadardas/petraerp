param([string]$Project,[string]$Out)
# Runs a SQL test project while a background job drops leaked PetraERP_SchemaTest_* databases that are idle (no sessions) and older than 3 minutes.
Set-Location D:\PetraERP
$env:PATH = "D:\PetraERP\.tools\dotnet\10.0.402;" + $env:PATH
$env:PETRAERP_SQL_TEST_SERVER = '.\SQLEXPRESS'; $env:PETRAERP_SQL_TEST_TRUST_CERTIFICATE = '1'; $env:PETRAERP_SQL_TEST_EXPECTED_MAJOR = '16'
$sql = @"
SET NOCOUNT ON; DECLARE @s nvarchar(max)=N'';
SELECT @s+=N'BEGIN TRY ALTER DATABASE ['+d.name+N'] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ['+d.name+N']; END TRY BEGIN CATCH END CATCH;'
FROM sys.databases d WHERE d.name LIKE N'PetraERP[_]SchemaTest[_]%' AND d.create_date < DATEADD(MINUTE,-3,GETDATE())
AND NOT EXISTS (SELECT 1 FROM sys.dm_exec_sessions s WHERE s.database_id=d.database_id);
EXEC(@s);
"@
$cleaner = Start-Job -ScriptBlock { param($q) while ($true) { Start-Sleep -Seconds 60; sqlcmd -S ".\SQLEXPRESS" -E -C -Q $q | Out-Null } } -ArgumentList $sql
try { dotnet test --project tests\$Project -c Release --no-build --explicit on 2>&1 | Out-File ($Out + '.full') -Encoding utf8; Get-Content ($Out + '.full') | Where-Object { $_ -match '^failed |^  (total|failed|succeeded|skipped|duration):|Failure|Expected:|Actual:|SqlException|Message' } | ForEach-Object { if ($_.Length -gt 400) { $_.Substring(0,400) } else { $_ } } | Out-File $Out -Encoding utf8 }
finally { Stop-Job $cleaner; Remove-Job $cleaner }
"DONE" | Out-File $Out -Append
