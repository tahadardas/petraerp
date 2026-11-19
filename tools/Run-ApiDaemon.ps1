# Dedicated daemon runner for PetraERP API server
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = "D:\PetraERP"
$previewRoot = Join-Path $taskRoot '.artifacts/local-preview'
$statePath = Join-Path $previewRoot 'state.json'
$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
$apiDirectory = Join-Path $previewRoot 'api'
$apiDll = Join-Path $apiDirectory 'PetraERP.Api.dll'
$dotnet = "D:\PetraERP\.tools\dotnet\10.0.402\dotnet.exe"

. (Join-Path $taskRoot 'tools/LocalPreview.Runtime.ps1')

$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'https://localhost:17483'
$env:ConnectionStrings__PetraERP = "Server=.\SQLEXPRESS;Database=$($state.Database);Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=PetraERP Local Preview"
$env:Petra__Database__CompatibilityLevel = '160'
$env:Petra__Security__Tokens__SigningKey = (Unprotect-PreviewSecret $state.SigningKey)
$env:Petra__Security__Tokens__Issuer = "PetraERP.LocalPreview.$($state.InstallationId)"
$env:Petra__Security__Tokens__Audience = 'PetraERP.LocalPreview.Clients'
$env:Petra__Api__RequireHttps = 'true'
$env:Petra__Api__ExposeOpenApi = 'true'
$env:AllowedHosts = 'localhost'

Set-Location $apiDirectory
Write-Host "Starting PetraERP API on https://localhost:17483..."
& $dotnet $apiDll
