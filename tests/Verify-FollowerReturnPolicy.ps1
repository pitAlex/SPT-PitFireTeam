param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-return-policy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$exe = Join-Path $temporary 'FollowerReturnPolicy.exe'
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
& dotnet @arguments (Join-Path $RepositoryRoot 'client/Modules/FollowerReturnPolicy.cs') (Join-Path $PSScriptRoot 'FollowerReturnPolicyFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Return policy fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Return policy fixture failed' }
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/InteractableObjects.cs')
foreach ($signature in @('public static void StoreItem(BotOwner bot, Item item)',
    'internal static void StoreGearSwapReturnItems(BotOwner bot, IEnumerable<Item> items)',
    'internal static List<string> GetReturnItemIds(BotOwner bot)',
    'private static bool ShouldGatherRaidEndFollowerInventory(BotOwner bot)')) {
    $index = $source.IndexOf($signature)
    if ($index -lt 0 -or !$source.Substring($index, [Math]::Min(360, $source.Length - $index)).Contains('!IsReturnSquadMember(bot)')) {
        throw "Membership must be the first gate: $signature"
    }
}
foreach ($path in @('client/Modules/FollowerDeathEscapeResolver.GearSnapshot.cs', 'client/Modules/FollowerDeathEscapeResolver.GearRecovery.cs')) {
    if ((Get-Content -Raw (Join-Path $RepositoryRoot $path)).Contains('InteractableObjects.GetStoredItems(')) {
        throw "Snapshot/recovery must use filtered cargo IDs: $path"
    }
}
if (!$source.Contains('TrackedItemIds = GetReturnItemIds(bot).ToArray()')) { throw 'Escape snapshot must match return policy' }
Write-Output 'Follower return membership/snapshot source guards passed.'
