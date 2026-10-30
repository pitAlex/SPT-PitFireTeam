param([string]$GameRoot='E:/SPTushanka',[string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'CompanionRaidPreparation') -c Debug --no-restore --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Client reservation fixture failed'}
$client=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/RaidStartPatch.cs')
$loading=$client.Substring($client.IndexOf('internal class MatchmakerTimeHasComeShowPatch'),$client.IndexOf('internal class PartyInfoPanelEquipmentHealthPatch')-$client.IndexOf('internal class MatchmakerTimeHasComeShowPatch'))
if($loading.Contains('CompanionRaidPreparation.Prepare(') -or $loading.Contains('CompanionRaidPreparation.Reset(')){throw 'Loading must retain AcceptScreen equipment'}
$coordinator=$client.Substring($client.IndexOf('internal static class CompanionAcceptScreenPreparation'),$client.IndexOf('internal class CompanionAcceptScreenClosePatch')-$client.IndexOf('internal static class CompanionAcceptScreenPreparation'))
if($coordinator.IndexOf('CompanionRaidPreparation.Prepare(') -lt 0 -or $coordinator.IndexOf('CompanionRaidPreparation.Prepare(') -gt $coordinator.IndexOf('RefreshLoadedTeammateVisuals(')){throw 'AcceptScreen kits must be prepared before visual refresh'}
$accept=$client.Substring($client.IndexOf('internal class MatchMakerAcceptScreenPatch'))
if(!$accept.Contains('CompanionAcceptScreenPreparation.Begin(__instance, raidSettings, controller)') -or !$accept.Contains('CompanionAcceptScreenPreparation.Refresh(controller)') -or !$client.Contains('CompanionAcceptScreenPreparation.MemberAdded(__instance)') -or !$client.Contains('CompanionRaidPreparation.Remove(id)') -or !$client.Contains('CompanionAcceptScreenPreparation.GroupRemoved(__instance)')){throw 'AcceptScreen must own preparation and membership changes'}
if(!$coordinator.Contains('preserveVisualBindings: true') -or !$client.Contains('bound.UpdateVisualRepresentation(refreshed.PlayerVisualRepresentation)') -or !$client.Contains('bound.InfoChanged.Invoke()')){throw 'AcceptScreen must refresh existing native visual bindings'}
if($client.Contains('CompanionRaidPreparationStartPatch')){throw 'Loading-start reset would discard prepared kits'}
$fetch=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
if(!$fetch.Contains('CompanionRaidPreparation.TokenFor(aid)') -or !$fetch.Contains('CompanionRaidPreparation.Reset()')){throw 'Spawn must transmit the reservation and clear it on raid end'}
$server=Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyTeammateService.cs')
if(!$server.Contains('PreparedCompanionForSpawn(sessionId, teammate!, healthMultiplier, preparationToken)') -or
   !$server.Contains('raidData.RaidConfiguration = previousRaidConfiguration;')){throw 'Spawn must consume reserved equipment and generation must restore context'}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$native=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$acceptType=$native.GetType('EFT.UI.Matchmaker.MatchMakerAcceptScreen')
$close=@($acceptType.Methods|Where-Object {$_.Name -eq 'Close' -and $_.Parameters.Count -eq 0})
$show=@($acceptType.Methods|Where-Object {$_.Name -eq 'Show' -and $_.Parameters.Count -eq 3 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.IEftSession'})
if($close.Count -ne 1 -or $show.Count -ne 1){throw 'Installed AcceptScreen show/close targets changed'}
$raidPlayer=$native.GetType('EFT.UI.Matchmaker.RaidPlayer')
if(!@($raidPlayer.Methods|Where-Object {$_.Name -eq 'UpdateVisualRepresentation' -and $_.Parameters.Count -eq 1}).Count -or !@($raidPlayer.Fields|Where-Object Name -eq 'InfoChanged').Count){throw 'Installed bound-preview visual/info APIs changed'}
$native.Dispose()
foreach($configuration in @('Debug','Release')){
    $assembly=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot "client/bin/$configuration/net472/pitFireTeam.dll"))
    $swap=$assembly.GetType('pitTeam.Modules.TeammateGearSwap')
    $compare=@($swap.Methods|Where-Object Name -eq 'DonationDiagnosticComparison')
    if($compare.Count -ne 1 -or @($compare[0].CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'System.Diagnostics.ConditionalAttribute'}).Count -ne 0 -or
       !@($compare[0].Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'LogError'}).Count){throw "$configuration must retain ordinary bounded mismatch logging"}
    if(@($swap.Methods|Where-Object {$_.Name -match 'DebugReplay|CapturedKnight'}).Count -or $assembly.GetType('pitTeam.Modules.DebugGearReplayDescriptor')){throw "$configuration retains a temporary replay/spawn override"}
    $assembly.Dispose()
}
'PASS: AcceptScreen/membership/visual order, spawn reservation, restored generation context, installed patch metadata and Debug/Release logging/removal checks; not a live UI test.'
