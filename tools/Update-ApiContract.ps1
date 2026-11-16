#requires -Version 5.1
[CmdletBinding()]
param([switch]$Check)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/10.0.402/dotnet.exe'
$env:DOTNET_ROOT = Split-Path -Parent $taskDotnet
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
$taskTemporary = Join-Path $taskRoot '.artifacts/api-contract'
New-Item -ItemType Directory -Path $taskTemporary -Force | Out-Null
Push-Location $taskRoot
try {
    if (-not $Check) {
        $taskPreviousUpdate = $env:PETRAERP_UPDATE_OPENAPI
        try {
            $env:PETRAERP_UPDATE_OPENAPI = '1'
            & $taskDotnet test --project tests/PetraERP.Api.Tests/PetraERP.Api.Tests.csproj -c Release --no-build -- --filter-method '*OpenApiDocumentMatchesTheCommittedContract'
            if ($LASTEXITCODE -ne 0) { throw 'OpenAPI extraction failed. Build API tests in Release first.' }
        }
        finally { $env:PETRAERP_UPDATE_OPENAPI = $taskPreviousUpdate }
    }
    $taskClientRoot = Join-Path $taskRoot 'src/PetraERP.ApiClient'
    $taskConfiguration = Get-Content (Join-Path $taskClientRoot 'nswag.json') -Raw | ConvertFrom-Json
    $taskConfiguration.documentGenerator.fromDocument.url = Join-Path $taskClientRoot 'openapi/petraerp-v1.json'
    $taskGenerated = Join-Path $taskTemporary 'PetraApiClient.g.cs'
    $taskConfiguration.codeGenerators.openApiToCSharpClient.output = $taskGenerated
    $taskConfigPath = Join-Path $taskTemporary 'nswag.json'
    $taskConfiguration | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $taskConfigPath -Encoding utf8
    & $taskDotnet tool run nswag run $taskConfigPath
    if ($LASTEXITCODE -ne 0) { throw 'NSwag generation failed.' }
    # Normalize generator-only blank-line whitespace deterministically for git diff --check.
    $taskGeneratedText = [regex]::Replace([IO.File]::ReadAllText($taskGenerated), '(?m)[ \t]+(?=\r?$)', '')
    [IO.File]::WriteAllText($taskGenerated, $taskGeneratedText.TrimEnd([char[]]"`r`n") + "`n", [Text.UTF8Encoding]::new($true))
    $taskCommitted = Join-Path $taskClientRoot 'Generated/PetraApiClient.g.cs'
    if ($Check) {
        $taskExpected = [IO.File]::ReadAllText($taskCommitted).Replace("`r`n", "`n")
        $taskActual = [IO.File]::ReadAllText($taskGenerated).Replace("`r`n", "`n")
        if ($taskExpected -cne $taskActual) { throw 'Generated API client drift. Review and run tools/Update-ApiContract.ps1.' }
        Write-Output 'PASS: generated C# client matches the saved OpenAPI contract.'
    }
    else { Copy-Item -LiteralPath $taskGenerated -Destination $taskCommitted }
}
finally { Pop-Location }
