# SAIN addon reference baseline

**Core base:** [Build baselines and SPT compatibility](../../docs/SPT-Compatibility.md). The requirements below apply specifically to the optional addon.

The addon targets SAIN 4.5.1.0 and retains the existing SPT 4.1.0 minimum compile baseline. General core compatibility still uses reflection without a hard SAIN assembly dependency.

Provision SAIN.dll and SAIN.Preset.Shared.dll into addon/refs/4.5.1 from the SAIN 4.5.1 installation used for testing. These private files are ignored by Git. Alternatively pass SainReferenceRoot to MSBuild. Both assembly identities are checked before reference resolution.

Do not replace client/libs4.1/SAIN.dll (the older reference) or the minimum SPT baseline. Do not deploy these references over the external SAIN installation. Deploy the addon and matching core outputs.

Source inspection used the local SAIN 4.5.1 snapshot listed in LOCAL.md. It has no Git metadata; its precise upstream commit and byte identity with installed SAIN were not established. Installed metadata, native action constructors and the private FindCoverPoint/sprint-field bridge checks separately validate the API. Replicated solo/squad routing, squad decision policy, and player-leader action adaptations retain upstream MIT attribution in SAIN-LICENSE.txt, which accompanies the addon.

## Verified local reference hashes (SHA-256)

- SAIN.dll: C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489
- SAIN.Preset.Shared.dll: CD68E6DC65508C8BBBE71103F2AE7DE083C5281D6A57BC7D3CB846C133FC1F58

## Native source findings retained from the rework

SAIN 4.5.1 exposes `SAINLayer` but keeps concrete combat layers and most actions internal. Use validated constructor/type resolution for inaccessible actions. Keep the native decision manager and its single event publication; a handled None squad result must be distinct from an unhandled provider. Human leadership cannot be represented by a fake native `BotComponent`.

The replication checkpoint was `60d725bd60050a7c02ebfaac3ef73772468d2f8e`. Its fixed-Chad, no-command and core-owned-addon-hook restrictions are historical and superseded by [Integration](Integration.md), [Combat](Combat.md) and [Personalities](Personalities-and-Aggression.md). The completed phase-one and rework plans are recoverable in Git history.

Copied SAIN code requires its upstream MIT attribution. The working-tree `addon/SAIN-LICENSE.txt` was already deleted before this documentation pass; this review does not restore or authorize removing required attribution from distributions. Resolve the missing notice before packaging copied code.

See [Validation](Validation.md) for dated deployment evidence. Source version agreement does not establish byte identity with installed binaries or prove raid behavior.
