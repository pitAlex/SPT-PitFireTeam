param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
function Extract($source,$name) {
    $match=[regex]::Match($source, '(?ms)^        (?:public|private|internal) static [^\r\n]+ '+$name+'\(.*?^        \}')
    if(!$match.Success){throw "Missing production boundary: $name"}
    $match.Value
}
$awareness=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerAwareness.cs')
$state=[regex]::Match($awareness,'(?ms)^        private sealed class State.*?^        \}').Value
if(!$state){throw 'Missing awareness state'}
$threat=Extract $awareness 'RegisterThreatLookPoint'
$fire=Extract $awareness 'TryGetRecentFireThreatLookPoint'
$threatened=Extract $awareness 'WasRecentlyThreatened'
$teleport=Extract (Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')) 'TryFindTeleportSpot'
$bridgeSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/SainAddonBridge.cs')
$bridge=foreach($name in @('IsAddonTacticSelected','IsSainManSelected')) {
    $match=[regex]::Match($bridgeSource,'(?ms)^        public static bool '+$name+'\(.*?;')
    if(!$match.Success){throw "Missing SAIN owner selection: $name"}
    $match.Value
}
$generated=@"
using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.AI;
public static class Awareness {
$state
static State state=new State(); static State GetState(BotOwner bot)=>state;
public static void Reset()=>state=new State();
public static bool IsHostileToBossGroup(BotOwner bot,Player player)=>player.Hostile;
public static void Register(BotOwner bot,Vector3 point,Player source,bool incoming)=>RegisterThreatLookPoint(bot,point,3f,threatSource:source,incomingFire:incoming);
$threat
$fire
$threatened
}
public static class Teleport {
public static bool Find(Vector3 player,List<Vector3> reserved,out Vector3 target)=>TryFindTeleportSpot(player,reserved,0,out target);
$teleport
}
public static class SainAddonBridge {
$($bridge -join "`n")
}
"@
$dir=Join-Path $RepositoryRoot 'tests/artifacts/reserve-regressions'
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'Production.cs'),$generated)
dotnet run --project (Join-Path $RepositoryRoot 'tests/ReserveRegression') -c Debug -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Reserve regression fixture failed'}
