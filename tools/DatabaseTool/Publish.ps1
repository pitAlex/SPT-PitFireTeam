param([string]$OutputDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts/database-tool/win-x64'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $PSScriptRoot 'DatabaseTool.csproj'
& dotnet publish $project -c Debug -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Portable utility publish failed.' }

# Include the exact runtime pack's license/notices with the self-contained binary.
$assets = Get-Content -Raw (Join-Path $PSScriptRoot 'obj/project.assets.json') | ConvertFrom-Json
$runtime = $assets.project.frameworks.'net10.0'.downloadDependencies | Where-Object name -eq 'Microsoft.NETCore.App.Runtime.win-x64'
if (!$runtime) { throw 'Published runtime package metadata missing.' }
$runtimeVersion = $runtime.version.Trim('[', ']').Split(',')[0].Trim()
$runtimeDirectory = $null
foreach ($root in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $root ('microsoft.netcore.app.runtime.win-x64/' + $runtimeVersion)
    if (Test-Path -LiteralPath $candidate) { $runtimeDirectory = $candidate; break }
}
if (!$runtimeDirectory) { throw 'Published runtime license directory missing.' }
Copy-Item -LiteralPath (Join-Path $runtimeDirectory 'LICENSE.TXT') -Destination (Join-Path $OutputDirectory 'DOTNET-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $runtimeDirectory 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $OutputDirectory 'DOTNET-THIRD-PARTY-NOTICES.txt') -Force
$archive = Join-Path (Split-Path $OutputDirectory -Parent) 'pitFireTeam.DatabaseTool-win-x64.zip'
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archive -Force
Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'pitFireTeam.DatabaseTool.exe'), $archive -Algorithm SHA256 | Format-List
Write-Output "Portable Windows x64 utility: $archive (.NET $runtimeVersion included)."
