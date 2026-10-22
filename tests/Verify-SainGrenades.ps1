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
    if(@($throw).Count -ne 1 -or $throw.IsStatic -or $throw.Parameters.Count -ne 0 -or $throw.ReturnType.FullName -ne 'System.Boolean'){throw 'Installed grenade start boundary changed'}
    if(@($throw.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'BotGrenadeController' -and $_.Operand.Name -eq 'DoThrow'}).Count -ne 1){throw 'Installed native throw path changed'}
    $friendly=$type.Methods | Where-Object Name -eq 'CheckFriendlyDistances'
    if($friendly.ReturnType.FullName -ne 'System.Boolean' -or ($friendly.Parameters.ParameterType.FullName -join '|') -ne 'UnityEngine.Vector3'){throw 'Installed native grenade friend check changed'}
    Write-Output 'Installed SAIN 4.5.1 grenade decision enable switches and EFT DoThrow path verified.'
} finally {$metadata.Dispose()}
foreach($assemblyPath in @((Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),(Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
    $game=[Mono.Cecil.ModuleDefinition]::ReadModule($assemblyPath)
    try {
        $hands=$game.GetType('EFT.Player/BaseGrenadeHandsController')
        $release=$hands.Methods | Where-Object Name -eq 'ThrowGrenade'
        $spawn=$hands.Methods | Where-Object Name -eq 'CreateAndRegisterGrenade'
        if(($release.Parameters.ParameterType.FullName -join '|') -ne 'System.Single|UnityEngine.Vector3|UnityEngine.Quaternion|UnityEngine.Vector3|System.Boolean' -or $release.ReturnType.FullName -ne 'System.Void'){throw 'Release safety signature changed'}
        if(($spawn.Parameters.ParameterType.FullName -join '|') -ne 'UnityEngine.Vector3|UnityEngine.Quaternion|UnityEngine.Vector3|System.Single' -or $spawn.ReturnType.FullName -ne 'EFT.Grenade'){throw 'Physical grenade creation signature changed'}
        $calls=@($release.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.Name})
        $inventory=[Array]::IndexOf($calls,'ThrowGrenade');$creation=[Array]::IndexOf($calls,'CreateAndRegisterGrenade')
        if($inventory -lt 0 -or $creation -le $inventory){throw 'Review changed grenade inventory/spawn ordering'}
        $playerField=$game.GetType('EFT.Player/ItemHandsController').Fields | Where-Object Name -eq '_player'
        if($playerField.FieldType.FullName -ne 'EFT.Player'){throw 'Injected player field changed'}
        $finish=$game.GetType('EFT.Player/GrenadeHandsController/HighThrowOperation').Methods | Where-Object Name -eq 'OnDropFinishedAction'
        if('_onUsedCallback' -notin $finish.Body.Instructions.Operand.Name){throw 'Native completion cleanup changed'}
        $drop=$game.GetType('EFT.Player/GrenadeHandsController/HighThrowOperation').Methods | Where-Object Name -eq 'SpawnGrenade'
        if('Throw' -notin $drop.Body.Instructions.Operand.Name){throw 'Native physical throw boundary changed'}
        Write-Output "Verified pre-inventory release veto, physical spawn and later completion callback: $assemblyPath"
    } finally {$game.Dispose()}
}
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pit-sain-grenades-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $native=Get-Content -Raw (Join-Path $SainSourceRoot 'Classes/Bot/WeaponFunction/Grenades/GrenadeThrowDecider.cs')
    $decision=[regex]::Match($native,'(?ms)^    public bool GetDecision\(.*?^    [}]').Value
    $throw=[regex]::Match($native,'(?ms)^    private bool TryThrowGrenade\(.*?^    [}]').Value
    $friendly=[regex]::Match($native,'(?ms)^    private bool CheckFriendlyDistances\(.*?^    [}]').Value
    if(!$decision -or !$throw -or !$friendly){throw 'Native grenade source boundaries changed'}
    $fixture=Get-Content -Raw (Join-Path $RepositoryRoot 'tests/SainGrenadeFixture.cs')
    $fixture=$fixture.Replace('__NATIVE_DECISION__',$decision).Replace('__NATIVE_THROW__',$throw).Replace('__NATIVE_FRIENDLY__',$friendly)
    $safety=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerShotSafety.cs')
    $geometry=@()
    foreach($name in @('IsFriendlyNearGrenadeImpact','IsPlayerNearGrenadeImpact','IsFollowerMovementSegmentNearImpact','DistancePointToSegmentXZSqr','GetSafeName','IsFinite')) {
        $body=[regex]::Match($safety,"(?ms)^        (?:public|private) static [^`r`n]+ $name\(.*?^        [}]").Value
        if(!$body){throw "Core impact geometry boundary changed: $name"}
        $geometry+=$body
    }
    $fixture=$fixture.Replace('__CORE_IMPACT_GEOMETRY__',($geometry -join "`n"))
    [IO.File]::WriteAllText((Join-Path $temporary 'Fixture.cs'),$fixture)
    foreach($path in @('addon/SainGrenadeThrowBridge.cs','client/Modules/FollowerGrenadeRuntimeGate.cs','client/Modules/FollowerGrenadeCooldowns.cs','client/Patches/FollowerGrenadeAvailabilityPatch.cs','client/Patches/FollowerGrenadeCooldownPatch.cs')) {
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot $path) -Destination $temporary
    }
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
