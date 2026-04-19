Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-SupabaseCliInvocation {
    $directCommand = Get-Command "supabase" -ErrorAction SilentlyContinue
    if ($null -ne $directCommand) {
        return @{
            Command = $directCommand.Source
            Prefix = @()
        }
    }

    $npxCommand = Get-Command "npx" -ErrorAction SilentlyContinue
    if ($null -eq $npxCommand) {
        throw "Neither 'supabase' nor 'npx' is available on PATH. Install Node.js or the Supabase CLI first."
    }

    return @{
        Command = $npxCommand.Source
        Prefix = @("supabase")
    }
}

function Invoke-SupabaseCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [string]$WorkingDirectory = (Get-Location).Path
    )

    $invocation = Get-SupabaseCliInvocation
    $resolvedArgs = @($invocation.Prefix + $Arguments)

    Write-Host ("[supabase] {0} {1}" -f $invocation.Command, ($resolvedArgs -join " "))
    Push-Location $WorkingDirectory
    try {
        & $invocation.Command @resolvedArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Supabase CLI command failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Resolve-SupabaseTargetSettings {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("staging", "production")]
        [string]$EnvironmentName,

        [string]$ProjectRef,
        [string]$DbPassword,
        [string]$DbUrl
    )

    if (-not [string]::IsNullOrWhiteSpace($DbUrl)) {
        return @{
            ProjectRef = $null
            DbPassword = $null
            DbUrl = $DbUrl
        }
    }

    if ([string]::IsNullOrWhiteSpace($ProjectRef)) {
        $projectRefCandidates = if ($EnvironmentName -eq "production") {
            @("SUPABASE_CANONICAL_PRODUCTION_PROJECT_REF", "SUPABASE_PRODUCTION_PROJECT_REF")
        }
        else {
            @("SUPABASE_CANONICAL_STAGING_PROJECT_REF", "SUPABASE_STAGING_PROJECT_REF")
        }

        foreach ($candidate in $projectRefCandidates) {
            $ProjectRef = [Environment]::GetEnvironmentVariable($candidate)
            if (-not [string]::IsNullOrWhiteSpace($ProjectRef)) {
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($DbPassword)) {
        $dbPasswordCandidates = if ($EnvironmentName -eq "production") {
            @("SUPABASE_CANONICAL_PRODUCTION_DB_PASSWORD", "SUPABASE_PRODUCTION_DB_PASSWORD")
        }
        else {
            @("SUPABASE_CANONICAL_STAGING_DB_PASSWORD", "SUPABASE_STAGING_DB_PASSWORD")
        }

        foreach ($candidate in $dbPasswordCandidates) {
            $DbPassword = [Environment]::GetEnvironmentVariable($candidate)
            if (-not [string]::IsNullOrWhiteSpace($DbPassword)) {
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($ProjectRef)) {
        throw "Supabase project ref is missing. Pass -ProjectRef or set the environment-specific SUPABASE_*_PROJECT_REF variable."
    }

    if ([string]::IsNullOrWhiteSpace($DbPassword)) {
        throw "Supabase database password is missing. Pass -DbPassword or set the environment-specific SUPABASE_*_DB_PASSWORD variable."
    }

    return @{
        ProjectRef = $ProjectRef
        DbPassword = $DbPassword
        DbUrl = $null
    }
}

function Get-RepositoryRootFromScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptRoot
    )

    return (Resolve-Path (Join-Path $ScriptRoot "..")).Path
}
