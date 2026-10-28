# Database Tool validation

## 2026-10-25 — Windows x64

The utility source-links `server/Persistence/TeammateDatabase.cs` and uses LiteDB 5.0.21. The server change adds a locked, authenticated document snapshot method; encryption, schema and existing storage behavior remain unchanged.

`dotnet run --project tests/DatabaseTool/DatabaseTool.csproj -c Debug -p:NuGetAudit=false --no-restore` passed 51 checks against synthetic encrypted databases. Coverage includes read-only export, selected documents, encrypted round trips, exact-byte backups, internal-record preservation, no-op edits, Boolean edits, reordered properties, stale exports, identity/type/property validation, mixed valid/invalid batches, process/file/log guards, malformed or duplicate JSON properties, corrupted ciphertext and missing databases.

The Debug portable Windows x64 package includes .NET 10.0.13 and the applicable LiteDB/.NET licenses and notices. Its executable independently passed an export/edit/import/re-export smoke test on a copied synthetic database, verifying the changed value and exact original encrypted backup. Help also ran with `DOTNET_ROOT` pointing to a nonexistent directory and multilevel lookup disabled.

The existing FollowerDatabase mode-smoke suite also passed 280 gameplay storage checks against the installed SPT runtime dependencies. NuGet vulnerability metadata could not be fetched in that run; this warning did not prevent the checks from executing.

Synthetic fixtures are under ignored `tests/artifacts/database-tool/e12f1ecaab1147c3a0bad1c3dd743e8f` and `tests/artifacts/database-tool-portable/c769a0aa501041f4af93483b4d3f450e`. No live database was modified. This does not establish that arbitrary edited EFT inventory or statistics are valid in game.

| Artifact | SHA-256 |
|---|---|
| `artifacts/database-tool/win-x64/pitFireTeam.DatabaseTool.exe` | `50995FC69ECD5F79D118225ED7D81C75819A9FE20FA5DA7F8A554375A4A163D0` |
| `artifacts/database-tool/pitFireTeam.DatabaseTool-win-x64.zip` | `1364226FDDEF67C5C9AF6A74F4F266FB67BA8D98DCAC079FA8458A52219E44F8` |
