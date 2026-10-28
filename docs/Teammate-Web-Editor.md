# Teammate web editor

Open `/pitfireteam` on the SPT server, for example `https://127.0.0.1:6969/pitfireteam`. The server landing page also lists **PitFireTeam**. This page uses SPT's Blazor/MudBlazor layout and administrator authorization; it does not replace the in-game settings screen.

Close Tarkov while editing. Keep the server running, choose the player and roster, then choose a saved teammate. Changes apply when the game next loads the teammate. The browser language selector provides English, Russian and Simplified Chinese without changing the game's locale.

Mode labels use the shared localized display names, including **Going Rogue**. Optional modes appear only after their addon registers with Core. Requests for an unavailable mode are rejected before opening or initializing its database, and the editor has no assembly dependency on the addon.

Registered companions restricted to Proficiency customization appear disabled with an explanation. They cannot be loaded or saved through the web editor, including direct requests using their account IDs. Eligibility follows the addon's registered role policy, so renaming a companion cannot bypass it. Ordinary recruits remain editable. Going Rogue uses this restriction for Knight, Big Pipe and Birdeye; see the [addon contract](../going-rogue/README.md).

The editor supports:

- Names, with whitespace trimming, a synchronized lowercase name and case-insensitive duplicate checks within the selected roster. These checks are shared with the client-facing rename flow.
- All installed voice templates, regardless of faction, validated through the same voice-template policy as the client-facing dropdown. The selection applies at the next spawn; browser voice playback is not provided.
- Heads from the installed customization templates, including heads outside the teammate's original faction.
- Head and voice labels use EFT's native `{template ID} Name` localization in the editor's selected language. Changing language refreshes labels without resetting the draft or changing selection IDs. Missing entries fall back to English, then the template name (or ID). The editor's `chs` language maps to EFT's `ch` global database. Native `LocaleService` supplies current mod-transformed translations; shared templates/locales are not modified.
- Level and XP. Selecting a level sets its starting XP; editing XP derives the matching level using the installed experience table. The server rejects inconsistent combinations.
- Existing common-skill progress (0–5100) and weapon mastery progress. Skill IDs and session metadata are preserved.
- Current and maximum health for existing body parts. Effects and other health properties remain unchanged.
- Existing pocket templates, keeping the pocket item identity and all contents. Occupied grids can expand; a smaller or incompatible occupied grid must be emptied in-game first. Special-slot contents must remain supported. The same controlled pocket-shell change is made to the saved Default equipment snapshot, in one database transaction, so spawn preparation cannot restore the old layout.

There is no teammate deletion, creation, faction change, combat-record editor, Proficiency/Aggression editor or equipment editor. The server accepts a narrow edit model rather than a replacement profile or inventory. The pocket shell is the sole inventory exception; guns, armor, ammunition, item identities, locations, condition, ownership and insurance are retained.

Loads and saves share the mode/request gate with in-game operations and use explicit roster storage without changing the active gameplay mode. An active raid blocks editing. A revision of both the profile and Default snapshot prevents one browser tab overwriting a newer save or an in-game equipment change. Native treatment payment recovery also blocks saves while incomplete. Valid changes use the existing encrypted database transaction and previous-document recovery backups; the editor never rewrites the database file externally.

Implementation: [page](../server/Web/Teammates.razor), [service](../server/Services/FriendlyTeammateEditorService.cs), [edit policy](../server/Services/FriendlyTeammateEditorPolicy.cs), [validation](validation/Teammate-Web-Editor.md). [Teammate Storage](Teammate-Storage.md) owns the database contract.
