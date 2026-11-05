param([string]$GameRoot='E:/SPTushanka',[string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'KnightQuestZones') -c Debug --no-restore --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Production pier-zone fixture failed'}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$native=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed'))
$parameters=[Mono.Cecil.ReaderParameters]::new();$parameters.AssemblyResolver=$resolver
$addon=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot 'going-rogue/client/bin/Debug/net472/pitFireTeam.GoingRogue.dll'),$parameters)
try {
    $trigger=$native.GetType('EFT.Interactive.TriggerWithId')
    $quest=$native.GetType('EFT.Interactive.QuestTrigger')
    if($quest.BaseType.FullName -ne $trigger.FullName -or !@($trigger.Methods|Where-Object {$_.Name -eq 'SetId' -and $_.IsPublic -and $_.Parameters.Count -eq 1}).Count){throw 'Native quest-zone ID API changed'}
    foreach($pair in @(@('TriggerEnter','AddTriggerZone'),@('TriggerExit','RemoveTriggerZone'))){
        $method=$trigger.Methods|Where-Object Name -eq $pair[0]
        if(!@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $pair[1]}).Count){throw 'Native trigger no longer maintains player zone membership'}
    }
    if(!@(($trigger.Methods|Where-Object Name -eq 'Awake').Body.Instructions|Where-Object {$_.Operand -eq 'Triggers'}).Count){throw 'Native trigger layer changed'}
    $world=$native.GetType('EFT.GameWorld')
    foreach($method in @('OnGameStarted','Dispose')){if(!@($world.Methods|Where-Object {$_.Name -eq $method -and $_.Parameters.Count -eq 0}).Count){throw "Native world hook missing: $method"}}
    $searcher=$native.GetType('TriggerColliderSearcher')
    $searchTypes=@($searcher)+@($searcher.NestedTypes)
    if(!@($searchTypes.Methods.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.TypeReference] -and $_.Operand.FullName -eq 'IPhysicsTrigger'}).Count){throw 'Native trigger search no longer discovers physics-trigger components'}
    if(!@($addon.Resources|Where-Object Name -eq 'pitFireTeam.GoingRogue.Resources.quests.FriendlyPMC_Zones.json').Count){throw 'Pier zone definition is not embedded in the deployed addon'}
    $count=0
    foreach($reference in $addon.GetMemberReferences()){
        if($reference.DeclaringType.GetElementType().Scope.Name -notin @('UnityEngine.CoreModule','UnityEngine.PhysicsModule')){continue}
        $resolved=$reference.Resolve();if(!$resolved){throw "Missing installed Unity API: $reference"}
        if(![IO.Path]::GetFullPath($resolved.Module.FileName).StartsWith([IO.Path]::GetFullPath((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed')),[StringComparison]::OrdinalIgnoreCase)){throw 'Unity API resolved outside installed game'}
        $count++
    }
    "PASS: $count installed Unity API references, native trigger entry/exit, physics discovery, world hooks and embedded zone resource verified. Native physics contacts remain live qualification."
} finally {$addon.Dispose();$native.Dispose();$resolver.Dispose()}
