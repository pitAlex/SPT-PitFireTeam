param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
$method = [regex]::Match($source, '(?ms)^        internal static void AbortSpawnFollower\(.*?^        \}').Value
$leader = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
$release = [regex]::Match($leader, '(?ms)^        internal void ReleaseFailedFollower\(.*?^        \}').Value
if (!$method -or !$release) { throw 'Missing production rollback boundary' }
$fixture = @'
using System;
using System.Collections.Generic;
using System.Linq;
public enum EBotState { Active, Disposed }
public class Player { public string ProfileId="human"; }
public class pitAIBossPlayer {
    public Player realPlayer=new Player(); public List<BotOwner> Followers=new List<BotOwner>();
    public int Unhooks,Releases;private void UnhookFollowerDeath(BotOwner b){Unhooks++;}
    public void NotifyFollowerCombatLayerReleased(BotOwner b,string reason){Releases++;}
__RELEASE__
}
public class BotOwner {
    public string ProfileId="knight";public EBotState BotState;public Follow BotFollower=new Follow();
    public Leave LeaveData;public Controller BotsController=new Controller();public int Despawns;public bool FailLeave;
    public BotOwner(){LeaveData=new Leave(this);}public void Dispose(){BotState=EBotState.Disposed;}
}
public class Follow { public object BossToFollow; }
public class Controller { public Spawner BotSpawner=new Spawner(); }
public class Spawner { public Game BotGame=new Game(); }
public class Game { public void BotDespawn(BotOwner bot){bot.Despawns++;} }
public class Leave {readonly BotOwner bot;public Leave(BotOwner b){bot=b;}public void RemoveFromMap(){if(bot.FailLeave)throw new Exception("leave failed");bot.Dispose();bot.Despawns++;} }
public class BotFollowerPlayer {
    public BotOwner Bot;public int Dismissals;public BotOwner GetBot()=>Bot;
    public void Dismiss(){if(Bot.BotState!=EBotState.Disposed)throw new Exception("must not dismiss a live actor into ordinary AI");Dismissals++;}
}
public static class GameplayModeAddons {public static int Releases;public static bool Fail;public static void AbortSpawn(string id){Releases++;if(Fail)throw new Exception("addon cleanup failed");} }
public static class FollowerQuestingBots {public static void Release(BotOwner b){} }
public static class InteractableObjects {public static int Clears;public static void ClearStoredItems(string id){Clears++;} }
public static class NpcMessage {public static bool Lost;public static void RemoveNpc(string id,bool lost){Lost|=lost;} }
namespace Modules {public static class Logger {public static void LogError(object value){} } }
namespace Patches {public static class BotOwnerManualUpdatePatch {public static Dictionary<string,Action> BotOwnerUpdate=new Dictionary<string,Action>();} }
public class BossPlayers {
    public static BossPlayers Instance;public static pitAIBossPlayer Leader;
    public List<BotFollowerPlayer> _followers=new List<BotFollowerPlayer>();
    public Dictionary<string,BotFollowerPlayer> _followersByProfileId=new Dictionary<string,BotFollowerPlayer>();
    public List<string> _shallBeFollower=new List<string>();
    public static pitAIBossPlayer GetBoss(string id)=>Leader;
__METHOD__
}
public class Fixture {
    static int checks;
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    static void Setup(out BotOwner bot,out BotFollowerPlayer follower,out pitAIBossPlayer leader,bool registered){
        BossPlayers.Instance=new BossPlayers();BossPlayers.Leader=leader=new pitAIBossPlayer();bot=new BotOwner();follower=new BotFollowerPlayer{Bot=bot};
        leader.Followers.Add(bot);bot.BotFollower.BossToFollow=leader;BossPlayers.Instance._shallBeFollower.Add(bot.ProfileId);
        if(registered){BossPlayers.Instance._followers.Add(follower);BossPlayers.Instance._followersByProfileId[bot.ProfileId]=follower;}
        Patches.BotOwnerManualUpdatePatch.BotOwnerUpdate.Clear();Patches.BotOwnerManualUpdatePatch.BotOwnerUpdate[bot.ProfileId]=()=>{};
        GameplayModeAddons.Fail=false;GameplayModeAddons.Releases=0;InteractableObjects.Clears=0;NpcMessage.Lost=false;
    }
    public static void Main(){
        foreach(bool registered in new[]{false,true}){
            Setup(out var bot,out var follower,out var leader,registered);
            BossPlayers.AbortSpawnFollower(bot.ProfileId,bot,leader,follower);
            Check(bot.BotState==EBotState.Disposed&&bot.Despawns==1,"failed actor is disposed/despawned");
            Check(BossPlayers.Instance._followers.Count==0&&BossPlayers.Instance._followersByProfileId.Count==0&&BossPlayers.Instance._shallBeFollower.Count==0,"partial and complete registry entries are removed");
            Check(leader.Followers.Count==0&&leader.Unhooks==1&&bot.BotFollower.BossToFollow==null,"human membership and death subscription released");
            Check(follower.Dismissals==1&&GameplayModeAddons.Releases==1,"addon and follower runtime release");
            Check(Patches.BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0&&InteractableObjects.Clears==1&&!NpcMessage.Lost,"watchers/cache removed without a lost/death outcome");
            BossPlayers.AbortSpawnFollower(bot.ProfileId,bot,leader);
            Check(bot.Despawns==1,"repeated rollback does not despawn twice");
        }
        Setup(out var broken,out var partial,out var boss,true);broken.FailLeave=true;GameplayModeAddons.Fail=true;
        BossPlayers.AbortSpawnFollower(broken.ProfileId,broken,boss,partial);
        Check(broken.BotState==EBotState.Disposed&&broken.Despawns==1&&partial.Dismissals==1,"native/addon cleanup failures still reach disposal and fallback despawn");
        Check(Patches.BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0&&InteractableObjects.Clears==1,"failed cleanup stage cannot skip later resource release");
        Setup(out var newer,out var newFollower,out var newBoss,true);var oldBoss=new pitAIBossPlayer();var oldBot=new BotOwner();
        BossPlayers.AbortSpawnFollower(oldBot.ProfileId,oldBot,oldBoss);
        Check(BossPlayers.Instance._followersByProfileId[newer.ProfileId]==newFollower&&newer.BotState==EBotState.Active,"old-raid completion cannot remove a new raid's same-profile follower");
        Check(GameplayModeAddons.Releases==0&&InteractableObjects.Clears==0,"old raid cannot release new addon/cache state");
        Console.WriteLine("PASS: "+checks+" production spawn rollback checks (stubbed native despawn).");
    }
}
'@
$fixture = $fixture.Replace('__METHOD__', $method).Replace('__RELEASE__', $release)
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('follower-spawn-rollback-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $file = Join-Path $temporary 'Fixture.cs'; [IO.File]::WriteAllText($file, $fixture)
    $exe = Join-Path $temporary 'Fixture.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" $file
    if ($LASTEXITCODE -ne 0) { throw 'Spawn rollback fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Spawn rollback fixture failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
