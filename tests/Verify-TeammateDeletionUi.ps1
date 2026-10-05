param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
# Exercises production client methods with native FlatItem/converters and controlled UI/HTTP stand-ins.
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Name) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot $Path)
    $matches = [regex]::Matches($source, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}')
    if ($matches.Count -ne 1) { throw "Expected one production method $Name, found $($matches.Count)" }
    return $matches[0].Value
}
$methods = @('NormalizePlayerStashSnapshot','BuildPlayerStashRefreshDelta','ToFlatItemDictionary',
    'ExpandAddCandidatesWithAddressableParents','HasAncestorInSet','ReduceToRootCandidates',
    'ExpandFlatItemRoots','AddFlatItemTree','PlacementChanged','GetParentId','JsonTokenEquals','IsNullOrEmptyJson') |
    ForEach-Object { Read-Method 'client/Patches/OtherPlayerProfileScreenPatch.LoadoutUi.cs' $_ }
$code = Get-Content -Raw (Join-Path $PSScriptRoot 'TeammateDeletionUiFixture.cs')
$code = $code.Replace('__STASH_METHODS__', ($methods -join "`n"))
$code = $code.Replace('__DELETE_METHOD__', (Read-Method 'client/Modules/TeammateDeletion.cs' 'DeleteAsync'))
$overlayMethods = @('RemoveTeammate','CloseRemoveConfirmOverlay') | ForEach-Object { Read-Method 'client/Components/SquadControlMenuUi.Roster.cs' $_ }
$code = $code.Replace('__OVERLAY_METHODS__', ($overlayMethods -join "`n"))
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-deletion-ui-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $sourceFile = Join-Path $temporary 'Fixture.cs'
    [IO.File]::WriteAllText($sourceFile, $code)
    $references = @()
    foreach ($reference in @('Assembly-CSharp.dll','Newtonsoft.Json.dll','UnityEngine.CoreModule.dll')) {
        $referencePath = Join-Path $RepositoryRoot ('client/libs4.1/' + $reference)
        if (!(Test-Path -LiteralPath $referencePath)) { $referencePath = Join-Path $RepositoryRoot ('client/libs/' + $reference) }
        $escapedPath = [Security.SecurityElement]::Escape($referencePath)
        $references += "<Reference Include='$reference'><HintPath>$escapedPath</HintPath></Reference>"
    }
    $project = Join-Path $temporary 'Fixture.csproj'
    [IO.File]::WriteAllText($project, "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><NoWarn>0436;0649</NoWarn></PropertyGroup><ItemGroup>" + ($references -join '') + '</ItemGroup></Project>')
    & dotnet run --project $project -- $RepositoryRoot
    if ($LASTEXITCODE -ne 0) { throw 'Deletion UI fixture failed' }
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-deletion-ui-*') { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
