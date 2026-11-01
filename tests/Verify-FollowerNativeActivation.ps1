param(
    [Parameter(Mandatory)][string]$NativeSourceRoot,
    [Parameter(Mandatory)][string]$SptCustomSourceRoot,
    [string]$GameRoot='E:/SPTushanka',
    [string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference='Stop'
function Read-Method([string]$path,[string]$signature) {
    $source=Get-Content -Raw -LiteralPath $path
    $start=$source.IndexOf($signature,[StringComparison]::Ordinal)
    if($start -lt 0){throw "Missing native method: $signature"}
    $brace=$source.IndexOf('{',$start); $depth=0
    for($i=$brace;$i -lt $source.Length;$i++) {
        if($source[$i] -eq '{'){$depth++}
        if($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($start,$i-$start+1)}}
    }
    throw "Unterminated method: $signature"
}
$plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
foreach($patch in @('FollowerHumanPatrolChooserPatch','FollowerNativeBrainActivationPatch')) {
    if(!$plugin.Contains("new $patch().Enable();")){throw "Patch not enabled: $patch"}
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$game=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$spt=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/plugins/spt/spt-custom.dll'))
try {
    $chooser=@($game.GetType('PatrollingData').Methods | Where-Object {
        $_.Name -eq 'GetPointChooser' -and $_.IsStatic -and $_.Parameters.Count -eq 3 -and
        $_.Parameters[0].ParameterType.FullName -eq 'EFT.BotOwner' -and $_.Parameters[1].ParameterType.FullName -eq 'PatrolMode' -and
        $_.Parameters[2].ParameterType.FullName -eq 'IGetProfileData'
    })
    if($chooser.Count -ne 1){throw 'Installed patrol chooser signature changed'}
    $prefix=@($spt.GetType('SPT.Custom.Patches.CustomAiPatch').Methods | Where-Object {
        $_.Name -eq 'PatchPrefix' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 2 -and
        $_.Parameters[0].IsOut -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.WildSpawnType&' -and
        $_.Parameters[1].ParameterType.FullName -eq 'StandartBotBrain'
    })
    if($prefix.Count -ne 1){throw 'Installed SPT brain prefix signature changed'}
    $follow=$game.GetType('BotFollower').Methods | Where-Object Name -eq 'SetToFollow'
    if(!($follow.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq $chooser[0].FullName})) {
        throw 'Installed native SetToFollow no longer uses the verified patrol chooser'
    }
} finally {$game.Dispose();$spt.Dispose()}
$fixture=Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerNativeActivationFixture.cs')
$fixture=$fixture.Replace('/*NATIVE_FOLLOW*/',(Read-Method (Join-Path $NativeSourceRoot 'BotFollower.cs') 'public virtual void SetToFollow('))
$fixture=$fixture.Replace('/*NATIVE_CHOOSER*/',(Read-Method (Join-Path $NativeSourceRoot 'PatrollingData.cs') 'public static PatrolPointChooserBasic GetPointChooser('))
$sptSource=Join-Path $SptCustomSourceRoot 'Patches/CustomAiPatch.cs'
$fixture=$fixture.Replace('/*SPT_PREFIX*/',(Read-Method $sptSource 'public static bool PatchPrefix('))
$fixture=$fixture.Replace('/*SPT_POSTFIX*/',(Read-Method $sptSource 'public static void PatchPostFix('))
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('follower-native-activation-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    foreach($name in @('0Harmony.dll','MonoMod.RuntimeDetour.dll','MonoMod.Utils.dll','Mono.Cecil.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Rocks.dll')) {
        Copy-Item -LiteralPath (Join-Path $GameRoot "BepInEx/core/$name") -Destination $temporary
    }
    $source=Join-Path $temporary 'Fixture.cs';[IO.File]::WriteAllText($source,$fixture)
    $exe=Join-Path $temporary 'Fixture.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" "/reference:$temporary/0Harmony.dll" $source (Join-Path $RepositoryRoot 'client/Patches/FollowerNativeActivationPatch.cs')
    if($LASTEXITCODE -ne 0){throw 'Native follower activation fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Native follower activation fixture failed'}
    'PASS: installed native/SPT target signatures and Core patch registration verified.'
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid temporary fixture path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
