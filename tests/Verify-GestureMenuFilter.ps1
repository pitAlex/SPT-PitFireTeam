param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka',
    [string]$NativeSource = 'F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp/EFT/UI/Gestures/GesturesMenu.cs'
)
$ErrorActionPreference = 'Stop'
function Get-Region([string]$source, [string]$start, [string]$end) {
    $first = $source.IndexOf($start)
    if ($first -lt 0) { throw "Missing region $start" }
    $last = $source.IndexOf($end, $first)
    if ($last -le $first) { throw "Missing region end $end" }
    return $source.Substring($first, $last - $first)
}
$production = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/GestureMenuPatch.cs')
$group = (Get-Region $production '    internal class CreatePhraseGroupPatch' '    internal class CreateGesturesPatch').Replace('private static bool PatchPrefix', 'internal static bool PatchPrefix')
$visibility = Get-Region $production '    internal static class GestureMenuUnsupportedCommandVisibility' '    internal class ViewBackpackQuickPanelTextPatch'
if (!$production.Contains('if (!GestureMenuUnsupportedCommandVisibility.ShouldFilter(__instance)) list_0.ForEach') -or
    !$production.Contains('GestureMenuUnsupportedCommandVisibility.PrepareForSideChange(__instance, side);')) { throw 'PMC-only policy must also govern injected groups and native Init' }
$native = Get-Content -Raw $NativeSource
$init = (Get-Region $native 'public void Init(GesturesCommandsStorage gesturesStorage' '// Token: 0x0601023D').Replace('public void Init(', 'private void NativeInit(')
$groups = Get-Region $native 'public virtual void InitPhraseGroups()' '// Token: 0x0601024F'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $menu = $assembly.MainModule.GetType('EFT.UI.Gestures.GesturesMenu')
    $side = $menu.Fields | Where-Object Name -eq '_side'
    if (!$side -or $side.FieldType.FullName -ne 'EFT.EPlayerSide') { throw 'Installed phrase menu side field changed' }
    $nativeInit = $menu.Methods | Where-Object { $_.Name -eq 'Init' -and $_.Parameters.Count -eq 3 }
    if (!$nativeInit -or $nativeInit.Parameters[2].Name -ne 'side' -or $nativeInit.Parameters[2].ParameterType.FullName -ne 'EFT.EPlayerSide') { throw 'Installed Init side argument changed' }
    if (!($nativeInit.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_side' -and $_.OpCode.Name -eq 'stfld' })) { throw 'Installed Init side publication changed' }
    $phrases = $assembly.MainModule.GetType('EPhraseTrigger')
    $values = $phrases.Fields | Where-Object HasConstant | ForEach-Object { $_.Name + '=' + $_.Constant }
    $enum = 'public enum EPhraseTrigger { ' + ($values -join ',') + ' }'
} finally { $assembly.Dispose() }
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'GestureMenuFilterFixture.cs')).Replace('/* PHRASE_ENUM */', $enum).
    Replace('/* NATIVE_INIT */', $init).Replace('/* NATIVE_GROUPS */', $groups).
    Replace('/* GROUP_PATCH */', $group).Replace('/* VISIBILITY */', $visibility)
Add-Type -TypeDefinition $fixture -Language CSharp
$count = [GestureMenuFilterChecks]::Run()
Write-Output "Passed $count production/native phrase menu filter checks; installed side and Init metadata verified. Unity menu rendering still needs in-game qualification."
