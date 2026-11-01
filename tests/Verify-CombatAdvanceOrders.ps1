param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
$names=@('ApplyThereGesture','ApplyGoForwardPhrase','TryAcceptCombatAdvanceOrder','CountKnownAdvanceEnemies',
    'RollPickupFollowerPushAcceptance','RollPickupFollowerOrder','IsPickedUpFollower','HasActiveCombatEnemy',
    'GetFollowerLevel','GetPlayerLevel','GetEquipmentPower','GetEnemyEquipmentPower')
$methods=foreach($name in $names){
    $matches=[regex]::Matches($source,'(?ms)^        (?:private|internal|public) (?:static )?[^\r\n]*\b'+$name+'\(.*?^        \}')
    if($matches.Count -ne 1){throw "Expected one production command method: $name"}
    $matches[0].Value
}
$policies=foreach($path in @('client/Modules/CompanionCommandPolicy.cs','client/Modules/CompanionAdvanceOrderContext.cs','going-rogue/client/GoonCommandPolicy.cs','going-rogue/client/GoonCombatConfidence.cs',
    'client/Components/PickupFollowerPersonality.cs','client/BigBrain/FollowerPushRiskPolicy.cs')){
    $policy=Get-Content -Raw (Join-Path $RepositoryRoot $path)
    $policy=[regex]::Replace($policy,'(?m)^using [^\r\n]+\r?\n','')
    if($policy -match 'namespace pitTeam.BigBrain;'){$policy=$policy.Replace('namespace pitTeam.BigBrain;','namespace pitTeam.BigBrain {')+"`n}"}
    $policy
}
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'CombatAdvanceOrdersFixture.cs')).Replace('/* PRODUCTION METHODS */',($methods -join "`n")).Replace('/* PRODUCTION POLICIES */',($policies -join "`n"))
# Use the exact production random adapter; qualify fixture assignments to avoid System.Random ambiguity.
$fixture=[regex]::Replace($fixture,'(?<![.\w])Random\.value=','UnityEngine.Random.value=')
Add-Type -TypeDefinition $fixture -Language CSharp
$count=[CombatAdvanceOrdersChecks]::Run()
Write-Output "PASS: $count production There/Go Forward dispatch, boss confidence/threat, recruit and sniper checks. Unity navigation remains a raid check."
