param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/SquadControlMenuUi.Roster.cs')
$onboarding=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/SquadControlMenuUi.Onboarding.cs')
$screen=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/SquadControlMenuUi.cs')
$backend=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/SquadControlMenuUi.Backend.cs')
$methods=foreach($name in @('DrainPortraitQueueCoroutine','LoadTeammatePortraitCoroutine','CancelPortraitQueue','RefreshRosterTile','HideSquadPanels','FindRosterTile','RosterTiles','UpdateRosterSections','CreatePinnedRosterArea','ApplyGroupBadgeVisibility')) {
    $owner=if($name -eq 'HideSquadPanels'){$onboarding}elseif($name -eq 'ApplyGroupBadgeVisibility'){$backend}else{$source}
    $method=[regex]::Match($owner,'(?ms)^        private (?:IEnumerator|IEnumerable<Transform>|Transform|void|bool) '+$name+'\(.*?^        \}').Value
    if(!$method){throw ('Missing production portrait coroutine: '+$name)}
    $method
}
$work=Join-Path $RepositoryRoot 'tests/artifacts/roster-portraits'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$first=$screen.IndexOf('            // Rebuild roster only on first open')
$last=$screen.IndexOf('        private IEnumerator RebuildAfterTransitionCoroutine', $first)
if($first -lt 0 -or $last -le $first){throw 'Roster injection rebuild decision boundary changed'}
$inject=$screen.Substring($first,$last-$first).TrimEnd()
$inject=$inject.Substring(0,$inject.LastIndexOf('}'))
$constants=[regex]::Matches($screen,'(?m)^        private const float Roster(?:TileWidth|TileHeight|TileSpacing|ViewportTopInset) = .*?;') | ForEach-Object Value
$remove=[regex]::Match($source,'(?ms)^        private void CreateRemoveButton\(.*?(?=^            GameObject buttonObject)').Value
if(!$remove){throw 'Roster remove-button admission boundary changed'}
$remove=$remove.Replace('private void CreateRemoveButton','private void ProbeRemoveButton')+"            RemoveButtons++;`n        }"
[IO.File]::WriteAllText((Join-Path $work 'Production.cs'),"using System; using System.Collections; using System.Collections.Generic; using System.Linq; using UnityEngine; using UnityEngine.UI; using PlayerIcons; using Comfort.Common; using EFT.UI;`npartial class PortraitHarness {`n"+($constants -join "`n")+"`n"+($methods -join "`n")+"`n$remove`npublic void Inject() {`n$inject`n}`n}")
dotnet run --project (Join-Path $RepositoryRoot 'tests/RosterPortraits') -c Debug
if($LASTEXITCODE -ne 0){throw 'Production roster portrait fixture failed'}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $image=$module.GetType('PlayerIcons.PlayerIconImage')
    if(!($image.Methods | Where-Object {$_.Name -eq 'SetPresetIcon' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'PlayerIconRequest'})){throw 'Native completed icon binding changed'}
    $creator=$module.GetType('EFT.PlayerIcons.PlayerIconCreator')
    foreach($field in @('_containerIsBusy','_renderQueue')){if(!($creator.Fields | Where-Object {$_.Name -eq $field -and $_.IsPublic})){throw "Native renderer diagnostic field changed: $field"}}
    if(!($creator.Methods | Where-Object {$_.Name -eq 'GetIcon' -and $_.IsPublic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'PlayerIconRequest' -and $_.ReturnType.FullName -eq 'ItemIcon'})){throw 'Native portrait renderer changed'}
    Write-Output 'PASS: installed native portrait cache/binding and passive diagnostic APIs verified.'
} finally {$module.Dispose()}
