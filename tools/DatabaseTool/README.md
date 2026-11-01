# pitFireTeam Database Tool

Export encrypted teammate data to readable JSON, edit it yourself or with an AI, then import the changes. It uses the mod's existing LiteDB/encryption code. You do not need the game, server DLL or an encryption key to use the portable Windows build.

Close Tarkov and the SPT server first. Work on the original `<24-character-profile-id>.db` filename; renaming it to another profile ID changes which encryption key is used.

Each mode has a separate database under the server mod:

- `Resources/teammates/<profileId>.db`: Guns for Hire
- `Resources/teammates-allegiance/<profileId>.db`: Allegiance
- `Resources/teammates-going-rogue/<profileId>.db`: Going Rogue

Open PowerShell in the utility folder. Replace the example paths with your own:

```powershell
.\pitFireTeam.DatabaseTool.exe list "C:\Squad\0123456789abcdef01234567.db"
.\pitFireTeam.DatabaseTool.exe export "C:\Squad\0123456789abcdef01234567.db" "C:\Squad\squad.json"
```

To export just one document, add `--document`, using a name from `list`:

```powershell
.\pitFireTeam.DatabaseTool.exe export "C:\Squad\0123456789abcdef01234567.db" "C:\Squad\settings.json" --document "1538550-settings.json"
```

Edit only the exported `documents[*].value` contents. Keep document names, profile ID, format/version, original hashes and source fingerprint intact. The JSON is readable private profile data; you can share only a selected document instead of the whole squad.

An AI editing request can say: "Edit only the requested fields inside `documents[*].value`. Preserve all metadata, identities, original fields and JSON types. Return the complete valid JSON file."

Import your edited export:

```powershell
.\pitFireTeam.DatabaseTool.exe import "C:\Squad\0123456789abcdef01234567.db" "C:\Squad\squad.json"
```

The tool rejects invalid/duplicate JSON properties, changed original field types, changed teammate identities, internal/nonexistent documents, wrong profiles and stale exports. It updates only changed documents. Omitted documents are retained; deleting a teammate/document is not an import operation. It does not validate every EFT item template, stat range or inventory link: keep those consistent when editing game data.

Import first edits and verifies an encrypted working copy, then atomically replaces the database and keeps the original beside it as `<profileId>.db.<timestamp>.<suffix>.bak`. A failed validation leaves the original unchanged. Formatting-only/no-op imports do not replace it or create a backup. Export never overwrites an existing output file. Internal migration and recovery records are retained and are not exported for editing.

If the database changed after export, export again and reapply the intended edit. Do not substitute old fingerprints. To restore a backup, close SPT/Tarkov and copy the desired `.bak` over the original `.db`, retaining its original profile ID filename. An uncheckpointed `-log.db` must be resolved by opening and closing the mod normally; do not delete it to bypass the tool's check.

## Build from source

Requires the .NET 10 SDK. The normal build uses the .NET 10 runtime:

```powershell
dotnet run --project tools/DatabaseTool -- --help
pwsh -NoProfile -File tools/DatabaseTool/Publish.ps1
```

The script creates `artifacts/database-tool/pitFireTeam.DatabaseTool-win-x64.zip`, including the self-contained executable, README and dependency notices. Unzip the complete package before running it. It requires Windows x64 but no separate .NET installation. No live database is included. The utility is an offline support tool; do not install it in BepInEx or the server mod directory.
