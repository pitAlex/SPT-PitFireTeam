param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$NativeRoot='F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp',[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
function Read-Native([string]$source,[string]$name) {
    $match=[regex]::Match($source,'(?ms)^\s*public (?:ActiveHealthController.Effect|void|bool) '+$name+'\([^\n]*\)\r?\n\s*\{')
    if(!$match.Success){throw "Native method missing: $name"}
    $start=$source.IndexOf('{',$match.Index);$end=$start+1;$depth=1
    while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
    return $source.Substring($match.Index,$end-$match.Index)
}
$native=Get-Content -Raw (Join-Path $NativeRoot 'EFT/HealthSystem/ActiveHealthController.cs')
$stims=Get-Content -Raw (Join-Path $NativeRoot 'BotStimulators.cs')
$policy=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerStimulatorPolicy.cs')
$predicate=[regex]::Match($policy,'(?ms)^        internal static bool HasAntidoteEffect\(.*?^        \}').Value
$medical=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
$clear=[regex]::Match($medical,'(?ms)^        internal static void ClearFollowerPoison\(.*?^        \}').Value
if(!$predicate -or !$clear){throw 'Production predicate/cleanup missing'}
$patches=[regex]::Replace((Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerPoisonImmunityPatch.cs')),'(?m)^using [^\r\n]+\r?\n','')
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerPoisonImmunityFixture.cs')).Replace('/* NATIVE ORDINARY */',(Read-Native $native 'DoIntoxication')).Replace('/* NATIVE LETHAL */',(Read-Native $native 'DoLethalIntoxication')).Replace('/* NATIVE SELECTION */',(Read-Native $stims 'FindMeds')).Replace('/* ANTIDOTE PREDICATE */',$predicate).Replace('/* CLEAR POISON */',$clear).Replace('/* PRODUCTION PATCHES */',$patches)
Add-Type -TypeDefinition $fixture -Language CSharp
$count=[FollowerPoisonChecks]::Run()
$plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
foreach($name in @('FollowerPoisonImmunityPatch','FollowerLethalPoisonImmunityPatch','FollowerStimulatorSelectionPatch','FollowerStimulatorUsePatch')){if(!$plugin.Contains("new $name().Enable();")){throw "Unregistered patch $name"}}
foreach($path in @('client/BigBrain/FollowerCombatCommon.cs','client/BigBrain/FollowerPatrolLayer.cs','addon/SainMedicalDecisionBridge.cs')){
    if((Get-Content -Raw (Join-Path $RepositoryRoot $path)) -match 'NeedsAntidote|TrySelectAntidote|TryPreparePatrolAntidote'){throw "Abandoned antidote route in $path"}
}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
foreach($path in @((Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),(Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))){
    $module=[Mono.Cecil.ModuleDefinition]::ReadModule($path)
    try{
        $health=$module.GetType('EFT.HealthSystem.ActiveHealthController')
        foreach($name in @('DoIntoxication','DoLethalIntoxication')){
            $method=$health.Methods|Where-Object Name -eq $name
            if(!$method -or $method.ReturnType.FullName -ne 'EFT.HealthSystem.ActiveHealthController/Effect' -or !$method.HasBody){throw "Poison entry changed: $name"}
        }
        $activate=($health.NestedTypes|Where-Object Name -eq 'Stimulator').Methods|Where-Object Name -eq 'ActivateBuff'
        foreach($name in @('DoIntoxication','DoLethalIntoxication')){if($activate.Body.Instructions.Operand.Name -notcontains $name){throw "Native toxin no longer enters $name"}}
        $find=$module.GetType('BotStimulators').Methods|Where-Object Name -eq 'FindMeds'
        if($find.Body.Instructions.Operand.Name -notcontains 'GetAcceptableItemsNonAlloc'){throw 'Native stim cache source changed'}
        $use=$module.GetType('BotStimulators').Methods|Where-Object Name -eq 'TryApply'
        if($use.Body.Instructions.Operand.Name -notcontains 'SetInHands'){throw 'Native stim execution boundary changed'}
        Write-Output "Verified native poison entries, toxin dispatch and stim-cache boundary: $path"
    }finally{$module.Dispose()}
}
Write-Output "Passed $count follower poison immunity and native stim-selection checks. Fresh raid qualification remains required."
