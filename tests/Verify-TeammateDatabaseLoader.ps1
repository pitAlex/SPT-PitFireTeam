param(
    [Parameter(Mandatory)][string]$ServerRuntimeRoot,
    [string]$ServerBuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'server/bin/Debug/net10.0')
)
$ErrorActionPreference = 'Stop'
$ServerRuntimeRoot = (Resolve-Path -LiteralPath $ServerRuntimeRoot).Path
$ServerBuildDirectory = (Resolve-Path -LiteralPath $ServerBuildDirectory).Path
$testRoot = Join-Path $PSScriptRoot ('artifacts/database-loader/' + [Guid]::NewGuid().ToString('N'))
$modDirectory = Join-Path $testRoot 'mod'
New-Item -ItemType Directory -Path $modDirectory -Force | Out-Null
# Match the complete production dependency set: our server plus one managed library.
foreach ($name in @('pitFireTeam.Server.dll', 'LiteDB.dll')) {
    Copy-Item -LiteralPath (Join-Path $ServerBuildDirectory $name) -Destination $modDirectory
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
namespace PitFireTeam.DatabaseSmoke {
    public sealed class Loader : AssemblyLoadContext {
        private readonly string runtime;
        private readonly string mod;
        private Loader(string runtime, string mod) : base("teammate-database-smoke", true) {
            this.runtime = runtime; this.mod = mod;
        }
        protected override Assembly Load(AssemblyName name) {
            foreach (string root in new[] { mod, runtime }) {
                string path = Path.Combine(root, name.Name + ".dll");
                if (File.Exists(path)) return LoadFromAssemblyPath(path);
            }
            return null;
        }
        public static string Run(string runtime, string mod, string databases) {
            var context = new Loader(runtime, mod);
            try {
                var assemblies = Directory.GetFiles(mod, "*.dll").Select(context.LoadFromAssemblyPath).ToArray();
                int types = assemblies.Sum(a => a.GetTypes().Length);
                var assembly = assemblies.Single(a => a.GetName().Name == "pitFireTeam.Server");
                var type = assembly.GetType("pitTeam.Server.Persistence.TeammateDatabase", true);
                object store = Activator.CreateInstance(type, databases, "eeeeeeeeeeeeeeeeeeeeeeee");
                Func<IReadOnlyDictionary<string, string>> legacy = () => new Dictionary<string, string> { ["12.json"] = "{\"nickname\":\"Loader test\"}" };
                int imported = (int)type.GetMethod("Initialize").Invoke(store, new object[] { legacy });
                string json = (string)type.GetMethod("Read").Invoke(store, new object[] { "12.json" });
                if (imported != 1 || json != legacy()["12.json"]) throw new Exception("Storage round trip failed.");
                return types + " types loaded; encrypted LiteDB round trip passed through direct DLL loading.";
            }
            catch (ReflectionTypeLoadException ex) {
                throw new Exception(string.Join(Environment.NewLine, ex.LoaderExceptions.Select(e => e.Message)), ex);
            }
            finally { context.Unload(); }
        }
    }
}
'@
[PitFireTeam.DatabaseSmoke.Loader]::Run($ServerRuntimeRoot, $modDirectory, (Join-Path $testRoot 'databases'))
