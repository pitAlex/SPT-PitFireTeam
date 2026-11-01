param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
$patch = Join-Path $RepositoryRoot 'client/Patches/FollowerFoundInRaidPatch.cs'
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
if (!$plugin.Contains('new FollowerFoundInRaidPatch().Enable();')) { throw 'Follower FIR patch is not enabled' }
$spawn = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
# Both squad and boss/transit activation paths mark ownership before native brain creation.
$markedActivations = [regex]::Matches($spawn, 'BossPlayers\.ShallBeFollower\(profile\.ProfileId\);\s*(?:bool activated = )?await ActivateBotAtPosition\(')
if ($markedActivations.Count -ne 2) { throw 'Follower ownership must precede both native activation paths' }
$boss = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
if (!$boss.Contains('return Instance.IsBotFollower(bot, boss) || Instance._shallBeFollower.Contains(bot.ProfileId);')) {
    throw 'Follower FIR admission requires the existing pre-registration marker'
}

Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$spt = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/plugins/spt/spt-custom.dll'))
try {
    $type = $spt.Types | Where-Object FullName -eq 'SPT.Custom.CustomAI.PmcFoundInRaidEquipment'
    $method = @($type.Methods | Where-Object {
        $_.Name -eq 'ConfigurePMCFindInRaidStatus' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and
        $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.BotOwner'
    })
    if ($method.Count -ne 1) { throw 'Installed SPT FIR target signature changed' }
    $activation = ($spt.Types | Where-Object FullName -eq 'SPT.Custom.Patches.CustomAiPatch').Methods | Where-Object Name -eq 'PatchPrefix'
    if (!($activation.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq $method[0].FullName
    })) { throw 'Installed SPT no longer invokes the FIR routine during brain activation' }
    $promotions = @($method[0].Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'set_SpawnedInSession' -and
        $_.Previous.OpCode.Code.ToString() -eq 'Ldc_I4_1'
    })
    if ($promotions.Count -ne 2) { throw 'Installed container/dogtag FIR promotion contract changed; inspect SPT implementation' }
} finally { $spt.Dispose() }

$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('follower-fir-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $exe = Join-Path $temporary 'FollowerFir.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" (Join-Path $PSScriptRoot 'FollowerFoundInRaidFixture.cs') $patch
    if ($LASTEXITCODE -ne 0) { throw 'Follower FIR fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Follower FIR fixture failed' }
    Write-Output 'Installed SPT FIR call/signature, early follower admission and patch installation verified. Raid UI still requires live qualification.'
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
