# Raid kill results

Core's `SquadRaidKillReport` preserves native EFT victim records for the raid-results kill list. Registered saved teammates and registered `BossFollowerPlayer` allies qualify through `ReportsRaidKills`. Knight, Big Pipe and Birdeye therefore qualify even with `IsSquadMate == false`; ordinary field recruits and unregistered/hostile bosses retain their existing exclusion. This display admission does not change ownership, equipment permissions, saved progression or quest-credit gates.

Follower registration and kill capture use the same eligibility. Death handling copies the killer's already-recorded native victim entry before applying saved-squadmate progression rules. Missing native entries produce a diagnostic rather than a fabricated result. The native real/dumb statistics-manager implementations both record victims for AI killers.

`SessionResultKillListShowPatch` renders nonempty groups under each ally's nickname. Victims are deduplicated by identity, and both capture and result snapshots use independent copies. Recorded kills survive follower removal/death and map transit; a fresh raid resets the report. Existing dogtag-based victim identification remains in force.

The report does not require the ally to become a saved squadmate or expose its hidden profile raid-statistics panel. Current checks and live UI limitations are recorded in [Core validation](validation/Release-1.1.0.md).
