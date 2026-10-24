param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$follower=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$boss=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
function Method([string]$source,[string]$name) {
    $m=[regex]::Match($source,'(?ms)^        (?:public|private|internal)[^\r\n]*\b'+$name+'\(.*?^        \}')
    if(!$m.Success){throw "Missing production method $name"}; $m.Value
}
if (!(Method $boss 'HandleAttentionCommand').Contains('followerData?.ResetCombatPercentageOverrides("Attention")')) {
    throw 'Attention must reset both percentage overrides through the shared helper'
}
$start=$follower.IndexOf('        public float SelfDetermination');$end=$follower.IndexOf('        public bool CombatRegroupUsesBossAnchor',$start)
$properties=$follower.Substring($start,$end-$start)
$start=$follower.IndexOf('        public float CombatAggression');$end=$follower.IndexOf('        public bool IsTemporaryCombatAggressionOverrideActive',$start)
$aggression=$follower.Substring($start,$end-$start)
$methods=@('SetCombatIndependent','ClearActiveCombatIndependent','ClearCombatIndependent','SetTemporaryCombatAggressionOverride','ClearTemporaryCombatAggressionOverride')|ForEach-Object {Method $follower $_}
$start=$boss.IndexOf('        private readonly HashSet<string> _addonFollowerCombatProfiles');$end=$boss.IndexOf('        private bool IsCombatRegroupContext()',$start)
$bossState=$boss.Substring($start,$end-$start)
$server=Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyTeammateService.cs')
$save=[regex]::Match($server,'(?ms)^    public void SetTeammateProficiency\(.*?^    \}').Value
if(!$save){throw 'Missing production proficiency save'}
$code=Get-Content -Raw (Join-Path $PSScriptRoot 'SelfDeterminationFixture.cs')
$code=$code.Replace('__PROPERTIES__',$properties+$aggression).Replace('__METHODS__',($methods -join "`n")).Replace('__BOSS_STATE__',$bossState)
$code=$code.Replace('__SAVE_METHOD__',$save)
$sdk=dotnet --list-sdks|Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('self-determination-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp|Out-Null
try {
    $source=Join-Path $temp 'Fixture.cs';[IO.File]::WriteAllText($source,$code)
    $exe=Join-Path $temp 'Fixture.exe'
    & dotnet $compiler /nologo /target:exe /langversion:latest /nullable:disable /nostdlib+ "/out:$exe" "/reference:$framework/mscorlib.dll" "/reference:$framework/System.dll" "/reference:$framework/System.Core.dll" $source (Join-Path $RepositoryRoot 'shared/SelfDeterminationPolicy.cs')
    if($LASTEXITCODE -ne 0){throw 'Determination fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Determination fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temp)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
