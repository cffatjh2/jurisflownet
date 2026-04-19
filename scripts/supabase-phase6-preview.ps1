<#
.SYNOPSIS
Builds a dry-run preview report for the Phase 6 ETL and seed package without touching any database.

.DESCRIPTION
Reads the Phase 6 manifest, transform rules, id maps, and verification profiles from `supabase/etl`
and writes a Markdown preview report that shows the scenario scope, wave coverage, script files,
transform count, id/fk map count, and verification set.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-preview.ps1 -Scenario A
#>

param(
    [ValidateSet("A", "B")]
    [string]$Scenario = "A",

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$etlRoot = Join-Path $repoRoot "supabase\etl"

function Read-JsonFile([string]$Path) {
    return (Get-Content -Path $Path -Raw | ConvertFrom-Json)
}

$manifest = Read-JsonFile (Join-Path $etlRoot "manifest.phase6.json")
$transforms = Read-JsonFile (Join-Path $etlRoot "transforms.phase6.json")
$idMaps = Read-JsonFile (Join-Path $etlRoot "id-maps.phase6.json")
$verification = Read-JsonFile (Join-Path $etlRoot "verification.phase6.json")

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot ("documentation\PHASE6_PREVIEW_{0}.md" -f $Scenario)
}

$scenarioConfig = $manifest.scenarios.$Scenario
if ($null -eq $scenarioConfig) {
    throw "Scenario '$Scenario' was not found in manifest.phase6.json."
}

$selectedWaves = if ($Scenario -eq "A") { $manifest.waves } else { @() }
$scriptLines = @()
foreach ($artifact in $scenarioConfig.requiredArtifacts) {
    $fullPath = Join-Path $etlRoot $artifact
    $exists = Test-Path $fullPath
    $scriptLines += ("- `{0}` : {1}" -f $artifact, ($(if ($exists) { "ok" } else { "missing" })))
}

$waveLines = @()
foreach ($wave in $selectedWaves) {
    $transformCount = @($transforms.rules | Where-Object { $_.wave -eq $wave.id }).Count
    $verificationProfile = $verification.profiles | Where-Object { $_.id -eq $wave.verificationProfile } | Select-Object -First 1
    $waveLines += @(
        "## $($wave.id) - $($wave.name)",
        "",
        "- kaynak tablolar: $(@($wave.sourceTables).Count)",
        "- hedef tablolar: $(@($wave.targetTables).Count)",
        "- transform kurali: $transformCount",
        "- verification profili: $($wave.verificationProfile)",
        ("- script: {0}" -f $wave.script)
    )

    if ($null -ne $verificationProfile) {
        $waveLines += ""
        $waveLines += "Verification:"
        foreach ($item in $verificationProfile.rowCountChecks) { $waveLines += ("- row-count: {0}" -f $item) }
        foreach ($item in $verificationProfile.orphanChecks) { $waveLines += ("- orphan-check: {0}" -f $item) }
        foreach ($item in $verificationProfile.sampleChecks) { $waveLines += "- sample-check: $item" }
    }

    $waveLines += ""
}

$content = @(
    "# Phase 6 Preview Report",
    "",
    ("- scenario: {0}" -f $Scenario),
    "- scenario-name: $($scenarioConfig.name)",
    "- aciklama: $($scenarioConfig.description)",
    "- toplam transform kurali: $(@($transforms.rules).Count)",
    "- toplam id-map tanimi: $(@($idMaps.mapTables).Count)",
    "- toplam fk-rewire tanimi: $(@($idMaps.foreignKeyRewires).Count)",
    "- toplam verification profili: $(@($verification.profiles).Count)",
    "",
    "## Gerekli Artefaktlar",
    ""
)

$content += $scriptLines
$content += @(
    "",
    "## ID Map Seti",
    ""
)

foreach ($map in $idMaps.mapTables) {
    $content += ("- {0} : {1} -> {2}" -f $map.mapKey, $map.sourceTable, $map.targetTable)
}

$content += @(
    "",
    "## FK Rewire Seti",
    ""
)

foreach ($fk in $idMaps.foreignKeyRewires) {
    $content += ("- {0}.{1} <- {2}" -f $fk.entity, $fk.targetField, $fk.usesMapKey)
}

if ($Scenario -eq "A") {
    $content += @("", "## Wave Ozeti", "")
    $content += $waveLines
}
else {
    $content += @(
        "",
        "## Scenario B Ciktilari",
        "",
        "- minimal seed template hazir",
        "- tenant/admin/office/foundational role assignment onboarding akisi tanimli",
        "- execution yok; sadece preview ve parameterized SQL template var"
    )
}

$content | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host ("Phase 6 preview report written to {0}" -f $OutputPath)
