param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'FollowerInsuranceMail/FollowerInsuranceMail.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Mail insurance client fixture failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'FollowerInsurance/FollowerInsurance.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Follower insurance fixture failed.' }
$patch = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerInsuranceMailClaimPatch.cs')
$client = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerInsuranceMailClaim.cs')
$service = Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FollowerInsuranceCourierTransferService.cs')
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
if (!$patch.Contains('!result.Succeed') -or !$patch.Contains('InventoryWarnings?.Any()') -or
    !$plugin.Contains('new FollowerInsuranceMailClaimPatch().Enable();')) { throw 'Missing successful-claim gate or patch installation.' }
if ($client.Contains('ClearInsuredItems') -or $client.Contains('RegisterInsuredItems(') -or
    !$client.Contains('OnItemInsured.Invoke(registered)') -or !$client.Contains('while (!request.IsCompleted) yield return null;')) {
    throw 'Mail refresh must be additive, publish native insurance events and apply on the coroutine thread.'
}
$readOnly = $service.Substring($service.IndexOf('public List<FriendlyTeammateInsuredItem> GetClaimedPolicies'))
if ($readOnly.Contains('storage.Write') -or $readOnly.Contains('SaveProfile') -or $readOnly.Contains('TryApplyClaimed(')) {
    throw 'Claim policy read endpoint must never grant coverage or mutate storage.'
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $session = $module.GetType('EFT.ClientBackendSession')
    $callback = $session.Methods | Where-Object { $_.Name -eq 'SendCallback' -and $_.Parameters.Count -eq 2 }
    if (!$callback -or $callback.Parameters[0].Name -ne 'commandsToSend' -or
        $callback.Parameters[0].ParameterType.FullName -ne 'System.Object[]' -or
        $callback.Parameters[1].Name -ne 'result' -or
        $callback.Parameters[1].ParameterType.FullName -notmatch 'Result`1<EFT.ClientBackendSession/QueueData>') {
        throw 'Installed native SendCallback signature changed.'
    }
    $company = $module.GetType('EFT.UI.Insurance.InsuranceCompany')
    if (!($company.Methods | Where-Object { $_.Name -eq 'RegisterSingleItem' -and $_.Parameters.Count -eq 2 }) -or
        !($company.Fields | Where-Object { $_.Name -eq 'OnItemInsured' })) { throw 'Installed native insurance refresh API changed.' }
    $move = $module.GetType('EFT.InventoryLogic.Operations.MoveOperation').NestedTypes | Where-Object { $_.Name -eq 'MoveCommand' }
    if (!($move.Fields | Where-Object { $_.Name -eq 'Item' -and $_.FieldType.FullName -eq 'System.String' })) {
        throw 'Installed mail-move item identity changed.'
    }
    Write-Output 'Installed mail callback/insurance APIs and additive/read-only source guards verified (not raid qualification).'
} finally { $module.Dispose() }
