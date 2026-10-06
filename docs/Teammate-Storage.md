# Teammate database storage

Current policy (2026-09-09): import once and retain the original JSON folder with a .backup suffix. **No automatic deletion.**

## Location and lifecycle

Each player profile owns `Resources/teammates/<profileId>.db` for Guns for Hire and `Resources/teammates-allegiance/<profileId>.db` for Allegiance in the server mod directory. Database caches include the mode root. Allegiance starts empty and never imports the Guns for Hire legacy files. All teammate documents, including equipment, settings, and recruitment requests, follow the active database. Account-id allocation checks both roots to keep identities distinct. Mode changes and teammate route operations share a request gate so an operation cannot cross database roots while it runs.

Captured recruits preserve their BEAR/USEC side. If a captured profile is missing or its side is absent, recruitment uses the invitation's validated side before falling back to the player's side. Allegiance raid spawns retain the saved PMC faction; follower loyalty and group relations use the human leader independently of that faction.
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

Recruit invitations carry nullable `Aggression` captured from the follower's base combat aggression after native SAIN personality mapping. Acceptance writes it into the teammate settings in the existing profile/default-equipment/receipt transaction. It remains stable through invitation storage, acceptance and roster reload; temporary raid command overrides are never saved here. Missing legacy values and non-finite values use 50%, and finite values are clamped to 0–100%.

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

Manual hiring stores its pending candidate, quote and payment journal under `pending-creation.json`
in the same encrypted database. This document is excluded from roster profile enumeration.
Confirmation commits the teammate, settings, Default equipment and completion receipt together;
the journal reconciles the separate SPT player-money save after an interruption. The no-gear path
commits the stripped profile and Default snapshot without saving player money. See
[teammate addition cost](Loadout-Management.md#teammate-addition-cost) for quote lifetime,
pricing-version checks, duplicate-request handling and recovery rules.

Raid-recruit invites store `RecruitmentGearPrice`; acceptance preserves this value in the member's
settings. Acceptance commits the member/profile, Default equipment, settings, remaining invitations
and `accepted-recruits.json` receipt together under the shared lifecycle lock. Repeated acceptance
returns the receipt without creating another member, including after that member is removed.
Accept All commits one invitation at a time; after a failure only unfinished invitations remain.
An old captured member already saved with its invitation still pending is recognized by profile ID. A missing value on an existing member means no deletion fee. Paid deletion uses a separate
`pending-deletion.json` money journal and prepared courier outbox. Removing the profile/settings/Default documents and writing
the `delivering` outbox happen in one database transaction. Both normal and recruited removals return current equipment.
The prepared native message and attachment IDs survive restarts. The SPT player profile saves a namespaced
`SptData.Migrations["pitFireTeam/removal-courier"]` receipt alongside the mail before the outbox becomes `complete`;
collecting or deleting that mail does not clear the receipt. A delivery failure keeps the outbox pending and the
committed removal paid, so recovery cannot refund a delivered kit or create another one. Both payment journals and acceptance receipts are excluded from
roster profile enumeration. A failed payment commit records `refunding` and the original rouble
item snapshot before refund persistence. Recovery preserves unrelated inventory, rejects ambiguous
money changes, and verifies the saved player JSON before marking payment/refund state resolved.
A save-hash cache left by a failed SPT write can skip a retry; a mismatched file keeps recovery
blocked, and a server restart permits a fresh verified save. See [raid-recruit deletion fees](Loadout-Management.md#raid-recruit-deletion-fee).

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
