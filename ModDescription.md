This is the official successor of the Friendly PMC mod that was available for SPT 3.x.

Pit Fire Team makes it possible to have bots follow you around and fight alongside you against enemies. You can create a customizable PMC squad, bring selected teammates into raids, recruit eligible same-side bots during a raid, and use Tarkov's existing phrase and gesture system to command your teammates.

---

If you would like to show your appreciation, you can support me at [Ko-Fi](https://ko-fi.com/n00bish).

---

**Beta release note:** This is the first beta of the new version. Not every feature from the previous version has been ported yet. Features that are planned but not currently available are listed under Upcoming.

---

It is highly recommended for new players to read **Gameplay Guide** and **Known Issues** before playing.

---

**Gameplay:**

<div style="position: relative; width: 100%; max-width: 960px; padding-bottom: 56.25%; height: 0; overflow: hidden;">
  <iframe src="https://www.youtube.com/embed/cAwb9gRN8tU" title="PIT Fireteam - Live Combat Demo" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%;" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
</div>

[Open video on YouTube](https://www.youtube.com/watch?v=CgQeimMDnls)

---

# Tabs {.tabset}

## Description

You can manage your teammates from the in-game **My Squad** screen. From there, you can build your roster, adjust squad settings, customize individual teammates, invite them into your raid group, and decide who should automatically join future raids.

**Notable features:**

- **Dedicated squad screen** - manage your roster, customize teammates, and settings from a separate My Squad interface.
- **Teammate customization** - change teammate name, appearance, voice, tactic, aggression, and loadout.
- **Weapon-aware combat** - teammate decisions consider weapon role, caliber, magazine capacity, ammo penetration, and secondary weapon options.
- **Teammate commands** - issue combat, movement, attention, loot, and door commands through existing Tarkov phrases and gestures.
- **Objective-based combat orders** - use commands such as **Go Forward**, **Need Help**, **Cover Me**, and **Suppress** to shift squad priorities without directly micromanaging every movement.
- **Raid group support** - invite teammates into your group manually or use Auto Join to preload selected teammates into your next PMC raid.
- **Map transitions** - teammates who you spawned with can follow you through map transitions.
- **Progression system** - teammates gain raid experience and common-skill progress that persists between raids.
- **Quest assist** - teammate kills can count toward player kill quests when the kill meets the quest criteria.
- **Faction hostility repair** - a default-on Raid setting repairs missing BEAR-versus-USEC and PMC-versus-Scav/Scav-boss enemy relationships. Cultists, Raiders, and Rogues use neutral warning relationships toward Scavs instead of immediate hostility, while existing player-Scav karma hostility remains authoritative. Partisan is excluded so his stock karma, zone, and proximity behavior remains in control.
- **Looting and loot return** - teammates who you spawned with can pick up loot, search bodies and containers on command, return carried items after the raid, and let you manage their backpacks while in raid. (See Looting)
- **Fallen teammate gear gathering** - outside combat, a teammate can be ordered to check a body and gather recoverable gear from it, mainly to help collect gear from fallen squadmates.
- **Post-raid reports** - receive report about if your team made it out with the loot after you died. (See Gameplay Guide > Raid Survival Post Player)

**Compatibility tested with:**

- SAIN installed
- Looting Bots installed
- Acid's Bot Placement System
- Acid's Progressive Bot System

The mod is still sensitive to other mods that heavily change bot AI, grouping, perception, hostility, or spawning. If teammates become hostile, ignore enemies, or behave strangely, test with fewer bot-related mods first.

## Installation

**Required dependency:**

- [BigBrain](https://forge.sp-tarkov.com/mod/902/bigbrain)

**Recommended:**

- [WAYPOINTS - EXPANDED NAVMESH](https://forge.sp-tarkov.com/mod/827/waypoints-expanded-navmesh), because teammates can have a harder time navigating without expanded navmesh data.

Extract the downloaded archive into your SPT install directory. It should add files under both **BepInEx** and **user** / **SPT/user**, depending on your SPT layout.

## My Squad Screen

![My Squad screen](https://iili.io/BsjHsql.png)

**My Squad** is the main management screen.

It has two main tabs:

- **Roster** - create teammates, view existing teammates, invite them to the group, toggle Auto Join, open their profile, or remove them.
- **Settings** - configure squad options from an in-game screen instead of relying only on the BepInEx configuration window.

**Teammate portrait actions (Right-click):**

- **Invite to group** - adds the teammate to your current pre-raid group.
- **View profile** - opens the teammate profile for customization.
- **Auto Join on/off** - controls whether this teammate automatically joins your next PMC raid setup.

During a raid, the settings view can also be opened from the pause menu through the My Squad settings entry.

## Squad Customization

Teammates can be customized from their profile screen.

**Currently available:**

- Rename teammate.
- Change clothing using the stock clothing selectors.
- Manage teammate equipment through the active **Loadout Management** mode.
- In **Simple**, select **Default** or a saved player equipment build as a template without consuming stash items.
- In **Restricted**, **Immersive**, and **Realistic**, use **Kit Loadouts** to purchase or equip saved player kits for the teammate.
- Edit the teammate's **Default** kit from the profile screen.
- Select a combat tactic.
- Adjust aggression for Rifleman and Marksman tactics.
- View teammate-relevant skills.

**Tactics available:**

- **`Rifleman`** - the default balanced combat style. Riflemen stay useful near the boss when there is no good attack opportunity, but can push, search, and pressure when the enemy state and aggression allow it.
- **`Marksman`** - ranged-focused behavior for sniper-style teammates. Marksmen prefer firing positions and distance, avoid generic assault pushes, and can switch to an automatic secondary for close fights when appropriate.

**Aggression slider:**

Aggression controls how willing a teammate is to leave boss-local safety for proactive pressure. Lower aggression keeps teammates more defensive and boss-local. Higher aggression allows more search, push, and pressure when combat conditions justify it. At 0%, teammates avoid proactive pressure and prefer to stay around the boss. The combat **Hold Position** command temporarily behaves like 0% aggression until combat ends or **Go Go Go** clears it.

**Rifleman aggression:** Rifleman uses 50% as its default balanced baseline. Lower values bias toward cover, support, and regroup. Higher values make Riflemen more willing to push or search farther from the boss when threat checks allow it.

**Marksman aggression:** Marksman uses 30% as its default baseline. Marksman aggression is tactic-relative: it mainly controls proactive automatic-weapon close-search/auto-search pressure. It does not turn Marksman into a generic Rifleman, and it does not block defensive automatic secondary use when enemies get close. At 0%, Marksman avoids proactive auto-search and stays range/position focused. Higher values make Marksman more willing to use automatic-weapon offensive search when distance and threat checks are safe.

**Loadout customization:**

Loadout customization changes based on the selected **Loadout Management** mode.

In **Simple**, teammate gear is template-based. The editor can use gear from your stash as a reference without consuming the real items, and teammate gear is protected from raid loss.

In **Restricted**, **Immersive**, and **Realistic**, teammate equipment is treated as real gear. Editing the teammate's **Default** kit stages real stash movement and is committed when you press **Save**. These modes also replace the saved-loadout dropdown with **Kit Loadouts**, where saved player equipment builds can be purchased for a teammate.

The **Kit Loadouts** screen prices the selected kit, including nested weapon parts, armor plates, magazine contents, and container contents where applicable. The **Use items in stash** option lets you choose which matching stash items should be used instead of purchased; selected stash items reduce the final price. If every required item is supplied from your stash, the action becomes **Equip** instead of **Purchase**.

When a kit is purchased or equipped, the teammate's current kit is returned through the -P|T- FireTeam delivery service instead of being discarded. The new kit becomes the teammate's active equipment and new **Default** kit.

**Realistic** is the only mode where teammate secure containers are fully player-managed. In other modes, secure containers are managed automatically and are not counted as part of kit purchase or loadout editing. The auto-managed secure container gives saved teammates a Grizzly and a surgery kit for raid use, unless they already carry equivalent supplies in their backpack.

## Squad Commands

![Gestures Menu](https://iili.io/BQdlFv1.md.png)

Commands use Tarkov's existing phrase and gesture system. Depending on voice and side, some phrases may appear in different places or may not be available for every voice.
Some of the commands can be applied to individual teammates by looking directly at them when issuing the command.
Commands influence teammate behavior but do not force exact actions. teammates will adapt based on combat conditions and may not always respond immediately if engaged or under threat.

**In COMMAND:**

- **Follow Me / Cooperative** - recruit an eligible same-side bot or tell existing teammates to resume following.
- **Attention / Look** - clears command pressure and makes teammates focus on the boss or indicated direction.
- **Regroup** - tells teammates to converge near the boss. In combat, this becomes a combat regroup objective (within 18 meters radius of the boss, Marksman within 24m).
- **Hold Position** - in combat, temporarily behaves like setting teammate aggression to 0%. The override resets after combat ends or when replaced by another command. Can be applied to an individual teammate by looking at him.
- **Go Go Go** - clears the temporary Hold Position combat-aggression override and returns teammates to their saved aggression. Can be applied to an individual teammate by looking at him.
- **Go Forward** - orders saved teammates with an enemy to focus that enemy as an ordered push objective. They will pressure, move to reachable firing positions, or go in for the kill while still respecting healing, reload, and immediate survival needs. Outside combat, it can send teammates toward the pointed location. Can be applied to an individual teammate by looking at him.
- **Stop** - stops teammates out of combat without forcing crouch. If the boss moves too far away, teammates resume normal follow behavior. Can be applied to an individual teammate by looking at him.
- **Suppress** - orders teammates to create short pressure on a known enemy position. If you are looking directly at a teammate, only that teammate tries to suppress using his own current enemy or a boss-visible contact. If you are not looking at a teammate, eligible squadmates can suppress together while avoiding teammates who are already shooting, healing, under immediate pressure, or in a close fight. Riflemen are the normal suppression role. A Marksman can join only when no Rifleman is active and he has a loaded automatic second primary.
    - Riflemen need a suppress-capable weapon: full-auto, a magazine capacity of at least 25 rounds, or a usable grenade launcher in the second primary slot. Squad suppression allows only one grenadier, chosen by position, enemy target, launch lane, and friendly safety. If no safe lane or suitable equipment exists, the teammate can say "negative" and continue normal combat decisions.
- **On Your Own** - lets teammates spread out and act more independently instead of staying tied to your position. Outside combat, they use normal follow while you are moving, then patrol around the current area using Patrol Radius after you stop and they are close enough to start patrol. In combat, it lets them hold their own and manage the fight from where they are while you work somewhere else. Use **Go Forward** when you want Riflemen to take the initiative against a known enemy.
    - **Regroup** during combat still calls them back to you for that order, but it does not cancel On Your Own. Use **Cover Me** during combat if you want them to start watching your position again. Outside combat, **Cover Me**, **Regroup**, or **Follow Me** returns them to normal follow behavior.

**In HELP:**

- **Need Sniper** - urge Marksman to provide sniper support against the closest enemy to you. He will say "negative" if no suitable spot is found.
- **Need Help** - urge your teammates to provide combat support against the closest enemy to you.

**In CONTACT:**

- **Contact** - makes teammates look toward the boss aim direction and can help them acquire a visible enemy.
- **Front / Left / Right / On Six** - directional look commands relative to the boss look direction.
- **Status Report** - shows teammate status, distance, health summary, and tactic information.

**Implemented gesture/interaction commands:**

- **Come To Me Gesture** - targets the teammate you are looking at. The teammate must be active, no more than 30 meters away, and visible enough for the gesture to be handled.
    - Outside combat: he moves close to your current position.
    - During combat: he tries to move back toward you using nearby boss-local cover; if no cover is available, he moves to a deterministic point within about 2 meters of you along his path back to you.
- **There Direction Gesture** - points a nearby teammate toward a location. The command selects the closest active teammate within 15 meters who can see/react to your gesture, and the pointed location must resolve to a reachable nav point.
    - Outside combat: he moves to the pointed spot.
    - During combat: this becomes a short tactical reposition order to the pointed nav point within 30 meters of you, instead of calling him back to your position.
- **Stop Gesture** - tells nearby teammates to hold position, including crouch behavior.
- **Over There Gesture** - gesture-based contact/attention toward the pointed direction.
- **Open Door** - the closest eligible teammate opens the targeted door.
- **Loot This** - the closest eligible teammate picks up the targeted loot item.
- **Check Him / Loot Body** - the closest eligible saved teammate checks the targeted body. Fallen teammates use the recovery rules, while other bodies use your Looting Settings and the limited gear-swapping rules described below.

Saved teammates and recruited allies share the basic follower system once they are following you, but saved teammates have the full squad feature set. Saved teammates keep their customization, loadouts, tactics, aggression, progression, backpack access, and post-raid handling. Recruited allies are temporary raid pickups that use the default combat tactic with moderate aggression, rely on their current bot profile and gear, and have a simpler combat command set: they do not use **Need Sniper**, combat **There**, combat **Open Door**, or combat **Go Forward** push orders. If a recruited ally was told **Hold Position** in combat, **Go Forward** only clears that temporary aggression hold.

## Looting (WIP)

Looting is currently command-driven. Teammates do not wander away to loot on their own: you choose the item, body, or container and give the order.

**This functionality is still a work in progress. Abstain from commenting suggestions and things that you see missing!**

**Giving an order:**

- Look at a loose item and use **Loot This**. Any available follower can collect it if they can reach it and have somewhere suitable to put it.
- Look at a body and use **Check Him / Loot Body**, or use the loot command while looking at a container. Body and container searches are handled only by saved teammates who spawned into the raid with you.
- The closest available teammate by walking route, within roughly 22 meters, takes the job. A teammate who is fighting or already carrying out another loot order is skipped.
- Different teammates can search different targets when you issue several orders quickly. A target already being handled cannot be assigned to a second teammate.

![Look Pickup](https://iili.io/BpKc90x.md.png)

A body or container search takes a short amount of time and plays the familiar searching sound. Containers are closed again after a completed search. Combat can interrupt the search and leave the container open, allowing you to resume later.

The voice response also gives useful feedback. A weapon callout means a found weapon is ready to become the teammate's combat primary. A general found-loot response means something was accepted as ordinary loot or support gear. A negative response usually means the teammate found something but could not carry it, while a nothing-found response means no qualifying loot was available.

**Choosing what to take:**

Looting settings are found under **My Squad → Settings**.

- **Minimum Price** and **Maximum Price** control the value range for ordinary body and container loot.
- **Pickup Food**, **Pickup Meds**, **Pickup Valuables**, **Pickup Weapons**, and **Pickup Gear** control the broad types of loot your teammates may take.
- Money is always accepted when **Pickup Valuables** is enabled, regardless of the price range.
- Dogtags are always attempted on enemy USEC and BEAR bodies. A teammate may still report that he found nothing when the dogtag was the only item taken.

Teammates use real inventory space. Ordinary loot goes into backpacks and pockets, leaving tactical rigs available for combat magazines and reloads. Weapons, helmets, armor, and rigs are valued and carried as complete items rather than being stripped for their best parts.

When **Pickup Gear** is enabled, a helmet, armor vest, armored rig, or tactical rig is considered as one complete package first. If armor or a rig cannot be carried whole, its eligible contents can still be considered separately. Installed armor plates are taken only as this fallback, and only when they pass the price settings and still have at least half their durability. Loose plates remain untouched.

**Weapons and gear:**

**Allow Gear Swapping** enables the current weapon-readiness system. Despite the setting name, it is currently focused mainly on filling missing weapon slots safely; it does not yet compare every found weapon, armor, helmet, or rig against a teammate's complete loadout.

- A teammate with no primary weapon checks the weapon, its magazines, and available ammunition before deciding whether it is ready for combat.
- A usable weapon becomes the active primary on the right shoulder. The weapon-specific voice response tells you that the teammate intends to fight with it.
- An under-supplied weapon may be kept on the left shoulder or in the backpack while the teammate waits for compatible magazines or ammunition. A later loot order can make that weapon ready.
- Detachable magazines must fit in the tactical rig or pockets before the bot can rely on them. Magazines left in a backpack are cargo and are not used by the game's normal bot reload behavior. Leave enough suitable rig space if you want a found weapon to become dependable.
- A teammate who already has a working primary may add a usable support weapon only when the secondary slot is empty and **Pickup Weapons** allows it. Looting never replaces an occupied secondary weapon or holster.
- Later body and container searches maintain the working primary first, then may top off an equipped support weapon and collect compatible loaded magazines when safe rig or pocket space remains. The support weapon stays on the left shoulder, overflow remains behind, and compatible primary magazines are not reassigned to it.
- Grenade launchers prefer the secondary slot when a conventional primary weapon is available.
- Tactical-vest changes are currently limited to filling an empty slot or making a narrow protection upgrade in Immersive and Realistic. Broad armor and equipment optimization is planned for a later phase.

You can inspect a teammate's backpack while out of combat using the lower-left interaction prompt. Items placed there manually remain ordinary cargo. To have a weapon or magazine reconsidered for combat use, take it back out and order the teammate to use **Loot This** on it.

![Teammate backpack inspection](https://iili.io/BpKvke1.md.png)

**After the raid:**

Only saved teammates who spawned with you can return carried loot. You must extract together, or the teammate must survive after your death. If the teammate dies, the loot carried by that teammate is lost.

In **Simple** and **Restricted**, weapons and gear added during the raid are treated as loot and returned instead of becoming permanent equipment. In **Immersive** and **Realistic**, accepted equipment can remain as part of the teammate's new kit.

## Gameplay Guide

---

-P|T- FireTeam is built around command and coordination, not autonomous co-op buddies who independently clear the map for you. Your teammates react to danger, hold angles, seek cover, heal, and follow the priorities you set. They are not meant to behave like a separate squad that always hunts ahead while you tag along.

Treat your squad like a tactical team you are responsible for managing. Use commands to shape who holds, who moves, who suppresses, who pushes, and who protects your position. Teammates perform best when you create openings, call contacts, give clear priorities, and avoid constantly interrupting their current fight. If you want them to take ground or pressure an enemy, give an explicit combat order instead of expecting them to read your intent.

---

In Non-Realistic loadout management mode, saved teammates automatically have ammo (primary weapon only and works best with vanilla ammo) and medical supplies available, in their secure container, and do not require these items in their loadout. This automatic medical supply is meant as a baseline, not endless sustain: a Grizzly and surgery kit may not be enough if a teammate goes through many fights, heavy bleeding, repeated blacked limbs, or long combat chains. If you find extra meds during a raid, it is worth giving them to your followers so they can keep treating themselves if their secure-container supplies are depleted or no longer enough. Recruited allies found during a raid do not receive this behavior and rely on their existing equipment.
teammates still use Tarkov bot movement and navigation. They can choose cover or movement paths that are not exactly where you expected, especially in complex interiors.

---

**Adding teammates to a raid:**

- Open **My Squad**.
- Create or select a teammate from the roster.
- Use the roster portrait/context action **Invite to group** to add them to the current raid group.
- Use **Auto Join** if you want that teammate to be preloaded into future PMC raid setup automatically.
- If you remove a teammate from the current group, they will not auto-join again until manually re-added or toggled.

Teammates are not scripted companions with exact RTS-style control. Commands influence their priorities and intent, but teammates still react to danger, visibility, healing, cover, and survival. A teammate under pressure may delay or ignore a command if executing it would be dangerous.

Think of commands as:

- tactical guidance
- combat priorities
- movement intent

—not direct movement control.

### Squad Roles

#### Rifleman

Rifleman is the default all-purpose combat role.

Riflemen:

- stay useful near the boss
- support nearby teammates
- suppress enemies
- push when aggression and combat conditions allow it
- regroup more aggressively around the player
- can adapt pressure based on their current weapon, ammo, and magazine capacity

Best used for:

- close and medium-range combat
- indoor fights
- aggressive pushes
- general squad support

#### Marksman

Marksman is focused on ranged support and firing positions.

Marksmen:

- prefer distance and sightlines
- avoid generic assault pushes
- reposition for firing opportunities
- can switch to automatic secondary weapons in close fights
- are more careful about offensive movement when their current weapon or ammo is poorly suited for the target

Best used for:

- overwatch
- outdoor maps
- long sightlines
- supporting Rifleman pushes

Do not expect Marksman teammates to rush enemies like Riflemen.

### Weapon and Ammo Awareness

Teammates do not treat every gun the same way. Their combat choices now account for the weapon they are holding, the weapon they can switch to, and the ammunition loaded in that weapon.

This affects:

- how willing they are to push armored enemies
- whether they prefer cautious pressure instead of an aggressive close
- whether a Rifleman can provide useful suppression
- whether a Marksman should stay ranged or use an automatic secondary in a close fight
- whether a shotgun, low-capacity weapon, or low-penetration caliber is a poor choice for a specific push

Ammo penetration matters most against PMCs, raiders, bosses, and boss followers. Low-penetration ammo makes teammates less willing to proactively push armored targets except at very close range. Mid-penetration ammo is more acceptable early, but becomes less reliable as enemy armor expectations rise. High-capacity rifle or heavy-caliber setups can make armor-wear pressure more reasonable, while small calibers usually need much more capacity to justify the same confidence.

Weapon capacity also matters. A low-capacity weapon may make a teammate less eager to push, while a large magazine can support suppression or armor-wear pressure. Shotguns keep their close-range usefulness, and DMR/sniper-style weapons are not judged like low-capacity assault weapons when used in their intended role.

Secondary weapons can matter. Riflemen can favor an automatic secondary over a shotgun primary for mid-range fights. Marksmen can switch to an automatic secondary when enemies get close, but this does not make them behave like generic Riflemen at all ranges.

Grenade launchers can also be used by Riflemen for suppression when equipped as a usable secondary weapon. They still use safety checks and will not fire if the target area is too close to you or other teammates.

### Recommended Beginner Setup

For a stable beginner squad:

- 1 Rifleman
- 1 Marksman
- Rifleman aggression around `50%`
- Marksman aggression around `30%`

### Important Combat Advice

Do not constantly pull teammates back toward you while they are already fighting another enemy. In fights with multiple enemies, this can disrupt their current engagement and make combat unstable, because their enemy priority keeps changing.

The default squad style is that you create opportunities for your teammates to fight while you provide support. Use **Status Report** often to keep track of the squad. Your own play should usually be more defensive and less aggressive than your Riflemen. Use enemy markers, callouts, and angles instead of crowding the fight.

If you play too aggressively, teammates may collapse onto your position to protect or rescue you. This can create crowding, blocked lines of fire, and friendly-fire risk.

Use **On Your Own** in combat when you want teammates to hold their own while you handle something else.

Use **Need Help** when you want to temporarily pull squad attention toward a threat near you.

Use **Cover Me** when you want them to stop acting independently and care about protecting you again.

Use **Go Forward** when you want Riflemen to take ownership of the current enemy instead of waiting for you to advance or create another opening. This is useful when an enemy is known, pinned, wounded, isolated, or holding an angle you do not want to cross yourself.

The ordered teammate will keep that enemy as the objective and work from reachable pressure points until the target dies, becomes unreachable, or another explicit order changes the priority. This lets you stay back, cover another lane, heal, loot, or manage the raid while the squad handles the fight.

Teammates generally perform better when:

- they are allowed to finish their current engagement
- they lead the push
- you support them instead of constantly repositioning them

Over-commanding teammates can:

- interrupt movement
- reset positioning
- confuse enemy prioritization
- create unstable combat behavior

Use commands deliberately instead of continuously micromanaging.

### Basic Combat Usage

#### Regroup

One of the most important commands.

Teammates move back toward the boss and nearby cover.

Use it:

- after long chases
- when teammates spread too far
- before crossing dangerous areas
- before entering a new fight

#### Hold Position

Hold Position does **not** mean:

> "stand perfectly still."

In combat, it temporarily makes teammates behave much more defensively by reducing aggressive push behavior.

Teammates can still:

- shoot
- reposition for survival
- defend themselves
- react to danger

Good for:

- holding buildings
- healing
- defensive fights
- stopping overextension

#### Go Go Go

Clears the temporary Hold Position combat behavior and returns teammates to their saved aggression settings.

Use it after:

- defensive holds
- regrouping
- recovering from dangerous fights

#### Go Forward

Orders saved teammates to focus their current enemy as an ordered push objective.

Best used when:

- enemies are pinned
- enemies are already engaged
- enemies are wounded, isolated, or holding a dangerous angle
- you want Riflemen to finish a fight without needing you to personally advance
- you want time to heal, loot, watch another lane, or manage the raid while the squad handles that enemy
- the squad is ready to advance

The ordered teammate keeps that enemy as the objective and may move to reachable firing positions, hold a pressure point, shoot, suppress, or close in for the kill depending on the situation.

This is not a suicide rush command. Teammates still evaluate danger, cover, reloads, healing, and immediate survival before pushing.

#### Suppress

Orders teammates to create short pressure on a known enemy position.

Useful for:

- pinning enemies behind cover
- helping another teammate reposition
- supporting a push
- forcing fire through bushes or foliage when bots hesitate to shoot

How to use it:

- Look directly at a teammate before ordering **Suppress** if you want only that teammate to create pressure. Because you are looking at the teammate, he chooses from his own current enemy or from enemies visible to you.
- Give **Suppress** without looking at a teammate if you want eligible squadmates to suppress together. The squad will avoid interrupting teammates who are already shooting, healing, under immediate pressure, dogfighting, or committed to emergency combat movement.
- Use it before or during **Go Forward** when you want pressure on an enemy position before Riflemen move.

Riflemen are the main suppression role. They need a weapon that can actually support suppression, such as full-auto fire, at least 25 rounds in the current magazine, or a usable grenade launcher in the second primary slot.

Only one teammate will use a grenade launcher for a squad suppression order. The grenadier is chosen by position, usable enemy target, launch lane, and friendly safety. Launcher suppression checks the target area and will not fire if the impact point or lane is unsafe for you or other teammates.

Marksmen are precision support, not normal suppressors. A Marksman can join squad suppression only when there is no active Rifleman available and he has a loaded automatic second primary weapon. Do not expect a Marksman with only a sniper rifle or DMR to provide useful suppressive fire.

If the teammate does not have appropriate equipment, does not have a usable enemy target, is busy surviving the current fight, or cannot find a safe lane, he can reject the order.

Bushes and dense foliage can make bots hesitate to shoot, especially when SAIN is installed. If a teammate has enemy contact but will not fire through a bush, order **Suppress**. Suppression targets the enemy's known location and can make Riflemen shoot through the foliage; this often wounds or kills the hidden enemy even when normal aimed fire is being delayed.

#### Need Sniper

Urges Marksman teammates to actively search for a firing position against the closest threat.

Useful because Marksmen naturally prefer sitting on good positions and waiting for opportunities instead of constantly searching for new ones.

Use it when:

- enemies are far away
- enemies are holding angles
- you need overwatch support
- the sniper has become too passive during a fight

Marksmen may reject the order if:

- no useful firing position exists
- the fight is too close-range
- survival or healing takes priority

#### Contact / Directional Commands

Commands like:

- **Contact**
- **Front**
- **Left**
- **Right**
- **On Six**
- **Over There**

help teammates orient toward threats or suspected enemy locations.

These are especially useful before enemies become fully visible.


### Raid Survival Post Player

Your teammates can still successfully extract after your death and return any loot they were carrying for you. The escape chance is calculated based on the distance to extraction, how many teammates are still alive, their equipment quality, the estimated threat level of enemies between them and the extraction, as well as their current health and available medical supplies. The amount of gear they will be able to return upon escaping depends on their available inventory space as well as their strength level.

## Loadout Management

Found in My Squad → Settings

Before editing teammate loadouts, check **Known Issues and Conflicts** for current loadout management limitations.

![Loadout Management](https://iili.io/BpKDP4I.md.png)

- **Simple** — Create teammate loadouts freely using gear from your stash as a template, without consuming any items. Teammate gear is protected: it is not lost on death and cannot be extracted with.
- **Restricted** — Teammate loadouts must use gear from your stash or be purchased through **Kit Loadouts**. Gear is still protected: it is not lost on death and cannot be extracted with.
    - **Field Upkeep** — Track raid wear and spent supplies for teammates while their gear remains protected from death loss and extraction.
- **Immersive** — Same as Restricted, but teammate gear behaves like real raid equipment. Equipment can become damaged, dead teammates lose their gear, and their bodies can be looted.
- **Realistic** — Same as Immersive, but secure containers are no longer automatically managed for teammates. You are fully responsible for configuring them yourself.

Switching away from **Simple** also changes profile customization. The saved-loadout dropdown is replaced by **Kit Loadouts**, where saved player equipment builds can be priced, purchased, or equipped using selected stash items. Secure containers are only included in **Realistic** mode.

In non-Realistic modes, the automatically managed secure container provides basic medical support, including a Grizzly and surgery kit. For long raids or repeated fights, supplement this by putting extra meds in the teammate's backpack or giving them useful meds you find in raid.

## Upcoming

The following are planned features in reaching a release version (1.0.0) and beyond.

**Version 1.0.0:**

- **Squad Budget** - restricts the maximum number of teammates you can add to your squad based on available Command Points. Command Points are gained by leveling up, keeping teammates alive, and keeping picked-up raid allies alive. Points are lost if you kill teammates or allies.
- **Loadout Managment Reworked** - "Restricted" mode becomes "Standard" mode and "Simple" mode gets dropped

### Addons:

Addons are standalone features that extend the mod’s core functionality. They are developed independently and may be released alongside major mod versions or at any point between them, without following a fixed release order or schedule.

- **Scavs for hire** - being able to play with teammates as a Scav
- **Going Rogue** - being able to recruit and command Goons along with the Rogues in raids
- **SAIN tactics addon** - being able to use SAIN personalities as teammate tactics
- **Expanded looting** - expanding the looting capabilities through existing mods (such as Looting Bots mod)

## Known Issues and Conflicts

The mod changes bot grouping, teammate ownership, commands, and combat routing. Mods that heavily change bot AI, spawning, hostility, senses, or group behavior can conflict with it.

Mods that add custom gear like belts should not be used on teammates, it can cause game crashes.

The Labyrinth is a special map with special AI, not meant to AI followers. They will not spawn there.

In teammate loadout editing, do not repair equipment and then move items into or out of the teammate loadout before saving. For now, repair should be the last step before saving. If the editor starts failing after this, cancel out of the Edit Loadout overlay, re-open Edit Loadout, then save without moving anything.

In teammate loadout editing, if you happen to end up in a situation where you cannot save teammate loadout due to message regarding duplicate items, restart the game to recover the teammates profile. Note that any duplicate item will be stripped in the process.

- Teammates can linger after combat. Use **Attention** to reset them.
- Teammates might not heal their health all the way. It is a game issue, use the Heal key to force heal.
- Teleporting teammates while they are interacting with doors or other objects can leave them in a bad state.
- **The game has navigation problems that even SAIN is not able to fully resolve. If your bots get stuck, use teleportation. In other situations, their movement is in teleportation-like bursts. Be mindful of this and stay aware of their position or you will find yourself in a fight all alone or without all your squad as they got stuck somewhere.**
- **Faction Hostilities** repairs missing enemy relationships but does not grant bots awareness of enemies they have not seen or heard. If bots behave incorrectly toward the opposite PMC faction or other normally hostile factions, disable **Faction Hostilities** under **Raid Settings** and test again. This setting can conflict with other mods or settings that also attempt to repair or change faction relationships.
- Teammates can sometimes pick up an enemy they never saw or heard. Use **Attention** to reset them. In some cases, they may keep reacquiring that enemy until the enemy is dead. This comes from the game's detection and memory logic, and broad workarounds can break normal enemy behavior.
- SAIN can interfere with teleportation, teleporting the bot back to previous location. You may need to trigger teleportation multiple times for it to stick.
- Teammates can occasionally have registration delay on enemies. This is buggy behavior within the game that I am not able to fix.
- Teammates may have shaky aiming during some executions. It does not affect their performance, but can be an annoying visual glitch.
- Bushes are cursed with SAIN. Teammates can hesitate or refuse to shoot through bushes even when they know where the enemy is. Use **Suppress** with suitable suppression weapons to force fire at the enemy location through foliage.
- If you have problems with My Squad screen and are not on English lanuage, switch to it, to see if that works. If so, post the issue along with the language that you originally tried.

If a teammate appears stuck, try Attention or teleportation before assuming the raid is unrecoverable.

{.endtabset}
