# Going Rogue implementation topics

Core base: [architecture](../../docs/Architecture.md), [combat](../../docs/Combat-Tactics.md), [commands](../../docs/Commands.md).

- [Knight combat](Knight-Combat.md): implemented BigBrain combat, commands/support, native BossLogic, source comparison and pending live qualification.
- [Big Pipe combat](BigPipe-Combat.md): native-style fire/suppression/search, planned launcher use, Knight positioning, shared Goons ownership and pending raid qualification.
- [Birdeye combat](BirdEye-Combat.md): native sniper distance preferences, shared Marksman secondary handling, Knight firing-position leadership and pending raid qualification.
- [Knight trader](Knight-Trader.md): legacy stock and loyalty requirements, Going Rogue/level-20 access and native trading guards.
- [Knight quests](Knight-Quests.md): first three legacy tasks, native rewards, companion kill/survival admission, stolen launcher and quest artwork; quest 3 container binding is pending.
- [Addon guide](../README.md): current mode, profile and relationship preparation.
- [Validation](../Validation.md): dated build/fixture evidence and live qualification limits.

Going Rogue owns its character-specific combat. The external SAIN plugin and the separate [SAIN addon](../../addon/docs/Integration.md) retain their existing ownership.
