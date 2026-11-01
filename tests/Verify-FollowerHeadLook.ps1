param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$NativeRoot = 'F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp',
    [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-head-look-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } | Copy-Item -Destination $temporary
    $exe = Join-Path $temporary 'HeadLook.exe'
    $arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nullable:disable', '/nostdlib+', "/out:$exe", "/reference:$harmony")
    foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) { $arguments += '/reference:' + (Join-Path $framework $reference) }
    & dotnet @arguments (Join-Path $NativeRoot 'BotHeadData.cs') `
        (Join-Path $RepositoryRoot 'client/Patches/FollowerPatrolHeadLookPatch.cs') `
        (Join-Path $RepositoryRoot 'tests/FollowerHeadLookFixture.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Head-look fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Head-look fixture failed' }
    if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains('new FollowerPatrolHeadLookPatch().Enable();')) {
        throw 'Shared head-look patch is not registered'
    }
    Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
    foreach ($path in @((Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),
        (Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
        $module = [Mono.Cecil.ModuleDefinition]::ReadModule($path)
        try {
            $type = $module.GetType('BotHeadData')
            $patrol = @($type.Methods | Where-Object { $_.Name -eq 'UpdatePatrolState' -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Void' })
            if ($patrol.Count -ne 1 -or !$patrol[0].IsPublic -or $patrol[0].IsStatic) { throw 'Native patrol-head target changed' }
            if (@($type.Fields | Where-Object { $_.Name -eq '_targetHorizontalRotation' -and $_.FieldType.FullName -eq 'System.Single' -and $_.IsPublic }).Count -ne 1) { throw 'Native head target field changed' }
            $manual = $type.Methods | Where-Object Name -eq 'ManualUpdate'
            foreach ($name in @('UpdateStates', 'UpdateHeadRotation')) {
                if ($manual.Body.Instructions.Operand.Name -notcontains $name) { throw "Native head update lost $name" }
            }
            if ($patrol[0].Body.Instructions.Operand.Name -notcontains 'TryChangeState') { throw 'Native enemy/combat head transitions changed' }
            "Verified patrol-head target and native rotation lifecycle: $path"
        } finally { $module.Dispose() }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-head-look-*') { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
