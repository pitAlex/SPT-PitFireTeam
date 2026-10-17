param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka',
    [string]$NativeRoot = 'F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp'
)
$ErrorActionPreference = 'Stop'
$production = foreach ($relative in @('client/Modules/FollowerHazardAvoidance.cs','client/Patches/FollowerHazardMovementPatch.cs',
    'client/Modules/FollowerWireNavigation.cs','client/BigBrain/FollowerWireLayer.cs','client/BigBrain/Actions/FollowerWireMoveAction.cs',
    'client/Patches/FollowerBarbedWireContactPatch.cs')) {
    [regex]::Replace((Get-Content -Raw (Join-Path $RepositoryRoot $relative)), '(?m)^using [^\r\n]+\r?\n', '')
}
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerHazardFixture.cs')).Replace('/* PRODUCTION */', ($production -join "`n"))
$usable = [regex]::Match((Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')), '(?ms)^        public bool IsCoverUsable\(.*?^        \}').Value
if (!$usable) { throw 'Core cover eligibility boundary missing' }
$fixture = $fixture.Replace('/* USABLE METHOD */', $usable)
Add-Type -TypeDefinition $fixture -Language CSharp
$count = [FollowerHazardChecks]::Run()
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains('new FollowerHazardMovementPatch().Enable();')) { throw 'Movement guard not installed' }
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains('new FollowerBarbedWireContactPatch().Enable();')) { throw 'Late native wire registration not installed' }
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerPatrolLayer.cs')).Contains('AddCustomLayer(typeof(FollowerWireLayer), brains, FollowerWireLayer.LayerPriority)')) { throw 'Shared wire response layer missing' }
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')).Contains('FollowerHazardAvoidance.Reset();')) { throw 'Missing raid cleanup' }
if (Test-Path (Join-Path $RepositoryRoot 'client/Patches/FollowerTrapImmunityPatch.cs')) { throw 'Unbuilt trap exemption workaround must be removed' }
$emitter = Get-Content -Raw (Join-Path $NativeRoot 'EFT/GameTriggers/TriggersEmitter.cs')
if ($emitter -notmatch '(?s)SetTriggerState\(string triggerId, bool state\).*?_ignoreTriggers.Remove\(triggerId\).*?_ignoreTriggers.Add\(triggerId\)') { throw 'Native enable/disable contract changed' }
$nativeMotion = Get-Content -Raw (Join-Path $NativeRoot 'EFT/MovementContext.cs')
if ($nativeMotion -notmatch '(?s)DirectApplyMotion\(Vector3 motion, float deltaTime\).*?CharacterController.Move\(motion \+ this.PlatformMotion, deltaTime\)') { throw 'Native physical movement boundary changed' }
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
foreach ($path in @((Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'), (Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($path)
    try {
        $context = $module.GetType('EFT.MovementContext')
        $wire = $module.GetType('EFT.Interactive.BarbedWire')
        if (@($wire.Methods | Where-Object { $_.Name -eq 'AddPenalty' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.IObserverToPlayerBridge' }).Count -ne 1) { throw "Native barbed-wire contact changed: $path" }
        $motion = $context.Methods | Where-Object Name -eq 'DirectApplyMotion'
        if (!$motion.IsVirtual -or $motion.Parameters.Count -ne 2 -or $motion.Parameters[0].ParameterType.FullName -ne 'UnityEngine.Vector3' -or
            $motion.Parameters[1].ParameterType.FullName -ne 'System.Single' -or $motion.ReturnType.FullName -ne 'System.Void' -or
            $context.Fields.Name -notcontains '_player') { throw "Physical movement boundary missing: $path" }
        foreach ($pair in @(
            @('EFT.GameTriggers.TriggerZone', @('_triggerId')),
            @('EFT.GameTriggers.HandlerTriggerState', @('_targetState','_controlledTriggerId')),
            @('EFT.GameTriggers.HandlerDamage', @('_triggerIdStartDamage')),
            @('EFT.GameTriggers.TriggersEmitter', @('_ignoreTriggers')))) {
            $type = $module.GetType($pair[0])
            foreach ($field in $pair[1]) { if ($type.Fields.Name -notcontains $field) { throw "Native field missing: $($pair[0]).$field" } }
        }
        Write-Output "Verified native movement and trap-state metadata: $path"
    } finally { $module.Dispose() }
}
Write-Output "Passed $count production hazard discovery, geometry, state and movement checks. Live edge stopping/resume remains unqualified."
