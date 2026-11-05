param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Name) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot $Path)
    $found = [regex]::Matches($source, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + $Name + '\(.*?^        \}')
    if ($found.Count -ne 1) { throw "Expected one production method ${Path}::$Name" }
    $found[0].Value
}
$awareness = @('BulletFelt','RegisterThreatLookPoint','TryGetRecentFireThreatLookPoint','TryGetIncomingFireSuppressTarget') |
    ForEach-Object { Read-Method 'client/Utils/FollowerAwareness.cs' $_ }
$recovery = @('TrySelectRetreatAttackCover','TryAssignRetreatAttackCover','IsUnsafeRecoveryCoverPath') |
    ForEach-Object { Read-Method 'client/BigBrain/FollowerCombatCommon.cs' $_ }
$policy = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerSuppressTargetPolicy.cs')
$overlay = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/FollowerCombatFireOverlay.cs')
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'IncomingFireRecoveryFixture.cs')
$fixture = $fixture.Replace('/* AWARENESS */', ($awareness -join "`n")).Replace('/* RECOVERY */', ($recovery -join "`n"))
foreach ($source in @($policy,$overlay)) { $fixture += "`n" + [regex]::Replace($source,'(?m)^using [^\r\n]+\r?\n','') }
Add-Type -TypeDefinition $fixture -Language CSharp -CompilerOptions '/nullable:annotations'
[IncomingFireRecoveryChecks]::Run()
