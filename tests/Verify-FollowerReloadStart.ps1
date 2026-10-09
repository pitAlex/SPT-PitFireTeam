param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka',
    [string]$NativeSource = 'F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp/EFT/Player.cs'
)
$ErrorActionPreference = 'Stop'
function Get-Region([string]$source, [string]$start, [string]$end) {
    $first = $source.IndexOf($start)
    if ($first -lt 0) { throw "Missing region $start" }
    $last = $source.IndexOf($end, $first)
    if ($last -le $first) { throw "Missing region end $end" }
    $source.Substring($first, $last - $first)
}
$session = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$gate = Get-Region $session '        private static string UnsafeReason(' '        private static Profile Clone('
$admission = (Get-Content -Raw (Join-Path $PSScriptRoot 'GearSwapAdmissionFixture.cs')).Replace('/* GATE */', $gate)
$patchPath = Join-Path $RepositoryRoot 'client/Patches/FollowerMagazineReloadStartPatch.cs'
$patch = Get-Content -Raw $patchPath
$patrol = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerPatrolLayer.cs')
if (!(Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains('new FollowerMagazineReloadStartPatch().Enable()') -or
    !$patch.Contains('is Player.FirearmController.Idling') -or !$patch.Contains('ReferenceEquals(__state.Operation, __instance.CurrentOperation)') -or
    !$patch.Contains('__state.CallbackObserved') -or !$patch.Contains('__state.Wrapped.Fail(')) {
    throw 'Missing scoped native skipped-start completion boundary'
}
foreach ($forbidden in @('Reloading =', 'FastForwardCurrentState(', 'ForceStopInteractions(', 'CanStartReload(', 'CanRemove(')) {
    if ($patch.Contains($forbidden)) { throw "Recovery must not reset/re-query native state: $forbidden" }
}
if (!$patrol.Contains('if (TryHandleSkippedReloadStart()) return;') -or
    !$patrol.Contains('OutOfCombatReloadMaxFailedReloadsPerWeapon = 2')) { throw 'Existing bounded patrol budget required' }
$retry = Get-Region $patrol '        private bool TryHandleSkippedReloadStart()' '        private bool ShouldReloadCurrentWeaponOutOfCombat()'
$native = Get-Content -Raw $NativeSource
$nativeReload = Get-Region $native 'public virtual void ReloadMag(Magazine magazine, ItemAddress itemAddress, Callback callback)' 'public virtual void QuickReloadMag('
# Decompile uses tabs; accept its exact method region without retyping the implementation.
if (!$nativeReload.Contains('ForceStopInteractions()') -or !$nativeReload.Contains('IsInteractionPlaying')) { throw 'Native silent-branch contract changed' }
$reload = (Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerReloadStartFixture.cs')).Replace('/* NATIVE RELOAD */', $nativeReload).Replace('/* PATROL RETRY */', $retry)
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-reload-start-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nowarn:0649', '/nostdlib+')
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
function Run-Fixture([string]$name, [string]$code, [string[]]$extra, [string[]]$runArgs = @()) {
    $source = Join-Path $temporary "$name.cs"
    $exe = Join-Path $temporary "$name.exe"
    [IO.File]::WriteAllText($source, $code)
    & dotnet @arguments "/out:$exe" $source @extra
    if ($LASTEXITCODE -ne 0) { throw "$name compilation failed" }
    & $exe @runArgs
    if ($LASTEXITCODE -ne 0) { throw "$name failed" }
}
Run-Fixture 'Admission' $admission @()
$oldGate = $gate.Replace('bot.WeaponManager?._currentWeaponInfo?.Reload?.Reloading == true', 'bot.WeaponManager?.info?.Values.Any(i => i?.Reload?.Reloading == true) == true')
if ($oldGate -eq $gate) { throw 'Active reload gate missing' }
Run-Fixture 'OldAdmission' ((Get-Content -Raw (Join-Path $PSScriptRoot 'GearSwapAdmissionFixture.cs')).Replace('/* GATE */', $oldGate)) @() @('--old')
Run-Fixture 'ReloadStart' $reload @($patchPath, (Join-Path $RepositoryRoot 'client/Modules/FollowerReloadStartRecovery.cs'))
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $firearm = $module.GetType('EFT.Player/FirearmController')
    $method = $firearm.Methods | Where-Object { $_.Name -eq 'ReloadMag' -and $_.Parameters.Count -eq 3 }
    if (!$method -or $method.Parameters[2].Name -ne 'callback' -or $method.Parameters[2].ParameterType.FullName -ne 'Comfort.Common.Callback') {
        throw 'Installed ReloadMag callback patch signature changed'
    }
    foreach ($member in @('CurrentOperation', 'Blindfire')) {
        if (!($firearm.Properties | Where-Object Name -eq $member)) { throw "Installed firearm property missing: $member" }
    }
    $calls = @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
    foreach ($call in @('get_Blindfire', 'ForceStopInteractions', 'get_IsInteractionPlaying', 'CanStartReload', 'ReloadMag', 'Fail')) {
        if ($calls -notcontains $call) { throw "Installed native reload branch changed: $call" }
    }
    if (!($firearm.NestedTypes | Where-Object Name -eq 'Idling')) { throw 'Installed idle operation missing' }
    if (!($module.GetType('EFT.Player/ItemHandsController').Fields | Where-Object Name -eq '_player')) { throw 'Installed hands owner field missing' }
    foreach ($owner in @('BotWeaponManager', 'BotReload', 'EFT.AnimatedInteractionsSubsystem.AnimatedInteractions')) {
        if (!$module.GetType($owner)) { throw "Installed native type missing: $owner" }
    }
    Write-Output 'Installed callback/idle/hands metadata verified; source fixtures do not establish Mattdokn raid causality.'
} finally { $module.Dispose() }
