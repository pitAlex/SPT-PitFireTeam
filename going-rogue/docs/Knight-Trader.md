# Knight trader

Core base: [mode storage](../../docs/Teammate-Storage.md), [localization](../../docs/Localization.md), [architecture](../../docs/Architecture.md). Future quest/recruitment progression: [roadmap](../Roadmap.md).

## Registration and access

Going Rogue registers Knight under his legacy trader ID `67768b19fa281ca31708b187`, with the original portrait and USD currency. The addon owns the trader data, registration and access hooks. Core supplies the existing mode settings and language service; it has no typed dependency on the trader implementation.

Knight is locked by default. The native player trader entry is unlocked only in **Going Rogue at player level 20 or higher**. Access does not grant standing or trade totals. Native `TraderHelper` initializes a missing entry with the player's edition defaults, recalculates level from XP and preserves existing progression. Profile projection synchronizes before native cloning/sanitation; native loyalty recalculation updates access after level changes. Other modes relock Knight without resetting standing or sales.

The trader remains registered for native profile consistency across mode switches. The client addon filters Knight out of the merchants grid and trader-navigation strip outside Going Rogue, without removing him from session/profile data. In Going Rogue he remains visible below level 20 with the native access lock. An open trading screen refreshes through native screen controllers when the mode changes; a hidden remembered selection is cleared locally, and stale direct selections fall back to an available visible trader. Native profile access flags still require their normal profile refresh after changed access. Backend access checks independently gate assort queries (including `showLockedAssorts`) and both native buy/sell execution paths, so a stale client flag cannot trade below level 20 or in another mode. Native disabled status remains authoritative. Other traders retain their existing handlers.

## Legacy requirements and stock

| Loyalty tier | Player level | Standing | Trade total |
|---|---:|---:|---:|
| 1 | 20 | 0.3 | $0 |
| 2 | 20 | 0.5 | $0 |
| 3 | 20 | 1.5 | $7,000 |

These are the legacy loyalty-table values. SPT's native loyalty calculation retains its minimum of tier 1; the additional access lock is based on mode and player level. No separate reputation system or starting-standing bonus is introduced.

The stock preserves the old 32 ordinary offers and 16 configured weapon builds (274 item nodes), their IDs, USD prices and loyalty assignments. The old 55% buy-price coefficient, buying categories/prohibitions and 3,600–7,200 second restock interval remain. Repair, insurance, healing and clothing services remain disabled. Knight's stock is not published on the flea market.

Legacy JSON and weapon builds live in `server/Resources/traderKnight` and are embedded in the addon assembly. `KnightTraderCatalog` produces fresh instances and validates IDs, installed item templates, parent links, barter requirements and loyalty tiers before registration. The portrait is deployed separately at `Resources/avatars/knight.jpg`, served through the native image router. Native trader identity/error text uses the central language resources with English fallback.

The old quest-assort file referred to seven reward offers absent from the current stock. Their orphan loyalty references are removed. The [first two Knight quests](Knight-Quests.md) now register native XP, standing and USD rewards; they add no trader stock unlocks. Further offer unlocks must be added together with their actual quests. Virtual's quest loader is not required.

## Source and qualification

- Legacy source: `friendlypmc/server/src/Trader.ts`, `TraderHelper.ts`, `traderKnight/base.json`, `assort.json`, `weapons/*.json`, and `avatar/knight.jpg` (machine-local root in `LOCAL.md`).
- Current source: `server/KnightTraderCatalog.cs`, `KnightTraderModule.cs`, `KnightTraderAccess.cs`, and `client/Trading/KnightTraderVisibility.cs` in this addon.
- Client display/Harmony fixture: `pwsh -File tests/Verify-KnightTraderVisibility.ps1`.
- Server boundary fixture: `dotnet run --project tests/KnightTrader/KnightTrader.csproj -c Debug -- <SPT_Runtime>`.

Fixtures exercise native registration, image routing, localization, profile initialization/projection, XP/loyalty recalculation and real Harmony interception of blocked assort/buy/sell paths against synthetic profiles. Successful live purchases/sales, trader card rendering, portrait delivery, loyalty UI and the level-19-to-20 UI refresh remain in-game qualification. [Validation](../Validation.md) records build/deployment evidence separately.
