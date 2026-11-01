param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot)
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
$start = $source.IndexOf('    internal class BossSpawnWaveManagerClassPatch')
$method = [regex]::Match($source.Substring($start), '(?ms)^        private static bool PatchPrefix\(BossSpawnScenario __instance\).*?^        \}').Value
if (!$method) { throw 'Missing production boss-wave gate' }
$block = $source.IndexOf('spawner.SetBlockedRoles(blocked.Concat(new[] { "bossKnight", "followerBirdEye", "followerBigPipe" })')
$spawn = $source.IndexOf('Modules.Logger.LogInfo("Start Squad Spawn")')
if ($block -lt 0 -or $block -gt $spawn) { throw 'Native role blocking must precede saved-profile spawning' }
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
public enum WildSpawnType { bossKnight, bossOther }
public enum EPlayerSide { Usec, Savage }
public enum EQuestStatus { Started }
public class AbstractGame {}
public static class Singleton<T> { public static bool Instantiated=true; }
public class Health { public bool IsAlive=true; }
public class Quest { public string Id="quest"; public EQuestStatus Status=EQuestStatus.Started; }
public class Profile { public List<Quest> QuestsData=new List<Quest>{new Quest()}; }
public class Player { public Health HealthController=new Health();public EPlayerSide Side;public Profile Profile=new Profile(); }
public static class GamePlayerOwner { public static Player MyPlayer=new Player(); }
public class Wave { public WildSpawnType BossType;public bool ShallSpawn=true,ForceSpawn=true; }
public class BossSpawnScenario { public List<Wave> BossSpawnWaves=new List<Wave>(); }
public static class GameplayModeAddons { public static bool HasSelectedBossCompanions; }
namespace Utils {
    public static class Utils { public static bool Quest;public static bool FlagGet(string flag)=>Quest; }
    public static class Props { public static Dictionary<WildSpawnType,List<string>> QuestBosses=new Dictionary<WildSpawnType,List<string>>{{WildSpawnType.bossKnight,new List<string>{"quest"}}}; }
}
public class Fixture {
__METHOD__
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
        Console.WriteLine("PASS: "+checks+" production Goons wave checks; native role gate precedes saved-profile spawning.");
    }
}
'@
$fixture = $fixture.Replace('__METHOD__', $method)
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
