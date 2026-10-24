# Build baselines and SPT compatibility

This document records compile-time boundaries, not a promise that every SPT/SAIN combination has been tested. Machine-local runtime/source locations and packaging instructions are in [LOCAL.md](../LOCAL.md).

## Core build baseline

- Supported development line targets SPT 4.1.x with a **4.1.0 minimum SPT API baseline**. The previous 0.9 line is a legacy reference, not a deployment target for the current install.
- [Client](../client/pitFireTeam.csproj) targets .NET Framework 4.7.2. Direct SPT common/reflection references live in `client/libs4.1/spt4.1.0` and must have assembly version `4.1.0.0`.
- Changed game assemblies use `client/libs4.1`; byte-identical older references may remain in `client/libs`. This is separate from the minimum SPT API baseline.
- [Server](../server/pitFireTeam.Server.csproj) targets .NET 10 and validates private references under `server/refs/4.1.0` (or `SptReferenceRoot`) at `4.1.0.0`.
- Installed runtime versions and source snapshots do not replace minimum compile references. Verify actual references and supported runtime behavior before declaring another version supported.

## Optional dependencies

Core detects the external SAIN plugin at runtime and keeps general compatibility independent of addon presence. [SAIN compatibility](SAIN-Compatibility.md) documents the verified native boundaries and checks. The optional addon has its own [SAIN 4.5.1 references](../addon/docs/References.md); core support for another SAIN version does not imply addon support for it.

## Build and package

```powershell
dotnet build client/pitFireTeam.csproj -c Debug -p:BuildSAINAddon=false
dotnet build server/pitFireTeam.Server.csproj -c Debug
```

Inspect build targets before running a server build: LOCAL.md notes a legacy copy target. Use the documented machine-specific deployment flow and matching outputs. Release archives contain `BepInEx/` and `SPT_Runtime/` at their root, rather than an extra enclosing version directory. Private reference DLLs are not shipped over the external dependencies.

Run the tests relevant to changed behavior. Build success proves compilation, not in-raid compatibility, navigation, healing or performance.
