# Teammate medkit treatment — 2026-11-03

Contract: [Teammate health](../Teammate-Health.md#medkit-treatment).

## Source and fixture checks

- Verified the native equipment arrow uses `EFT.UI.EquipItemWindow`. The treatment picker clones its search/grid/resource views with a custom selection context; native slot equip handlers are not used.
- Verified native `OfflineHealthController.MedEffect` spends one resource point per HP. `hpResourceRate` controls the amount per treatment animation, not HP conversion.
- `dotnet run --project tests/TeammateTreatment/TeammateTreatment.csproj -c Debug --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- E:\SPTushanka\SPT_Runtime`: **129 checks passed**. Includes existing money/recovery/raid-health checks plus medkit eligibility, nested/equipment/sorting-table ownership, locks, malformed/duplicate ownership, resource/default capacity, partial/exhausted kits, stale quotes, money/relationship/player-health preservation, receipt recovery, regeneration-adjusted spending and failed player saves with native cached-hash recovery.
- Core client and server Debug builds passed. NuGet vulnerability metadata retrieval was unavailable (`NU1900`); no compiler errors.
- `Verify-SptCompatibility.ps1` passed against installed SPT 4.1.6: server 225 types / 860 members, client 920 types / 2,323 members. All six reflected picker fields were also found with the expected types in the installed game assembly.
- Scoped `git diff --check` passed.

## Deployment

Tarkov and SPT.Server were stopped before replacement. Deployed only the Core Debug client/server DLLs and English resource; source/destination SHA-256 matched:

| File | SHA-256 |
|---|---|
| `BepInEx/plugins/pitFireTeam/pitFireTeam.dll` | `CD4FCCA3D5DFBAA2A6C2B97266F58DD068E675A34CC5331786BA4FAA044EF993` |
| `SPT_Runtime/user/mods/pitFireTeam-ServerMod/pitFireTeam.Server.dll` | `AF2A322BC8B89972C35CC4E2F936330E439FBC28B649CB5DD9DB70D1BEA0F9E7` |
| `SPT_Runtime/user/mods/pitFireTeam-ServerMod/Resources/lang/en.json` | `2236946D36FA9132FABFB9DFB328E57C70E25DA8506E603CA4DC1A17876BE110` |

## Pending live qualification

No game session was started for this change. Confirm native popup sizing/input ownership, search and text/grid toggle, Escape/X cancellation without spending, choosing nested/equipped medicine, immediate resource/removal display, reopening after partial treatment, and full-health animation before the overlay hides. Fixture/API/deployment success does not establish these Unity UI behaviors.
