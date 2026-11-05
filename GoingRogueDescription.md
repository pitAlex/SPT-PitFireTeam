# Tabs {.tabset}

## Description

**Going Rogue expands pitFireTeam's Allegiance mode with Knight as a trader, a seven-quest storyline, and the opportunity to raid alongside Rogues (including the Goons).**

Starting at level 20, earn Knight's trust through jobs that bring you into the squad's affairs. Fight alongside them, help secure their foothold, recover stolen equipment and hunt their enemies. Progress through the questline to unlock Knight, Big Pipe and Birdeye as permanent roster members.

Going Rogue **1.0.0** requires **pitFireTeam 1.1.1 or later** and operates within **Allegiance**.

**Back up your SPT profile before installing.** Going Rogue makes persistent profile changes, including Knight's trader, quests and custom items. Removing the addon files does not undo those changes and is not a supported rollback. Restore a pre-installation profile backup if you want to return to a profile without Going Rogue.

## Gameplay

Each Goon keeps his own fighting style. Knight leads the assault, Big Pipe brings heavy firepower, and Birdeye provides precision support while remaining capable of fighting with his secondary weapon.

Knight can command both the other Goons and your Rogue teammates during a raid. He takes the lead in combat and directs the squad around him. Big Pipe takes command when Knight is absent; without either commander, Rogues use their regular Rifleman behavior and Birdeye provides support. You can still call for help, request suppression or order a regroup while the team is combined.

**Deploying with all three Goons calls for a more aggressive playstyle.** Follow Knight's lead as he takes control of the fight and advances with the squad. Stay close enough to support the push, move between cover with them, and be ready to commit when Knight advances. Keeping pace with the group helps you make use of Pipe's firepower and Birdeye's support.

The Goons deploy with freshly generated boss equipment and retain their skill progression between raids. The confirmation screen previews the equipment they will deploy with. Their worn equipment stays fixed, with limited exceptions for compatible plate upgrades, supplies and a suitable secondary weapon if its slot is empty. They do not search bodies or containers or return loot after a raid. Their original non-consumable equipment cannot be kept after extraction; items you give them remain yours to recover.

Goon customization is limited to Proficiency. Recruited Rogues also keep their assigned combat role, aggression and Self-Determination settings.

Hand in Knight's first quest to make Rogues friendly, including when you raid without the Goons. Active quests that require killing Rogues or Goons suspend that friendship and prevent these allies from joining your raid until the conflicting task is handed in or failed.

Complete Knight's questline and maintain at least **1.0 Knight standing** to recruit Rogues. Bring a Goon with you and eligible Rogues will accept. Without one, their base acceptance chance rises from **50% at standing 1.0** to **70% at standing 2.0**. Rogues assigned to stationary weapons will refuse recruitment—they have a job to do.

Your choices carry consequences. Killing friendly Rogues damages Knight's trust, reduces future friendly encounters and lowers Rogue recruitment acceptance. It also turns ambient Rogues and Goons against you and your squad for the remainder of that raid. Teammates you deployed with are exempt from these penalties.

Bringing a Goon prevents friendly PMC encounters. Bringing Rogues reduces those chances once per squad. Friendly Scav encounters remain possible.

### Team Split and Team Combine

**Going Rogue adds two squad commands: Team Split and Team Combine.** Both require this addon and appear under **Command** when your living squad includes a Goon commander (Knight, otherwise Big Pipe), someone he commands, and at least one teammate who remains under your command. You can assign a key to each phrase; both stay available whether the teams are split or combined.

- **Team Split:** lets the commander and his Goons/Rogues fight as a separate unit. You can activate it at any time, but its effects apply only during combat. Out of combat, everyone follows and responds normally. During combat, Regroup, Go Forward, Hold, Suppress, and Cover Me affect only your side. **Attention always resets everyone, regardless of Team Split.** The detached unit can still respond to your combat gesture commands and to **Need Help when each responding member is within 40 metres**. **Need Sniper** can call BirdEye to your support even as split mode is on. Status Report still includes everyone.
- **Team Combine:** reunites the squad and automatically issues **Regroup toward you**. Normal command control resumes.

The split remains armed between fights. It is discarded if the commander changes or dies, his last subordinate is lost, your last separate teammate is lost, or the raid ends. Use Team Combine when you want the whole squad to respond to your combat commands.

## Installation

Install **pitFireTeam 1.1.1 or later** and its required **BigBrain** dependency, then extract Going Rogue's `BepInEx` and `SPT_Runtime` folders into your SPT game folder. Both the client plugin and server mod are required. Select **Allegiance** in pitFireTeam.
