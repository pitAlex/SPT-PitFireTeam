param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka',[string]$SainSourceRoot='F:/Projects/SPT-Tarkov/SPT-4.1.3/SAIN-4.5.1/SAIN')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$metadata=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    if($metadata.Name.Version.ToString() -ne '4.5.1.0'){throw 'Grenade bridge requires SAIN 4.5.1'}
    $type=$metadata.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.WeaponFunction.GrenadeThrowDecider'
    $method=$type.Methods | Where-Object {$_.Name -eq 'GetDecision' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy|System.String&'}
    if(!$method -or $method.ReturnType.FullName -ne 'System.Boolean'){throw 'Installed grenade decision boundary changed'}
    foreach($switch in @('_grenadesEnabled','_canThrowGrenades','BotVsBotGrenade')) {
        if(@($method.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq $switch}).Count -ne 1){throw "Installed enable switch changed: $switch"}
    }
    $throw=$type.Methods | Where-Object Name -eq 'TryThrowGrenade'
    if(@($throw.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'BotGrenadeController' -and $_.Operand.Name -eq 'DoThrow'}).Count -ne 1){throw 'Installed native throw path changed'}
    Write-Output 'Installed SAIN 4.5.1 grenade decision enable switches and EFT DoThrow path verified.'
} finally {$metadata.Dispose()}
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pit-sain-grenades-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $native=Get-Content -Raw (Join-Path $SainSourceRoot 'Classes/Bot/WeaponFunction/Grenades/GrenadeThrowDecider.cs')
    $decision=[regex]::Match($native,'(?ms)^    public bool GetDecision\(.*?^    [}]').Value
    $throw=[regex]::Match($native,'(?ms)^    private bool TryThrowGrenade\(.*?^    [}]').Value
    if(!$decision -or !$throw){throw 'Native grenade source boundaries changed'}
    $fixture=Get-Content -Raw (Join-Path $RepositoryRoot 'tests/SainGrenadeFixture.cs')
    $fixture=$fixture.Replace('__NATIVE_DECISION__',$decision).Replace('__NATIVE_THROW__',$throw)
    [IO.File]::WriteAllText((Join-Path $temporary 'Fixture.cs'),$fixture)
    foreach($path in @('addon/SainGrenadeThrowBridge.cs','client/Modules/FollowerGrenadeRuntimeGate.cs','client/Modules/FollowerGrenadeCooldowns.cs','client/Patches/FollowerGrenadeAvailabilityPatch.cs')) {
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot $path) -Destination $temporary
    }
    $patch=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerGrenadeCooldownPatch.cs')
    $end=$patch.IndexOf('    internal class FollowerGrenadeReleasePatch')
    if($end -lt 0){throw 'Core grenade hook boundaries changed'}
    [IO.File]::WriteAllText((Join-Path $temporary 'CoreThrowPatch.cs'),$patch.Substring(0,$end)+"`n}")
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'client/libs/0Harmony.dll') -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $sdk=dotnet --list-sdks | Select-Object -Last 1
    if($sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'SDK path unavailable'}
    $compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
    $framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $exe=Join-Path $temporary 'Grenades.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+','/nowarn:8632,0649',"/out:$exe",('/reference:'+(Join-Path $temporary '0Harmony.dll')))
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $sources=Get-ChildItem -LiteralPath $temporary -Filter '*.cs' | ForEach-Object FullName
    & dotnet @arguments @sources
    if($LASTEXITCODE -ne 0){throw 'Grenade integration compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Grenade integration fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pit-sain-grenades-*'){throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
