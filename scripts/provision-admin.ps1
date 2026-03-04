param(
    [Parameter(Mandatory = $true)]
    [string]$Tenant,

    [Parameter(Mandatory = $true)]
    [string]$Email,

    [string]$Name = "Admin User",

    [string]$Password,

    [string]$ConnectionString,

    [string]$DatabaseProvider = "postgres",

    [string]$DbEncryptionEnabled,

    [string]$DbEncryptionKey
)

$ErrorActionPreference = "Stop"

function Read-Secret([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Password)) {
    $Password = Read-Secret "Admin password"
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:ConnectionStrings__DefaultConnection
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Connection string is required. Pass -ConnectionString or set ConnectionStrings__DefaultConnection."
}

$env:Database__Provider = $DatabaseProvider
$env:ConnectionStrings__DefaultConnection = $ConnectionString

if (-not [string]::IsNullOrWhiteSpace($DbEncryptionEnabled)) {
    $env:Security__DbEncryptionEnabled = $DbEncryptionEnabled
}

if (-not [string]::IsNullOrWhiteSpace($DbEncryptionKey)) {
    $env:Security__DbEncryptionKey = $DbEncryptionKey
}

$tempPasswordVar = "JURISFLOW_ADMIN_TOOL_PASSWORD"
$env:$tempPasswordVar = $Password

try {
    dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user create-admin --tenant $Tenant --email $Email --name $Name --password-env $tempPasswordVar
}
finally {
    Remove-Item "Env:$tempPasswordVar" -ErrorAction SilentlyContinue
}
