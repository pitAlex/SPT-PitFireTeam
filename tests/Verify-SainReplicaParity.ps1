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
    $replica=$replica.Replace(' || !pitFireTeam.UseSainFollowerCombat(BotOwner)','')
    $replica=[regex]::Replace($replica,'SAINActionTypes.Get\("(?:Solo|Squad)\.(?:Cover\.)?([^"]+)"\)','typeof($1)')
    $replica=$replica.Replace('SAINFollowerSquadRegroupAction','RegroupAction').Replace('SAINFollowerFollowSearchPartyAction','FollowSearchParty')
    foreach($method in @('GetNextAction','IsActive','IsCurrentActionEnding')){
        if((Normalize (Method $native $method)) -ne (Normalize (Method $replica $method))){throw "$kind replica diverges in $method"}
        $count++;Write-Output "PASS $kind $method matches native SAIN 4.5.1 after ownership/type substitutions"
    }
}
$native=Read-Source (Join-Path $SainSourceRoot 'Classes/Bot/Decision/SquadDecisionClass.cs')
$replica=Read-Source (Join-Path $RepositoryRoot 'addon/SAINFollowerSquadDecision.cs')
$native=$native.Substring($native.IndexOf('public class '));$replica=$replica.Substring($replica.IndexOf('public class '))
$native=$native.Replace('SquadDecisionClass','SAINFollowerSquadDecision')
$native=$native.Replace('!Squad.BotInGroup || Bot.Squad.SquadInfo?.LeaderComponent == null || Squad.LeaderComponent?.IsDead == true','!Squad.BotInGroup || !SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player leader) || leader.HealthController?.IsAlive != true')
$native=$native.Replace('Bot.Squad.LeaderComponent != null && shallGroupSearch()','shallGroupSearch()')
$native=$native.Replace('var lead = squad.LeaderComponent;','SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player lead);').Replace('lead.Transform.Position','lead.Position')
if((Normalize $native) -ne (Normalize $replica)){throw 'Squad decision policy/settings diverge beyond player-leader substitutions'}
$count++;Write-Output 'PASS complete squad provider preserves native branch order, thresholds, and disabled regroup selection'

foreach($entry in @(
    @{Native='RegroupAction';Replica='SAINFollowerSquadRegroupAction';Methods=@('Update','OnSteeringTicked')},
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
Write-Output "Passed $count source parity checks. Registration, ownership, internal type resolution, and player-leader substitutions are intentional differences."