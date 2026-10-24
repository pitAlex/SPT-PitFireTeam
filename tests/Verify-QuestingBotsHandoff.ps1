param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$GameRoot
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/QuestingBots/QuestingBots-Client.dll'))
try {
    $manager = $assembly.MainModule.GetType('QuestingBots.Components.BotObjectiveManager')
    if (!$manager) { throw 'Installed Questing Bots objective component is missing' }
    $stop = @($manager.Methods | Where-Object { $_.Name -eq 'StopQuesting' -and $_.IsPublic -and !$_.IsStatic -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Void' })
    if ($stop.Count -ne 1) { throw 'Installed StopQuesting signature changed' }
    foreach ($name in @('IsQuestingAllowed','HasInitialQuestBeenSelected')) {
        $property = $manager.Properties | Where-Object Name -eq $name
        if ($property.PropertyType.FullName -ne 'System.Boolean' -or !$property.GetMethod.IsPublic) { throw "Installed readiness/permission getter changed: $name" }
    }
    $allowed = $manager.Properties | Where-Object Name -eq 'IsQuestingAllowed'
    if (!$allowed.SetMethod.IsPrivate -or !($stop[0].Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq $allowed.SetMethod.FullName })) { throw 'Installed StopQuesting no longer sets its restorable permission' }
    Write-Output "Installed Questing Bots $($assembly.Name.Version) objective permission API verified."
} finally { $assembly.Dispose() }
$boss = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
$init = [regex]::Match($boss,'(?ms)            try\r?\n            \{\r?\n                FollowerQuestingBots.Pause\(bot\);.*?_follower.Init\(\);.*?            catch \(Exception e\).*?FollowerQuestingBots.Release\(bot\);')
if (!$init.Success) { throw 'Recruitment must suspend before initialization and release after initialization failure' }
foreach ($method in @('Destroy','RemoveBotFollower')) {
    $body = [regex]::Match($boss,'(?ms)^        private void '+$method+'\(.*?^        \}').Value
    $call = if ($method -eq 'Destroy') { 'FollowerQuestingBots.ClearRaid();' } else { 'FollowerQuestingBots.Release(bot);' }
    if (!$body.Contains($call)) { throw "Missing Questing Bots cleanup in $method" }
}
$follower = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$dismiss = [regex]::Match($follower,'(?ms)^        public virtual void Dismiss\(.*?^        \}').Value
if ($dismiss -notmatch 'finally\s*\{\s*FollowerQuestingBots.Release\(_bot\);' -or $follower.Contains('SPTQuestingBots.Components.BotObjectiveManager')) { throw 'Dismissal cleanup or obsolete Questing Bots implementation changed' }
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-questing-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' | Where-Object Name -ne '0Harmony.dll' | Copy-Item -Destination $temporary
    foreach ($absent in @($false,$true)) {
        $exe = Join-Path $temporary ("Questing-$absent.exe")
        $arguments = @($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:annotations','/nostdlib+',"/out:$exe","/reference:$harmony")
        if ($absent) { $arguments += '/define:NO_QUESTING_BOTS' }
        foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += '/reference:'+(Join-Path $framework $reference) }
        $arguments += @((Join-Path $RepositoryRoot 'tests/QuestingBotsHandoffFixture.cs'),(Join-Path $RepositoryRoot 'client/Modules/FollowerQuestingBots.cs'),(Join-Path $RepositoryRoot 'client/Modules/BotOwnerUpdateHub.cs'))
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Questing handoff fixture compilation failed' }
        & $exe
        if ($LASTEXITCODE -ne 0) { throw 'Questing handoff regression failed' }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-questing-*') { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
