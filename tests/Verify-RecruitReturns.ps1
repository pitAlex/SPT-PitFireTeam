param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
$method=$source.IndexOf('public static void SaveFollowersProgress(')
$first=$source.IndexOf('                    Profile pr = item.GetBot().Profile;', $method)
$last=$source.IndexOf('                    string progressSaveKey =', $first)
if($first -lt 0 -or $last -le $first){throw 'Production recruit capture boundary changed'}
$candidates=[regex]::Match($source,'(?ms)^    internal class RecruitPickupCandidateRequest.*?(?=^    internal class RecruitPickupRequest)').Value
if(!$candidates){throw 'Recruit request contract missing'}
$work=Join-Path $RepositoryRoot 'tests/artifacts/recruit-returns'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$body=$source.Substring($first,$last-$first)
[IO.File]::WriteAllText((Join-Path $work 'Production.cs'),"using System; using System.Collections.Generic; using System.Linq;`n$candidates`npartial class RecruitHarness {`npublic static List<RecruitPickupCandidateRequest> Capture(params Follower[] followers) {var recruitCandidates=new List<RecruitPickupCandidateRequest>();var _defaultJsonConverters=new object();foreach(var item in followers){`n$body`n}return recruitCandidates;}}")
dotnet run --project (Join-Path $RepositoryRoot 'tests/RecruitReturns') -c Debug
if($LASTEXITCODE -ne 0){throw 'Production recruit-return capture fixture failed'}
