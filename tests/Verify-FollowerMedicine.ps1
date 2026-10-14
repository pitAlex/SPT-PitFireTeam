param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$EftSource = 'F:\Projects\SPT-Tarkov\SPT-4.1.3\CLIENT-4.1.3\Assembly-CSharp'
)
$ErrorActionPreference = 'Stop'
function Read-NativeMember([string]$Source, [string]$Name) {
    $match = [regex]::Match($Source, '(?m)^\s*public (?:virtual )?(?:void|bool) ' + [regex]::Escape($Name) + '(?:\(|\s*\r?\n)')
    if (!$match.Success) { throw "Missing native member $Name" }
    $start = $Source.IndexOf('{', $match.Index)
    $depth = 1
    $end = $start + 1
    while ($depth -gt 0 -and $end -lt $Source.Length) {
        if ($Source[$end] -eq '{') { $depth++ }
        if ($Source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unclosed native member $Name" }
    return $Source.Substring($match.Index, $end - $match.Index)
}
$abstract = Get-Content -Raw (Join-Path $EftSource 'BotAbstractMeds.cs')
$firstAid = Get-Content -Raw (Join-Path $EftSource 'BotFirstAid.cs')
$surgery = Get-Content -Raw (Join-Path $EftSource 'BotSurgicalKit.cs')
$player = Get-Content -Raw (Join-Path $EftSource 'EFT/Player.cs')
$operationStart = $player.IndexOf('class MedsInHandsOperation')
if ($operationStart -lt 0) { throw 'Missing native medical operation' }
$patches = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerMedicalPatch.cs')
$patches = [regex]::Replace($patches, '(?m)^using [^\r\n]+\r?\n', '')
$code = Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerMedicineFixture.cs')
$code = $code.Replace('__PATCHES__', $patches).
    Replace('__CANCEL__', (Read-NativeMember $abstract 'CancelCurrent')).
    Replace('__USING__', (Read-NativeMember $abstract 'Using')).
    Replace('__FIRST_AID__', (Read-NativeMember $firstAid 'FindDamagedPart')).
    Replace('__SURGERY__', (Read-NativeMember $surgery 'FindDamagedPart')).
    Replace('__HIDE__', (Read-NativeMember $player.Substring($operationStart) 'HideWeapon'))
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
foreach ($patch in @('FollowerMedicalCancelPatch', 'FollowerFirstAidMinimumHealthPatch', 'FollowerSurgeryTargetPatch')) {
    if (!$plugin.Contains("new $patch().Enable();")) { throw "Unregistered medical patch $patch" }
}
$medical = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
if ($medical -match 'FirstAidMinVisibleNormalizedHealth|GetFirstAidVisibleHealth') { throw 'Obsolete artificial first-aid health floor remains' }
Add-Type -TypeDefinition $code -Language CSharp
$count = [FollowerMedicineChecks]::Run()
Write-Output "Passed $count follower medicine checks using production hooks and native EFT selection/cancellation/hide methods. Unity hands transitions still require raid validation."
