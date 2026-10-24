param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $inventory=$assembly.MainModule.GetType('EFT.InventoryLogic.ItemController')
    $preview=$assembly.MainModule.GetType('EFT.UI.PlayerModelLoader')
    $hands=$assembly.MainModule.GetType('EFT.Player').NestedTypes | Where-Object Name -eq 'FirearmController'
    if(@($inventory.Methods | Where-Object {$_.Name -eq 'ProcessActivity' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.InventoryLogic.ItemEventArgs' -and $_.Parameters[0].Name -eq 'args'}).Count -ne 1){throw 'Inventory diagnostic boundary changed'}
    if(@($preview.Methods | Where-Object {$_.Name -eq 'CreateWeapon' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.Single|System.Boolean|EFT.InventoryLogic.Item|System.Int32' -and $_.Parameters[2].Name -eq 'weapon'}).Count -ne 1){throw 'Preview diagnostic boundary changed'}
    if(@($preview.Fields | Where-Object Name -eq '_weaponPrefab').Count -ne 1){throw 'Preview prefab field changed'}
    if(@($hands.Methods | Where-Object {$_.Name -eq 'ResetAimingAnimationsFlags' -and $_.Parameters.Count -eq 0}).Count -ne 1){throw 'Aiming diagnostic boundary changed'}
    $owner=$hands
    while($owner -and !($owner.Fields | Where-Object Name -eq '_player')){$owner=$assembly.MainModule.GetType($owner.BaseType.FullName)}
    if(!$owner){throw 'Hands player ownership field changed'}
    Write-Output 'Installed diagnostic method signatures, Harmony argument names, preview prefab and hands ownership verified.'
} finally {$assembly.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-diagnostics-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $exe=Join-Path $temporary 'Diagnostics.exe'
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object Name -ne '0Harmony.dll' | Copy-Item -Destination $temporary
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/define:DEBUG','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    foreach($source in @('client/Patches/RuntimeFailureDiagnostics.cs','tests/RuntimeFailureDiagnosticsFixture.cs')){$arguments+=(Join-Path $RepositoryRoot $source)}
    & dotnet @arguments
    if($LASTEXITCODE -ne 0){throw 'Diagnostic fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Diagnostic behavior fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-diagnostics-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
