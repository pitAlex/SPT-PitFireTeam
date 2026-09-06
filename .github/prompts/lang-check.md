# Language File Translation Check

As the localization maintenance agent for pitFireTeam. Check and fix the completeness of the language (translation) files. **Only modify files under the following paths:**

- `server/Resources/lang/` (language files such as `en.json`, `chs.json`, `ru.json`)
- `client/Localization/` (embedded English fallback source)

## Authoritative source and reference

1. `server/Resources/lang/en.json` — the editable English authoritative resource; all other language files follow it.
2. `client/Localization/EmbeddedEnglishLanguageProvider.cs` — embedded English runtime fallback; its keys should match `en.json` exactly.

## What to check

1. Compare `server/Resources/lang/chs.json` (Simplified Chinese) and `server/Resources/lang/ru.json` (Russian) key-by-key against `en.json`, including nested structures: `socialUi`, `gestures`, `botStatus`, `deathEscape`, and every `{ "Name": ..., "Description": ... }` config entry. Missing or empty keys fall back to English at runtime, so users see English text — they must be filled in.
2. Check that `en.json` and the embedded English keys match (case-sensitive, e.g. `pmcArmbands`, not `PmcArmbands`). If `en.json` contains stale or duplicate key variants, fix `en.json` and mirror the change in every language file.
3. If the files are already complete (e.g. the triggering change already filled them in), skip and do not redo the work.

## Translation requirements

1. Translate every missing key into the target language, keeping the existing terminology style:
   - Simplified Chinese (chs): 队员 / 小队成员 / 敌方人员 / 人机 / 战局 / 装备包 / 手雷 / 撤离
   - Russian (ru): боец / отряд / противник / рейд / снаряжение
2. Keep JSON keys byte-for-byte identical to `en.json`; **do not rename keys, reorder entries, or reformat the file structure** — only add missing entries.
3. Preserve placeholders (e.g. `{0}`, `{1}`, `\n`) and format strings; do not change them.

## Wrap-up

1. Validate every JSON file you modified with `python3 -m json.tool`.
2. Do not run git commands, do not create branches or commits, do not push. Git operations are handled by the CI workflow.
3. Report only: which files were modified, which keys were added or fixed, and the validation result. If nothing needs changing, state clearly: "No changes needed".
