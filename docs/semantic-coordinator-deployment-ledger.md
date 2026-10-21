# Semantic coordinator deployment ledger — 2026-10-21

Branch: `dev/semantic-external-coordinators-20261021`. Source of rule names: `resolver-knowledge/semantic-library.json` at this branch.

This ledger describes **deployment format**, not semantic implementation status.
A rule marked `NOT_CONVERTED` still retains its existing reviewed inline/owned behavior.
`NOT_CONVERTED` is not evidence of incompatibility.

- Converted: `streetgamespoker` (Poker PASS4 scheduler only; world sweep remains in the author `StreetTables.lua`; its native location bindings move with the scheduler if present).
- Not converted: 80 remaining production rule entries.
- The previously described 15 extended-dormancy A/B/C passes have not been batch-converted. The conversion is deliberately opt-in and source-proven, not bulk lexical extraction.
- Behavior equivalence is not claimed until regression CI and game runtime tests pass. Lua coordinator packaging is not semantic code inference.
- The `streetgamespoker-hud-invisible-native-idle` and `native-location-poker` rules are separate entries and have **not** independently been converted; existing native Poker hooks remain attached to the reviewed scheduler.

| Rule ID | External coordinator deployment |
|---|---|
| `gta-joyride` | NOT_CONVERTED |
| `combat` | NOT_CONVERTED |
| `repeatable-cyberpsychos` | NOT_CONVERTED |
| `cybertrials-closed-overlay-provider` | NOT_CONVERTED |
| `cybertrials` | NOT_CONVERTED |
| `streetgamespoker-hud-invisible-native-idle` | NOT_CONVERTED |
| `streetgamespoker` | POKER_PASS4_CONVERTED_PENDING_RUNTIME |
| `nightcitybilliards` | NOT_CONVERTED |
| `npcd-hotline` | NOT_CONVERTED |
| `gameentityexaminertool` | NOT_CONVERTED |
| `givemeeverything` | NOT_CONVERTED |
| `roulette` | NOT_CONVERTED |
| `driveaerialvehicle` | NOT_CONVERTED |
| `illegal-mechanic-npc-position-reuse` | NOT_CONVERTED |
| `illegal-mechanic` | NOT_CONVERTED |
| `shift` | NOT_CONVERTED |
| `blackjack` | NOT_CONVERTED |
| `questrunner` | NOT_CONVERTED |
| `alternative-midair-movement` | NOT_CONVERTED |
| `metro-system` | NOT_CONVERTED |
| `nightcitypizza` | NOT_CONVERTED |
| `overclocked-jenkins-tendons` | NOT_CONVERTED |
| `songsdeck` | NOT_CONVERTED |
| `dynamic-outfits-judy` | NOT_CONVERTED |
| `fov-sentinel` | NOT_CONVERTED |
| `fenix-mantis-blade` | NOT_CONVERTED |
| `easytrainer` | NOT_CONVERTED |
| `easytrainer-background-dormancy` | NOT_CONVERTED |
| `teleport-gateway-system` | NOT_CONVERTED |
| `discard-ammo-on-reload` | NOT_CONVERTED |
| `advanced-settings` | NOT_CONVERTED |
| `auto-ammo-crafting` | NOT_CONVERTED |
| `drivebus` | NOT_CONVERTED |
| `quest-tracking-toggle` | NOT_CONVERTED |
| `sitanywhere` | NOT_CONVERTED |
| `repeatable-increased-criminal-activity` | NOT_CONVERTED |
| `dedka-auto-shop` | NOT_CONVERTED |
| `marmurbank` | NOT_CONVERTED |
| `immersive-third-person` | NOT_CONVERTED |
| `immersivefirstperson` | NOT_CONVERTED |
| `nativeinteractions` | NOT_CONVERTED |
| `minimap-widgets-time-weather-snapshot` | NOT_CONVERTED |
| `minimap-widgets` | NOT_CONVERTED |
| `autoloot` | NOT_CONVERTED |
| `autoloot-world-pin-and-early-distance` | NOT_CONVERTED |
| `better-loot-markers` | NOT_CONVERTED |
| `give-craft-mat` | NOT_CONVERTED |
| `simple-notepad-cet` | NOT_CONVERTED |
| `dedra-palmjet-quickslot` | NOT_CONVERTED |
| `backstep-duo` | NOT_CONVERTED |
| `nightcity-allies-missions` | NOT_CONVERTED |
| `good-feelings` | NOT_CONVERTED |
| `good-feelings-hard-draw` | NOT_CONVERTED |
| `air-backflip` | NOT_CONVERTED |
| `auto-drop-weapon-dynamic-routing` | NOT_CONVERTED |
| `drone-companions-revamp` | NOT_CONVERTED |
| `ghost-void-system` | NOT_CONVERTED |
| `straight-edged-controls-input-dormancy` | NOT_CONVERTED |
| `immersive-head-inertia` | NOT_CONVERTED |
| `joytoys-of-night-city-bridge-active-split-idle-facts-v2` | NOT_CONVERTED |
| `tunnel-rescue-v3` | NOT_CONVERTED |
| `ziggy-last-play-navigation-sentinel-v3` | NOT_CONVERTED |
| `aerial-race-active-idle-split` | NOT_CONVERTED |
| `appearance-menu-mod-squeeze` | NOT_CONVERTED |
| `gigcore-blackboard-once` | NOT_CONVERTED |
| `specter-recovery-prerequisite-reads` | NOT_CONVERTED |
| `npc-outfit-manager-id-normalization` | NOT_CONVERTED |
| `npc-outfit-manager-idle-state` | NOT_CONVERTED |
| `minimap-stealth-widget-locality` | NOT_CONVERTED |
| `yaiba-penthouse-distant-bathtub` | NOT_CONVERTED |
| `calllist-action-lazy-display-query` | NOT_CONVERTED |
| `immersive-third-person-action-routing` | NOT_CONVERTED |
| `hotscenes-ui-action-dormancy` | NOT_CONVERTED |
| `native-location-poker` | NOT_CONVERTED |
| `native-location-tunnel` | NOT_CONVERTED |
| `native-location-illegal` | NOT_CONVERTED |
| `native-location-dedka` | NOT_CONVERTED |
| `native-location-card-collection` | NOT_CONVERTED |
| `native-location-marmurbank` | NOT_CONVERTED |
| `native-location-yaiba-bathtub` | NOT_CONVERTED |
| `native-location-ziggy-offered` | NOT_CONVERTED |

Implementation contract: `SemanticPatchContext.WriteOwnedModule` owns the mod-local namespace and guards against author-file overwrite; `PokerSemanticCoordinatorSource.TryStage` executes after `ApplyPreparedNativeLocationStages`; completion proof must read both author bridge and owned coordinator. No generic semantic source inference or Lua SHA compatibility gate is introduced.
