param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    $type=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.Components.VisionRaycastJob'
    $method=@($type.Methods | Where-Object { $_.Name -eq 'UpdateEFTVision' -and !$_.IsStatic -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Collections.IEnumerator' })
    if($method.Count -ne 1){throw 'Native vision factory signature changed'}
    if(!($type.Fields | Where-Object { $_.Name -eq '_disposed' -and $_.FieldType.FullName -eq 'System.Boolean' })){throw 'Native dispose field changed'}
    $base=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.BotManagerBase'
    if(!($base.Properties | Where-Object { $_.Name -eq 'BotController' -and $_.PropertyType.FullName -eq 'SAIN.Components.BotManagerComponent' })){throw 'Native controller changed'}
    Write-Output 'Installed SAIN vision factory and lifecycle signatures validated.'
} finally {$assembly.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-vision-recovery-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$patch=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/SainVisionRecoveryPatch.cs')
$patch=$patch.Replace('Type.GetType("SAIN.Components.VisionRaycastJob, SAIN")','VisionRecoveryChecks.ResolveType("SAIN.Components.VisionRaycastJob")')
$patchPath=Join-Path $temporary 'Patch.cs';[IO.File]::WriteAllText($patchPath,$patch)
$harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
Copy-Item -LiteralPath $harmony -Destination $temporary
Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' | Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
$exe=Join-Path $temporary 'Recovery.exe'
$arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+','/nowarn:8632,0414',"/out:$exe","/reference:$harmony")
foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+="/reference:$(Join-Path $framework $reference)"}
$arguments+=@($patchPath,(Join-Path $RepositoryRoot 'client/Modules/SainVisionRecoveryEnumerator.cs'),(Join-Path $RepositoryRoot 'tests/SainVisionRecoveryFixture.cs'))
& dotnet @arguments
if($LASTEXITCODE -ne 0){throw 'Recovery fixture compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Recovery fixture failed'}
