param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$NativeRoot='F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp')
$ErrorActionPreference='Stop'
$boss=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
$reply=[regex]::Match($boss,'(?ms)^        private static void ReplyToSuppressionCommand\(.*?^        \}').Value
$native=Get-Content -Raw (Join-Path $NativeRoot 'BotTalk.cs')
$trySay=[regex]::Match($native,'(?ms)^\tpublic void TrySay\(EPhraseTrigger type, ETagStatus\? additionaMask, bool withGroupDelay\).*?^\t\}').Value
if(!$reply -or !$trySay){throw 'Suppression reply/native speech boundary changed'}
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'SuppressionCommandReplyFixture.cs')).Replace('/* PRODUCTION REPLY */',$reply).Replace('/* NATIVE TRY SAY */',$trySay)
Add-Type -TypeDefinition $fixture -Language CSharp
$count=[SuppressionCommandReplyChecks]::Run()
$dispatch=[regex]::Match($boss,'(?ms)^        private void ApplySuppressPhrase\(.*?^        \}').Value
foreach($marker in @('"received"','"debounced"','"busy.lootCommand"','"unsupportedMarksmanWeapon"','"weaponUnavailable"','"busy.followerLocal"','ReplyToSuppressionCommand(closestResponseFollower, accepted: false')){
    if(!$dispatch.Contains($marker)){throw "Missing passive suppression dispatch diagnostic/refusal: $marker"}
}
$issue=[regex]::Match($boss,'(?ms)^        private bool TryIssueSuppressCommand\(.*?^        \}').Value
if(!$issue.Contains('ReplyToSuppressionCommand(follower, accepted: true')){throw 'Accepted suppression must use explicit command reply'}
Write-Output "PASS: $count production suppression response/native speech checks plus receipt/refusal diagnostics. Actual audio/gesture playback requires a raid check."
