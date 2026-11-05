param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatDefault.cs')
$router=[regex]::Match($source,'(?ms)^        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision\(EnemyInfo goalEnemy\).*?^        \}').Value
$gate=[regex]::Match($source,'(?m)^        private bool ShouldAdvance\(EnemyInfo enemy\)\r?\n\s*=>[^;]+;').Value
$stop=[regex]::Match($source,'(?ms)^        internal bool TryPrepareCompanionAdvanceStop\(.*?^        \}').Value
if(!$router -or !$gate -or !$stop){throw 'Missing production Rifleman companion boundary'}
$fixture=(Get-Content -Raw (Join-Path $RepositoryRoot 'tests/ExUsecRiflemanRouterFixture.cs')).Replace('/* ROUTER */',$router).Replace('/* ADVANCE GATE */',$gate).Replace('/* COMPANION STOP */',$stop)
Add-Type -TypeDefinition $fixture -Language CSharp
[ExUsecRiflemanRouterFixture]::Run()
