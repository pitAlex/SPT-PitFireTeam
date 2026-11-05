# SAIN follower report synchronization validation

Current contract: [Core SAIN compatibility](../SAIN-Compatibility.md#accepted-report-synchronization) and [enemy tracking](../Enemy-Tracking.md).

## 2026-11-04 — Customs stale-report investigation and correction

Record `20261104-165335-bigmap.jsonl`, late Scav `6aeb46e6d3c1ce898c54fe20` (Севастьян Шустрила): Hawk/Whisky initially retained a 31–33-second-old `nativeReport` at `(190.626648, -1.20221019, 191.64679)` while both were around height 5.83. Hawk's EFT sensed-report timestamp was fresh at acquisition. Hawk's tracking changed to `closeThreat` only at 2.58m; the Scav passed within about 0.63m. Whisky's magazine remained at 56, Hawk's at 16, with no weapon activity recorded during this encounter. Whisky had no shootable sight; Hawk obtained it around 1.9m near the encounter's end. Whisky's arbitration refused for `unreliableEnemyLocation`, not an insufficient aggression score. These observations do not establish an earlier clear line of fire.

The exact-native-report return in Core tracking deliberately protects SAIN uncertainty from EFT mirror bookkeeping. The correction preserves that rule, adding a genuine-report integration with both trackers updated. Completed native hearing reactions are observed after actual dispersion/publication; missing native publication receives a heard place, normal publication is not duplicated, and Core reads native final coordinates (including native height correction). Corrected personal sight and explicit accepted Core reports synchronize an existing native known place using original timestamps. Native goal selection, EFT visibility/fire flags, ordinary bots and the SAIN-absent route are unchanged. No native sensing/decision provider runs for diagnostics.

### Controlled evidence

- `Verify-SainTrackingReports.ps1`: 31 production tracking/bridge/Harmony checks. Compiles native heard/seen/last-known bodies and hearing/flyby reactions from the referenced SAIN 4.5.1 source. Covers stale downstairs knowledge, absent native squad, next-frame consistency/no timestamp renewal, actual struct boxing, ordinary/removed/unaccepted/dead contacts, absent SAIN, Simple mode, rejected sounds, no sensor reevaluation, native height adjustment, one publication, sound-kind preservation, provisional sight exclusion, future/stale/mismatched reports, nested scopes and exception propagation/cleanup. Initial fixture-only compile issues and a Simple-mode resolver double were corrected; final checks pass.
- `Verify-EnemyTracking.ps1`: 56 existing tracking checks pass. Installed metadata validates 21 tactical reads and source boundaries; production IL validation covers 14 native methods and 25 stack-compatible replacements. Both original-hearing and new report readers compile against installed SAIN metadata.
- `Verify-CoreSoundAwareness.ps1`: 28 existing peaceful sound-awareness checks pass.
- `Verify-SainAddonCombat.ps1`: 1,705 production addon combat checks pass. The fixture emits its existing stand-in/type-shadowing warnings; this is distinct from the plugin build.

Total: 1,820 controlled checks. These are source/metadata/fixture checks, not live Unity perception, steering, navigation, frame-time or next-raid qualification. Native SAIN setters/getters used by the bridge are verified against the installed 4.5.1 assembly; Core retains no typed SAIN dependency.

### Build and deployment

Final Core Debug build passed with zero warnings/errors. Core assembly metadata confirms no typed SAIN reference. Deployment completed with Tarkov closed; only Core client DLL/PDB files were replaced and both installed SHA-256 hashes matched the frozen outputs.

Receipt: `tests/artifacts/sain-report-sync/deployed-592fa62e204143abb6e7a8844c944480/manifest.json`.

- Core DLL: `4EC9AECE011998F9486B7CA8FFEB0F156E3A570EDCE882F189ADA542207256B1`.
- Core PDB: `1CCFBA4763DC1F51DF19D7EB0A7C54A51C67EFEB81C3DD4B095777A8D6043243`.

External SAIN, optional-addon, server and profile files were untouched. Live raid qualification remains pending.
