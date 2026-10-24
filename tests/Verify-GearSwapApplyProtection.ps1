param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
function Get-Region([string]$source, [string]$start, [string]$end) {
    $first = $source.IndexOf($start)
    $last = $source.IndexOf($end, $first)
    if ($first -lt 0 -or $last -le $first) { throw "Missing source region $start" }
    $source.Substring($first, $last - $first)
}
$session = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'GearSwapApplyProtectionFixture.cs')
$fixture = $fixture.Replace('/* PROTECTION */', (Get-Region $session '        internal static bool Protects(Player player)' '        internal bool IsLiveLocked('))
$fixture = $fixture.Replace('/* CANCEL */', (Get-Region $session '        private void Cancel(string reason)' '        private static string UnsafeReason('))
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-swap-protection-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temporary
$source = Join-Path $temporary 'Protection.cs'
[IO.File]::WriteAllText($source, $fixture)
$exe = Join-Path $temporary 'Protection.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
& dotnet @arguments $source (Join-Path $RepositoryRoot 'client/Modules/GearSwapApplyScope.cs') (Join-Path $RepositoryRoot 'client/Patches/GearSwapApplyProtectionPatch.cs')
if ($LASTEXITCODE -ne 0) { throw 'Protection fixture compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Protection fixture failed.' }
$apply = Get-Region $session '        internal async Task Apply()' '        private void ValidateFinalEquipment()'
if ($apply.IndexOf('new GearSwapApplyScope()') -lt $apply.IndexOf('ValidateFinalEquipment();') -or
    $apply.IndexOf('HoldMovement();') -gt $apply.IndexOf('await EmptyHands(Owner.Player)') -or
    !$apply.Contains('_applyScope.Preparation.ThrowIfCancellationRequested();') -or
    $apply.IndexOf('_committed = true;') -lt $apply.IndexOf('GearSwapTransaction.Execute(') -or
    !$apply.Contains('if (Applying && CanContinue(Bot.GetPlayer))') -or !$apply.Contains('_applyScope?.Dispose();')) {
    throw 'Protection must begin after validation, preserve commit, gate late restoration and always release.'
}
$boss = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
if ($boss.IndexOf('TeammateGearSwap.EndRaid();') -gt $boss.IndexOf('FollowerDeathEscapeResolver.ResolveAndSend(')) {
    throw 'Death-escape capture must follow exchange teardown.'
}
$patches = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/GearSwapApplyProtectionPatch.cs')
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
foreach ($match in [regex]::Matches($patches, 'class (GearSwap\w+Patch) : ModulePatch')) {
    if (!$plugin.Contains("new $($match.Groups[1].Value)().Enable();")) { throw "Uninstalled protection patch: $($match.Groups[1].Value)" }
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    foreach ($contract in @(
        @('EFT.BotOwner','UpdateManual',0), @('AICoreAgent`1','Update',0),
        @('EFT.Player','Move',1), @('EFT.Player','ApplyShot',5), @('EFT.Player','ApplyDamageInfo',4),
        @('EFT.Player','ApplyExplosionDamageToArmor',2), @('EFT.HealthSystem.ActiveHealthController','ApplyDamage',3),
        @('EFT.HealthSystem.ActiveHealthController','ChangeHealth',3), @('EFT.HealthSystem.ActiveHealthController','Kill',1),
        @('EFT.Player','OnDead',1),
        @('EFT.LocalGame','Stop',4), @('EFT.BotsController','StopGettingInfo',0))) {
        $type = $module.Types | Where-Object FullName -eq $contract[0]
        $method = $type.Methods | Where-Object { $_.Name -eq $contract[1] -and $_.Parameters.Count -eq $contract[2] }
        if (!$method) { throw "Installed API missing: $($contract -join '/')" }
        if ($contract[1] -eq 'ChangeHealth' -and !($method.Parameters | Where-Object Name -eq 'value')) { throw 'Health-drain argument changed.' }
    }
    $sainSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/SAINPatches.cs')
    if (!$sainSource.Contains('if (TeammateGearSwap.Protects(botOwner?.GetPlayer)) return false;')) {
        throw 'Independent external-SAIN reload must honor Apply protection regardless of combat brain ownership.'
    }
    $selector = $module.GetType('BotWeaponSelector')
    if (!($selector.Methods | Where-Object { $_.Name -eq 'TryChangeToSlot' -and $_.Parameters.Count -eq 3 })) {
        throw 'Installed independent weapon selection API changed.'
    }
    Write-Output 'Installed protection/teardown APIs and lifecycle ordering verified (not raid qualification).'
} finally { $module.Dispose() }
