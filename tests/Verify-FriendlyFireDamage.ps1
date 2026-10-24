param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
foreach ($assemblyPath in @(
    (Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),
    (Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($assemblyPath)
    try {
        $player = $module.GetType('EFT.Player')
        $entry = @($player.Methods | Where-Object Name -eq 'ApplyDamageInfo')
        if ($entry.Count -ne 1 -or ($entry[0].Parameters.ParameterType.FullName -join ',') -ne
            'EFT.Ballistics.DamageInfo,EBodyPart,EBodyPartColliderType,System.Single') { throw 'Damage entry signature changed' }
        if (!$module.GetType('EFT.Ballistics.DamageInfo').IsValueType) { throw 'DamageInfo must remain a value copy' }
        if ($entry[0].Parameters[0].Name -ne 'damageInfo' -or $entry[0].Parameters[1].Name -ne 'bodyPartType') {
            throw 'Harmony argument binding changed'
        }
        $calls = @($entry[0].Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand })
        if (@($calls | Where-Object { $_.Name -eq 'ApplyDamage' -and $_.DeclaringType.FullName -eq 'EFT.HealthSystem.ActiveHealthController' }).Count -ne 1) {
            throw 'Expected one native health entry'
        }
        $shot = $player.Methods | Where-Object Name -eq 'ApplyShot'
        $shotCalls = @($shot.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
        if ([Array]::IndexOf($shotCalls, 'ProceedDamageThroughArmor') -lt 0 -or
            [Array]::IndexOf($shotCalls, 'ProceedDamageThroughArmor') -ge [Array]::IndexOf($shotCalls, 'ApplyDamageInfo')) {
            throw 'Armor must be resolved before reduction'
        }
        $overrides = @($module.Types | ForEach-Object { $_.Methods | Where-Object Name -eq 'ApplyDamageInfo' } | ForEach-Object { $_.DeclaringType.FullName })
        if (($overrides | Sort-Object) -join ',' -ne 'EFT.ClientPlayer,EFT.Player') { throw 'New player override needs damage-route review' }
        $health = $module.GetType('EFT.HealthSystem.ActiveHealthController').Methods | Where-Object Name -eq 'ApplyDamage'
        if ($health.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('ApplyDamageInfo', 'ApplyDamage') }) {
            throw 'Health overflow must not reenter reduction'
        }
        Write-Output "Verified damage entry, armor order and nonrecursive health boundary: $assemblyPath"
    } finally { $module.Dispose() }
}
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains('new FollowerFriendlyFireDamagePatch().Enable();')) {
    throw 'Damage patch is not registered'
}
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-friendly-fire-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$harmony = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
Copy-Item -LiteralPath $harmony -Destination $temporary
Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
    Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } | Copy-Item -Destination $temporary
$exe = Join-Path $temporary 'FriendlyFire.exe'
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nullable:disable', '/nostdlib+', "/out:$exe", "/reference:$harmony")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
$arguments += @((Join-Path $RepositoryRoot 'client/Patches/FollowerFriendlyFireDamagePatch.cs'), (Join-Path $PSScriptRoot 'FriendlyFireDamageFixture.cs'))
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Friendly-fire fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Friendly-fire fixture failed' }
