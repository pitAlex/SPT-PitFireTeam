param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$sain=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
$core=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/pitFireTeam/pitFireTeam.dll'))
try {
    $type=$sain.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Decision.SelfActionDecisionClass'
    if(@($type.Methods | Where-Object {$_.Name -eq 'GetDecision' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'SAIN.Preset.Shared.Enums.ESelfActionType&|SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'Native self-action provider signature changed'}
    $common=$core.MainModule.Types | Where-Object FullName -eq 'pitTeam.BigBrain.FollowerCombatCommon'
    foreach($name in @('CountLoadedRounds','IsGrenadeLauncherWeapon')) {
        $return=if($name -eq 'CountLoadedRounds'){'System.Int32'}else{'System.Boolean'}
        if(@($common.Methods | Where-Object {$_.Name -eq $name -and $_.IsStatic -and $_.ReturnType.FullName -eq $return -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.InventoryLogic.Weapon'}).Count -ne 1){throw "Installed Core weapon helper changed: $name"}
    }
} finally {$sain.Dispose();$core.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-weapons-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'Weapons.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $arguments+=(Join-Path $RepositoryRoot 'addon/SainEmergencyWeaponBridge.cs')
    $arguments+=(Join-Path $RepositoryRoot 'tests/SainEmergencyWeaponFixture.cs')
    & dotnet @arguments
    if($LASTEXITCODE -ne 0){throw 'Emergency weapon harness compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Emergency weapon harness failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-sain-weapons-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
