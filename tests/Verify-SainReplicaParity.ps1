param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$SainSourceRoot)
$ErrorActionPreference='Stop'
function Read-Source([string]$path){(Get-Content -Raw $path).Replace([string][char]13,'')}
function Normalize([string]$source){
    $source=[regex]::Replace($source,'(?m)//.*$','')
    [regex]::Replace($source,'\s+','')
}
function Method([string]$source,[string]$name){
    $match=[regex]::Match($source,'(?m)^\s*(?:public|private|protected)[^\r\n]*\b'+[regex]::Escape($name)+'\(')
    if(!$match.Success){throw "Missing method $name"}
    $start=$source.IndexOf('{',$match.Index)
    $depth=0
    for($i=$start;$i -lt $source.Length;$i++){
        if($source[$i] -eq '{'){$depth++}
        if($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($match.Index,$i-$match.Index+1)}}
    }
    throw "Unbalanced method $name"
}
$count=0
foreach($kind in @('Solo','Squad')){
    $native=Read-Source (Join-Path $SainSourceRoot ('Layers/Combat/'+$kind+'/Combat'+$kind+'Layer.cs'))
    $replica=Read-Source (Join-Path $RepositoryRoot ('addon/SAINFollower'+$kind+'CombatLayer.cs'))
    # The post-checkpoint linger extension is tested behaviorally; native combat routing
    # must still match after removing only its three explicitly delimited entry guards.
    $handoffPattern='(?ms)^[ \t]*// BEGIN addon post-combat handoff\n.*?^[ \t]*// END addon post-combat handoff\n'
    if([regex]::Matches($replica,$handoffPattern).Count -ne 3){throw "$kind handoff guard count changed"}
    $replica=[regex]::Replace($replica,$handoffPattern,'')
    if($kind -eq 'Solo'){
        $relocation='(?ms)^[ \t]*// BEGIN addon relocation\n.*?^[ \t]*// END addon relocation\n'
        if([regex]::Matches($replica,$relocation).Count -ne 2){throw 'Solo relocation guards changed'}
        $replica=[regex]::Replace($replica,$relocation,'')
        $pushHold='(?ms)^[ \t]*// BEGIN addon push hold\n.*?^[ \t]*// END addon push hold\n'
        if([regex]::Matches($replica,$pushHold).Count -ne 2){throw 'Solo push hold guard count changed'}
        $replica=[regex]::Replace($replica,$pushHold,'')
    }
    if($kind -eq 'Squad'){
        $regroupEnd='(?ms)^[ \t]*// BEGIN addon regroup ending\n.*?^[ \t]*// END addon regroup ending\n'
        if([regex]::Matches($replica,$regroupEnd).Count -ne 1){throw 'Squad regroup ending guard changed'}
        $replica=[regex]::Replace($replica,$regroupEnd,'')
    }
    $replica=$replica.Replace(' || !pitFireTeam.UseSainFollowerCombat(BotOwner)','')
    $replica=[regex]::Replace($replica,'SAINActionTypes.Get\("(?:Solo|Squad)\.(?:Cover\.)?([^"]+)"\)','typeof($1)')
    $replica=$replica.Replace('SAINFollowerSquadRegroupAction','RegroupAction').Replace('SAINFollowerFollowSearchPartyAction','FollowSearchParty').Replace('SAINFollowerMoveToEngageAction','MoveToEngageAction')
    foreach($method in @('GetNextAction','IsActive','IsCurrentActionEnding')){
        $replicaName=switch($method){'GetNextAction' {'SelectAction'} 'IsCurrentActionEnding' {'ShouldEndAction'} default {$method}}
        $replicaMethod=(Method $replica $replicaName).Replace('private Action SelectAction','public override Action GetNextAction').Replace('private bool ShouldEndAction','public override bool IsCurrentActionEnding')
        if((Normalize (Method $native $method)) -ne (Normalize $replicaMethod)){throw "$kind replica diverges in $method"}
        $count++;Write-Output "PASS $kind $method matches native SAIN 4.5.1 after ownership/type substitutions and explicit linger guards"
    }
}
$native=Read-Source (Join-Path $SainSourceRoot 'Classes/Bot/Decision/SquadDecisionClass.cs')
$replica=Read-Source (Join-Path $RepositoryRoot 'addon/SAINFollowerSquadDecision.cs')
$native=$native.Substring($native.IndexOf('public class '));$replica=$replica.Substring($replica.IndexOf('public class '))
$regroupEntry='(?ms)^[ \t]*// BEGIN addon regroup objective\n.*?^[ \t]*// END addon regroup objective\n'
if([regex]::Matches($replica,$regroupEntry).Count -ne 1){throw 'Squad regroup objective entry changed'}
$replica=[regex]::Replace($replica,$regroupEntry,'')
# The unused native automatic-regroup method/settings are superseded by the tested
# follower objective. All remaining native squad policy still compares in full.
$unused=$native.IndexOf('    float SquadDecision_Regroup_NoEnemy_StartDist')
if($unused -lt 0){throw 'Native unused regroup section changed'}
$native=$native.Substring(0,$unused)+'}'
$native=$native.Replace('SquadDecisionClass','SAINFollowerSquadDecision')
$native=$native.Replace('!Squad.BotInGroup || Bot.Squad.SquadInfo?.LeaderComponent == null || Squad.LeaderComponent?.IsDead == true','!Squad.BotInGroup || !SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player leader) || leader.HealthController?.IsAlive != true')
$native=$native.Replace('Bot.Squad.LeaderComponent != null && shallGroupSearch()','shallGroupSearch()')
$native=$native.Replace('var lead = squad.LeaderComponent;','SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player lead);').Replace('lead.Transform.Position','lead.Position')
if((Normalize $native) -ne (Normalize $replica)){throw 'Squad decision policy/settings diverge beyond player-leader substitutions'}
$count++;Write-Output 'PASS existing squad policy preserves native branch order and thresholds around the explicit regroup extension'

foreach($entry in @(
    @{Native='FollowSearchParty';Replica='SAINFollowerFollowSearchPartyAction';Methods=@('Update','OnSteeringTicked','MoveToLead','GetPosNearLead','Start','Stop')}
)){
    $native=Read-Source (Join-Path $SainSourceRoot ('Layers/Combat/Squad/'+$entry.Native+'.cs'))
    $replica=Read-Source (Join-Path $RepositoryRoot ('addon/'+$entry.Replica+'.cs'))
    $native=$native.Replace('var SquadLeadPos = Bot.Squad.LeaderComponent?.Position;','Vector3? SquadLeadPos = SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player leader) && leader.HealthController?.IsAlive == true ? leader.Position : (Vector3?)null;')
    $native=$native.Replace('var leader = Bot.Squad.SquadInfo?.LeaderComponent;','SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player leader);').Replace('if (leader == null)','if (leader == null || leader.HealthController?.IsAlive != true)')
    foreach($method in $entry.Methods){
        if((Normalize (Method $native $method)) -ne (Normalize (Method $replica $method))){throw ($entry.Replica+' diverges in '+$method)}
        $count++;Write-Output ('PASS '+$entry.Replica+' '+$method+' retains native behavior with player leader')
    }
}
Write-Output "Passed $count source parity checks. Registration, ownership, internal type resolution, player-leader substitutions, post-combat linger guards, recorder lifecycle wrappers, the bounded MoveToEngage action, the tested push objective and stationary hold, the tested combat relocation objective/action, and the tested two-mode regroup objective/action are intentional differences."