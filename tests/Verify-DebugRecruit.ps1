param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$script:assertions = 0
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:assertions++
}
function AllTypes($types) {
    foreach ($type in $types) {
        $type
        AllTypes $type.NestedTypes
    }
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
foreach ($configuration in @('Debug', 'Release')) {
    $debug = $configuration -eq 'Debug'
    $client = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot "client/bin/$configuration/net472/pitFireTeam.dll"))
    $server = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot "server/bin/$configuration/net10.0/pitFireTeam.Server.dll"))
    try {
        $plugin = $client.GetType('pitTeam.pitFireTeam')
        foreach ($method in @('SpawnFollowerConsoleCommand', 'SpawnScavFollowerConsoleCommand',
                'SpawnRecruitConsoleCommandAsync', 'ResolveDebugRecruitSide')) {
            Check ((@($plugin.Methods | Where-Object Name -eq $method).Count -gt 0) -eq $debug) "$configuration console handler scope: $method"
        }
        $registration = $plugin.Methods | Where-Object Name -eq 'RegisterDebugConsoleCommands'
        $strings = @($registration.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldstr' } | ForEach-Object Operand)
        Check (($strings.Contains('fs_spawnfollower')) -eq $debug) "$configuration must register fs_spawnfollower only with DEBUG"
        Check (($strings.Contains('fs_spawnscavfollower')) -eq $debug) "$configuration must register fs_spawnscavfollower only with DEBUG"
        Check ($strings.Contains('fs_spawnscav')) "$configuration leaves the unrelated fs_spawnscav command unchanged"
        $spawner = $client.GetType('pitTeam.Patches.BotsControllerPatch')
        Check ((@($spawner.Methods | Where-Object Name -eq 'SpawnDebugFollower').Count -gt 0) -eq $debug) "$configuration spawn entry point scope"
        Check ((@($spawner.Methods | Where-Object Name -eq 'TryPrepareDebugRecruitProfile').Count -gt 0) -eq $debug) "$configuration SPT profile preparation scope"
        Check (@($spawner.Methods | Where-Object Name -eq 'PrepareDebugScavGear').Count -eq 0) "$configuration special Scav kit is removed"
        foreach ($method in @('SpawnRecruitConsoleCommandAsync', 'SpawnDebugFollower')) {
            $states = @(AllTypes $client.Types | Where-Object { $_.Name.StartsWith('<' + $method + '>') })
            Check (($states.Count -gt 0) -eq $debug) "$configuration generated async spawn body follows DEBUG scope: $method"
        }
        $friendship = $client.GetType('pitTeam.Modules.AllegiancePmcFriendship')
        Check ((@($friendship.Methods | Where-Object Name -eq 'RegisterDebugRecruit').Count -gt 0) -eq $debug) "$configuration selected-friendly test registration scope"
        $follower = $client.GetType('pitTeam.Components.BotFollowerPlayer')
        Check ((@($follower.Fields | Where-Object Name -eq 'DebugForceRecruitInvite').Count -gt 0) -eq $debug) "$configuration follower test marker scope"
        $clientRequest = $client.GetType('pitTeam.Modules.RecruitPickupCandidateRequest')
        Check ((@($clientRequest.Properties | Where-Object Name -eq 'DebugForceInvite').Count -gt 0) -eq $debug) "$configuration client request marker scope"
        $serverRequest = $server.GetType('pitTeam.Server.Models.FriendlyRecruitPickupCandidate')
        Check ((@($serverRequest.Properties | Where-Object Name -eq 'DebugForceInvite').Count -gt 0) -eq $debug) "$configuration server request marker scope"
        $recruits = $server.GetType('pitTeam.Server.Services.FriendlyRecruitService')
        foreach ($method in @('IsDebugForcedInvite', 'PreferDebugRecruitCandidates')) {
            Check ((@($recruits.Methods | Where-Object Name -eq $method).Count -gt 0) -eq $debug) "$configuration server test policy scope: $method"
        }
    } finally { $client.Dispose(); $server.Dispose() }
}
$spawning = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
$first = $spawning.IndexOf('public async Task<bool> SpawnDebugFollower(')
$last = $spawning.IndexOf('public async Task<bool> SpawnDebugScavEnemy(', $first)
$spawn = $spawning.Substring($first, $last - $first)
Check (!$spawning.Contains('PrepareDebugScavGear')) 'Debug Scav keeps its normal generated kit'
Check ($spawn.Contains('BossPlayers.AddFollower(me, player, false, botRole, "Default")')) 'Command must use the actual field-recruit conversion path'
Check (!$spawn.Contains('BossPlayers.AddFollower(me, player, true')) 'Command must not create a squadmate'
Check ($spawn.Contains('recruit.DebugForceRecruitInvite = spawnSide == EPlayerSide.Usec || spawnSide == EPlayerSide.Bear')) 'Only command-spawned PMCs receive the Debug invitation marker'
Check ($spawn.Contains('separateScavGroup = spawnSide == EPlayerSide.Savage && GameplayModeRuntime.IsAllegiance') -and
    $spawn.Contains('ShallBeGroup = separateScavGroup ? null') -and $spawn.Contains('group.TargetMembersCount = 1;') -and
    $spawn.Contains('group.Lock();') -and $spawn.Contains('botSpawnerClass.Groups.AddNoKey(group, zn);')) 'Allegiance test Scav needs an independent locked native group'
