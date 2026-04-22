[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [ValidateSet("sqlite", "postgres")]
    [string]$Provider = "postgres",

    [switch]$GenerateScriptOnly
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "JurisFlow.Server\JurisFlow.Server.csproj"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Database__Provider = $Provider
$env:ConnectionStrings__DefaultConnection = $ConnectionString

try {
    if ($GenerateScriptOnly) {
        $outputDir = Join-Path $root "artifacts\ef-migrations"
        if (-not (Test-Path $outputDir)) {
            New-Item -ItemType Directory -Path $outputDir | Out-Null
        }

        $outputPath = Join-Path $outputDir "ef-migrations.sql"

        $scriptArgs = @(
            "ef", "migrations", "script",
            "--project", $project,
            "--startup-project", $project,
            "--context", "JurisFlow.Server.Data.JurisFlowDbContext",
            "--output", $outputPath
        )

        if ($Provider -eq "postgres") {
            $scriptArgs += "--idempotent"
        }

        & dotnet @scriptArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet ef migrations script failed with exit code $LASTEXITCODE."
        }

        Write-Host "Generated migration script: $outputPath"
        return
    }

    dotnet ef database update `
        --project $project `
        --startup-project $project `
        --context JurisFlow.Server.Data.JurisFlowDbContext
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef database update failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    Remove-Item Env:\Database__Provider -ErrorAction SilentlyContinue
    Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
}
