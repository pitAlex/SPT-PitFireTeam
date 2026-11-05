param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-knight-quests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'QuestRuntime.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @arguments (Join-Path $RepositoryRoot 'going-rogue/shared/KnightQuestPolicy.cs') (Join-Path $RepositoryRoot 'going-rogue/client/Quests/KnightQuestRuntime.cs') (Join-Path $RepositoryRoot 'tests/KnightQuestRuntimeFixture.cs')
    if($LASTEXITCODE -ne 0){throw 'Quest fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Quest fixture failed'}
    $raid=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/RaidStartPatch.cs')
    $preview=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/QuestCompanionPreview.cs')
    if(!$raid.Contains('QuestCompanionPreview.RolesFor(profile, settings)') -or
        !$preview.Contains('!GameplayModeRuntime.IsGoingRogueActive') -or !$preview.Contains('settings?.IsPmc != true') -or
        !$preview.Contains('MainMenuControllerPatch.GroupPlayers.Count != 0') -or
        !$preview.Contains('GameplayModeAddons.CanSpawnQuestCompanion(quest.Id, settings.LocationId,')) {throw 'Missing shared Going Rogue/solo/map quest preview and spawn admission'}
    $plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'going-rogue/client/GoingRoguePlugin.cs')
    $module=Get-Content -Raw (Join-Path $RepositoryRoot 'going-rogue/server/KnightQuestModule.cs')
    if(!$preview.Contains('quest.CompletedConditions?.Contains(new MongoID(objective))') -or
       !$plugin.Contains('RegisterQuestCompanionProgressPolicy(GameplayMode.Allegiance, RogueAlliance.NeedsCompanion)') -or
       !$module.Contains('KnightQuestPolicy.NeedsCompanion(quest.QId.ToString(), location, objective => quest.CompletedConditions?.Contains(objective) == true)')){throw 'Preview and server must share current native per-objective completion admission'}
    $server=Get-Content -Raw (Join-Path $RepositoryRoot 'going-rogue/server/GoingRogueServerPlugin.cs')
    if($server.Contains('SeedForTesting: true') -or !$server.Contains('CanSpawnForQuest = KnightQuestModule.CanSpawnKnight')) {throw 'Quest test Goons must not be reseeded; native quest authorization required'}
    $companion=Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyTeammateService.QuestCompanions.cs')
    if($companion.Contains('SaveTeammate(') -or $companion.Contains('storage.Write(') -or !$companion.Contains('MergeGeneratedCompanionEquipment(')) {throw 'Quest companion must reuse raid provisioning without becoming a saved teammate'}
    Write-Output 'PASS: Going Rogue solo/quest-map spawn, no roster seeding and temporary native provisioning source guards.'
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-knight-quests-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
