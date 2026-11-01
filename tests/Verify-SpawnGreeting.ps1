param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
$method = [regex]::Match($source, '(?ms)^        private static void TrySayFollowerReadyControlled\(.*?^        \}').Value
if (!$method) { throw 'Missing production spawn greeting' }
$fixture = @'
using System;
using System.Collections.Generic;
public enum EPhraseTrigger { Ready, MumblePhrase }
public enum EBotState { Active, Disposed }
public enum WildSpawnType { pmcUSEC, exUsec, bossKnight, followerBigPipe, followerBirdEye }
public class Settings { public WildSpawnType Role; }
public class Info { public Settings Settings = new Settings(); }
public class Profile { public Info Info = new Info(); }
public class BotOwner {
    public bool IsDead; public EBotState BotState; public Profile Profile = new Profile();
    public Talk BotTalk; public BotOwner(){BotTalk=new Talk(this);}
}
public class Talk {
    readonly BotOwner owner; public int Calls; public EPhraseTrigger Phrase; public bool Fail;
    public Talk(BotOwner bot){owner=bot;}
    public void TrySay(EPhraseTrigger phrase,bool groupDelay){
        if(groupDelay || !ReferenceEquals(FollowerForcedPhraseGate.Owner,owner) || FollowerForcedPhraseGate.Phrase!=phrase)
            throw new Exception("greeting must arm its exact target/phrase at playback");
        Calls++; Phrase=phrase; if(Fail) throw new Exception("voice failed");
    }
}
public static class FollowerForcedPhraseGate {
    public static BotOwner Owner;public static EPhraseTrigger Phrase;public static int Clears;
    public static void Arm(BotOwner bot,EPhraseTrigger phrase,float duration){Owner=bot;Phrase=phrase;}
    public static void Clear(BotOwner bot){if(!ReferenceEquals(Owner,bot))throw new Exception("wrong owner");Owner=null;Clears++;}
}
namespace Utils {
    public static class Props { public static List<WildSpawnType> BossFollowersType=new List<WildSpawnType>{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}; }
    public static class Utils {public static Action Pending;public static void SetTimeout(Action run,int delay){if(delay!=2000)throw new Exception("delay changed");Pending=run;} }
}
public class Fixture {
__METHOD__
    static int checks;
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    public static void Main(){
        foreach(WildSpawnType role in Enum.GetValues(typeof(WildSpawnType))){
            var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;
            TrySayFollowerReadyControlled(bot,2000,()=>true);
            Check(bot.BotTalk.Calls==0 && FollowerForcedPhraseGate.Owner==null,"greeting waits for playback");
            Utils.Utils.Pending();
            var expected=Utils.Props.BossFollowersType.Contains(role)?EPhraseTrigger.MumblePhrase:EPhraseTrigger.Ready;
            Check(bot.BotTalk.Calls==1 && bot.BotTalk.Phrase==expected,"correct role greeting: "+role);
            Check(FollowerForcedPhraseGate.Owner==null,"forced phrase released after playback");
        }
        foreach(string change in new[]{"readiness","dead","disposed"}){
            var bot=new BotOwner();bool ready=true;TrySayFollowerReadyControlled(bot,2000,()=>ready);
            if(change=="readiness")ready=false;else if(change=="dead")bot.IsDead=true;else bot.BotState=EBotState.Disposed;
            Utils.Utils.Pending();Check(bot.BotTalk.Calls==0 && FollowerForcedPhraseGate.Owner==null,"delayed validation: "+change);
        }
        Utils.Utils.Pending=null;TrySayFollowerReadyControlled(new BotOwner(),2000,()=>false);
        Check(Utils.Utils.Pending==null,"unready follower cannot schedule a greeting");
        var failing=new BotOwner();failing.BotTalk.Fail=true;TrySayFollowerReadyControlled(failing,2000,()=>true);
        try{Utils.Utils.Pending();}catch(Exception){}
        Check(FollowerForcedPhraseGate.Owner==null,"voice failure releases the forced gate");
        Console.WriteLine("PASS: "+checks+" production spawn-greeting checks; no voice assets played.");
    }
}
'@
$fixture = $fixture.Replace('__METHOD__', $method)
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('follower-spawn-greeting-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $file = Join-Path $temporary 'Fixture.cs'; [IO.File]::WriteAllText($file, $fixture)
    $exe = Join-Path $temporary 'Fixture.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" $file
    if ($LASTEXITCODE -ne 0) { throw 'Spawn greeting fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Spawn greeting fixture failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
