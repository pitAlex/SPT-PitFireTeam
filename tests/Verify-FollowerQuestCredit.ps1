param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/PlayerPatch.cs')
$fixture=Get-Content -Raw (Join-Path $RepositoryRoot 'tests/FollowerQuestCreditFixture.cs')
foreach($name in @('TryCreditFollowerKillQuestProgress','BuildQuestKillTargets')) {
    $method=[regex]::Match($source,'(?ms)^        private static (?:void|List<string>) '+$name+'\(.*?^        \}').Value
    if(!$method){throw ('Production quest forwarding boundary changed: '+$name)}
    $fixture=$fixture.Replace('__'+$name+'__',$method)
}
$first=$source.IndexOf('if (follower == null)', $source.IndexOf('BotFollowerPlayer follower = BossPlayers.GetFollowers().Find('))
$last=$source.IndexOf('EPlayerSide killedPlayerSide =', $first)
if($first -lt 0 -or $last -le $first){throw 'Production kill dispatch boundary changed'}
$fixture=$fixture.Replace('__DISPATCH__',$source.Substring($first,$last-$first))
$regular=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$boss=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BossFollowerPlayer.cs')
$factory=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
if(!$regular.Contains('IsRosterMember = isSquad;') -or !$boss.Contains('IsRosterMember = savedCombatPreferences;') -or
   !$boss.Contains(': base(bot, player, false, bossRole)') -or
   !$factory.Contains('new BossFollowerPlayer(bot, player, role, savedCombatPreferences: squadMate)')) {
    throw 'Roster provenance must come from saved deployment without changing boss squadmate permissions'
}
$property=[regex]::Match($regular,'(?m)^        public bool IsRosterMember \{ get; protected set; \}').Value
if(!$property){throw 'Roster origin flag missing'}
$fixture=$fixture.Replace('__ROSTER_PROPERTY__',$property)
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$work=Join-Path $RepositoryRoot ('tests/artifacts/follower-quest-credit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$file=Join-Path $work 'Fixture.cs'
[IO.File]::WriteAllText($file,$fixture)
$exe=Join-Path $work 'QuestCredit.exe'
$arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nostdlib+',"/out:$exe")
foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
& dotnet @arguments $file
if($LASTEXITCODE -ne 0){throw 'Follower quest-credit fixture compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Follower quest-credit fixture failed'}