Check ($spawn.IndexOf('AllegiancePmcFriendship.RegisterDebugRecruit(me, player.realPlayer)') -lt
    $spawn.IndexOf('BossPlayers.AddFollower(me, player, false')) 'Selected-friendly identity must exist before conversion/diplomacy/cargo capture'
Check ($spawn.Contains('new IProfileData(spawnSide, role, BotDifficulty.normal') -and
    $spawn.Contains('TryPrepareDebugRecruitProfile(profile, spawnSide, role)') -and
    !$spawn.Contains('new IProfileData(EPlayerSide.Savage, WildSpawnType.assault')) 'Fallback must preserve and validate the requested faction/role'
$pluginSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
Check ($pluginSource.Contains('SpawnRecruitConsoleCommandAsync("fs_spawnfollower", false)') -and
    $pluginSource.Contains('SpawnRecruitConsoleCommandAsync("fs_spawnscavfollower", true)') -and
    $pluginSource.Contains('bool allegiance = GameplayModeRuntime.IsAllegiance;') -and
    $pluginSource.Contains('!scavFollower && allegiance && UnityEngine.Random.Range(0, 2) == 0') -and
    $pluginSource.Contains('ResolveDebugRecruitSide(player.Side, allegiance, scavFollower, chooseBear)')) 'Commands must use current mode and randomize only Allegiance PMC requests'
$boss = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
Check ($boss.Contains('if (item.DebugForceRecruitInvite && !debugInvite) continue;') -and $boss.Contains('queueRecruit |= debugInvite;') -and
    $boss.Contains('DebugForceInvite = debugInvite')) 'Living Debug recruits must submit their marker without changing the saved invitation setting'
$service = Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyRecruitService.cs')
Check ($service.IndexOf('IsValidCandidate(candidate)') -lt $service.IndexOf('SanitizeRecruitCandidateEquipment(sessionId, candidate)') -and
    $service.IndexOf('SanitizeRecruitCandidateEquipment(sessionId, candidate)') -lt $service.IndexOf('IsDebugForcedInvite(candidate)')) 'Debug bypass must not skip validation or Restricted equipment filtering'
Check ($service.Contains('var picked = successfulCandidates[Random.Shared.Next(successfulCandidates.Count)]')) 'Existing one-invitation selection remains authoritative'
Write-Output "PASS: $script:assertions Debug/Release recruit command metadata and source assertions; not a live spawn/invitation test."

# Compile the production side-selection and encounter-registration bodies with controlled stand-ins.
$friendshipSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/AllegiancePmcFriendship.cs')
$sidePolicy = [regex]::Match($pluginSource, '(?ms)^        internal static EPlayerSide ResolveDebugRecruitSide\(.*?^        \}').Value
$registrationPolicy = [regex]::Match($friendshipSource, '(?ms)^        internal static void RegisterDebugRecruit\(.*?^        \}').Value
$profilePolicy = [regex]::Match($spawning, '(?ms)^        internal static bool TryPrepareDebugRecruitProfile\(.*?^        \}').Value
if (!$sidePolicy -or !$registrationPolicy -or !$profilePolicy) { throw 'Production Debug recruit policy boundary changed' }
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'DebugRecruitSpawnFixture.cs')).Replace('/* SIDE POLICY */', $sidePolicy).Replace('/* REGISTRATION POLICY */', $registrationPolicy).Replace('/* PROFILE POLICY */', $profilePolicy)
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-debug-recruit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$source = Join-Path $temporary 'Spawn.cs'
$exe = Join-Path $temporary 'Spawn.exe'
[IO.File]::WriteAllText($source, $fixture)
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
& dotnet @arguments $source
if ($LASTEXITCODE -ne 0) { throw 'Debug recruit fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Debug recruit policy fixture failed' }
