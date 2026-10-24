param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
$session = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$icon = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerInsuranceRaidIconPatch.cs')
$display = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerInsuranceRaidDisplay.cs')
foreach ($source in @($session, $icon, $display)) {
    if ($source -match 'GearSwapInsuranceDiagnostics|TraceInsurance\(|DiagnosticState\(') {
        throw 'Retired per-item insurance diagnostics must not return to the runtime path.'
    }
}
foreach ($forbidden in @('RegisterInsuredItems(', 'RegisterSingleItem(', 'ClearInsuredItems(', 'InsuredItems =')) {
    if ($icon.Contains($forbidden) -or $display.Contains($forbidden)) {
        throw "Display-only shield handling must not alter native coverage: $forbidden"
    }
}
if (!$icon.Contains('FollowerInsuranceRaidDisplay.IsInsured(__instance.Item.Id)') -or
    !$icon.Contains('__instance.InsuredItemBorder?.SetActive(true)') -or
    !$icon.Contains('__instance.InsuredIcon?.SetActive(true)') -or
    !$display.Contains('currentServerId == raidServerId') -or
    !$display.Contains('GiftedIds.UnionWith(ids)') -or !$display.Contains('InsuredIds.ExceptWith(GiftedIds)')) {
    throw 'Missing exact-raid insurance shield display or final gift exclusion.'
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $type = $module.GetType('EFT.UI.DragAndDrop.GridItemView')
    $method = $type.Methods | Where-Object { $_.Name -eq 'EnableInsuranceIcons' -and $_.Parameters.Count -eq 1 }
    if (!$method -or $method.Parameters[0].Name -ne 'enable' -or $method.Parameters[0].ParameterType.FullName -ne 'System.Boolean') {
        throw 'Installed native insurance-icon argument signature changed.'
    }
    $company = $module.GetType('EFT.UI.Insurance.InsuranceCompany')
    $lookup = $company.Methods | Where-Object { $_.Name -eq 'Insured' -and $_.Parameters.Count -eq 1 }
    if (!$lookup -or $lookup.Parameters[0].ParameterType.FullName -ne 'System.String') { throw 'Installed exact-ID insurance lookup changed.' }
    Write-Output 'Installed exact-ID insurance/icon metadata, shield preservation and diagnostic-cleanup guards verified (not raid qualification).'
} finally { $module.Dispose() }
