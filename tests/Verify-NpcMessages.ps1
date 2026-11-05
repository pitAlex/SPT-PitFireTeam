param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/NpcMessage.cs')
$methods = foreach ($name in @('AddNpc', 'GetNpcType', 'NpcSendThankYou')) {
    $found = [regex]::Matches($source, '(?ms)^        public static[^\r\n]*\b' + $name + '\(.*?^        \}')
    if ($found.Count -ne 1) { throw "Expected one production method: $name" }
    $found[0].Value
}
$boss = [regex]::Match($source, '(?m)^        private static bool IsBoss[^\r\n]*\r?\n[^;]+;').Value
if (!$boss) { throw 'Production boss classifier missing' }
$work = Join-Path $RepositoryRoot 'tests/artifacts/npc-messages'
New-Item -ItemType Directory -Force -Path $work | Out-Null
[IO.File]::WriteAllText((Join-Path $work 'Production.cs'), "using System; using System.Collections.Generic; using System.Linq; using System.Reflection; using Newtonsoft.Json; using SPT.Common.Http;`npartial class NpcMessage {`n$($methods -join "`n")`n$boss`n}")
dotnet run --project (Join-Path $RepositoryRoot 'tests/NpcMessages') -c Debug -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Production NPC recipient fixture failed' }
