param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    $source=Get-Content -Raw (Join-Path $RepositoryRoot 'addon/SainEnemyTracking.cs')
    $types=[regex]::Matches($source,'"(SAIN\.[^"]+)"') | ForEach-Object {$_.Groups[1].Value}
    $reads=@('get_LastKnownPosition','get_Position','get_HasArrivedPersonal','get_HasArrivedSquad','get_DistanceToBot','EnemyHeadAtPosition','Distance','DistanceSqr')
    $readOwners=@('SAIN.SAINComponent.Classes.EnemyClasses.Enemy','SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces','SAIN.SAINComponent.Classes.EnemyClasses.EnemyPlace')
    $consumerReads=0
    foreach($name in $types){
        $type=$native.MainModule.Types | Where-Object FullName -eq $name
        if(!$type){throw "Missing native tactical type: $name"}
        foreach($owner in @($type)+@($type.NestedTypes)){
            foreach($method in $owner.Methods){if($method.HasBody){
                $consumerReads+=@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in $reads -and $_.Operand.DeclaringType.FullName -in $readOwners}).Count
            }}
        }
    }
    if($consumerReads -lt 10){throw 'Native tactical position consumer coverage changed'}
    $checker=$native.MainModule.Types | Where-Object FullName -eq 'SAIN.Components.BotComponentSpace.Classes.EnemyClasses.EnemyKnownChecker'
    $known=@($checker.Methods | Where-Object {$_.Name -eq 'ShallKnowEnemy' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.Single|System.Single|System.Boolean' -and $_.ReturnType.FullName -eq 'System.Boolean'})
    if($known.Count -ne 1 -or !($checker.Fields | Where-Object {$_.Name -eq 'Enemy' -and $_.FieldType.FullName -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'})){throw 'Native eligibility boundary changed'}
    $places=$native.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces'
    if(!($places.Fields | Where-Object Name -eq 'Enemy') -or !($places.Methods | Where-Object Name -eq 'get_TimeLastKnownUpdated')){throw 'Known-place provenance boundary changed'}
    $path=$native.MainModule.Types | Where-Object FullName -eq 'SAIN.Classes.Bot.Search.SearchPathFinder'
    if(@($path.Methods | Where-Object {$_.Name -eq 'checkFinishedSearch' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'Native search completion boundary changed'}
    $info=$native.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Info.SAINBotInfoClass'
    if(!($info.Fields | Where-Object {$_.Name -eq '<ForgetEnemyTime>k__BackingField' -and $_.FieldType.FullName -eq 'System.Single'})){throw 'Native forget timer boundary changed'}
    Write-Output "Installed tracking metadata validated: $consumerReads tactical reads, eligibility, search completion, report timestamps and duration owner."
} finally {$native.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-tracking-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $exe=Join-Path $temporary 'Tracking.exe'
    # Compile the actual repair method alongside the contact service to exercise their boundary.
    $enemySource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/Enemy.cs')
    $repairStart=$enemySource.IndexOf('        public static void RepairPersonalMemory(')
    $repairEnd=$enemySource.IndexOf('        /// <summary>', $repairStart)
    if($repairStart -lt 0 -or $repairEnd -le $repairStart){throw 'Personal memory repair method boundary missing'}
    $repairSource=Join-Path $temporary 'EnemyRepair.cs'
    [IO.File]::WriteAllText($repairSource, "using EFT; using UnityEngine; using pitTeam.Modules; namespace pitTeam.Utils { public static class Enemy { private static bool IsFinite(Vector3 p) => FollowerEnemyTracking.IsFinite(p); " + $enemySource.Substring($repairStart,$repairEnd-$repairStart) + ' } }')
    $argsList=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+','/nowarn:8632',"/out:$exe")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$argsList+='/reference:'+(Join-Path $framework $reference)}
    foreach($source in @('client/Modules/FollowerEnemyTracking.cs','addon/SainEnemyTrackingPolicy.cs','tests/EnemyTrackingFixture.cs')){$argsList+=(Join-Path $RepositoryRoot $source)}
    $argsList+=$repairSource
    # Exercise the production marker refresh condition, including the addon cadence bypass.
    $markerSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/PingTeamates.cs')
    $refreshMatch=[regex]::Match($markerSource,'bool shouldRefreshPosition\s*=\s*([^;]+);')
    if(!$refreshMatch.Success){throw 'Marker refresh condition missing'}
    $markerFixture=Join-Path $temporary 'MarkerRefresh.cs'
    [IO.File]::WriteAllText($markerFixture, 'using pitTeam.Modules; using UnityEngine; public static class MarkerRefresh { public static bool Check(bool visible, bool sain, bool captureHiddenPosition, bool captured, float due) { var resolution = new { IsVisible = visible, UsesSainKnowledge = sain }; var contact = new { HasCapturedPosition = captured, NextHiddenPositionRefreshTime = due }; return ' + $refreshMatch.Groups[1].Value + '; } }')
    $argsList+=$markerFixture
    & dotnet @argsList
    if($LASTEXITCODE -ne 0){throw 'Tracking fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Tracking behavior fixture failed'}
    $hookExe=Join-Path $temporary 'Hooks.exe'
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    $hookArgs=@($compiler,'/nologo','/target:exe','/langversion:latest','/nostdlib+',"/out:$hookExe","/reference:$harmony",(Join-Path $RepositoryRoot 'tests/EnemyTrackingHookFixture.cs'))
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$hookArgs+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @hookArgs
    if($LASTEXITCODE -ne 0){throw 'Tracking hook fixture compilation failed'}
    & $hookExe $RepositoryRoot $GameRoot
    if($LASTEXITCODE -ne 0){throw 'Production native tracking IL validation failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-tracking-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
