param(
    [Parameter(Mandatory)][string]$ServerRuntimeRoot,
    [Parameter(Mandatory)][ValidatePattern('^4\.1\.\d+$')][string]$SptVersion,
    [string]$ClientGameAssembly,
    [string]$ClientSptRoot,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ServerDll,
    [string]$ClientDll
)
# Checks real binaries. Does not start the game/server or simulate raid behavior.
$ErrorActionPreference = 'Stop'
if (!$ServerDll) { $ServerDll = Join-Path $RepositoryRoot 'server/bin/Debug/net10.0/pitFireTeam.Server.dll' }
if (!$ClientDll) { $ClientDll = Join-Path $RepositoryRoot 'client/bin/Debug/net472/pitFireTeam.dll' }
$ServerRuntimeRoot = (Resolve-Path -LiteralPath $ServerRuntimeRoot).Path
$ServerDll = (Resolve-Path -LiteralPath $ServerDll).Path
$actualVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ServerRuntimeRoot 'SPTarkov.Server.Core.dll')).Version.ToString(3)
if ($actualVersion -ne $SptVersion) { throw "Expected SPT $SptVersion runtime, found $actualVersion in $ServerRuntimeRoot" }
if ([bool]$ClientGameAssembly -ne [bool]$ClientSptRoot) { throw 'Supply both ClientGameAssembly and ClientSptRoot for client checks.' }
[Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')) | Out-Null

function Test-MemberReferences([string]$Dll, [string[]]$SearchRoots, [bool]$Client) {
    $resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    # Avoid silently resolving game/SPT dependencies from the caller's working directory.
    foreach ($path in @($resolver.GetSearchDirectories())) { $resolver.RemoveSearchDirectory($path) }
    foreach ($path in $SearchRoots) { $resolver.AddSearchDirectory($path) }
    $parameters = [Mono.Cecil.ReaderParameters]::new()
    $parameters.AssemblyResolver = $resolver
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($Dll, $parameters)
    try {
        $dependencies = @($module.AssemblyReferences | Where-Object {
            if ($Client) { $_.Name -like 'spt-*' } else { $_.Name -like 'SPTarkov.*' }
        })
        if ($dependencies.Count -eq 0) { throw "No SPT dependencies found in $Dll" }
        foreach ($dependency in $dependencies) {
            if ($dependency.Version.ToString() -ne '4.1.0.0') {
                throw "SPT 4.1.0 baseline violated: $dependency in $Dll"
            }
            $runtimeDependency = $resolver.Resolve($dependency)
            if ($runtimeDependency.Name.Version -lt $dependency.Version) {
                throw "Runtime dependency is older than requested: $dependency"
            }
        }
        $checkedTypes = 0
        $checkedMembers = 0
        foreach ($reference in @($module.GetTypeReferences()) + @($module.GetMemberReferences())) {
            $type = if ($reference -is [Mono.Cecil.TypeReference]) { $reference } else { $reference.DeclaringType }
            $scope = $type.GetElementType().Scope.Name
            $relevant = if ($Client) { $scope -eq 'Assembly-CSharp' -or $scope -like 'spt-*' } else { $scope -like 'SPTarkov.*' }
            if (!$relevant) { continue }
            $resolved = $reference.Resolve()
            if ($null -eq $resolved) { throw "Missing runtime API: $reference" }
            if ($reference -is [Mono.Cecil.TypeReference]) { $checkedTypes++ } else { $checkedMembers++ }
        }
        [pscustomobject]@{ Binary = [IO.Path]::GetFileName($Dll); Types = $checkedTypes; Members = $checkedMembers }
    }
    finally { $module.Dispose(); $resolver.Dispose() }
}

if (-not ('PitFireTeam.SptCompatibilityTests.Loader' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
namespace PitFireTeam.SptCompatibilityTests {
    public sealed class Loader : AssemblyLoadContext {
        private readonly string root;
        private Loader(string root) : base("pitFireTeam-compatibility", true) { this.root = root; }
        protected override Assembly Load(AssemblyName name) {
            string path = Path.Combine(root, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            // Do not let a custom resolver override the normal minimum-version rule.
            if (AssemblyName.GetAssemblyName(path).Version < name.Version) return null;
            return LoadFromAssemblyPath(path);
        }
        public static string Smoke(string root, string modPath) {
            var context = new Loader(root);
            try {
                var core = context.LoadFromAssemblyPath(Path.Combine(root, "SPTarkov.Server.Core.dll"));
                var contract = core.GetType("SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata", true);
                var mod = context.LoadFromAssemblyPath(modPath);
                // Match the server's type enumeration and IModMetadata activation boundary.
                var types = mod.GetTypes();
                var metadataType = types.Single(t => contract.IsAssignableFrom(t));
                var metadata = Activator.CreateInstance(metadataType);
                var range = metadataType.GetProperty("SptVersion").GetValue(metadata);
                var satisfies = range.GetType().GetMethod("IsSatisfied", new[] { typeof(string), typeof(bool), typeof(bool) });
                foreach (string candidate in new[] { "4.0.13", "4.1.0", "4.1.1", "4.1.2", "4.1.3", "4.1.4", "4.1.5", "4.2.0" }) {
                    bool accepted = (bool)satisfies.Invoke(range, new object[] { candidate, false, false });
                    if (accepted != candidate.StartsWith("4.1.")) throw new Exception("Unexpected SPT range result: " + candidate);
                }
                return "SPT " + core.GetName().Version + ": " + types.Length + " mod types loaded; metadata accepts 4.1.0-4.1.5 and rejects 4.0/4.2.";
            }
            catch (ReflectionTypeLoadException e) {
                throw new Exception(string.Join(Environment.NewLine, e.LoaderExceptions.Select(x => x.Message)), e);
            }
            finally { context.Unload(); }
        }
    }
}
"@
}

Test-MemberReferences $ServerDll @($ServerRuntimeRoot, [IO.Path]::GetDirectoryName([object].Assembly.Location)) $false
[PitFireTeam.SptCompatibilityTests.Loader]::Smoke($ServerRuntimeRoot, $ServerDll)
if ($ClientGameAssembly) {
    $ClientGameAssembly = (Resolve-Path -LiteralPath $ClientGameAssembly).Path
    if ([IO.Path]::GetFileName($ClientGameAssembly) -ne 'Assembly-CSharp.dll') { throw 'ClientGameAssembly must be named Assembly-CSharp.dll.' }
    $ClientSptRoot = (Resolve-Path -LiteralPath $ClientSptRoot).Path
    $ClientDll = (Resolve-Path -LiteralPath $ClientDll).Path
    Test-MemberReferences $ClientDll @(
        [IO.Path]::GetDirectoryName($ClientGameAssembly),
        $ClientSptRoot,
        (Join-Path $RepositoryRoot 'client/libs4.1'),
        (Join-Path $RepositoryRoot 'client/libs')
    ) $true
}
