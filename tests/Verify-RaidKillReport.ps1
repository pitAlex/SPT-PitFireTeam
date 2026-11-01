param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot)
$ErrorActionPreference = 'Stop'
$follower = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$property = [regex]::Match($follower, '(?m)^        internal bool ReportsRaidKills => .*;').Value
$patch = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/PlayerPatch.cs')
$lookup = $patch.IndexOf('BotFollowerPlayer follower = BossPlayers.GetFollowers().Find(')
$first = $patch.IndexOf('if (follower == null)', $lookup)
$last = $patch.IndexOf('TryCreditFollowerKillQuestProgress(', $first)
if (!$property -or $lookup -lt 0 -or $first -lt 0 -or $last -le $first) { throw 'Missing production kill-report boundary' }
$gate = $patch.Substring($first, $last - $first)
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')).Contains('if (_follower.ReportsRaidKills)')) {
    throw 'Kill-report registration must use the same eligibility as kill capture'
}
$fixture = @'
using System;
using System.Collections.Generic;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
namespace EFT {
    public class VictimStats {
        public string AccountId,ProfileId,Name,Side,BodyPart,Weapon,Role,Location;
        public TimeSpan Time;public int Level,PrestigeLevel;public float Distance;
    }
    public class Stats { public List<VictimStats> Victims=new List<VictimStats>(); }
    public class Profile { public string Nickname;public Stats EftStats=new Stats(); }
    public class Player { public string ProfileId;public Profile Profile=new Profile(); }
}
namespace EFT.InventoryLogic {
    public class Item { public T GetItemComponent<T>() where T:class=>null;public IEnumerable<Item> GetAllItems()=>new[]{this}; }
    public class CompoundItem:Item {}
    public class DogtagComponent {public string ProfileId;}
}
namespace pitTeam {
    public static class pitFireTeam {public static Log Log=new Log();}
    public class Log {public int Warnings;public void LogWarning(string message){Warnings++;}}
}
namespace pitTeam.Components {
    public class BotFollowerPlayer {public bool IsSquadMate;
__PROPERTY__
    }
    public class BossFollowerPlayer:BotFollowerPlayer {}
}
namespace pitTeam.Modules {public static class Logger {public static void LogInfo(string value){}public static void LogError(object value){throw new Exception(value.ToString());}}}
public class Fixture {
    static int checks,ProgressCredits;
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    static void Capture(BotFollowerPlayer follower,Player followerPlayer,Player __instance){
__GATE__
        ProgressCredits++;
    }
    public static void Main(){
        foreach(string role in new[]{"saved","Knight","Big Pipe","Birdeye","recruit","hostile"}){
            SquadRaidKillReport.BeginRaid(false);ProgressCredits=0;
            BotFollowerPlayer follower=role=="hostile"?null:role=="saved"||role=="recruit"?new BotFollowerPlayer{IsSquadMate=role=="saved"}:new BossFollowerPlayer();
            var killer=new Player{ProfileId="killer",Profile=new Profile{Nickname=role}};
            var victim=new Player{ProfileId="victim"};
            var native=new VictimStats{ProfileId="victim",AccountId="account",Name="Native victim",Weapon="rifle",Role="pmcBEAR",Distance=70,Time=TimeSpan.FromSeconds(15)};
            killer.Profile.EftStats.Victims.Add(native);
            Capture(follower,killer,victim);Capture(follower,killer,victim);
            var snapshot=SquadRaidKillReport.CreateResultSnapshot();
            bool eligible=role!="recruit"&&role!="hostile";
            Check(snapshot.Count==(eligible?1:0),"result eligibility: "+role);
            Check(ProgressCredits==(role=="saved"?2:0),"display admission does not broaden saved-member progression: "+role);
            if(!eligible)continue;
            Check(snapshot[0].TeammateNickname==role&&snapshot[0].Victims.Count==1,"native victims grouped once under killer: "+role);
            Check(snapshot[0].Victims[0].Weapon=="rifle"&&snapshot[0].Victims[0].Distance==70,"native victim detail retained: "+role);
            native.Name="changed";Check(snapshot[0].Victims[0].Name=="Native victim","native mutation cannot rewrite captured rows: "+role);
            snapshot[0].Victims[0].Name="changed snapshot";
            Check(SquadRaidKillReport.CreateResultSnapshot()[0].Victims[0].Name=="Native victim","result snapshots are independent: "+role);
            SquadRaidKillReport.BeginRaid(true);Check(SquadRaidKillReport.CreateResultSnapshot().Count==1,"transit preserves kills: "+role);
            SquadRaidKillReport.BeginRaid(false);Check(SquadRaidKillReport.CreateResultSnapshot().Count==0,"new raid resets kills: "+role);
        }
        SquadRaidKillReport.RegisterTeammate("empty-boss","Knight");Check(SquadRaidKillReport.CreateResultSnapshot().Count==0,"zero-kill groups stay hidden");
        var unrecorded=new Player{ProfileId="boss",Profile=new Profile{Nickname="Knight"}};
        Capture(new BossFollowerPlayer(),unrecorded,new Player{ProfileId="missing"});
        Check(SquadRaidKillReport.CreateResultSnapshot().Count==0&&pitTeam.pitFireTeam.Log.Warnings==1,"missing native victim is diagnosed without inventing a result row");
        Console.WriteLine("PASS: "+checks+" production kill-report eligibility/capture/snapshot checks; no live result UI.");
    }
}
'@
$fixture = $fixture.Replace('__PROPERTY__', $property).Replace('__GATE__', $gate)
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('raid-kill-report-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $file = Join-Path $temporary 'Fixture.cs'; [IO.File]::WriteAllText($file, $fixture)
    $exe = Join-Path $temporary 'Fixture.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" $file (Join-Path $RepositoryRoot 'client/Modules/SquadRaidKillReport.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Raid kill-report fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Raid kill-report fixture failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if ($GameRoot) {
    [Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')) | Out-Null
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
    try {
        foreach ($typeName in @('EFT.BaseStatisticsManager', 'EFT.DumbStatisticsManager')) {
            $method = $module.GetType($typeName).Methods | Where-Object Name -eq 'OnEnemyKill'
            $operands = @($method.Body.Instructions | Where-Object Operand | ForEach-Object { $_.Operand.ToString() })
            if (!($operands | Where-Object { $_ -match 'ProfileStats::Victims' }) -or
                !($operands | Where-Object { $_ -match 'BindableList.*VictimStats.*::Add\(' })) {
                throw "Installed native victim recording changed: $typeName"
            }
        }
        'Installed real/dumb statistics managers record native victim rows.'
    }
    finally { $module.Dispose() }
}
