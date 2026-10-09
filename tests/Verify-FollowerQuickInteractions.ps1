param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
function Get-Region([string]$source, [string]$start, [string]$end) {
    $first = $source.IndexOf($start)
    if ($first -lt 0) { throw "Missing region $start" }
    $last = $source.IndexOf($end, $first)
    if ($last -le $first) { throw "Missing region end $end" }
    return $source.Substring($first, $last - $first)
}
$player = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/PlayerPatch.cs')
$inspection = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateBackpackInspection.cs')
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
$code = Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerQuickInteractionFixture.cs')
$code = $code.Replace('/* CUSTOM_PHRASES */', (Get-Region $plugin '    public enum CustomPhrases' '    public enum CustomGestures'))
$code = $code.Replace('/* ROUTER */', (Get-Region $player '    internal static class FollowerQuickInteractionRouter' '    internal static class BossGestureCommandRouter'))
$code = $code.Replace('/* INPUT_PATCHES */', (Get-Region $player '    internal class QuickMumbleStartViewBackpackPatch' '    /** Have follower kills'))
$code = $code.Replace('/* SWAP_TARGET */', (Get-Region $inspection '        public static bool CanShowSwapGearInteraction' '        public static bool TryOpenFromQuickInteraction'))
$code = $code.Replace('/* TARGET_RESOLUTION */', (Get-Region $inspection '        internal static Player ResolveInteractionTargetPlayer' '        private static bool IsInspectableBotActive'))
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-quick-interactions-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$source = Join-Path $temporary 'QuickInteractions.cs'
$exe = Join-Path $temporary 'QuickInteractions.exe'
[IO.File]::WriteAllText($source, $code)
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
& dotnet @arguments $source
if ($LASTEXITCODE -ne 0) { throw 'Quick interaction fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Quick interaction fixture failed' }

foreach ($file in Get-ChildItem (Join-Path $RepositoryRoot 'server/Resources/lang') -Filter '*.json') {
    $locale = Get-Content -Raw -Encoding UTF8 $file.FullName | ConvertFrom-Json
    if (!$locale.gestures.SwapGear) { throw "Missing SwapGear in $($file.Name)" }
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $owner = $module.Types | Where-Object FullName -eq 'EFT.GamePlayerOwner'
    $panel = $module.Types | Where-Object FullName -eq 'EFT.UI.Gestures.GesturesQuickPanel'
    if (!($panel.Methods | Where-Object { $_.Name -eq 'CloseDropdown' -and $_.Parameters.Count -eq 1 -and
        $_.Parameters[0].ParameterType.FullName -eq 'System.Action`1<EPhraseTrigger>' })) {
        throw 'Native dropdown callback boundary changed'
    }
    $dropdown = $owner.Methods | Where-Object Name -eq 'MumbleDropdown'
    if (!($dropdown.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldftn' -and $_.Operand.DeclaringType.FullName -eq 'EFT.GamePlayerOwner' -and
        $_.Operand.Parameters.Count -eq 1 -and $_.Operand.Parameters[0].ParameterType.FullName -eq 'EPhraseTrigger' })) {
        throw 'Dropdown callback no longer targets GamePlayerOwner'
    }
} finally { $module.Dispose() }
Write-Output 'Installed quick-input/dropdown metadata and all Swap Gear locales verified.'
