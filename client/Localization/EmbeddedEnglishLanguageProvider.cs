using System.Collections.Generic;

namespace pitTeam.Localization
{
    internal static class EmbeddedEnglishLanguageProvider
    {
        private static LanguageOptions cached;

        public static LanguageOptions Fallback => cached ??= Create();

        private static Dictionary<string, string> Entry(string name, string description)
        {
            return new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Description"] = description
            };
        }

        public static LanguageOptions Create()
        {
            return new LanguageOptions
            {
                baseSettings = "Base Settings",
                followSettings = "Follow Settings",
                combatSettings = "Combat Settings",
                inputSettings = "Input Settings",
                miscSettings = "Miscellaneous",
                testSettings = "Testing",
                raidSettings = "Raid Settings",
                loadoutManagementSettings = "Loadout Management",
                equipOptions = new[] { "Default" },
                tacticOptions = new[] { "Rifleman", "Support", "Marksman", "Pusher", "Holder", "Assist" },

                statusSound = Entry(
                    "Enemy Location Volume",
                    "Volume of the ping sound for the enemy location marker during combat"),
                enemyMarker = Entry(
                    "Enemy Marker",
                    "Show enemy position when reporting status. If disabled, the enemy marker sound will also be disabled"),
                scanDistance = Entry(
                    "Maximum scan distance",
                    "Maximum distance to pick up any visible enemy that the player is signaling when issuing 'Contact' phrase"),
                patrolRadius = Entry(
                    "Patrol Radius",
                    "Maximum distance from the player the followers will patrol around"),
                followDistance = Entry(
                    "Follow Distance",
                    "Distance followers keep from the player while following."),
                regroupRadius = Entry(
                    "Regroup Radius",
                    "Distance from the player at which followers automatically regroup during combat. Marksman followers use about 1.5x this radius."),
                goToDistance = Entry(
                    "Maximum 'Go To' Distance",
                    "Maximum distance followers will move when 'There' or 'GoForward' is issued."),
                enemyRemember = Entry(
                    "Time to forget about the enemy (in sec.)",
                    "Maximum time a follower will remember an enemy. This is applied only at the beginning of a raid"),
                healthMultiplier = Entry(
                    "Squad Health Multiplier",
                    "Health multiplier for the followers you spawn with. This is applied per each body part."),
                pickup = Entry(
                    "Pickup",
                    "Enable or disable the ability to recruit same-side bots during a raid."),
                tieredPickup = Entry(
                    "Tiered Pickup",
                    "Use the player-vs-bot level difference rules when deciding whether a bot accepts a pickup request."),
                maximumPickup = Entry(
                    "Maximum Pickup",
                    "Maximum number of non-squad same-side bots you can pick up during a raid."),
                recruitPickup = Entry(
                    "Recruit Pickup",
                    "Allow picked-up followers that were successfully extracted with to send friend requests. This uses player-vs-bot level difference rules when deciding."),
                teamEscape = Entry(
                    "Team Escape",
                    "Allow surviving squadmates to attempt an escape after you die. Escaped teammates can return eligible follower loot and recoverable gear that would otherwise be lost."),
                teamEscapeUseAnyExtract = Entry(
                    "Team Escape: Use Any Extraction Point",
                    "Allow the squad escape simulation to choose any usable extraction point on the map. Disable this to restrict escape routes to extraction points assigned to the player."),
                memberTactic = Entry(
                    "Squad Member {0} Tactic",
                    "Set Squad member fight tactic."),
                memberEquipment = Entry(
                    "Squad Member {0} Equipment",
                    "Set Squad member equipment."),
                memberName = Entry(
                    "Squad Member {0} Nickname",
                    "Set a custom nickname for this Squad member. Leave blank for default"),
                memberVoice = Entry(
                    "Squad Member {0} Voice",
                    "Set what voice this member should use."),
                memberUniformTop = Entry(
                    "Squad Member {0} Top",
                    "Set what the top clothes for this member should be. Leave blank for default"),
                memberUniformBottom = Entry(
                    "Squad Member {0} Bottom",
                    "Set what the pants for this member should be. Leave blank for default"),
                equipmentLock = Entry(
                    "Lock Squad Equipment",
                    "Locks the equipment of the squad members."),
                loadoutManagement = Entry(
                    "Loadout Management",
                    "Controls how teammate loadouts are selected, consumed, and preserved."),
                loadoutManagementSimple = Entry(
                    "Simple",
                    "Create teammate loadouts freely using gear from your stash as a template, without consuming any items. Teammate gear is protected: it is not lost on death and cannot be extracted with."),
                loadoutManagementRestricted = Entry(
                    "Restricted",
                    "Teammate loadouts must use gear from your stash or be purchased through the kit buyout system. Gear is still protected: it is not lost on death and cannot be extracted with."),
                loadoutManagementRestrictedGearMaintenance = Entry(
                    "Field Upkeep",
                    "Track raid wear and spent supplies for teammates while their gear remains protected from death loss and extraction."),
                loadoutManagementImmersive = Entry(
                    "Immersive",
                    "Same as Restricted, but teammate gear behaves like real raid equipment. Equipment can become damaged, dead teammates lose their gear, and you can extract with their items."),
                loadoutManagementExtreme = Entry(
                    "Realistic",
                    "Same as Immersive, but secure containers are no longer automatically managed for teammates. You are fully responsible for configuring them yourself."),
                npcSendMessage = Entry(
                    "Raid End Messages",
                    "Followers will send message at the end of the raid based on conditions such as if all made it out or if you picked up a follower and kept him alive. Return items messages are excluded"),
                pitFireTeam = Entry(
                    "Friendly PMC Side",
                    "Should PMC Bots of the same side always be friendly to each other. Turn this off would let the normal game aggro system decide if same side bots will be friendly to you or not."),
                badGuy = Entry(
                    "Bad Guy",
                    "Should the player be hostile to all PMC bots, regardless of faction"),
                pmcArmbands = Entry(
                    "PMC Faction Arm Bands",
                    "Should PMC bots have armbands (red for BEARs, blue for USECs)."),
                englishBear = Entry(
                    "BEARs speak English",
                    "Should BEAR bots speak English only"),
                pingSquad = Entry(
                    "Status Report Shortcut",
                    "Alternative shortcut key for the Status Report quick phrase"),
                pingRadioVolume = Entry(
                    "Report Status Volume",
                    "Volume of the radio sound when triggering report status"),
                pingTime = Entry(
                    "Report Status Display Time",
                    "Time in seconds to display the followers status"),
                enemyContact = Entry(
                    "Enemy Contact Shortcut",
                    "Alternative shortcut key for the Contact quick phrase"),
                overThere = Entry(
                    "Over There Shortcut",
                    "Alternative shortcut key for the Over There quick gesture"),
                hideUnsupportedCommands = Entry(
                    "Hide Unsupported Commands",
                    "Hide phrases for which no command is actually registered in the game's gestures menu"),
                botTeleport = Entry(
                    "Teleport Followers",
                    "Teleport all followers to the player's position"),
                botHeal = Entry(
                    "Heal Followers",
                    "Heal all followers and restore blacked limbs."),
                botPrefetch = Entry(
                    "Prefetch follower data",
                    "Prefetch follower data to reduce the time it takes to spawn them. If you experience problems with followers spawning, disable this option and try again. Take note that this can increase the spawn time."),
                botGrenades = Entry(
                    "Followers use grenades",
                    "Allow followers to use grenades"),
                botTalk = Entry(
                    "Followers trash talk",
                    "Frequency at which a follower will trash talk during combat. Set to 0 to disable."),
                spawnPoint = Entry(
                    "Use Coop Spawn Point",
                    "Use Coop Spawn Points when spawning with followers."),
                battleRecorder = Entry(
                    "Battle Recorder",
                    "Write deep follower combat timelines to a dedicated JSONL file for raid analysis."),
                battleRecorderSnapshotIntervalMs = Entry(
                    "Battle Recorder Snapshot Interval (ms)",
                    "Snapshot cadence for follower battle recorder positional samples."),

                gestures = new Dictionary<string, string>
                {
                    ["OverThere"] = "Over There",
                    ["TeamStatus"] = "Status Report",
                    ["ViewBackpack"] = "View Backpack",
                    ["OnRepeatedContact"] = "Contact"
                },
                botStatus = new Dictionary<string, string>
                {
                    ["Dead"] = "Dead",
                    ["Engaged"] = "In Combat",
                    ["Alerted"] = "Enemy Detected",
                    ["Heal"] = "Healing",
                    ["WantToHeal"] = "Wants to heal"
                },
                socialUi = new Dictionary<string, string>
                {
                    ["AddTeammate"] = "+ Add teammate",
                    ["AddTeammateInProgress"] = "{0} was added to your friends list",
                    ["AddTeammateConfirm"] = "Add Teammate",
                    ["AddTeammateFlowActive"] = "Add teammate flow is already open.",
                    ["AddTeammateOpenFailed"] = "Could not open teammate creation screen.",
                    ["AddTeammateCreateFailed"] = "Could not create teammate.",
                    ["AddTeammateUnsupportedSide"] = "Add teammate only supports PMC profiles right now.",
                    ["Cancel"] = "Cancel",
                    ["Save"] = "Save",
                    ["Done"] = "Done",
                    ["Ok"] = "OK",
                    ["EnterNickname"] = "Enter player nickname",
                    ["NicknameTooShort"] = "Nickname too short",
                    ["NameCannotBeEmpty"] = "Name cannot be empty.",
                    ["RenameTeammateTitle"] = "Rename teammate",
                    ["RenameSave"] = "Save",
                    ["RenameCancel"] = "Cancel",
                    ["RenameChange"] = "EDIT NAME",
                    ["EditLoadout"] = "EDIT LOADOUT",
                    ["BuyGearLoadout"] = "KIT LOADOUTS",
                    ["KitLoadoutsOpenFailed"] = "Unable to open teammate kit loadouts.",
                    ["KitLoadoutPriceFailed"] = "Unable to price selected teammate kit.",
                    ["KitLoadoutPurchaseFailed"] = "Unable to purchase teammate kit.",
                    ["NotEnoughResourcesKitPrompt"] = "Not enough resources to purchase {0} kit",
                    ["BuyKitTitle"] = "BUY KIT",
                    ["PurchaseKitPrompt"] = "Purchase {0} Kit for {1}?",
                    ["KitCurrentGearDeliveryNotice"] = "Teammate's current kit will be returned to you via delivery service.",
                    ["KitItemsTakenFromStash"] = "The following items will be taken from stash:",
                    ["KitItemsPurchased"] = "The following items will be purchased:",
                    ["PurchaseKitAction"] = "Purchase",
                    ["EquipKitAction"] = "EQUIP",
                    ["UseItemsInStash"] = "Use items in stash",
                    ["CurrencyRoubles"] = "{0:N0} RUB",
                    ["UnknownItem"] = "Unknown item",
                    ["EditLoadoutTitle"] = "Edit Loadout",
                    ["EditLoadoutTitleWithName"] = "Edit Loadout : {0}",
                    ["EditLoadoutSubtitle"] = "Edit cloned items for {0}. Changes here do not touch the real stash yet.",
                    ["EditLoadoutSubtitleReal"] = "Edit staged gear for {0}. Saving moves items between your stash and this teammate.",
                    ["ProfileRecoveredTitle"] = "Profile recovered",
                    ["ProfileRecoveredBody"] = "The profile of this teammate has been recovered from a bad state. Some items from his inventory may have been deleted in the process.",
                    ["DuplicateProfileRecoveryTitle"] = "Profile recovered",
                    ["DuplicateProfileRecoveryBody"] = "Duplicate items were found in both player and teammate profiles. The following teammate profiles have been stripped of the duplicate in order to safely recover them: {0}",
                    ["LoadoutEditorSaveFailed"] = "Failed to save teammate inventory.",
                    ["LockedStashItemBlocked"] = "Teammate loadout save blocked: an item inside a locked stash container was moved or changed. Unlock the container and try again. Locked container: id={0}, tpl={1}. Blocked item: id={2}, tpl={3}.",
                    ["PlayerStash"] = "Player Stash",
                    ["PlayerStashPlaceholder"] = "Failed to load cloned stash view.\n{0}",
                    ["BotInventory"] = "Follower Inventory",
                    ["BotInventoryPlaceholder"] = "Failed to load cloned follower inventory.\n{0}",
                    ["SaveEquipmentPresetFailed"] = "Failed to save equipment preset.",
                    ["ProfileTactic"] = "Rifleman",
                    ["ProfileTacticMarksman"] = "Marksman",
                    ["ProfileTacticProtector"] = "Protector",
                    ["ProfileAggression"] = "Aggression",
                    ["RenameFailed"] = "Could not rename teammate",
                    ["RenameClose"] = "x",
                    ["RemoveTeammateTitle"] = "Remove teammate",
                    ["RemoveTeammatePrompt"] = "Are you sure you want to delete member {0}? Process cannot be undone.",
                    ["RemoveTeammateConfirm"] = "Remove",
                    ["SquadControlDeleteTooltip"] = "Delete",
                    ["SquadControlInviteToGroup"] = "Invite to group",
                    ["SquadControlRemoveFromGroup"] = "Remove from group",
                    ["SquadControlViewProfile"] = "View profile",
                    ["SquadControlAutoJoinOn"] = "Auto join: On",
                    ["SquadControlAutoJoinOff"] = "Auto join: Off",
                    ["SquadControlAutoJoinTooltip"] = "Auto-join",
                    ["SquadControlInGroupTooltip"] = "In group",
                    ["SquadControlInviteAcceptedToast"] = "{0} joined the group.",
                    ["SquadControlInvitePendingFailedToast"] = "Group invite for {0} was not accepted.",
                    ["SquadControlRemovedFromGroupToast"] = "Removed {0} from the group.",
                    ["SquadControlRemoveFromGroupFailedToast"] = "Failed to remove {0} from the group.",
                    ["SquadControlAutoJoinEnabledToast"] = "Enabled PMC raid auto-join for {0}.",
                    ["SquadControlAutoJoinDisabledToast"] = "Disabled PMC raid auto-join for {0}.",
                    ["SquadControlAutoJoinEnableFailedToast"] = "Failed to enable auto-join for {0}",
                    ["SquadControlAutoJoinDisableFailedToast"] = "Failed to disable auto-join for {0}",
                    ["SquadControlButton"] = "My Squad",
                    ["SquadControlTitle"] = "My Squad",
                    ["SquadControlRaidSettingsButton"] = "Squad Settings",
                    ["SquadControlRaidSettingsTitle"] = "My Squad Settings",
                    ["SquadControlBack"] = "Back",
                    ["SquadControlClose"] = "Close",
                    ["SquadControlRosterTab"] = "Roster",
                    ["SquadControlSettingsTab"] = "Settings",
                    ["SquadControlRosterPlayer"] = "PLAYER",
                    ["SquadControlRosterLeader"] = "Leader",
                    ["SquadControlRosterMemberA"] = "TEAMMATE 01",
                    ["SquadControlRosterMemberB"] = "TEAMMATE 02",
                    ["SquadControlRosterRole"] = "Squad Member",
                    ["SquadControlEmptyRoster"] = "You have not created any team members yet, press the add button below to get started",
                    ["SettingsPressKey"] = "Press key...",
                    ["SettingsNotBound"] = "Not Bound",
                    ["SettingsUnavailableDuringRaid"] = "Not available during raid",
                    ["LoadoutManagementConfirmTitle"] = "SWITCH LOADOUT MANAGMENT",
                    ["LoadoutManagementConfirmPrompt"] = "Switching loadout management will switch all teammates to their Default loadout.",
                    ["LoadoutManagementConfirm"] = "Continue"
                },
                returnItems = new[] { "Items received from your teammate. Ready for you to claim." },
                returnItemsDeath = new[] { "Your teammate recovered these items." },
                teamEscaped = new[] { "Nice!\nWe managed to get out." },
                teamSomeEscaped = new[] { "Well it's a shame about {0}, but at least the rest of us made it." },
                friendlyEscaped = new[] { "Glad we made it.\nThanks for letting me tag along." },
                deathEscapeMessages = new[]
                {
                    "Squad extraction report:\n{0}",
                    "Post-raid squad report:\n{0}",
                    "Your squad's final status:\n{0}",
                },
                deathEscape = new Dictionary<string, string>
                {
                    ["MadeItOut"] = "Made it out: {0}",
                    ["Lost"] = "Lost: {0}",
                    ["ExtractRoute"] = "Extract route: {0}",
                },
                traitorKillMessages = new[]
                {
                    "I trusted you. Guess that was my mistake.",
                    "So that's how you treat someone who joined your side?",
                    "You picked me up just to put me down? Real classy.",
                    "I had your back. You shot me in it.",
                },
                jerkKillMessages = new[]
                {
                    "Same side, genius. Try checking your targets next time.",
                    "Friendly PMC. Friendly. The word means something.",
                    "You shoot everyone wearing your colors, or was I special?",
                    "Nice work. You killed one of your own.",
                }
            };
        }
    }
}
