param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
foreach($assemblyPath in @((Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),(Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
    $module=[Mono.Cecil.ModuleDefinition]::ReadModule($assemblyPath)
    try {
        $owner=$module.Types | Where-Object FullName -eq 'EFT.BotOwner'
        $activate=@($owner.Methods | Where-Object Name -eq 'method_10')
        if($activate.Count -ne 1 -or $activate[0].Parameters.Count -ne 0){throw 'Native activation target changed'}
        $handlers=@($activate[0].Body.ExceptionHandlers)
        if($handlers.Count -ne 1 -or $handlers[0].CatchType.FullName -ne 'System.Exception' -or $handlers[0].HandlerStart.OpCode.Name -ne 'pop'){
            throw 'Native activation catch changed'
        }
        Write-Output ('Verified native catch: '+$assemblyPath)
    } finally {$module.Dispose()}
}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-activation-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'Activation.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @arguments (Join-Path $RepositoryRoot 'client/Patches/FollowerActivationDiagnostics.cs') (Join-Path $RepositoryRoot 'tests/FollowerActivationDiagnosticsFixture.cs')
    if($LASTEXITCODE -ne 0){throw 'Activation fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Activation fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-activation-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
