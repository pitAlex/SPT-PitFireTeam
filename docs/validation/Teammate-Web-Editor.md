# Teammate web editor validation

## 2026-10-26 — optional-mode labels and managed companions

The roster dropdown now resolves the central `SquadControlMode*` display names, showing **Going Rogue** while preserving its stored `GoingRogue` identifier. Companion eligibility comes from the addon's registered `ProficiencyOnly` role policy through Core's shared role lookup. Managed companions are disabled in the list with a localized explanation; both Load and Save reject them before loading equipment. Renaming a Goon does not bypass the policy. Ordinary recruits remain editable.

The editor fixture passes **89 checks**. New cases cover absent-addon mode discovery and rejected roster/load/save requests without creating Going Rogue storage or loading an addon assembly; registered-mode availability; renamed Knight, Big Pipe and case-insensitive Birdeye roles; direct load/save rejection and unchanged profiles; ordinary exUsec edit/save; and unchanged base-roster eligibility. These tests register the Core capability contract without a project/assembly reference to the Going Rogue addon. The existing **316 gameplay storage checks** also pass. Client/server Debug compilation and locale JSON/whitespace checks pass.

Deployed only the server DLL, symbols and EN/RU/CHS resources with matching hashes. Server DLL SHA-256: `F5F307C0E4E7D33E346923173A2E72C50EE3C0B95D0B25B90BE02C1CDF34765D`. The server remains stopped at the user's request; the updated disabled-control/tooltip presentation still needs a native browser check after the next server start. No live teammate values were edited.

## 2026-10-26 — native head and voice names

Verified the client `BaseCustomizationItem.NameLocalizationKey` contract is `{Id} Name` for both head and voice templates. The editor now obtains labels from native `LocaleService`, following the browser's selected language with per-entry English and template-name/ID fallback. Its `chs` setting maps to the installed `ch` global database. Save validation still uses template IDs directly and is independent of display labels.

The editor fixture passes **66 checks**, including 12 new cases for native English/Russian labels, Chinese mapping, missing/blank/echoed-key fallback, unavailable locale fallback, untranslated custom names and unchanged selection IDs. The deployed Debug server DLL matches SHA-256 `284ADC681DD2E4B64E0E53AC49B403230A93D4A4E52CF01193080A1DD9F07687`; only the verification server was restarted with Tarkov closed.

Native browser verification showed Brick's voice/head as **Chris / Taylor**, switching immediately to **Крис / Тэйлор** in Russian and back to English without clearing his name, level, XP or selections. The server restarted successfully with no editor errors. No live values were saved; language was restored to English.

## 2026-10-26 — native server page

The server now compiles Razor components using SPT's `IModBlazorMetadata` discovery, native MudBlazor layout and administrator route authorization. Compile references remain SPT 4.1.0.0; inspected baseline and installed SPT 4.1.6 both reference MudBlazor 9.7.0. Private SPT/MudBlazor binaries were not deployed.

Validation:

- **54 editor checks**: allowed field changes; preservation of inventory, identities, combat records, insurance, unknown fields, voice/skill/effect metadata; valid/invalid voices; trimmed names and synchronized lowercase names; case-insensitive duplicate-name refusal; invalid IDs/values, stale drafts and active raids; explicit profile/mode boundaries; pocket shell expansion in both live and Default snapshots; unchanged contents/weapons; occupied shrink refusal; encrypted persistence and untouched other-mode/settings documents.
- **316 existing gameplay storage checks** pass after the identity policy was shared with the client-facing rename/voice routes.
- Core server and client Debug builds passed without warnings/errors. The client build verifies added embedded English text; runtime deployment for this feature changed only the server DLL, symbols and three locale files.
- Direct DLL loading plus encrypted storage round trip passes with **940 types** across the server/LiteDB assemblies. PowerShell fixtures now resolve ASP.NET Core 10's shared framework as the native server host does.
- Installed SPT 4.1.6 compatibility check passes: **222 types/837 members**, **537 mod types** loaded and metadata instantiated.
- English/Russian/Simplified Chinese resource JSON parses. Scoped whitespace checks pass.

Synthetic fixtures are under ignored `tests/artifacts/teammate-editor/`; the final 54-check fixture is `ac695387caf449f29b2ae407c1437ee2`. No test modified a live teammate record.

With Tarkov and SPT closed, deployed the first server build and locales, then started SPT for verification. Native startup completed without editor errors. The browser initially required the user to handle the local self-signed certificate; the user confirmed the editor loaded, and browser inspection showed their Allegiance roster and the Character/Skills/Health tabs. No live Save Changes action was exercised.

After the requested name/voice extension, the final build and locales were deployed with matching source/destination hashes and only the verification server was restarted. Server DLL SHA-256: `3079852D8FE8BDD9DE693075DB7082139B513F051F8D54CE31A0C28C2A065DA0`.

Final browser inspection confirmed both player rosters can be selected, the Allegiance teammate opens with Name and Voice alongside head/level/XP/pockets, and Health shows all seven existing body parts with current/maximum controls. A screenshot was inspected at the in-app browser's narrow viewport; the layout stacks the roster above the editor. Only selections and tabs were changed; no live teammate values were entered or saved. The server remains running for the user.

Remaining qualification: exercise an intentional edit on a disposable teammate and verify its next in-game spawn, including head compatibility, voice, pocket layouts and health. The server-side validation and native page load do not establish that every installed/custom head is visually compatible with every body.
