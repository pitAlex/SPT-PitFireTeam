param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot)
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
$start = $source.IndexOf('    internal class BossSpawnWaveManagerClassPatch')
$method = [regex]::Match($source.Substring($start), '(?ms)^        private static bool PatchPrefix\(BossSpawnScenario __instance\).*?^        \}').Value
if (!$method) { throw 'Missing production boss-wave gate' }
$props = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/Props.cs')
if ($props.Substring($props.IndexOf('public static Dictionary<WildSpawnType, List<string>> QuestBosses')).Contains('bossPartisan')) { throw 'Tripwire must not force Partizan through the legacy quest-wave override' }
if (Test-Path (Join-Path $RepositoryRoot 'going-rogue/client/Quests/PartizanQuestSpawn.cs')) { throw 'Partizan quest spawn override must remain removed' }
$block = $source.IndexOf('spawner.SetBlockedRoles(blocked.Concat(new[] { "bossKnight", "followerBirdEye", "followerBigPipe" })')
$spawn = $source.IndexOf('Modules.Logger.LogInfo("Start Squad Spawn")')
if ($block -lt 0 -or $block -gt $spawn) { throw 'Native role blocking must precede saved-profile spawning' }
$transit = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/TransitPointPatch.cs')
$route = [regex]::Match($transit, '(?ms)^ +if \(player\.Profile\.Side == EPlayerSide\.Savage\) Utils\.SpawnHelper\.spawnMemberIdsScav\.Add.*?^ +else Utils\.SpawnHelper\.spawnMemberIds\.Add\(bot\.AccountId\);').Value
$cache = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerTransitStateCache.cs')
$consume = [regex]::Match($cache, '(?ms)^        public static bool TryConsumeProfile\(.*?^        \}').Value
$addonPolicy = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/GameplayModeAddons.cs')
$bossSelection = [regex]::Match($addonPolicy, '(?ms)^        public static bool HasSelectedBossCompanions =>.*?;').Value
if (!$route -or !$consume -or !$bossSelection) { throw 'Missing production transit routing or companion selection' }
if ($GameRoot) {
    [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')) | Out-Null
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
    try {
        $spawner = $module.GetType('EFT.BotSpawner')
        if (!($spawner.Fields | Where-Object { $_.Name -eq '_blockedRoles' -and $_.IsPublic }) -or
            !($spawner.Methods | Where-Object { $_.Name -eq 'SetBlockedRoles' -and $_.IsPublic }) -or
            !($spawner.Methods | Where-Object { $_.Name -eq 'CanSpawnRole' -and $_.Body.Instructions.Operand.Name -contains '_blockedRoles' })) {
            throw 'Installed native spawn-role blocking boundary changed'
        }
        $leave = $module.GetType('BotLeaveData').Methods | Where-Object Name -eq 'RemoveFromMap'
        if (!$leave.IsPublic -or !($leave.Body.Instructions.Operand.Name -contains 'BotDespawn') -or
            !($leave.Body.Instructions.Operand.Name -contains 'Dispose')) {
            throw 'Installed native failed-actor despawn boundary changed'
        }
    }
    finally { $module.Dispose() }
}
$fixture = @'
using System;
using System.Collections.Generic;
using System.Linq;
public enum WildSpawnType { bossKnight, followerBigPipe, followerBirdEye, pmcUSEC, bossOther, bossKolontay, bossPartisan }
public enum EPlayerSide { Usec, Savage }
public enum EQuestStatus { Started }
public enum GameplayMode { Allegiance }
public class AbstractGame {}
public static class Singleton<T> { public static bool Instantiated=true; }
public class Health { public bool IsAlive=true; }
public class Quest { public string Id="quest"; public EQuestStatus Status=EQuestStatus.Started; }
public class Settings { public WildSpawnType Role; }
public class Info { public Settings Settings=new Settings(); }
public class Profile {
    public List<Quest> QuestsData=new List<Quest>{new Quest()};
    public EPlayerSide Side;
    public Info Info=new Info();
    public string Nickname,ProfileId;
    public Profile Clone() => (Profile)MemberwiseClone();
}
public class BotOwner { public string AccountId; }
public class Follower { public bool IsRosterMember; }
public class Player { public Health HealthController=new Health();public EPlayerSide Side;public Profile Profile=new Profile(); }
public static class GamePlayerOwner { public static Player MyPlayer=new Player(); }
public class Wave { public WildSpawnType BossType;public bool ShallSpawn=true,ForceSpawn=true; }
public class BossSpawnScenario { public List<Wave> BossSpawnWaves=new List<Wave>(); }
public static class GameplayModeAddons { public static bool HasSelectedBossCompanions; }
public static class FollowerTransitStateCache { public static bool HasCarriedGoons; }
namespace Modules { public static class Logger { public static void LogInfo(string message) {} } }
namespace Utils {
    public static class Utils { public static bool Quest;public static bool FlagGet(string flag)=>Quest; }
    public static class Props { public static Dictionary<WildSpawnType,List<string>> QuestBosses=new Dictionary<WildSpawnType,List<string>>{{WildSpawnType.bossKnight,new List<string>{"quest"}},{WildSpawnType.bossKolontay,new List<string>{"quest"}}}; }
    public static class SpawnHelper {
        public static List<string> spawnMemberIds=new List<string>(),spawnMemberIdsScav=new List<string>();
        public static List<WildSpawnType> spawnMemberIdsBoss=new List<WildSpawnType>();
    }
}
public class Fixture {
__METHOD__
    static void Route(Player player, BotOwner bot, Follower follower, Profile transitProfile, bool isBoss) {
__ROUTE__
    }
    static GameplayMode Current=GameplayMode.Allegiance;
    static HashSet<GameplayMode> Registered=new HashSet<GameplayMode>();
    static Func<bool> goonCompanionsPresent=()=>false;
__BOSS_SELECTION__
    class TransitFollowerState { public Profile Profile; }
    static Dictionary<string,TransitFollowerState> carried=new Dictionary<string,TransitFollowerState>();
    static string GetRoleKey(WildSpawnType role)=>"role:"+role;
    static bool TryTakeState(string key,out TransitFollowerState state) {
        if(!carried.TryGetValue(key,out state)) return false;
        carried.Remove(key);
        return true;
    }
    static void TrackTransitSpawnProfile(Profile profile,TransitFollowerState state) {}
__CONSUME__
    static int checks;
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    public static void Main(){
        foreach(bool quest in new[]{false,true}){
            GameplayModeAddons.HasSelectedBossCompanions=true;Utils.Utils.Quest=quest;
            var goons=new Wave{BossType=WildSpawnType.bossKnight};var other=new Wave{BossType=WildSpawnType.bossOther};
            Check(PatchPrefix(new BossSpawnScenario{BossSpawnWaves=new List<Wave>{goons,other}}),"other boss processing continues");
            Check(!goons.ShallSpawn&&!goons.ForceSpawn,"selected Knight blocks normal and forced Goons, including quest overrides");
            Check(other.ShallSpawn&&other.ForceSpawn,"unrelated boss wave is preserved");
        }
        GameplayModeAddons.HasSelectedBossCompanions=false;Utils.Utils.Quest=false;
        var normal=new Wave{BossType=WildSpawnType.bossKnight};PatchPrefix(new BossSpawnScenario{BossSpawnWaves=new List<Wave>{normal}});
        Check(normal.ShallSpawn&&normal.ForceSpawn,"ordinary Goons remain available without selected companion");
        Utils.Utils.Quest=true;normal.ShallSpawn=false;PatchPrefix(new BossSpawnScenario{BossSpawnWaves=new List<Wave>{normal}});
        Check(normal.ShallSpawn,"legacy quest override still works outside companion raids");
        GameplayModeAddons.HasSelectedBossCompanions=true;
        var kollontay=new Wave{BossType=WildSpawnType.bossKolontay,ShallSpawn=false,ForceSpawn=false};
        PatchPrefix(new BossSpawnScenario{BossSpawnWaves=new List<Wave>{kollontay}});
        Check(kollontay.ShallSpawn,"automatic Goon quest raid enables its native Kollontay wave while blocking hostile Goons");
        foreach(bool nativeSpawn in new[]{false,true}) {
            var partisan=new Wave{BossType=WildSpawnType.bossPartisan,ShallSpawn=nativeSpawn,ForceSpawn=nativeSpawn};
            PatchPrefix(new BossSpawnScenario{BossSpawnWaves=new List<Wave>{partisan}});
            Check(partisan.ShallSpawn==nativeSpawn && partisan.ForceSpawn==nativeSpawn,"Partizan keeps the native wave decision during quest companion raids");
        }
        var pmc=new Player();var saved=new Follower{IsRosterMember=true};
        var roles=new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye};
        foreach(var role in roles) {
            var profile=new Profile();profile.Info.Settings.Role=role;
            Route(pmc,new BotOwner{AccountId=role.ToString()},saved,profile,true);
        }
        Route(pmc,new BotOwner{AccountId="Rusak"},saved,new Profile(),false);
        Check(Utils.SpawnHelper.spawnMemberIds.SequenceEqual(roles.Select(role=>role.ToString()).Concat(new[]{"Rusak"})),"mixed squad queues every saved Goon and PMC in the account route");
        Check(Utils.SpawnHelper.spawnMemberIdsBoss.Count==0,"saved Goons do not enter role-only prefetch or spawning");
        Check(!HasSelectedBossCompanions,"ordinary account selection alone does not suppress hostile Goons");
        FollowerTransitStateCache.HasCarriedGoons=true;
        Check(HasSelectedBossCompanions,"carried Goons suppress hostile waves before addon selection resolves");
        FollowerTransitStateCache.HasCarriedGoons=false;
        var temporary=new Profile();temporary.Info.Settings.Role=WildSpawnType.bossKnight;
        Route(pmc,new BotOwner(),new Follower(),temporary,true);
        Check(Utils.SpawnHelper.spawnMemberIdsBoss.SequenceEqual(new[]{WildSpawnType.bossKnight}),"eligible unsaved boss retains the role route");
        var scav=new Player();scav.Profile.Side=EPlayerSide.Savage;
        Route(scav,new BotOwner{AccountId="scav"},saved,new Profile(),false);
        Check(Utils.SpawnHelper.spawnMemberIdsScav.SequenceEqual(new[]{"scav"}),"player-Scav transit keeps its own route");
        carried["knight"]=new TransitFollowerState{Profile=new Profile{ProfileId="carriedKnight"}};
        carried[GetRoleKey(WildSpawnType.pmcUSEC)]=new TransitFollowerState{Profile=new Profile{ProfileId="otherPmc"}};
        Check(TryConsumeProfile("knight",WildSpawnType.pmcUSEC,out var knight)&&knight.ProfileId=="carriedKnight","saved Goon restores its exact snapshot even with a PMC generation role");
        Check(!TryConsumeProfile("missing",WildSpawnType.pmcUSEC,out _),"missing saved account cannot steal another follower's role snapshot");
        carried[GetRoleKey(WildSpawnType.bossKnight)]=new TransitFollowerState{Profile=new Profile{ProfileId="legacyKnight"}};
        Check(TryConsumeProfile(null,WildSpawnType.bossKnight,out var legacy)&&legacy.ProfileId=="legacyKnight","accountless legacy boss still restores by role");
        Console.WriteLine("PASS: "+checks+" production Goons wave checks; native role gate precedes saved-profile spawning.");
    }
}
'@
$fixture = $fixture.Replace('__METHOD__', $method).Replace('__ROUTE__', $route).Replace('__CONSUME__', $consume).Replace('__BOSS_SELECTION__', $bossSelection)
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('going-rogue-spawns-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $file = Join-Path $temporary 'Fixture.cs'
    [IO.File]::WriteAllText($file, $fixture)
    $exe = Join-Path $temporary 'Fixture.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" $file
    if ($LASTEXITCODE -ne 0) { throw 'Goons wave fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Goons wave fixture failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
