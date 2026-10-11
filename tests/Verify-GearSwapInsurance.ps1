param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
$diagnosticsPath = Join-Path $RepositoryRoot 'client/Modules/GearSwapInsuranceDiagnostics.cs'
$session = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$icon = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerInsuranceRaidIconPatch.cs')
$display = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerInsuranceRaidDisplay.cs')
$diagnostics = Get-Content -Raw $diagnosticsPath
foreach ($phase in @('"open", draft: true', '"staged:" + _edits.Count', '"applyDraft"', '"applyBefore"', '"applyAfter"', '"closeCommitted"', '"closeCancelled"')) {
    if (!$session.Contains($phase)) { throw "Missing insurance trace phase: $phase" }
}
foreach ($forbidden in @('RegisterInsuredItems(', 'RegisterSingleItem(', 'ClearInsuredItems(', 'InsuredItems =', 'RequestHandler.', 'SetActive(')) {
    if ($diagnostics.Contains($forbidden)) { throw "Diagnostics must remain passive: $forbidden" }
}
if (!$icon.Contains('bool enable') -or !$icon.Contains('GearSwapInsuranceDiagnostics.Icon(') -or
    !$display.Contains('GearSwapInsuranceDiagnostics.Clear();') -or !$display.Contains('DiagnosticState()')) {
    throw 'Missing native icon observation or raid-reset/cache boundary.'
}
if (!$diagnostics.Contains('Time.time + 60f') -or !$diagnostics.Contains('MaximumItems = 512')) { throw 'Observations must be bounded.' }
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-insurance-trace-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temporary
$exe = Join-Path $temporary 'InsuranceTrace.exe'
$arguments = @($compiler, '/nologo', '/target:exe', '/define:DEBUG', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
& dotnet @arguments (Join-Path $PSScriptRoot 'GearSwapInsuranceDiagnosticsFixture.cs') $diagnosticsPath
if ($LASTEXITCODE -ne 0) { throw 'Insurance diagnostics fixture compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Insurance diagnostics fixture failed.' }
$releaseExe = Join-Path $temporary 'InsuranceTraceRelease.exe'
$releaseArguments = @($arguments | Where-Object { $_ -notlike '/define:*' -and $_ -notlike '/out:*' }) + "/out:$releaseExe"
& dotnet @releaseArguments (Join-Path $PSScriptRoot 'GearSwapInsuranceDiagnosticsFixture.cs') $diagnosticsPath
if ($LASTEXITCODE -ne 0) { throw 'Release insurance diagnostics fixture compilation failed.' }
& $releaseExe --release
if ($LASTEXITCODE -ne 0) { throw 'Release insurance diagnostics fixture failed.' }
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $type = $module.GetType('EFT.UI.DragAndDrop.GridItemView')
    $method = $type.Methods | Where-Object { $_.Name -eq 'EnableInsuranceIcons' -and $_.Parameters.Count -eq 1 }
    if (!$method -or $method.Parameters[0].Name -ne 'enable' -or $method.Parameters[0].ParameterType.FullName -ne 'System.Boolean') {
        throw 'Installed native insurance-icon argument signature changed.'
    }
    $company = $module.GetType('EFT.UI.Insurance.InsuranceCompany')
    $lookup = $company.Methods | Where-Object { $_.Name -eq 'Insured' -and $_.Parameters.Count -eq 1 }
    if (!$lookup -or $lookup.Parameters[0].ParameterType.FullName -ne 'System.String') { throw 'Installed exact-ID insurance lookup changed.' }
    Write-Output 'Installed exact-ID insurance/icon metadata and passive diagnostic source guards verified (not raid qualification).'
} finally { $module.Dispose() }
