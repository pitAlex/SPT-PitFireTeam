param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.SpawnPreparation.cs')
$method = [regex]::Match($source, '(?ms)^        private static async Task<Dictionary<string, BotDetails>> LoadSavedSpawnSettings\(.*?^        \}').Value
if (!$method) { throw 'Missing production saved-settings loader' }
$fixture = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using pitTeam.Modules;
public class BotDetails {public string Role;public float Aggression,SelfDetermination;public Dictionary<string,float> Proficiency;}
public static class RequestHandler {
    public static Queue<Func<string>> Responses=new();public static int Calls;
    public static string GetJson(string route){Calls++;return Responses.Dequeue()();}
}
public static class GameplayModeAddons {
    public static int Confirmed;public static bool Reject;
    public static void ConfirmSpawnSelection(string json){Confirmed++;if(Reject)throw new Exception("addon unavailable");}
}
namespace Modules {public static class Logger {public static void LogInfo(object value){} } }
public class Fixture {
__METHOD__
    static int checks;
    const string Good="[{\"Aid\":\"knight\",\"Role\":\"bossKnight\",\"Tactic\":\"Rifleman\",\"Aggression\":0,\"SelfDetermination\":90,\"Proficiency\":{\"VisionDistance\":125,\"VisionSpeed\":75,\"Accuracy\":140,\"AimSpeed\":100}}]";
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    static async Task Fails(Func<Task> action,string reason){try{await action();}catch{Check(true,reason);return;}Check(false,reason);}
    static void Setup(params Func<string>[] responses){RequestHandler.Calls=GameplayModeAddons.Confirmed=0;GameplayModeAddons.Reject=false;RequestHandler.Responses=new(responses);}
    public static async Task Main(){
        Func<string> failure=()=>throw new Exception("network failed");Func<string> good=()=>Good;
        Setup(failure,failure,good);
        var result=await LoadSavedSpawnSettings(new[]{"knight"},()=>true);
        Check(RequestHandler.Calls==3&&GameplayModeAddons.Confirmed==1,"only a successful retry confirms selection");
        Check(result["knight"].Aggression==0&&result["knight"].SelfDetermination==90&&result["knight"].Proficiency["Accuracy"]==140,"retry preserves saved values without substitution");
        Setup(failure,failure,failure);
        await Fails(()=>LoadSavedSpawnSettings(new[]{"knight"},()=>true),"failed lookup cannot return defaults");
        Check(RequestHandler.Calls==3&&GameplayModeAddons.Confirmed==0,"lookup retries are bounded and fail closed");
        Func<string> partial=()=>"[{\"Aid\":\"knight\",\"Role\":\"bossKnight\"}]";
        Setup(partial,partial,partial);
        await Fails(()=>LoadSavedSpawnSettings(new[]{"knight"},()=>true),"partial settings cannot be deserialized into defaults");
        Check(RequestHandler.Calls==3&&GameplayModeAddons.Confirmed==0,"invalid payload cannot release selection gates");
        bool valid=true;Setup(()=>{valid=false;return Good;});
        await Fails(()=>LoadSavedSpawnSettings(new[]{"knight"},()=>valid),"teardown after response cancels preflight");
        Check(RequestHandler.Calls==1&&GameplayModeAddons.Confirmed==0,"old response cannot publish into a new raid");
        Setup(good);
        await Fails(()=>LoadSavedSpawnSettings(new[]{"knight"},()=>false),"teardown before lookup cancels preflight");
        Check(RequestHandler.Calls==0,"invalid raid does not start a request");
        Setup(good,good,good);GameplayModeAddons.Reject=true;
        await Fails(()=>LoadSavedSpawnSettings(new[]{"knight"},()=>true),"missing addon provider aborts preflight");
        Check(RequestHandler.Calls==3,"addon handshake retries are bounded");
        Console.WriteLine("PASS: "+checks+" production saved-settings retry/cancellation checks.");
    }
}
'@
$fixtureRoot = Join-Path $RepositoryRoot 'tests/artifacts'
$temporary = Join-Path $fixtureRoot ('spawn-settings-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $metadata = [Security.SecurityElement]::Escape((Join-Path $RepositoryRoot 'client/Modules/FollowerSpawnMetadata.cs'))
    $json = [Security.SecurityElement]::Escape((Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll'))
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="' + $metadata + '" Link="FollowerSpawnMetadata.cs"/><Reference Include="Newtonsoft.Json"><HintPath>' + $json + '</HintPath></Reference></ItemGroup></Project>'
    [IO.File]::WriteAllText((Join-Path $temporary 'Fixture.csproj'), $project)
    [IO.File]::WriteAllText((Join-Path $temporary 'Fixture.cs'), $fixture.Replace('__METHOD__', $method))
    & dotnet run --project (Join-Path $temporary 'Fixture.csproj') -p:UseSharedCompilation=false -p:NuGetAudit=false --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Saved-settings fixture failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath($fixtureRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
