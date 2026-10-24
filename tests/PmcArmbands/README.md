# PMC armband checks

Run from the repository root:

```powershell
dotnet run --project tests/PmcArmbands/PmcArmbands.csproj -c Debug -- E:\SPTushanka\SPT_Runtime
```

The first argument selects the SPT assembly directory. Additional directories are dependency fallbacks; use `server/refs/4.1.0` first and the installed runtime second to check the minimum supported baseline.

The fixture uses native SPT inventory models and installs the production Harmony postfix. It registers the settings service and hook through SPT's actual DI registration method using their production lifetime attributes, then resolves startup, hook and later settings-update consumers independently. A test prefix returns a replacement inventory while skipping SPT's original method, reproducing the boundary used by Acid's generator. Checks cover startup settings, enabled/disabled setting changes, independence from shared SPT config changes, faction colours, missing/duplicate bands, idempotence, replaced item descendants, unrelated cargo and excluded profiles.

This does not load Acid's full generator or establish in-game appearance. Restart the server and test a fresh raid with PMC Faction Arm Bands enabled and Acid active; check both factions, then disable the setting and verify generator choices are retained on newly generated bots.

## Validation: 2026-10-12

- Debug server/fixture build passed against the SPT 4.1.0 compile baseline. NuGet vulnerability lookup reported an unavailable network feed.
- All 17 checks passed separately with SPT server assemblies 4.1.0.0 and installed 4.1.6.0.
- Debug server DLL/PDB deployed with Tarkov and SPT server stopped; copied SHA256 hashes matched the build.
- Full Acid generation and fresh-raid appearance remain pending in-game qualification.

## Validation: 2026-10-14

- User reported the initial fix did not work in-game. The deployed DLL matched the earlier build, both settings files enabled armbands, and server logs showed successful hook installation and settings application.
- Installed SPT DI metadata and source confirmed `[Injectable]` defaults to transient. The initial hook therefore captured a separate settings service whose applied flag was never initialized; the original fixture missed this by sharing one manually constructed instance.
- The revised fixture uses native SPT service registration and separate resolutions. It failed on the previous implementation at startup preference enforcement, then passed all 18 checks against SPT 4.1.0.0 and installed 4.1.6.0 after making the settings service a singleton. Later disable/enable resolutions also pass.
- Corrected Debug DLL/PDB deployed with Tarkov and SPT server stopped; copied SHA256 hashes matched the build. The startup hook log now includes its applied preference.
- Fresh-raid appearance with Acid remains pending in-game qualification.
