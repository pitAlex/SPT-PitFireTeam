Build a PMC squad, bring teammates into raids, recruit friendly bots along the way, and lead them with the game's phrases and gestures. Pit Fire Team gives you persistent squad members to equip, customize, and fight alongside.

---

If you would like to show your appreciation, you can support me at [Ko-Fi](https://ko-fi.com/n00bish).

---

New players: read **Gameplay Guide** and **Known Issues and Conflicts** before deploying.

---

**Gameplay:**

<div style="position: relative; width: 100%; max-width: 960px; padding-bottom: 56.25%; height: 0; overflow: hidden;">
  <iframe src="https://www.youtube.com/embed/CgQeimMDnls" title="PIT Fireteam - Live Combat Demo" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%;" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
</div>

[Open video on YouTube](https://www.youtube.com/watch?v=CgQeimMDnls)

---

# Tabs {.tabset}

## Description

The in-game **My Squad** screen is your squad management hub. Choose between **Guns for Hire**, where you can hire teammates, and **Allegiance**, where you build your squad through field recruitment. Each mode has its own roster. Manage your teammates and their equipment, adjust squad settings, invite members into your raid group, or enable **Auto Join** for future PMC raids.

**Notable features:**

- **Dedicated squad screen** - manage your roster, customize teammates, and adjust settings from the in-game My Squad interface.
- `≥ v. 1.0.0` **Two gameplay modes** - hire and customize your squad in **Guns for Hire**, or build it through field recruitment in **Allegiance**, with separate rosters.
- **Field recruitment** - recruit friendly PMCs during raids. Eligible PMC recruits may later request to join your permanent squad.
- `≥ v. 1.0.0` **Friendly Scav encounters** - meet and recruit friendly solo Scavs in Allegiance. Scav companions remain raid-only allies rather than permanent squad members.
- **Teammate customization** - rename teammates, change clothing and equipment, select combat tactics, and tune Aggression, Vision, Precision, and Reaction.
- `≥ v. 1.1.0` **Teammate workshop** - customize saved teammates from your server's web page: names, heads, voices, level, skills, health and pocket size, while keeping their equipment intact.
- `≥ v. 1.0.0` **Self-Determination** - tune how independently each saved teammate acts in combat, separately from Aggression, with temporary command overrides.
- `≥ v. 1.0.0` **Loadout management and insurance** - equip teammates from your stash, purchase kits, and insure their equipment under your chosen loadout rules.
- `≥ v. 1.1.0` **Persistent teammate health** - carry raid injuries between raids, recover with your hideout bonuses, or pay to heal wounded teammates. Direct treatment currently uses money only; stash medkits are not supported yet.
- `≥ v. 1.0.0` **In-raid equipment exchange** - use **Swap Gear** to rearrange a spawned teammate's equipment and inventory, then confirm when ready.
- `≥ v. 1.0.0` **Give Gear** - offer equipment to recruited PMCs and Scavs, who evaluate weapons, protection, ammunition support, and storage before deciding what to equip.
- **Weapon-aware combat** - teammate decisions consider weapon role, caliber, magazine capacity, ammo penetration, and secondary weapon options.
- **Teammate commands** - issue combat, movement, attention, loot, and door commands through the game's existing phrases and gestures.
- **Combat coordination** - use **Go Forward**, **Need Help**, **Cover Me**, and **Suppress** to guide the fight without controlling every movement.
- **Raid group support** - invite teammates into your group manually or use Auto Join to preload selected teammates into your next PMC raid.
- **Map transitions** - teammates who you spawned with can follow you through map transitions.
- **Progression system** - teammates gain raid experience and common-skill progress that persists between raids.
- **Quest assist** - teammate kills can count toward player kill quests when the kill meets the quest criteria.
- **Faction relationships** - repair missing hostility between factions without making your followers attack Scavs neutral to the whole squad. Partisan keeps his normal behavior.
- **Looting and loot return** - order followers to collect items and search bodies or containers, manage spawned teammates' backpacks, and receive eligible carried loot after extraction. (See Looting)
- `≥ v. 1.0.0` **Loot priorities** - use corpse commands to request weapons or gear without changing your usual loot settings.
- `≥ v. 1.0.0` **Scav cargo returns** - extract alive with a living recruited Scav still following you to receive his remaining donated or command-collected items through Fence. Also supports player-Scav raids; the companion's starting kit is excluded.
- **Fallen teammate gear gathering** - outside combat, a teammate can be ordered to check a body and gather recoverable gear from it, mainly to help collect gear from fallen squadmates.
- **Post-raid reports** - find out whether surviving teammates escaped and recovered items after your death.

**Compatibility tested with:**

- SAIN
- Looting Bots
- ORBIT
- SVM
- Questing Bots
- Acid's Bot Placement System
- Acid's Progressive Bot System

The mod is still sensitive to other mods that heavily change bot AI, grouping, perception, hostility, or spawning. If teammates become hostile, ignore enemies, or behave strangely, test with fewer bot-related mods first.

## Installation

**Required SPT version: 4.1.x (minimum 4.1.0).**

**Required dependency:**

- [BigBrain](https://sp-mod.com/mod/902/bigbrain)

**Recommended:**

- [WAYPOINTS - EXPANDED NAVMESH](https://sp-mod.com/mod/827/waypoints-expanded-navmesh), because teammates can have a harder time navigating without expanded navmesh data.

Extract the downloaded archive into your SPT install directory. It should add files under **BepInEx/plugins/pitFireTeam** and **SPT_Runtime/user/mods/pitFireTeam-ServerMod**.

## My Squad Screen

![My Squad screen](https://iili.io/BsjHsql.png)

Open **My Squad** from the main menu to manage your roster, prepare your raid group, and configure the mod.

**Spawned teammates** are roster members you bring into a raid. **Recruits** are friendly bots who join you during that raid. Some commands and equipment options differ between them.

`≥ v. 1.0.0` On your first visit, choose **Guns for Hire** or **Allegiance**. Existing rosters are preserved. If you have no squad in either mode, you receive a one-time invitation from a free teammate; accept it to add them to your roster.

After choosing a mode, use the **Roster**, **Settings**, and **Mode** tabs to manage your squad.

### Roster

View your saved teammates, open their profiles, manage raid-group membership, and choose who should automatically join future PMC raids.

In **Guns for Hire**, **Add Teammate** opens the hiring flow. Choose a name, head, and voice, then review the candidate and equipment cost before hiring.

In **Allegiance**, manual hiring is unavailable. Grow your roster by recruiting PMCs during raids and accepting their later invitations.

**Teammate portrait actions (Right-click):**

- **Invite to group / Remove from group** - add or remove the teammate from your current pre-raid group without removing them from your roster.
- **View profile** - open the teammate's equipment, clothing, tactics, proficiency, and skills.
- **Auto Join on/off** - choose whether this teammate automatically joins your future PMC raid groups.

Removing someone from your raid group is not the same as deleting them from your roster. Roster removal mails back their equipped kit. Former field recruits can also have a removal fee, shown before confirmation; manually hired teammates have no such fee.

---

### Settings

Configure squad, combat, looting, and loadout options from the in-game screen instead of relying only on the BepInEx configuration window.

Some settings are fixed by **Allegiance** rules, while others cannot be changed during a raid. Equipment loss and supplies are handled separately under **Loadout Management**.

During a raid, open **Squad Settings** from the pause menu to access the settings available in raid. Roster management and gameplay-mode switching remain outside-raid features.

---

### Modes

`≥ v. 1.0.0` Choose your gameplay mode from the in-game **Mode** tab:

- **Guns for Hire** - hire and equip teammates before deploying, customize the squad rules, and recruit friendlies during raids.
- **Allegiance** - build your squad through field recruitment rather than manual hiring, with friendly encounters and a stricter set of gameplay rules.

Each mode has its own roster, equipment, teammate settings, and pending invitations. Switching modes does not delete the other roster or transfer members between them.

You can switch modes outside raids. Settings that Allegiance does not lock are shared between modes, including your **Loadout Management** choice.

## Squad Customization

### Teammate workshop

`≥ v. 1.1.0` Open **PitFireTeam** on your SPT server's home page, or visit `https://127.0.0.1:6969/pitfireteam`, to edit saved teammates' names, heads, voices, level, experience, skills, health and pocket size. Head and voice options use the game's localized names. Close Tarkov while editing, save your changes, then launch the game to use them. The workshop keeps their equipment and pocket contents; teammates cannot be deleted there.

### Teammate profiles

Right-click a teammate's portrait in **My Squad → Roster** and select **View profile** to rename them, change their clothing, choose a tactic, adjust Proficiency, or view their skills.

Use **Edit Loadout** and **Kit Loadouts** to manage equipment; the next tab explains those options. Voices can also be changed from the profile's voice dropdown. Use the teammate workshop to change a saved teammate's head. These profile controls are for roster members, not temporary raid recruits.

### Combat tactics

- **Rifleman** - an all-purpose fighter who supports the squad, uses cover, and advances when conditions allow.
- **Marksman** - ranged support focused on distance and useful firing positions, with suitable secondary weapons available for close fights.

Optional addons can provide other tactics, including **SAINGrunt** and **SAINShooter**. Those two require both SAIN and the pitFireTeam SAIN Addon; see the addon's own description for details.

### Proficiency

Each teammate has five sliders:

- **Aggression (0–100%)** - willingness to advance and pressure enemies. Lower values favor defense; higher values encourage taking an opening. Riflemen default to **50%**, Marksmen to **30%**. At **0%**, they still defend themselves.
- `≥ v. 1.0.0` **Self-Determination (0–100%)** - how independently they fight. **0%** favors close escort, **50%** balances support and independence, and **100%** lets them fight without automatically returning just because you moved away or diverting to protect you. They still respond to orders and immediate danger. Default: **50%**.
- **Vision (0–200%)** - how far they can spot enemies.
- **Precision (0–200%)** - shot accuracy and part of aiming speed. It also increases their preference for a visible head: **10%** at 0, **40%** at 100, and **70%** at 200. These are target-choice chances, not guaranteed headshot rates.
- **Reaction (0–200%)** - how quickly they recognize enemies and how quickly they finish aiming, together with Precision.

**100%** Vision, Precision, and Reaction means normal performance for the selected tactic. Changes apply only to that teammate.

**Reset** restores the tactic's default Aggression, Self-Determination to **50%**, and the other three sliders to **100%**.

Some combat commands temporarily change Aggression or Self-Determination. They do not overwrite these saved values; see **Squad Commands** for the exact effects.

## Loadout Management

`≥ v. 1.0.0` Choose how teammate equipment is handled under **My Squad → Settings → Loadout Management**, outside a raid.

This is separate from your gameplay mode. **Guns for Hire** and **Allegiance** share the same choice, and switching between them does not reset it. **Immersive** is the default.

**In Restricted and Immersive, you do not need to supply reserve ammo for saved teammates' primary weapons.** They deploy with ammunition in a hidden secure container and use it to refill their magazines. Equip a loaded weapon and compatible magazines; the reserve matches the ammo loaded in the weapon and is supplied automatically each raid. It is finite, so very long fights can still use it up. Field recruits need their own supplies.

![Loadout Management](https://iili.io/BpKDP4I.md.png)

### Restricted

**Gear comes from your stash and is kept if the teammate dies, but cannot be extracted as player loot.**

Enable **Field Upkeep** to keep raid wear, spent supplies and teammate injuries between raids. It is off by default. Taking a fallen teammate's protected kit does not remove it from their saved equipment or let you keep a free copy after extraction. Insurance is unavailable because the kit is already protected from death loss.

### Immersive — default

**Gear behaves as real raid equipment, without requiring you to manage the secure container.**

Equipment wears down, supplies are consumed, and gear lost when the teammate dies must be replaced. Fallen teammates can be looted. Surviving teammates keep their equipped kit, while carried loot follows the return rules in **Looting**. Eligible equipment can be insured.

### Realistic

**Uses Immersive's loss rules, but you also manage the secure container and its supplies.**

The container is visible in the loadout editor. No automatic medical or reserve-ammunition package is added; provide the supplies you want the teammate to have. The secure container is kept if the teammate dies.

### Editing and replacing a kit

All three choices use real equipment:

- **Edit Loadout** - move items between your stash and the teammate's current kit. Nothing changes until you press **Save**.
- **Kit Loadouts** - purchase or equip one of your saved player equipment builds. The price includes its parts and contents. **Use items in stash** lets you choose matching items to reduce the cost; if the stash supplies everything, the action becomes **Equip**.
- Replacing a full kit mails the previous kit through the **-P|T- FireTeam** delivery service rather than discarding it.

**Confirming a repair or insurance purchase also saves any pending loadout changes.** Closing the editor afterward does not undo those transfers or the paid service.

In Restricted and Immersive, the secure container is managed automatically and excluded from editing and kit purchases. Alongside the primary-weapon reserve ammo, it provides basic medical support, including a Grizzly and surgery kit unless equivalent medicine is already in the backpack. Bring extra supplies for long raids, secondary weapons, or field recruits.

### Teammate insurance

`≥ v. 1.0.0` In **Immersive** and **Realistic**, right-click an eligible item in **Edit Loadout** and select **Insure**. Choose a trader in the normal insurance window and pay from your roubles.

An insured item stays insured when you move it between yourself and a teammate, including when you recover it from their corpse or take it through Swap Gear.

Lost insured gear uses the trader's normal return chance, delay, and mail. Gear brought out by you or another teammate does not produce an extra insurance copy. Returned items must be insured again.

Equipment a teammate keeps and loot they return are not the same thing. See **Looting** for acquired gear, ammunition, PMC gifts, and Scav returns.

## Teammate Health

`≥ v. 1.1.0` Saved teammates retain their body-part injuries after raids when **Field Upkeep** is enabled in Restricted. Immersive and Realistic retain injuries automatically. Bleeding, fractures and other temporary effects are cleared after the raid.

Wounded teammates have a reddish portrait background and cannot join another raid until fully recovered. Their health recovers over time with your hideout bonuses. To heal them immediately, open their profile and press **HEAL** to confirm the rouble cost, which uses Therapist's healing prices and your discounts.

**Current limitation: direct post-raid treatment is available only for money. You cannot use medkits from your stash to heal teammates yet.** Turning Field Upkeep off stops retaining new raid injuries in Restricted; it does not heal existing wounds. Those teammates still need paid treatment or time to recover.

## Squad Commands

![Gestures Menu](https://iili.io/BQdlFv1.md.png)

Commands use the game's existing phrases and gestures. Open the voice-command menu to choose a phrase; right-click supported phrases to assign shortcuts. Phrase names and availability can differ by voice and player side.

Some orders can target one teammate when you look directly at them; others apply to the squad or select the nearest eligible follower. Gestures require the follower to see and react to you.

Orders guide behavior rather than overriding every action. Healing, reloads, immediate threats, unreachable routes, and active loot work can delay or prevent a response.

### Movement and following

- **Follow Me** - resume normal following and leave patrol mode.
- **Attention / Look** - clear current orders and reset follower behavior toward you. Also restores saved Aggression and Self-Determination.
- **Regroup** - call followers back toward you. In combat, they seek a nearby position when it is safe to move.
- **Exit Located** - gather followers close around you for extraction.
- **Stop** - stop without forcing a crouch. Followers may resume following if you move too far away.
- **Go Forward** outside combat - send available followers toward the location you point at.
- **On Your Own** outside combat - allow nearby followers to patrol the area after you stop moving. They catch up normally while you travel. **Follow Me**, **Regroup**, or **Cover Me** returns them to normal following.
- **Cover Me** outside combat - leave patrol mode and return to close-follow behavior.

---

### Combat orders

- **Go Forward** - ask a follower to pressure a known enemy, move into a firing position, or advance. Marksmen keep their ranged-support role.
- **Regroup** - call followers back for that order, including while On Your Own is active.
- **Need Help** - request support against the enemy closest to you.
- **Need Sniper** - ask a Marksman to find a useful firing position against the enemy closest to you. He can answer negative if busy or unable to find a suitable opportunity.
- **Hold Position** - temporarily set Aggression to **0%** for a defensive approach. Followers can still shoot, heal, and move out of danger.
- **On Your Own** - temporarily set Self-Determination to **100%**, allowing followers to fight without automatically regrouping solely because you moved away.
- **Cover Me** - temporarily set Self-Determination to **20%**, stop the current push, and regroup toward you.
- **Go Go Go** - restore saved Aggression and Self-Determination.

`≥ v. 1.0.0` A follower accepting combat **Go Forward** temporarily uses **100% Aggression**. None of these commands changes your saved settings. **Attention**, **Go Go Go**, or the end of squad combat restores them.

**Suppress** orders short pressure against a known enemy position:

- Look directly at a teammate to request his suppression; otherwise eligible squadmates may participate together.
- Riflemen normally handle suppression. Without an active Rifleman, a Marksman can join if he has a loaded automatic secondary.
- Suitable equipment includes a full-auto firearm, a firearm with at least a 25-round magazine, or a supported second-slot grenade launcher. Only one grenadier is selected for squad suppression.
- Hard cover, friendly-fire risk, immediate danger, treatment, or unsuitable equipment can prevent the order. A negative reply means the teammate could not start a suitable action.

Recruits may refuse combat Hold/Stop or Go Forward orders. Outside combat, personality does not make them refuse Stop/Hold, though they may still be busy or unable to respond.

---

### Recruitment and replies

- **Cooperative / Cooperate** - ask a friendly bot to join you. Look toward him within **5 metres** with clear sight, or up to **17 metres** while he is waiting and interacting with you.
- **Affirmative / Roger** or the **OK gesture** - accept the invitation of the friendly currently asking to accompany you.
- **Negative** or the **No gesture** - decline that invitation. You may ask the bot to cooperate later.

`≥ v. 1.0.0` Friendly encounters can greet you and offer to accompany you. Recruitment still depends on your gameplay mode, available capacity, the bot's willingness, and whether it is safe to join. A contextual Cooperate prompt does not guarantee acceptance.

---

### Contact and status

- **Contact / Over There** - direct attention toward where you are looking and help followers acquire a valid enemy. Explicitly designating a neutral bot can turn that bot and its group hostile; do not use Contact as a harmless greeting.
- **Repeated Contact** - say Contact again shortly afterward to have available followers outside combat come check from your position, facing where you pointed.
- **Front / Left / Right / On Six** - ask followers to look in that direction relative to your facing.
- **Status Report** - display teammate status, distance, health, and tactic information, with enemy markers where enabled and available.

---

### Gesture commands

- **Come To Me** - look at a visible teammate within **30 metres** to call him close. In combat, he seeks a nearby position toward you rather than blindly running straight through danger.
- **There** - direct a nearby teammate toward a reachable point. In combat, the destination must be within **30 metres** of you.
- **Stop gesture** - hold position with crouch behavior, unlike the spoken Stop command.
- **Over There gesture** - direct attention toward where you point.

---

### Loot and equipment interactions

- **Open Door** - ask the nearest available teammate to open the targeted door.
- **Take This** - ask a follower to collect the loose item you are looking at.
- **Loot This** - ask a follower to search the targeted container.
- **Check Him / Loot Body** - ask a follower to search the targeted corpse.
- `≥ v. 1.0.0` **Corpse CMD actions** - choose **Check Him**, **Loot & Get Weapon**, **Loot & Get Gear**, **Get Weapon**, or **Get Gear**. See **Looting** for category priorities and recruited-follower upgrades.
- **View Backpack** - manage a spawned teammate's backpack through immediate transfers.
- `≥ v. 1.0.0` **Swap Gear** - edit a spawned teammate's equipment and inventory, then confirm with **Apply Changes**.
- `≥ v. 1.0.0` **Give Gear** - offer items to a recruited PMC or Scav, then submit them with **Give Items**.

The **Looting** tab explains who takes each order, how the equipment screens differ, and what comes back after the raid.

## Looting

Tell followers what to collect: point out a loose item, corpse, or container and give a loot order. They need room to carry it and a safe route to reach it.

### Giving a loot order

- **Loose items:** look at the item and use **Take This**.
- **Bodies:** look at the corpse and use **Check Him / Loot Body**, or select a corpse CMD action below.
- **Containers:** look at the container and use **Loot This**. Containers retain their normal player interactions; no extra CMD menu is added.

**Who takes the order?**

Body and container orders go to spawned teammates while any remain alive. If you brought none, or all have died, recruits can take those orders instead. A living spawned teammate still has priority even if busy or too far away.

Loose items can go to any available follower. The follower with the shortest safe walking route takes the job; normal body/container searches are within about **22 metres** by route. Fighting or already-busy followers are skipped.

You can send different followers to different targets, but not two followers to the same target at once. You can order another search of a body or container already checked.

![Look Pickup](https://iili.io/BpKc90x.md.png)

Searching takes time and plays a search sound. Combat can interrupt it. A completed container search closes the container again; an interrupted one may leave it open.

### Corpse CMD options

![Corpse commands](https://i.postimg.cc/c419myqR/Screenshot-2026-10-22-025903.png)

`≥ v. 1.0.0` With at least one living follower, corpses offer these additional interaction-menu actions:

| Command | What it requests |
|---|---|
| **CMD: Check Him** | Normal body search using your saved Looting Settings. |
| **CMD: Loot & Get Weapon** | Look for a weapon first, then continue normal looting. |
| **CMD: Loot & Get Gear** | Look for gear first, then continue normal looting. |
| **CMD: Get Weapon** | Take weapons and suitable ammunition/magazines, without a general loot search. |
| **CMD: Get Gear** | Take wearable gear and its contents, without a general loot search. |

Weapon options appear only while the corpse has a gun. All five use your character's **Check Him** voice line. They apply to this order only—your saved pickup settings stay unchanged.

**Get Weapon** and **Get Gear** skip unrelated valuables, food, medicine, and dogtags.

With **Pickup Weapons off**, weapon requests favor the corpse's primary long gun, otherwise its secondary, plus a pistol if the follower has an empty holster. They do not take every gun hidden in the bags. A spawned teammate with both shoulder slots full can carry the chosen long gun as baggage, without collecting extra magazines for it. With **Pickup Weapons on**, normal broader weapon looting continues.

The recruited-follower upgrade behavior below applies to **Loot & Get Weapon** and **Loot & Get Gear**, not the standalone **Get** commands.

### Looting Settings

![Looting Settings](https://i.postimg.cc/hPqgPGdF/Screenshot-2026-10-22-023858.png)

Open **My Squad → Settings → Looting Settings** to choose ordinary search filters:

- **Minimum Price / Maximum Price** - ordinary loot value limits; **0** disables the corresponding bound.
- **Pickup Food** - food and drinks.
- **Pickup Meds** - medical supplies and stimulators.
- **Pickup Valuables** - barter items, keys, money, and other valuables.
- **Pickup Weapons** - guns, ammunition, magazines, weapon mods, and grenades.
- **Pickup Gear** - helmets, body armor, armored rigs, tactical rigs, and their carried contents.



Money is collected regardless of price limits when Pickup Valuables is on. Normal body searches also try to take PMC dogtags. If a dogtag is the only item taken, the follower may still say he found nothing.

Ordinary loot uses backpack space first, then pockets. Tactical-rig space is reserved for combat supplies and reloads, not general valuables. Complete weapons and wearable gear are considered with their attachments and contents rather than stripped for their most expensive parts.

If armor or a rig cannot be carried whole, the follower can check its contents separately. He may take installed plates if Pickup Gear is on, their price fits your limits, and they have at least **50%** durability. Loose plates are skipped during normal filtered looting.

### Weapons and ammunition

**Normal looting can fill a spawned teammate's empty equipment slots, but does not replace gear they are already wearing or using.** Enable the matching pickup setting or use a weapon/gear request for that search.

- A usable long gun can fill an empty primary or secondary slot; a usable pistol can fill an empty holster.
- A weapon lacking enough ammunition or magazines may be stored until later orders provide supplies.
- A gun needs enough compatible ammunition, usable magazines, and room to reload—not just a high value.
- Combat magazines must fit in the rig or normal pockets. Magazines left in a backpack are cargo, not normal fast-access reload supplies.
- Leave an opening for the magazine removed during a reload. All weapons can share that opening, but each magazine must fit.
- Later searches can replenish the primary first, then support the secondary and pistol. They can collect compatible magazines, top off ammunition, and support internal-magazine weapons.
- Grenade launchers prefer the secondary slot when a conventional primary is available.

A weapon carried as baggage is not automatically a usable combat upgrade. Listen for the follower's weapon or loot acknowledgement, and check the supplies you are asking them to use.

### Upgrades for recruited followers

`≥ v. 1.0.0` Recruited PMCs and Scavs can improve their kit through these orders, just as they do with **Give Gear**:

- **Take This** on a loose gun lets the recruit consider using it with the supplies he carries, even with Pickup Weapons off.
- **Loot & Get Weapon** considers the selected corpse weapon together with compatible ammunition and magazines.
- **Loot & Get Gear** considers the corpse's complete kit, **including weapons**, then continues normal looting.

Recruits handle these body orders only when no spawned teammate remains alive. Loose-firearm orders do not have that restriction.

A recruit can switch to a better usable primary, improve his protection, or take a larger backpack if his belongings fit. He will not switch primary just because you pointed out a gun: it must be useful with the available supplies. Other items may become secondary equipment or baggage, or be left/dropped if they cannot be carried.

These requests do not turn spawned teammates' ordinary looting into automatic equipment replacement.

---

### View Backpack, Swap Gear, and Give Gear

**View Backpack — spawned teammates**

Use the lower-left interaction while looking at a teammate outside combat. Only the backpack is shown, and transfers happen immediately. Items placed there are baggage, not a request to equip them; use Swap Gear to change equipment.

![Teammate backpack inspection](https://iili.io/BpKvke1.md.png)

**`≥ v. 1.0.0` Swap Gear — spawned teammates**

Look at a spawned teammate outside combat, then **hold the key assigned to Open voice command dropdown** to expand the lower-left interaction menu. Select **Swap Gear** from that dropdown.

![Swap Gear Command](https://i.postimg.cc/bJ2SxvgY/sw-ap-gear-command-circled.png)

Recommended voice-command bindings in the game's controls settings:

| Command | Key | Press type |
|---|---|---|
| **Phrase Menu** | **Y** | **Double click** |
| **Open voice command dropdown** | **Y** | **Continuous** |
| **Voice command quick phrase** | **Y** | **Release** |

With these bindings, hold **Y** to open the dropdown, scroll down and then release **Y** to use the selected interaction.

In **Swap Gear**, you can change weapons, worn gear, plates, pockets, and backpack contents. Nothing transfers until you press **Apply Changes**. Closing without applying cancels your edits.

Combat, healing, or other busy states can prevent opening or cancel editing. If a follower is stuck in an internal reload and will not open the editor, issue **Attention**, allow the reload to finish, then try again.

**`≥ v. 1.0.0` Give Gear — recruited PMCs and Scavs**

![Give Gear Staging](https://i.postimg.cc/WbmWmNYK/Screenshot-2026-10-22-030407.png)

Found a friendly PMC or Scav and recruited him during the raid? You can help him improve his equipment through **Give Gear**, which replaces **View Backpack** in his interaction dropdown. Unlike Swap Gear, you offer the items and let the recruit decide what to equip—you cannot take his existing equipment directly.

Place your offering in the grid and press **Give Items** when ready. Nothing transfers before you confirm. He compares the offered equipment with his current kit and swaps to useful upgrades: a better weapon he has the magazines and ammunition to use, stronger protection, or a larger backpack that can hold his belongings. Suitable equipment can also fill empty slots, such as a missing headset or helmet.

Headsets and glasses only fill empty slots; they do not replace ones he already wears. A better helmet can also be declined if it conflicts with protective face gear he is keeping.

You can give him a complete kit at once, including a weapon, its magazines, and a vest to carry them. He considers those items together, so the new vest may make the new weapon usable. You can also improve his kit over several handovers: a weapon kept as baggage may become worth equipping when you later provide its missing magazines or ammunition, and a spare vest he carries may become useful when you give him better armor. A larger vest can improve storage without changing his protection, but he still needs room to support his weapon and reload.

Items may be loose or directly inside an offered vest or backpack. He can also take magazines and ammo from a vest packed directly inside that backpack, even if he keeps his current vest. A full incoming vest can be rearranged to leave reload room; excess items are carried or dropped. Bags inside other bags are not unpacked for upgrades.

**Recommended way to give gear**

- Get both of you behind cover and out of enemy sight, away from an active fight. Let him finish healing or reloading first.
- For the simplest handover, place each item directly in the donation grid. Keep weapons, magazines, ammunition, and gear separate rather than packing containers inside other containers.
- Offer a weapon together with compatible magazines and ammunition, plus a suitable vest if he needs one to carry them.
- Offer a replacement backpack empty. Give any other equipment separately rather than packing it inside the backpack.
- After pressing **Give Items**, wait for the handover to finish before moving on or issuing another command.

Giving an item does not guarantee he will equip it. Replaced equipment and anything he does not use are kept as baggage if there is room, otherwise dropped. When he upgrades his backpack, he moves his belongings into the new one and drops the emptied old bag. A packed backpack may be rejected even if it is larger: equipment he declines stays inside it and can leave too little room for his current belongings. The handover cannot know whether you meant those packed items as equipment or cargo, so an empty backpack is the clearest way to offer a storage upgrade. **Check the ground after a handover.** See **After the raid** below for what PMCs keep and Scavs return.

---

### After the raid

**Spawned saved teammates**

Eligible carried cargo can be returned after extraction, or through Team Escape if the teammate survives after your death. A dead carrier cannot deliver their loot.

Equipped gear stays with the teammate in **every loadout mode**, including Restricted. Magazines inside weapons stay too, along with spare magazines the follower has assigned to those weapons in his rig or pockets. Magazines carried as backpack loot and other baggage can be returned.

Restricted protected equipment still belongs to its original saved teammate. Commanded recovery does not turn a fallen teammate's protected kit into free extractable loot.

**`≥ v. 1.0.0` Recruited Scavs**

Extract alive with the Scav alive and still following you to have **Fence** return the items you gave him or ordered him to collect. This also works during player-Scav raids. You do not receive his starting kit, things he looted on his own, or anything already dropped, used up, or taken back.

At transit, the Scav stays behind and sends that map's cargo through Fence. Read the message when you return to the menus. A dead Scav, one you dismissed, or one who left after a betrayal cannot make this delivery.

**Recruited PMCs**

Recruited PMCs do not mail your gifts back. What they keep can become part of their kit if they later join your roster.

`≥ v. 1.0.0` Equipping a recruit with your gifts adds **10 percentage points to his invitation chance for each different equipment slot**, up to **100%** total. Replacing the same slot repeatedly does not stack the bonus. Gifts remain insured during the raid; coverage is cancelled only if the PMC survives and keeps them afterward.

You can lend Restricted teammate gear through Give Gear for use during the raid. It still belongs to the original teammate: it gives no invitation bonus, is not mailed back, and is removed from the recruit's kit before he joins your roster.

## Gameplay Guide

Lead the squad rather than constantly steering every movement. Teammates react to danger, use cover, heal, and follow your orders, but they need time, supplies, and a usable route to do their job.

### Before deploying

1. Choose your gameplay mode and select teammates from its roster.
2. Check each teammate's tactic, equipment, and Proficiency.
3. Choose your **Loadout Management** rules before risking gear.
4. Use **Invite to group** or **Auto Join**, then check the pre-raid group.
5. Equip compatible magazines and leave enough carrying space. Restricted and Immersive supply saved teammates' primary-weapon reserve ammo and basic medicine automatically; provide your own supplies in Realistic and for field recruits. Insure gear where available.

Removing a teammate from the current group prevents Auto Join from immediately adding them again. Re-add them manually or toggle Auto Join when you want them back.

### A simple starting squad

Try **one Rifleman and one Marksman**, with default Aggression of **50%** and **30%**. Leave Vision, Precision, and Reaction at **100%**, and Self-Determination at **50%**, while learning their behavior.

Give the Rifleman a useful close-to-medium-range weapon and let the Marksman cover longer sightlines. An automatic secondary can help the Marksman nearby, but does not make him an assault specialist.

Weapon choice matters. A shotgun is not a long-range rifle, low-penetration rounds struggle against armor, and a large magazine cannot compensate for every weakness. Check the loadout before expecting a higher Aggression setting to solve the problem.

### Supplies during a raid

Long fights can exhaust medicine and ammunition, even with the automatic support provided outside Realistic mode.

- Keep combat magazines in the rig or normal pockets.
- Leave room for the magazine removed during a reload.
- Carry suitable medicine for bleeding and fractures.
- Remember that secondary weapons and pistols need their own supplies.

**View Backpack** is for immediate backpack transfers, **Swap Gear** is for changing a spawned teammate's kit, and **Give Gear** is for offering items to recruits. Full instructions and return rules are in **Looting**.

Give followers a safe moment to heal or reload before moving into another fight.

### Coordinating a fight

Pick a job for each teammate, then support it:

- Use **Status Report** to check the squad.
- Use **Suppress** to create a window for movement against a known enemy position.
- Use **Go Forward** when a Rifleman should pressure a pinned or isolated enemy while you cover another angle.
- Use **Need Sniper** when a Marksman needs a better firing opportunity.
- Use **Need Help** when the threat is near you.

Avoid repeatedly replacing orders while a teammate is already moving or fighting. Give them time to reach cover and finish their engagement. Keep clear of their muzzle: they may stop firing when you or another ally crosses the lane.

Be careful with **Contact** around neutral bots—it can turn the target and its group hostile.

With **Realistic Enemy Tracking**, a marker can show where an enemy was last reported, not where he is now. **Simple** updates the position of known enemies. Neither mode allows firing through hard cover or teammates.

### Staying together

Use **Regroup** after a fight, before crossing a dangerous area, or when followers spread out. Use **Exit Located** near extraction for a tighter formation.

**Hold Position** during combat favors defense, not standing perfectly still. Use **On Your Own** for independence, **Cover Me** to bring attention back toward you, and **Go Go Go** to restore the saved combat settings. The exact command effects are listed in **Squad Commands**.

Check that everyone has caught up before starting another fight. If a follower remains stuck after his treatment or reload, try **Attention**. Teleportation is a last resort; avoid it during door or inventory interactions.

### Recruits and extraction

Roster members keep their customization and progression. Field recruits are temporary allies with their own gear and personality; PMCs may later ask to join the roster, while Scavs never do.

Spawned teammates can accompany you through map transitions with their current gear and condition. Recruited Scavs stay behind and send eligible cargo through Fence. Check **Looting** before giving away anything you expect to get back.

### If you die

With **Team Escape** enabled, surviving roster teammates each have a chance to escape and recover items. Their chances depend on extraction distance, squad strength, health, equipment, medicine, and nearby threats.

Those who escape may return recoverable player equipment, fallen teammate gear in Immersive/Realistic, and carried loot. Space is limited: neither escape nor full recovery is guaranteed.

Restricted keeps the saved teammate's kit protected. Immersive and Realistic follow the resulting equipment losses. Disabling Team Escape prevents these post-death escape and recovery attempts; Allegiance keeps it on and limits the routes to your assigned exits.

Scav companions do not use Team Escape's courier. Their Fence returns require you to finish alive.

## Guns for Hire

`≥ v. 1.0.0`

> Hire and equip your squad before deploying. Make Tushonka yours, and play by your own rules.

Choose **Guns for Hire** on your first My Squad visit or later under **My Squad → Mode**. It is the configurable hiring mode: use **Add Teammate** to review and hire candidates, equip them, and bring selected members into PMC raids.

This mode has its own roster, equipment, teammate preferences, and pending invitations. Allegiance's roster is kept separately; switching does not copy or delete members.

### Settings you control

Guns for Hire uses your saved preferences, subject to each setting's normal restrictions:

| Setting | What it controls |
|---|---|
| **Friendly PMC Side** | Makes your own PMC faction friendly. This is a relationship setting, not guaranteed recruitment. |
| **Bad Guy** | Makes the player hostile to PMC bots regardless of faction. |
| **Friendly Encounter Chance** | How often you meet friendly solo PMCs of your faction, with Friendly PMC Side and Bad Guy off. |
| **Pickup** | Whether field recruitment is enabled. |
| **Tiered Pickup** | Makes your level matter when asking a PMC to join. On Scav runs, Fence reputation determines how many Scavs you can recruit. |
| **Maximum Pickup** | How many field recruits can follow you at once, not the size of your permanent roster. |
| **Recruit Pickup** | Allows PMC recruits you extract with to ask to join your roster afterward. |
| **Enemy Tracking** | Simple updates known enemy positions; Realistic relies on sightings and remembered reports. |
| **Team Escape** | Whether surviving saved teammates can attempt post-death escape and recovery. |
| **Team Escape: Use Any Extraction Point** | Lets teammates escaping after your death use any usable exit, rather than only your assigned exits. |

You can also choose your loadout rules, loot filters, Proficiency, health multiplier, and Heal Followers shortcut. Some changes are only available outside raids. **Friendly Scav encounters** is available only in Allegiance.

### Meeting and recruiting allies

**Friendly Encounter Chance** ranges from **5–80%**, default **30%**. In this mode it applies only to lone PMCs of your faction. Friendly PMC Side or Bad Guy takes priority when enabled.

Friendly PMCs can greet you, wait nearby, and offer to accompany you. Answer their invitation with **Roger / Affirmative** or the **OK gesture**, or ask with **Cooperate**.

Being friendly does not guarantee a bot will join you. He may be busy fighting, your group may be full, or your level may not impress him. If he turns you down because of your level, asking again during the same raid will not change his mind.

Eligible recruits who make it out with you may send a later invitation if Recruit Pickup permits it. Scavs never join the permanent roster.

### Player-Scav runs

On a Scav run, you can recruit friendly Scavs with Pickup enabled. Allegiance's encounter chance is not required.

With **Tiered Pickup** on, each full Fence reputation point lets you recruit one Scav, up to **six** and within your Maximum Pickup setting. You need at least **1** reputation for one recruit. With Tiered Pickup off, Maximum Pickup sets the limit.

Extract alive with your Scav companions to receive their remaining given or command-collected items through Fence. This does not add a separate cooperation reward or random gift.

### Karma and switching modes

When playing as a PMC, killing a friendly PMC or PMC recruit costs **0.025 PMC karma**. Extracting alive with PMC recruits restores **0.02 per living recruit**. This does not apply to spawned roster teammates; Scavs use Fence reputation instead.

Guns for Hire does not use Allegiance's 24-hour encounter penalties or its small peaceful-raid karma bonus.

Switching to Allegiance temporarily enforces its fixed settings. Returning restores your Guns for Hire preferences for those controls. Other settings are shared: changing loot filters, health multiplier, or Loadout Management in either mode keeps that latest choice.

## Allegiance

`≥ v. 1.0.0`

> Play by Tushonka's rules. Alliances are formed, not bought. Find your allies in the field, and escape together.

Choose **Allegiance** on your first My Squad visit or later under **My Squad → Mode**. Build the roster through field recruitment and later PMC invitations; **Add Teammate** is unavailable. The one-time welcome invitation still applies if you have no squad in either mode.

You can meet friendly USEC, BEAR, and regular Scavs, but not everyone will agree to join.

### Fixed settings

These controls are locked while Allegiance is active:

| Setting | Allegiance rule |
|---|---|
| **Bad Guy** | **Off** — PMCs are not all forced hostile to you. |
| **Friendly PMC Side** | **Off** — your faction is not made universally friendly. |
| **Enemy Tracking** | **Realistic** — sightings and last-known positions rather than live tracking. |
| **Pickup** | **On** — field recruitment is enabled. |
| **Tiered Pickup** | **On** — your level matters to PMCs; Fence reputation sets recruit limits on Scav runs. |
| **Maximum Pickup** | **2** field recruits at once when playing as a PMC, whether PMC or Scav. On Scav runs, use the Fence-based limit below. |
| **Recruit Pickup** | **On** — eligible extracted PMCs may send roster invitations. |
| **Team Escape** | **On** — surviving saved teammates can attempt post-death escape. |
| **Team Escape: Use Any Extraction Point** | **Off** — teammates escaping after your death use your assigned exits. |

The locks do not overwrite your Guns for Hire preferences. Settings outside this list remain shared, including **Loadout Management**, **Friendly Encounter Chance**, loot filters, **Squad Health Multiplier**, and the **Heal Followers** shortcut. Normal in-raid editing restrictions still apply.

### Friendly Encounter Chance

This **5–80%** slider, default **30%**, controls your chance of meeting friendly lone bots. It does not increase your follower limit.

| Slider | Same-faction PMC chance | Opposite-faction PMC / Scav base |
|---|---|---|
| **5%** | 5% | 5% |
| **15%** | 15% | 11.25% |
| **30%** | 30% | 15% |
| **50%** | 50% | 25% |
| **80%** | 80% | 40% |

Opposite-faction odds match the slider at 10% or below, rise gradually between 10% and 30%, and equal half the slider from 30% upward.

There is no fixed number of friendly encounters per raid. These encounters are for lone bots, not groups, bosses, guards, rogues, raiders, or sniper Scavs. A bot who simply has not attacked you is not necessarily a recruitable friendly.

### Friendly Scav encounters

**Friendly Scav encounters** is on by default beneath Friendly Encounter Chance. Change it outside raids to enable or disable these encounters. It does not disable friendly PMCs or Scav recruitment during player-Scav runs.

Higher Fence reputation means more chances to meet a friendly Scav. The Scav base chance above is multiplied by your reputation, counting up to **6**. At the default slider:

- **0 or negative Fence reputation:** no friendly Scav encounters.
- **0.2 reputation:** **3%** chance.
- **1 reputation:** **15%** chance.
- **3 reputation:** **45%** chance.
- **6 reputation:** **90%** chance.

Fence affects meeting a friendly Scav, not persuading him. Willingness to join depends on Friendly Encounter Chance: **100%** at 15% or below, about **79%** at 30%, and **50%** at 50–80%. Scav levels do not matter here.

If a Scav turns you down, asking repeatedly will not change his mind. If he is only busy fighting or your group is full, you can try again when that changes.

### Cooperation and invitations

Friendly encounters can greet you and wait facing you at close range. They may offer to accompany you; **Roger / Affirmative** or the **OK gesture** accepts, while **Negative** or the **No gesture** declines.

You can also ask with **Cooperate**: normally within **5 metres** with clear sight, or up to **17 metres** while the friendly is waiting and interacting with you. Declining his invitation does not stop you asking him later.

PMC players can have **two active field recruits**, whether PMC or Scav. Saved teammates are separate from this pickup count. Player-Scavs use **one active Scav per full Fence point**, capped at **six**.

PMC recruits may later ask to join your roster. Scavs remain raid-only companions. Both can improve their kit through **Give Gear** and the loot commands described in **Looting**.

### Scav companionship, trust, and returns

A living Scav companion can calm ordinary Scavs toward you and your squad, but some remain hostile and special roles are excluded. If your last Scav companion dies or leaves, you lose that protection. Attacking a neutral Scav can also break it.

Killing neutral Scavs damages that trust. After three betrayals, your Scav companions leave and ordinary Scavs turn hostile for the rest of the raid. Starting the attack yourself or ordering it with **Contact** counts; a PMC follower picking a fight on his own does not.

Intentional friendly-Scav kills cost **0.05 Fence reputation** for a recruited companion, or **0.025** for an unrecruited friendly/neutral Scav. Scavs do not affect PMC karma.

Finish alive with a living Scav still following you to receive remaining donated or commanded cargo through Fence. His starting kit is excluded. Scavs stay behind at transit and settle cargo for the departing map.

As a PMC, extracting with a living Scav companion at a normal exit grants **+0.025 Fence reputation once per raid**. Co-op exits use the game's usual reputation and gift rules instead. SVM-paid co-op exits still need a living companion. Transit and player-Scav cargo returns do not add this extra reward.

### Betrayal penalties and PMC karma

Killing a current field recruit reduces your friendly-encounter chances by **15% for 24 real hours**. For example, a **30%** chance becomes **25.5%**. Further kills stack the penalty: two reduce the original chance by 30%, and seven or more leave no friendly encounters.

Each penalty expires separately, including while you are not playing. Switching modes does not erase it. This penalty does not apply to spawned roster teammates.

PMC karma is separate. Killing a friendly PMC or PMC recruit costs **0.025**; extracting alive with PMC recruits restores **0.02 per living recruit**. If you encounter friendly PMCs but neither kill nor recruit them, completing the raid can restore a small **0.005**. These completion bonuses do not apply at transit.

Fence reputation, PMC karma, and the timed encounter penalty are three different systems.

## Known Issues and Conflicts

### BTR transport

Followers do not board or travel with you in the BTR. Vanilla bots automatically avoid the vehicle, and its passenger system does not support AI followers without custom handling. There are currently no plans to add follower BTR transport because of the implementation and testing effort involved.

### Stuck reloads and equipment interactions

A follower can occasionally say he is changing magazines but remain stuck without finishing the reload, even outside combat. This can prevent **Swap Gear** or **Give Gear** from opening, or produce a busy/in-danger warning when the area appears safe.

**Workaround:** issue **Attention**, give him time to finish the reload and return to his weapon, then try again. Sometimes moving on with the follower also allows the reload to complete.

This remains a known issue. Swap Gear and Give Gear also deliberately refuse to open during real combat, healing, reloads, or other busy actions; those normal restrictions are not the same as a stuck reload.

### Movement and combat troubleshooting

- **Not moving after a fight:** allow ongoing treatment or reloads to finish. If the follower still does not resume, try **Attention**.
- **Stuck on a route or falling behind:** followers can struggle with doors, stairs, tight interiors, or map geometry. Check their position before starting another fight. Try Attention, then teleportation if needed.
- **Teleportation problems:** avoid teleporting a follower while he is using a door, transferring items, or performing another interaction. With some AI setups, he may return to his previous position afterward.
- **Missing body after a fall:** followers have been reported falling below the playable map. Equipment may not be recoverable during that raid. Do not assume an empty spot means the gear was returned or safely stored.
- **Hesitation around foliage:** a known enemy behind bushes does not always produce an immediate shot. **Suppress** can help a suitably equipped follower fire toward a known position, but cannot bypass hard cover or friendly-fire safety.
- **Visual aiming jitter:** occasional shaky weapon or aiming animations may occur. Report persistent cases, especially if they also prevent firing or reloading.

Unexpected targeting, delayed reactions, or movement problems can have several causes. Please include logs rather than assuming the game, SAIN, or another mod is responsible.

### Equipment and inventory errors

Avoid teammate equipment that adds unsupported inventory slots, such as custom belt systems, unless compatibility is confirmed. These can cause inventory failures or crashes; this is not a blanket restriction on all modded weapons and armor.

If a loadout edit or gear exchange reports an error:

- Read the warning before retrying. Incompatible equipment, insufficient reload space, and a follower becoming busy can be valid reasons for rejection.
- If the editor stays open, correct the arrangement and try again.
- For a failed loadout save, cancel and reopen the editor to check the actual saved equipment.
- If the problem persists, stop moving those items, keep the logs, and restart the game and server before further inventory changes.

Do not rely on restarting to automatically repair missing items or delete duplicates. Report suspected item loss or duplication with the item names and the exact transfer you attempted.

### Conflicts with other mods

Mods that change bot AI, spawning, factions, perception, grouping, or inventories can overlap with pitFireTeam. A mod appearing in the compatibility list does not cover every version, preset, or combination.

**Custom spawning mods affect friendly encounters.** These encounters work only with bots spawned alone, not as part of a group. If your spawning mod or preset only spawns groups, there will be no friendly encounters. Make sure its settings allow solo PMC or Scav spawns for the encounters you want.

If faction relationships behave unexpectedly, test with **Faction Hostilities** disabled under **Raid Settings**, especially if another mod also changes faction relationships. This setting repairs relationships; it does not make followers see enemies through obstacles.

For other conflicts, test with fewer bot-related mods and compare the result. Installing SAIN and installing the optional pitFireTeam SAIN Addon are different setups; mention which you use when reporting a problem.

### Reporting an issue

Include:

- Your game/SPT and pitFireTeam versions, plus relevant mods.
- **Guns for Hire** or **Allegiance**, and your **Loadout Management** setting.
- Whether the affected follower spawned with you or was recruited during the raid.
- What you ordered or transferred, what happened instead, and whether Attention helped.
- Client/server logs from that session, plus a screenshot or short clip where useful.

For equipment problems, include the weapon/gear names and whether items disappeared, duplicated, stayed in the wrong slot, or only looked wrong on the follower.

If My Squad behaves differently in another language, try English and include the original language in your report.

{.endtabset}
