# Teammate database storage

Current policy (2026-09-09): import once and retain the original JSON folder with a .backup suffix. **No automatic deletion.**

## Location and lifecycle

Each player profile owns `Resources/teammates/<profileId>.db` in the server mod directory.
For example, `Resources/teammates/6a875d350ecfc1eccbd6fad0.db` replaces the active use of
the adjacent `6a875d350ecfc1eccbd6fad0/` folder.

Server post-load initializes databases for known SPT profiles and existing teammate stores before
duplicate-item recovery. The game-start route also initializes storage for profiles created since
server startup. Other storage access uses the same initializer.

The storage service is registered as an SPT singleton. Startup, social, recruit, and teammate
routes share one per-profile database cache and its operation locks, so overlapping requests
wait for the active operation to close its database connection.

The importer reads numeric teammate profile JSONs, settings, default equipment, and
`recruit-requests.json`. It validates the original documents using SPT's serializer and commits
them with an import marker in one transaction, verifying each imported value. Malformed active
documents fail that profile's migration without deleting files or accepting a partial roster; other profiles can still initialize. Old recovery-backup
files and unrelated files are not active documents; the folder rename preserves them too.

After successful import, the database is authoritative. Restarting with old JSONs present,
moving them away, or restoring them does not overwrite new saves or resurrect deleted teammates.
A migration failure remains retryable; an unreadable database is never silently replaced.

After successful initialization, the original folder is renamed to
`<profileId>.backup`. This also handles databases imported by earlier builds: their completed
migration marker is verified before renaming any remaining source folder. The files are retained
without content changes. Failed imports keep the original folder in place.

An existing backup path is never overwritten or merged; both it and the source folder remain.
A failed rename does not block a successfully initialized database and is retried on the next
server startup. Backup folders are excluded from automatic profile discovery and JSON import.
The normal startup log reports only that the database is ready; it does not announce cleanup
status or repeat an import count on every launch.

## Representation and protection

The LiteDB documents collection holds encrypted JSON payloads, retaining the existing SPT models and
client API. AES-GCM authenticates each payload against its profile ID and document key, with a
fresh nonce for every write. The versioned application key remains stable across updates and
platforms; changing it requires a migration. This deters casual editing, not a determined owner
of the local machine. Document keys are visible; profile data is not stored as plaintext.

Create, loadout/kit/repair bundle saves, and teammate deletion update their related documents in
one database transaction. Existing inventory validation and SPT player-profile saving remain
separate: the teammate database cannot make a transaction atomic with SPT's independent player stash file.
Recovery snapshots are encrypted documents inside the same database, saved with the correction.
Global mod settings and language resources remain normal configuration files.

LiteDB uses a write-ahead log and checkpoints on closing the database after each operation. Back up or
move the database with the server stopped. Runtime reads/writes do not create a missing database
mid-session; they fail instead of silently recreating an empty roster.

## Verification and backup retention

Start with an original JSON folder and confirm that the roster, settings, equipment, and pending
recruits are imported. The original folder should become `<profileId>.backup` with every file
unchanged. Change a setting and restart to confirm the database retains the new value.

An existing database must also keep its newer state when a source folder is restored. Restoring
old JSONs must never resurrect a removed teammate or overwrite settings saved after import.
If a backup already exists, leave both directories intact.

Keep automatic deletion unimplemented while users validate the database migration. These JSON
backups preserve the pre-import state; they do not track later database progress. Back up the
database separately with the server stopped when preserving current progress.

## Build and deployment

The server uses LiteDB 5.0.21. Deploy only the managed dependency `LiteDB.dll` beside
`pitFireTeam.Server.dll`. No native runtime folders or platform-specific SQLite DLLs are needed.
Never package runtime teammate databases, legacy JSON saves, or deployment backups.
See [LiteDB connection handling](https://www.litedb.org/docs/connection-string/).

### Local SQLite trial conversion

The earlier SQLite trial is converted offline while the game and server are stopped. Copy every
encrypted record, including migration metadata and recovery snapshots, directly into a fresh
LiteDB file. Authenticate all records with the production reader and compare every encrypted
payload byte-for-byte with the SQLite snapshot before replacing the live database.

Keep the original SQLite database and server binaries in a deployment backup. Do not rebuild
from the legacy JSONs: they do not contain changes saved during the database trial. The runtime
rejects SQLite files with a clear conversion error instead of overwriting them or reimporting
stale saves. The offline conversion helper lives only in the test project; the shipped server
has no SQLite dependency.

## Automated verification

Run from the repository root (substitute the local paths from LOCAL.md):

- dotnet run --project tests/FollowerDatabase -- "<SPT runtime root>" "<original or .backup profile directory>"
  tests the real storage adapter and SPT serializer with isolated copies of existing saves. It covers
  import fidelity, unchanged source hashes, folder renaming, existing-backup collisions,
  failed-import retention, backup exclusion, restart/move/restore phases, new records, deletion,
  recovery snapshots, failed-transaction rollback, malformed import retry, concurrent writes,
  overlapping reads/writes and deletions through SPT's real service-registration path,
  encryption/integrity failures, schema rejection, large payloads, interrupted version updates, and cross-profile isolation.
- pwsh -File tests/Verify-TeammateDatabaseLoader.ps1 -ServerRuntimeRoot "<SPT runtime root>"
  verifies direct loading of the server and LiteDB DLLs plus an encrypted storage roundtrip
  without relying on a host's package references.
- pwsh -File tests/Verify-SptCompatibility.ps1 -ServerRuntimeRoot "<SPT runtime root>" -SptVersion "<installed version>"
  checks the unchanged 4.1.0 reference baseline against the installed server.

Test artifacts are retained under ignored tests/artifacts/; the supplied JSON source files are only read; all rename tests use isolated copies.
Windows SPT 4.1.5 loader and copied-save tests pass; Linux and full in-game behavior still require
runtime validation.
