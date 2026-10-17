# Fence karma

## Allegiance Scav rules

Intentional friendly ordinary-Scav kills, including sniper Scavs calmed by a companion, cost exactly **0.05 Fence standing for a recruited companion**, or **0.025 for an unrecruited friendly/neutral Scav**. The player must initiate damaging fire or issue an accepted Contact command against that neutral victim; a PMC follower may finish the kill. An uncommanded PMC attack is excluded, even if the player later finishes that fight. The first intent/damage classification survives retaliation. Outside killers and Scav followers do not charge the player. Only a recruited victim plays the negative karma sound.

`ScavFenceKarma.RecordKill` uses native `Profile.FenceInfo.AddStanding` with the selected negative amount, `ScavKill`, and `false`. The final argument bypasses charisma discount and its two-decimal floor so the fixed amounts stay exact. No high-reputation multiplier is added. The native raid-result profile carries standing to the server; native post-raid handling clamps it and synchronizes PMC and player-Scav trader data. There is no second standing HTTP report. This follows native persistence at raid end, not crash-proof saving during a raid.

Survived/Runner extraction at a non-co-op exit with a living recruited Scav still following the human grants a flat **+0.025 Fence standing** once per raid. Multiple partners do not multiply it, and repeated use of the exit does not diminish it. The reward respects configured Fence bounds and does not touch native co-op use counts. Real co-op exits, including SVM-paid conversions still listed as co-op by the server, use only native standing/gift handling; the mod adds no reward or fallback when native rewards are disabled. No positive karma sound plays. See [cargo and extraction](looting/Friendly-Scav-Cargo.md).

Empty-handed ordinary extraction sends Fence's localized thank-you note as well as the existing standing gain. Transit departure sends the same note when no cargo remains, but adds no supplemental standing or random co-op reward. Real co-op messages/rewards and PMC recruit/transit behavior remain unchanged.

These rules do not change PMC `karmaValue` or the independent 24-hour encounter penalty ledger. Scavs also do not create PMC quiet-recovery eligibility.

## Scav encounter chance

Fence standing multiplies the Scav base supplied by [Friendly Encounter Chance](Allegiance.md#finding-friendly-pmcs). The standing factor is clamped to 0–6 and the result to 0–100%; encounter penalties then reduce that final chance proportionally. At the default slider value of 30%, the Scav base is 15%.

| Fence standing | Scav chance at the 30% default |
|---|---|
| 0 or below | 0% |
| 0.2 | 3% |
| 0.5 | 7.5% |
| 1 | 15% |
| 2 | 30% |
| 3 | 45% |
| 4 | 60% |
| 5 | 75% |
| 6 and above | 90% |

The formula is `clamp(oppositeSideBase * clamp(standing, 0, 6), 0, 1)`. At a slider value of 80%, the base is 40% and reputation 2.5 or higher reaches 100% before penalties. Standard starting standing 0 produces zero Scav encounter chance; editions with starting standing 0.2 produce 3% at the default. Missing/non-finite standing falls back to zero. There is no raid-wide encounter cap. Each profile still rolls once, with all role/group/trust exclusions retained; standing changes affect future candidates only.

## Native reference values

Inspected on 2026-10-12 against installed SPT 4.1.6 assemblies/data and the local SPT/EFT 4.1.3 source reference. Other mods can change runtime configuration. These are SPT findings, not a claim about current live Tarkov balance.

| Native source/action | Value |
|---|---|
| Standard / Left Behind starting standing | 0 |
| Prepare To Escape / Edge Of Darkness / Tournament starting standing | 0.2 |
| Unheard starting Fence standing | 0.5 |
| SPT Easy Start / Developer / Zero to Hero | 1 / 6 / 0 |
| Maximum Fence loyalty tier | 6 standing |
| Server standing bounds | −7 to 15 |
| Successful player-Scav extract | +0.01 |
| First co-op exit use | +0.25 |
| First car exit use | +0.20 |
| Repeated car/co-op use | `max(base gain / that exit's use count, 0.01)`; final standing rounded to two decimals |
| Ordinary Scav kill, native base | −0.03 easy / −0.04 normal / −0.05 hard |
| Sniper Scav kill, native base | −0.04 normal / −0.05 hard |
| Example boss kills: Reshala / Killa | −0.20 base |
| Reshala guard kill | −0.05 base |

Native maximum-tier Scav kill handling can additionally remove reputation above 6, bounded by `2^regularScavKills` per raid; charisma may reduce native kill penalties. The mod's fixed 0.05 recruited and 0.025 unrecruited penalties deliberately do not use those extra rules.

SPT's `ScavRepAdjustmentPatch` limits its kill adjustments to human player-Scavs. It uses the killed PMC's `StandingForKill` only when that PMC has killed a Scav, and otherwise calls native `GifterKill`. The installed PMC templates currently have a zero `StandingForKill`, so the commonly quoted universal positive reward for killing a PMC should not be assumed here. Vanilla aggressor/help methods and template `aggressorBonus` fields exist, but are not evidence of a fixed active SPT reward for every such kill. Fence quest rewards are quest-specific.

Evidence: `SPT_Data/configs/inraid.json`, `configs/trader.json`, `database/templates/profiles.json`, `database/bots/types/*.json`, `database/globals.json`; upstream `LocationLifecycleService.HandleCoopExtract`, `GetFenceStandingAfterExtract`, `HandlePostRaidPmc`, `HandlePostRaidPlayerScavAsync`; EFT `PlayerLoyaltyData.GifterKill` and `Profile.FenceTraderInfo.AddStanding`; SPT client `ScavRepAdjustmentPatch`. Machine-local source/runtime roots are in `LOCAL.md`.
