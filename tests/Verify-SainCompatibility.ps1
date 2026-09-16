param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$GameRoot
)
# Production boundary checks with real Harmony and controlled EFT/SAIN stand-ins.
# Body eligibility uses production methods; head-roll behavior, hearing, physics and raid AI use stand-ins.
$ErrorActionPreference = 'Stop'
$nl = [Environment]::NewLine
$aimSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerAimTargetPatch.cs')
$hearingSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/HearingSensorPatch.cs')
function Get-Method([string]$Source, [string]$Name) {
    $pattern = '(?ms)^        (?:protected|public)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}'
    $matches = [regex]::Matches($Source, $pattern)
    if ($matches.Count -ne 1) { throw "Expected one production method: $Name" }
    $matches[0].Value
}
$hearingSource = $hearingSource.Substring(0, $hearingSource.IndexOf('    internal class FootstepSoundPatch'))
$target = Get-Method $hearingSource GetTargetMethod
$dispatch = Get-Method $hearingSource PatchPostfix
# Test types share this assembly instead of a separate assembly named SAIN.
$legacyLookup = 'Type\.GetType\("SAIN\.SAINComponent\.Classes\.SAINShootData, SAIN"\)'
if (![regex]::IsMatch($aimSource, $legacyLookup)) {
    throw 'Review the changed SAIN type lookups before updating this harness.'
}
$aimSource = [regex]::Replace($aimSource, $legacyLookup, 'CompatibilityChecks.LegacyShootDataType')
$imports = [regex]::Matches($aimSource, '(?m)^using [^\r\n]+;') | ForEach-Object Value
$aimSource = [regex]::Replace($aimSource, '(?m)^using [^\r\n]+;\r?\n', '')
$fixture = @'
using Comfort.Common;
using System.Collections.Generic;
namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:new() { public static T Instance=new(); } }
namespace EFT {
    public enum EBotState { Inactive, Active }
    public enum AISoundType { step, gun }
    public interface IPlayer { string ProfileId{get;} }
    public class Player : IPlayer { public string ProfileId{get;set;}="source"; }
    public class GameWorld {}
    public class WeaponManager { public Launcher UnderbarrelLauncherController=new(); }
    public class Launcher { public bool IsActive; }
    public class BotOwner {
        public WeaponManager WeaponManager=new();
        public string ProfileId="follower"; public bool IsDead,IsFollower=true,ThrowHearing;
        public int Callbacks,NativeSounds; public EBotState BotState=EBotState.Active;
        public Player GetPlayer=new(); public object Memory=new(),EnemiesController=new(),BotsGroup=new();
        public BotHearingSensor HearingSensor; public BotOwner(){HearingSensor=new(this);}
    }
}
public class EnemyInfo {
    public BotOwner Owner=new();
    public EnemyPart? LastPartToShoot;
    public bool Corrected,CorrectedBody,CorrectedHead;
    public Dictionary<BodyPartType,EnemyPartVision> _allPartsVision=new();
    public Dictionary<BodyPartType,EnemyPart> _allParts=new();
    public EnemyInfo(){
        _allParts[BodyPartType.head]=new(BodyPartType.head,new Vector3(4,5,6));
        _allParts[BodyPartType.body]=new(BodyPartType.body,new Vector3(9,9,9));
        LastPartToShoot=_allParts[BodyPartType.body];
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public Vector3 GetVisiblePartToShoot(){LastPartToShoot=_allParts[BodyPartType.body];return new(9,9,9);}
}
public enum BodyPartType { head,body,leftArm,rightArm,leftLeg,rightLeg }
public class EnemyPartVision { public bool Visible; }
public class EnemyPart {
    public bool CanShoot;
    public BodyPartType BodyPartType; private readonly Vector3 point;
    public EnemyPart(BodyPartType type,Vector3 point){BodyPartType=type;this.point=point;}
    public Vector3 GetPartPositionWithOffset()=>point;
}
public class GlobalEventDispatcher {
    public event Action<IPlayer,Vector3,float,AISoundType>? OnSoundPlayed;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void PlaySound(IPlayer person,Vector3 position,float power,AISoundType type)=>OnSoundPlayed?.Invoke(person,position,power,type);
}
public class BotHearingSensor {
    public BotOwner Owner; public BotHearingSensor(BotOwner owner){Owner=owner;}
    public void Init(GlobalEventDispatcher dispatcher){dispatcher.OnSoundPlayed+=OnSoundPlayed;}
    public void OnSoundPlayed(IPlayer p,Vector3 v,float power,AISoundType type){Owner.NativeSounds++;}
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch {
        protected abstract MethodBase GetTargetMethod(); public MethodBase Target=>GetTargetMethod();
    }
    public class PatchPrefixAttribute:Attribute{}
    public class PatchPostfixAttribute:Attribute{}
}
namespace pitTeam.Modules {
    public class Follower { public BotOwner Bot=null!; public BotOwner GetBot()=>Bot; }
    public class BossPlayers {
        public static BossPlayers? Instance=new(); public static List<Follower> Followers=new();
        public static List<Follower> GetFollowers()=>Followers;
        public static bool IsFollowerProfileId(string id)=>Followers.Exists(f=>f.Bot.ProfileId==id);
        public static bool IsPlayerBoss(string id)=>id=="boss";
    }
    public sealed class FollowerProficiencyValues {}
    public static class FollowerProficiency {
        public static bool TryGetValues(BotOwner bot,out FollowerProficiencyValues? values){
            values=bot.IsFollower?new():null;return values!=null;
        }
    }
    public static class FollowerAimTargetPolicy {
        public static int Calls;
        public static bool Promote=true;
        public static Vector3 LastBaseline;
        public static bool LastBaselineHead;
__BODY_METHODS__
        public static bool TryEnhanceFollowerShootPoint(EnemyInfo? e,Vector3 native,bool nativeHead,EnemyPart? nativePart,out Vector3 point){
            point=native;if(e?.Owner.IsFollower!=true)return false;Calls++;LastBaseline=native;LastBaselineHead=nativeHead;
            if(!nativeHead && Promote)point=new(1,2,3);return true;
        }
    }
    public static class Logger {
        public static List<string> Errors=new();
        public static void LogError(string s){Errors.Add(s);} public static void LogInfo(string s){}
    }
}
namespace pitTeam.BigBrain {
    public static class FollowerEnemyInfoCorrection {
        public static bool TryGetVerifiedShootParts(EnemyInfo e,out bool head,out bool body){head=e.CorrectedHead;body=e.CorrectedBody;return e.Corrected;}
    }
}
namespace pitTeam.Patches {
    internal class HearingSensorPatch:ModulePatch {
__TARGET__
__DISPATCH__
        private static void ReactToSound(BotHearingSensor sensor,IPlayer p,Vector3 pos,float power,AISoundType type){
            if(sensor.Owner.ThrowHearing)throw new InvalidOperationException("test unavailable sensor");
            sensor.Owner.Callbacks++;
        }
    }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public enum EAimTargetPart { Head,Chest,LeftLeg,RightLeg }
    public class EnemyAimTarget {
        protected EnemyInfo EnemyInfo{get;}
        public EAimTargetPart? ChosenPart{get;set;}=EAimTargetPart.Chest;
        public bool ReturnNull{get;set;}
        public static int NativeSelections;
        public EnemyAimTarget(EnemyInfo enemyInfo){EnemyInfo=enemyInfo;}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public Vector3? GetPointToShoot(bool allowRepick=true){NativeSelections++;return ReturnNull?null:new Vector3(9,9,9);}
    }
    public class Enemy {
        public EnemyInfo EnemyInfo{get;set;}=new(); public bool IsVisible{get;set;}=true; public bool CanShoot{get;set;}=true;
        public EnemyAimTarget AimTarget{get;}
        public Enemy(){AimTarget=new EnemyAimTarget(EnemyInfo);}
    }
}
namespace SAIN.Patches.Aim {
    public static class BodyPartToShootPatch {
        public static int NativeSelections; public static bool SelectHead;
        public static bool Patch(ref Vector3 __result,EnemyInfo __instance){
            NativeSelections++;
            var target=new SAIN.SAINComponent.Classes.EnemyClasses.EnemyAimTarget(__instance){
                ChosenPart=SelectHead?SAIN.SAINComponent.Classes.EnemyClasses.EAimTargetPart.Head:SAIN.SAINComponent.Classes.EnemyClasses.EAimTargetPart.Chest
            };
            __result=target.GetPointToShoot(false)??new Vector3(7,8,9);
            __instance.LastPartToShoot=__instance._allParts[SelectHead?BodyPartType.head:BodyPartType.body];
            return false;
        }
    }
}
public static class Shoot450 {
    public static int NativeSelections;
    public static Vector3? Aim(SAIN.SAINComponent.Classes.EnemyClasses.Enemy? enemy)=>GetAimTarget(enemy,new object());
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Vector3? GetAimTarget(SAIN.SAINComponent.Classes.EnemyClasses.Enemy? enemy,object bot){
        if(enemy==null||!enemy.IsVisible||!enemy.CanShoot)return null;
        NativeSelections++;return enemy.EnemyInfo.GetVisiblePartToShoot();
    }
}
public static class Shoot451 {
    public static int NativeSelections;
    public static Vector3? Aim(SAIN.SAINComponent.Classes.EnemyClasses.Enemy? enemy)=>GetAimTarget(enemy);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Vector3? GetAimTarget(SAIN.SAINComponent.Classes.EnemyClasses.Enemy? enemy){
        if(enemy==null||!enemy.IsVisible||!enemy.CanShoot)return null;
        NativeSelections++;return enemy.AimTarget.GetPointToShoot()??enemy.EnemyInfo.GetVisiblePartToShoot();
    }
}
public static class UnsupportedShoot { private static int GetAimTarget(object enemy)=>1; }
public static class CompatibilityChecks {
    public static Type? LegacyShootDataType; private static int checks;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    private static (GlobalEventDispatcher dispatcher,Player source,BotOwner bot) Fresh(bool native=false){
        BossPlayers.Instance=new();BossPlayers.Followers.Clear();Singleton<GameWorld>.Instance=new();
        var bot=new BotOwner();BossPlayers.Followers.Add(new(){Bot=bot});
        var dispatcher=new GlobalEventDispatcher();if(native)bot.HearingSensor.Init(dispatcher);
        return(dispatcher,new Player(),bot);
    }
    private static void Sound((GlobalEventDispatcher dispatcher,Player source,BotOwner bot) h){h.dispatcher.PlaySound(h.source,default,50,AISoundType.gun);}
    public static int Run(){
        var harmony=new Harmony("pitFireTeam.tests.sain.compatibility");
        var partTarget=AccessTools.Method(typeof(EnemyInfo),nameof(EnemyInfo.GetVisiblePartToShoot));
        var sainPartPrefix=AccessTools.Method(typeof(SAIN.Patches.Aim.BodyPartToShootPatch),"Patch");
        var sainPartHarmony=new Harmony("BodyPartToShootPatch");
        sainPartHarmony.Patch(partTarget,prefix:new HarmonyMethod(sainPartPrefix));
        var followerPartHarmony=new Harmony("FollowerAimTargetPatch");
        followerPartHarmony.Patch(partTarget,
            postfix:new HarmonyMethod(AccessTools.Method(typeof(pitTeam.Patches.FollowerAimTargetPatch),"PatchPostfix")));

        int calls;
        foreach(var version in new[]{typeof(Shoot450),typeof(Shoot451)}){
            LegacyShootDataType=version;
            pitTeam.Patches.FollowerSainAimTargetPatch.Apply(harmony);
            var aimTarget=AccessTools.Method(version,"GetAimTarget");
            var patches=Harmony.GetPatchInfo(aimTarget);
            Check(patches?.Prefixes.Count==1&&patches.Postfixes.Count==1,version.Name+"_native_selector_registered");
            Func<SAIN.SAINComponent.Classes.EnemyClasses.Enemy?,Vector3?> aim=version==typeof(Shoot450)?Shoot450.Aim:Shoot451.Aim;
            Func<int> native=()=>version==typeof(Shoot450)?Shoot450.NativeSelections:Shoot451.NativeSelections;
            var enemy=new SAIN.SAINComponent.Classes.EnemyClasses.Enemy();
            SAIN.Patches.Aim.BodyPartToShootPatch.SelectHead=false;
            enemy.AimTarget.ChosenPart=SAIN.SAINComponent.Classes.EnemyClasses.EAimTargetPart.Chest;
            calls=FollowerAimTargetPolicy.Calls;int nativeBefore=native();
            Check(aim(enemy)?.x==1&&native()==nativeBefore+1&&FollowerAimTargetPolicy.Calls==calls+1,
                version.Name+"_nonhead_enhanced_after_native");
            SAIN.Patches.Aim.BodyPartToShootPatch.SelectHead=true;
            enemy.AimTarget.ChosenPart=SAIN.SAINComponent.Classes.EnemyClasses.EAimTargetPart.Head;
            calls=FollowerAimTargetPolicy.Calls;nativeBefore=native();
            float expectedHead=version==typeof(Shoot450)?4:9;
            Check(aim(enemy)?.x==expectedHead&&native()==nativeBefore+1&&FollowerAimTargetPolicy.Calls==calls+1,
                version.Name+"_native_head_preserved");
            enemy.EnemyInfo._allParts[BodyPartType.body]=new(BodyPartType.body,new Vector3(42,0,0));
            enemy.EnemyInfo.Corrected=true;enemy.EnemyInfo.CorrectedBody=true;
            FollowerAimTargetPolicy.Promote=false;calls=FollowerAimTargetPolicy.Calls;
            Check(aim(enemy)?.x==42 && !FollowerAimTargetPolicy.LastBaselineHead && FollowerAimTargetPolicy.Calls==calls+1,
                version.Name+"_verified_body_overrides_native_head_before_single_enhancement");
            SAIN.Patches.Aim.BodyPartToShootPatch.SelectHead=false;
            enemy.AimTarget.ChosenPart=SAIN.SAINComponent.Classes.EnemyClasses.EAimTargetPart.LeftLeg;
            Check(aim(enemy)?.x==42,version.Name+"_verified_body_overrides_native_leg");
            FollowerAimTargetPolicy.Promote=true;calls=FollowerAimTargetPolicy.Calls;
            Check(aim(enemy)?.x==1 && FollowerAimTargetPolicy.LastBaseline.x==42 && FollowerAimTargetPolicy.Calls==calls+1,
                version.Name+"_body_baseline_retains_one_precision_head_enhancement");
            FollowerAimTargetPolicy.Promote=false;
            enemy.EnemyInfo.CorrectedBody=false;
            enemy.EnemyInfo._allParts[BodyPartType.body].CanShoot=true;
            enemy.EnemyInfo._allPartsVision[BodyPartType.body]=new(){Visible=true};
            Check(aim(enemy)?.x==9,version.Name+"_corrected_blocked_body_preserves_exposed_native_fallback");
            enemy.EnemyInfo.Corrected=false;
            Check(aim(enemy)?.x==42,version.Name+"_uncached_body_requires_shootable_and_visible");
            enemy.EnemyInfo._allPartsVision[BodyPartType.body].Visible=false;
            Check(aim(enemy)?.x==9,version.Name+"_invisible_body_not_forced");
            enemy.EnemyInfo.Corrected=true;enemy.EnemyInfo.CorrectedBody=true;
            enemy.EnemyInfo.Owner.WeaponManager.UnderbarrelLauncherController.IsActive=true;
            Check(aim(enemy)?.x==9,version.Name+"_launcher_not_redirected_to_body");
            enemy.EnemyInfo.Owner.WeaponManager.UnderbarrelLauncherController.IsActive=false;
            FollowerAimTargetPolicy.Promote=true;
            enemy.EnemyInfo.Owner.IsFollower=false;calls=FollowerAimTargetPolicy.Calls;
            Check(aim(enemy)?.x==9&&FollowerAimTargetPolicy.Calls==calls,version.Name+"_ordinary_bot_unchanged");
            enemy.EnemyInfo.Owner.IsFollower=true;enemy.IsVisible=false;calls=FollowerAimTargetPolicy.Calls;
            Check(aim(enemy)==null&&FollowerAimTargetPolicy.Calls==calls,version.Name+"_invisible_guard");
            enemy.IsVisible=true;enemy.CanShoot=false;
            Check(aim(enemy)==null&&FollowerAimTargetPolicy.Calls==calls,version.Name+"_cannot_shoot_guard");
            Check(aim(null)==null,version.Name+"_null_enemy");
            if(version==typeof(Shoot451)){
                enemy.CanShoot=true;enemy.AimTarget.ReturnNull=true;
                SAIN.Patches.Aim.BodyPartToShootPatch.SelectHead=false;calls=FollowerAimTargetPolicy.Calls;
                Check(aim(enemy)?.x==1&&FollowerAimTargetPolicy.Calls==calls+1,"Shoot451_EnemyInfo_fallback_enhanced_once");
            }
            harmony.Unpatch(aimTarget,HarmonyPatchType.Prefix,harmony.Id);
            harmony.Unpatch(aimTarget,HarmonyPatchType.Postfix,harmony.Id);
        }

        SAIN.Patches.Aim.BodyPartToShootPatch.NativeSelections=0;
        SAIN.Patches.Aim.BodyPartToShootPatch.SelectHead=false;
        var partEnemy=new EnemyInfo();calls=FollowerAimTargetPolicy.Calls;
        Check(partEnemy.GetVisiblePartToShoot().x==1&&
              SAIN.Patches.Aim.BodyPartToShootPatch.NativeSelections==1&&
              FollowerAimTargetPolicy.Calls==calls+1,"EnemyInfo_SAIN_nonhead_enhanced_once");
        SAIN.Patches.Aim.BodyPartToShootPatch.SelectHead=true;calls=FollowerAimTargetPolicy.Calls;
        Check(partEnemy.GetVisiblePartToShoot().x==9&&
              SAIN.Patches.Aim.BodyPartToShootPatch.NativeSelections==2&&
              FollowerAimTargetPolicy.Calls==calls+1,"EnemyInfo_SAIN_head_preserved_once");
        partEnemy.Owner.IsFollower=false;calls=FollowerAimTargetPolicy.Calls;
        Check(partEnemy.GetVisiblePartToShoot().x==9&&FollowerAimTargetPolicy.Calls==calls,"EnemyInfo_ordinary_bot_unchanged");
        sainPartHarmony.Unpatch(partTarget,HarmonyPatchType.Prefix,sainPartHarmony.Id);
        partEnemy=new EnemyInfo();calls=FollowerAimTargetPolicy.Calls;
        Check(partEnemy.GetVisiblePartToShoot().x==1&&FollowerAimTargetPolicy.Calls==calls+1,"Vanilla_native_nonhead_enhanced");

        LegacyShootDataType=typeof(UnsupportedShoot);int errors=pitTeam.Modules.Logger.Errors.Count;
        pitTeam.Patches.FollowerSainAimTargetPatch.Apply(harmony);
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(UnsupportedShoot),"GetAimTarget"))==null&&pitTeam.Modules.Logger.Errors.Count==errors+1,"Unsupported_layout_fails_open");
        var hearing=new pitTeam.Patches.HearingSensorPatch();
        Check(hearing.Target.DeclaringType==typeof(GlobalEventDispatcher)&&hearing.Target.Name=="PlaySound","Shared_sound_boundary");
        harmony.Patch(hearing.Target,postfix:new HarmonyMethod(typeof(pitTeam.Patches.HearingSensorPatch).GetMethod("PatchPostfix")));
        foreach(bool subscribed in new[]{false,true}){
            var h=Fresh(subscribed);Sound(h);
            Check(h.bot.Callbacks==1,"One_callback_native_subscription_"+subscribed);
            Check(h.bot.NativeSounds==(subscribed?1:0),"Native_hearing_preserved_"+subscribed);
        }
        var f=Fresh();f.source.ProfileId="boss";Sound(f);Check(f.bot.Callbacks==0,"Boss_source_excluded");
        f=Fresh();f.source.ProfileId=f.bot.ProfileId;Sound(f);Check(f.bot.Callbacks==0,"Follower_source_excluded");
        f=Fresh();f.bot.IsDead=true;Sound(f);Check(f.bot.Callbacks==0,"Dead_follower_excluded");
        f=Fresh();f.bot.BotState=EBotState.Inactive;Sound(f);Check(f.bot.Callbacks==0,"Inactive_follower_excluded");
        f=Fresh();f.bot.HearingSensor=null!;Sound(f);Check(f.bot.Callbacks==0,"Missing_sensor");
        f=Fresh();BossPlayers.Followers.Clear();Sound(f);Check(f.bot.Callbacks==0,"Dismissed_follower_excluded");
        f=Fresh();BossPlayers.Instance=null;Sound(f);Check(f.bot.Callbacks==0,"Raid_teardown");
        f=Fresh();var second=new BotOwner(){ProfileId="second"};BossPlayers.Followers.Add(new(){Bot=second});Sound(f);
        Check(f.bot.Callbacks==1&&second.Callbacks==1,"Every_follower_once");
        f=Fresh();f.bot.ThrowHearing=true;second=new(){ProfileId="second"};BossPlayers.Followers.Add(new(){Bot=second});Sound(f);
        Check(second.Callbacks==1,"Bad_sensor_does_not_block_other_followers");
        f=Fresh();f.dispatcher.PlaySound(null!,default,50,AISoundType.gun);Check(f.bot.Callbacks==0,"Null_source");
        return checks;
    }
}
'@
$policy = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerAimTargetPolicy.cs')
$bodyMethods = foreach($name in @('TryGetBodyFirstShootPoint','GetEligiblePart','IsEligiblePart')) {
    $match=[regex]::Match($policy,'(?ms)^        (?:internal|private) static [^\r\n]*\b'+$name+'\(.*?^        \}')
    if(!$match.Success){throw "Missing body eligibility method: $name"}
    $match.Value
}
$fixture = $fixture.Replace('__BODY_METHODS__',($bodyMethods -join $nl)).Replace('__TARGET__', $target).Replace('__DISPATCH__', $dispatch)
$imports += 'using pitTeam.BigBrain;'
$harmonyPath = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
$harness = '#nullable enable' + $nl + ($imports -join $nl) + $nl + $fixture + $nl + $aimSource
$harness += $nl + 'public static class TestEntry { public static int Main(){try{Console.WriteLine("Passed "+CompatibilityChecks.Run()+" compatibility checks with real Harmony.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}} }'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sdk = (dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot locate the .NET SDK compiler.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-compat-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sourcePath = Join-Path $testRoot 'Compatibility.cs'
    $exePath = Join-Path $testRoot 'Compatibility.exe'
    [IO.File]::WriteAllText($sourcePath, $harness)
    Copy-Item -LiteralPath $harmonyPath -Destination $testRoot
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } |
        Copy-Item -Destination $testRoot
    $arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nullable:enable', '/nostdlib+',
        "/out:$exePath", "/reference:$harmonyPath")
    foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
        $arguments += '/reference:' + (Join-Path $frameworkRoot $reference)
    }
    $arguments += $sourcePath
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility harness compilation failed.' }
    & $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility checks failed.' }
    Write-Output 'Production routing verified with controlled policy/sensor stand-ins; actual perception, aiming, and movement still require a raid.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedRoot.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedRoot) -notlike 'pitFireTeam-sain-compat-*') {
        throw 'Refusing to clean a test directory outside the expected temporary location.'
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
