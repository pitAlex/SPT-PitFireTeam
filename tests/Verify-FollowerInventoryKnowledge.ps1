param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka',
    [string]$NativeSource = 'F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp/EFT/InventoryLogic/Magazine.cs'
)
$ErrorActionPreference = 'Stop'
$patchSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/TeammateBackpackInteractionPatch.cs')
$first = $patchSource.IndexOf('    internal class TeammateBackpackExaminedPatch')
$last = $patchSource.IndexOf('    internal class TeammateBackpackKnownItemPatch', $first)
if ($first -lt 0 -or $last -le $first) { throw 'Missing production examination patch' }
$patch = $patchSource.Substring($first, $last - $first).Replace('private static void PatchPostfix', 'internal static void PatchPostfix')
$helper = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerInventoryKnowledge.cs')
$helper = [regex]::Replace($helper, '(?m)^using [^\r\n]+\r?\n', '')
$native = Get-Content -Raw $NativeSource
$apply = [regex]::Match($native, '(?s)public virtual OperationResult ApplyItem\(.*?(?=\s*// Token: 0x0600CA85)')
if (!$apply.Success) { throw 'Missing native magazine ApplyItem guard' }
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerInventoryKnowledgeFixture.cs')).
    Replace('/* KNOWLEDGE_HELPER */', $helper).Replace('/* EXAMINATION_PATCH */', $patch).Replace('/* NATIVE_APPLY */', $apply.Value)
Add-Type -TypeDefinition $fixture -Language CSharp
$count = [FollowerInventoryKnowledgeChecks]::Run()
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $controller = $assembly.MainModule.GetType('EFT.InventoryLogic.InventoryController')
    $examined = @($controller.Methods | Where-Object { $_.Name -eq 'Examined' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.InventoryLogic.Item' })
    if ($examined.Count -ne 1 -or $examined[0].ReturnType.FullName -ne 'System.Boolean') { throw 'Installed examination signature changed' }
    $magazine = $assembly.MainModule.GetType('EFT.InventoryLogic.Magazine')
    $installedApply = $magazine.Methods | Where-Object { $_.Name -eq 'ApplyItem' -and $_.Parameters.Count -eq 4 }
    $calls = @($installedApply.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    if (@($calls | Where-Object { $_ -match 'InventoryLogic.ItemController::Examined' }).Count -ne 2 -or
        !($calls | Where-Object { $_ -match 'ItemNotExaminedError::.ctor' })) { throw 'Installed native examination admission changed' }
} finally { $assembly.Dispose() }
Write-Output "Passed $count follower inventory examination checks using the production helper/postfix and native magazine guard. Installed metadata verified; live refill still requires raid validation."
