param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'QuestCompanionPreview') -c Debug --no-restore --verbosity quiet "-p:GameRoot=$GameRoot"
if($LASTEXITCODE -ne 0){throw 'Quest preview fixture failed'}
$client=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/RaidStartPatch.cs')
$preview=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/QuestCompanionPreview.cs')
if(!$client.Contains('QuestCompanionPreview.RolesFor(profile, settings)') -or
   !$client.Contains('QuestCompanionPreview.RolesFor(_profile, _settings)') -or
   !$client.Contains('QuestCompanionPreview.RolesFor(session.Profile, raidSettings)') -or
   $preview.Contains('MainMenuControllerPatch.GroupPlayers.Add(')){throw 'Preview and spawn must share admission without selecting a synthetic member'}
if(!$client.Contains('QuestCompanionPreview.Remove();') -or !$client.Contains('contextInteractions = null')){throw 'Automatic preview needs close cleanup and no roster context commands'}
if(!$client.Contains('if (raidSettings_0.Local)') -or !$client.Contains('lifetime.AddDisposable<MatchMakerGroupPreview>(groupPreview);') -or
   !$client.Contains('if (QuestCompanionPreview.IsPreview(player?.AccountId)) return;')){throw 'Local confirmation previews need screen-owned cleanup and cannot become selected members'}
$guard=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/SyntheticTeammateRaidGuard.cs')
$plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
if(!$guard.Contains('nameof(TarkovApplication.OnReadyToStartMatchingAsync)') -or !$plugin.Contains('new TarkovApplicationLocalRaidStartPatch().Enable();') -or $client.Contains('TarkovApplicationOnlineFallbackPatch')){throw 'Companion launch must guard the native local/network selection, not manually redirect a network request'}
if(!$client.Contains('SyntheticTeammateRaidGuard.KeepLocal(_profile, _settings, _offlineSettings);')){throw 'Confirmation must normalize both native settings copies before preparing kits'}
$added=$client.Substring($client.IndexOf('internal static void MemberAdded('),$client.IndexOf('internal static void GroupRemoved(')-$client.IndexOf('internal static void MemberAdded('))
if(!$added.Contains('CompanionRaidPreparation.InvalidateQuest();') -or
   $added.IndexOf('InvalidateQuest();') -gt $added.IndexOf('if (_screen != null')){throw 'Invitations must invalidate automatic Knight even outside confirmation'}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
foreach($directory in @($resolver.GetSearchDirectories())){$resolver.RemoveSearchDirectory($directory)}
foreach($directory in @((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed'),(Join-Path $GameRoot 'BepInEx/core'),(Join-Path $RepositoryRoot 'client/bin/Debug/net472'))){$resolver.AddSearchDirectory($directory)}
$parameters=[Mono.Cecil.ReaderParameters]::new();$parameters.AssemblyResolver=$resolver
$native=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try{
    $ui=@($native.GetType('EFT.UI.UIInputNode').Fields|Where-Object {$_.Name -eq 'UI' -and $_.FieldType.FullName -eq 'EFT.UI.UIParent'})
    if($ui.Count -ne 1){throw 'Installed native confirmation UI lifetime changed'}
    $app=$native.GetType('EFT.TarkovApplication')
    $ready=@($app.Methods|Where-Object {$_.Name -eq 'OnReadyToStartMatchingAsync' -and !$_.HasParameters})
    if($ready.Count -ne 1){throw 'Native local/network launch boundary changed'}
    $readyBody=($app.NestedTypes|Where-Object Name -eq 'CG_OnReadyToStartMatchingAsync').Methods|Where-Object Name -eq 'MoveNext'
    $calls=@($readyBody.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    foreach($name in @('get_Local','LocalGameMatching','NetworkGameMatching')){if($name -notin $calls){throw "Native Ready branch changed: $name"}}
    $create=($app.NestedTypes|Where-Object Name -eq 'CG_LocalGameCreate').Methods|Where-Object Name -eq 'MoveNext'
    if(!@($create.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SendRaidSettings'}).Count){throw 'Native local raid must still publish settings through the existing spawn-selection hook'}
}finally{$native.Dispose()}
$core=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot 'client/bin/Debug/net472/pitFireTeam.dll'),$parameters)
try{
    $count=0
    $types=@('pitTeam.Modules.QuestCompanionPreview','pitTeam.Patches.SyntheticTeammateRaidGuard','pitTeam.Patches.TarkovApplicationLocalRaidStartPatch')
    foreach($name in $types){
      $type=$core.GetType($name)
      if(!$type){throw "Missing production local-companion boundary: $name"}
      $pending=[Collections.Generic.Stack[Mono.Cecil.TypeDefinition]]::new()
      $pending.Push($type)
      while($pending.Count){
       $current=$pending.Pop()
       foreach($nested in $current.NestedTypes){$pending.Push($nested)}
       foreach($method in $current.Methods){
        if(!$method.HasBody){continue}
        foreach($instruction in $method.Body.Instructions){
            $member=$instruction.Operand
            if(($member -isnot [Mono.Cecil.MethodReference] -and $member -isnot [Mono.Cecil.FieldReference]) -or
               $member.DeclaringType.GetElementType().Scope.Name -ne 'Assembly-CSharp'){continue}
            $resolved=$member.Resolve()
            if(!$resolved -or [IO.Path]::GetFullPath($resolved.Module.FileName) -ne [IO.Path]::GetFullPath((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))){throw "Unresolved installed preview API: $member"}
            $count++
        }
    }
    }
    }
    if($count -eq 0){throw 'No native preview APIs checked'}
    "PASS: $count production quest-preview/local-launch native API uses resolve against installed EFT; installed local/network selector, native spawn-settings publication, shared selection and visual cleanup guards pass. Unity rendering/raid startup remain unqualified."
}finally{$core.Dispose();$resolver.Dispose()}
