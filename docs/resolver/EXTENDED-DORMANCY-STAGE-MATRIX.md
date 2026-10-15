# Extended semantic dormancy — 62-mod A/B/C working matrix

**Status:** ACTIVE audit. Updated: 2026-10-15. Master task: [LATEST-WORK.md](../LATEST-WORK.md).
**Authority:** [User-approved per-mod wake and skip rules](DORMANCY-USER-AUTHORITATIVE-SPEC.md), [67-entry production semantic catalog](../../resolver-knowledge/semantic-library.json), [earlier all-mod audit](DORMANCY-ALL-MOD-AUDIT.md). This matrix tracks the **user's 62 mods**, while multiple catalog rule IDs can belong to one mod.

**The matrix is not a claim that previously developed semantics are absent.** `UNREVIEWED` means the A/B/C coverage of a rule has not yet been re-audited for this *new* two-direction dormancy task; many rules already contain tested Lua gates, caches or native routing. Do not falsely mark them missing or complete without inspecting both the source and current generated postconditions.

## Stages

- **A:** idle Lua execution/state processing; **B:** unnecessary Lua → native engine calls and marshalling; **C:** unnecessary native → Lua observer/subscriber dispatch, with exactly the original author wake paths.
- `EXISTING — partial`: an existing injector implements related work, but additional dormant and wake paths require review.
- `IMPLEMENTED — CI pending`: staged source change and regression contract exist, but the final tested build is not yet green.
- `SKIP — user`: preserve explicit user's SKIP. Don't force a new dormancy rule.
- `UNREVIEWED`: neither proven missing nor proven complete; further mod-source audit required.
- After reviewing a mod, update every A/B/C cell to one of `SOURCE_PROVEN_MISSING`, `ALREADY_SATISFIED`, `IMPLEMENTED_NEEDS_GAME_TEST`, `VERIFIED`, `NOT_APPLICABLE_SOURCE_PROVEN`, or `BLOCKED_SOURCE_PROOF`, with actual source postconditions/CI references in notes.

| # | Mod | Existing catalog rule(s) | A Lua | B Lua → native | C native → Lua | Evidence / next verification |
|---:|---|---|---|---|---|---|
| 1 | GTA - JoyRide | `gta-joyride` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 2 | Combat | `combat` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 3 | repeatable_cyberpsychos | `repeatable-cyberpsychos` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 4 | CyberTrials | `cybertrials` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 5 | StreetGamesPoker | `streetgamespoker` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 6 | NightCityBilliards | `nightcitybilliards` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 7 | NPCD_Hotline | `npcd-hotline` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 8 | GameEntityExaminerTool | `gameentityexaminertool` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 9 | GiveMeEverything | `givemeeverything` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 10 | Gambling System - Roulette | `roulette` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 11 | DriveAerialVehicle | `driveaerialvehicle` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 12 | illegal_mechanic_phase1a | `illegal-mechanic` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 13 | Shift | `shift` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 14 | gambling-system-blackjack | `blackjack` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 15 | QuestRunner | `questrunner` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 16 | Alternative Midair Movement | `alternative-midair-movement` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 17 | trainSystem | `metro-system` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 18 | NightCityPizza | `nightcitypizza` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 19 | OverclockedJenkinsTendons | `overclocked-jenkins-tendons` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 20 | songsdeck | `songsdeck` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 21 | DynamicOutfitsJudy | `dynamic-outfits-judy` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 22 | FovSentinel | `fov-sentinel` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 23 | FenixMantisBlade | `fenix-mantis-blade` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 24 | EasyTrainer | `easytrainer, easytrainer-background-dormancy` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 25 | TeleportGatewaySystem | `teleport-gateway-system` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 26 | DiscardAmmoOnReload | `discard-ammo-on-reload` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 27 | advanced_settings | `advanced-settings` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 28 | Auto Ammo Crafting (I need more bullets) | `auto-ammo-crafting` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 29 | DriveBus | `drivebus` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 30 | QuestTrackingToggle | `quest-tracking-toggle` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 31 | sitAnywhere | `sitanywhere` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 32 | repeatable_increased_criminal_activity | `repeatable-increased-criminal-activity` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 33 | Dedka Auto Shop | `dedka-auto-shop` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 34 | marmurbank | `marmurbank` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 35 | immersive_third_person | `immersive-third-person, immersive-third-person-action-routing` | EXISTING — partial | EXISTING — partial | IMPLEMENTED_NEEDS_GAME_TEST | Source: `ModPasses/ImmersiveThirdPerson.cs`; preserve CET keybind, FPP/TPP state/session reset; old v1 route upgrade |
| 36 | ImmersiveFirstPerson | `immersivefirstperson` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 37 | nativeInteractions | `nativeinteractions` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 38 | Minimap Widgets | `minimap-widgets, minimap-stealth-widget-locality` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 39 | AutoLoot | `autoloot, autoloot-world-pin-and-early-distance` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 40 | BetterLootMarkers | `better-loot-markers` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 41 | GiveCraftMat | `give-craft-mat` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 42 | Simple Notepad - CET | `simple-notepad-cet` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 43 | DedraPalmjetQuickslot | `dedra-palmjet-quickslot` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 44 | BackStepDuo | `backstep-duo` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 45 | NightCityAlliesMissions | `nightcity-allies-missions` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 46 | GoodFeelings | `good-feelings, good-feelings-hard-draw` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 47 | AirBackFlip | `air-backflip` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 48 | AutoDropWeaponOnPickupEquip | `auto-drop-weapon-dynamic-routing` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 49 | Drone Companions (Revamp) | `drone-companions-revamp` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 50 | GhostVoidSystem | `ghost-void-system` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 51 | Straight Edged Controls | `straight-edged-controls-input-dormancy` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 52 | ImmersiveHeadInertia | `immersive-head-inertia` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 53 | JoytoysOfNightCity | `joytoys-of-night-city-bridge-active-split-idle-facts-v2` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 54 | tunnel_rescue | `tunnel-rescue-v3` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 55 | ziggy_last_play | `ziggy-last-play-navigation-sentinel-v3` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 56 | AerialRace | `aerial-race-active-idle-split` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 57 | AppearanceMenuMod | `appearance-menu-mod-squeeze` | SKIP — user | SKIP — user | SKIP — user | Original explicit user SKIP; preserve previous compatible patches |
| 58 | GigCore | `gigcore-blackboard-once` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 59 | SpecterM11RecoveryDev | `specter-recovery-prerequisite-reads` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 60 | NPC Outfit Manager | `npc-outfit-manager-idle-state` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 61 | yaibaPenthouse | `yaiba-penthouse-distant-bathtub` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |
| 62 | CallList-VehicleDetailsOverlay-CET | `calllist-action-lazy-display-query` | UNREVIEWED | UNREVIEWED | UNREVIEWED | Compare source and existing pass; no absence claim |

## First implementation — immersive_third_person (mod #35)

- Existing `immersive-third-person-action-routing` rule already replaces an independent `PlayerPuppet::OnAction` observer with finite exact-interest 0-Engine routing, preserving original native fallback. This is not the same as deactivating its subscribed handler when the camera is off.
- Real mod source ([prior live-source audit](DORMANCY-ALL-MOD-AUDIT.md)) contains `state.enabled = true` in `applyTppMode`, and `state.enabled = false` in `restoreFppMode` and `updateSessionGuard` only. Its existing CET-owned `tpp_camera_toggle` and other inputs remain the wake path. Its `handlePlayerAction` already returns while disabled, proving the subscriber's downstream action work is unnecessary in FPP.
- Extended the same mod-owned injector: the shared action subscriber initially registers with the compatible default active state, then synchronizes `SetActive(state.enabled == true)` through the returned handle when that method is supported. This avoids stranding the subscriber on older runtimes; source-defined TPP enable/FPP restore/session-gap transitions resume/pause the same handle. If 0-Engine is unavailable the prior native `Observe` fallback remains; a runtime without `SetActive` preserves original routed behavior. **Only the per-mod subscriber is paused; the shared 0-Engine native-to-Lua crossing is not removed.**
- The source matcher requires each of the three reviewed state writers exactly once and verifies the v2 route, handle and transition postconditions. A new author-side state writer is drift requiring re-review, not silently ignored. Supports pristine V1+V2 one pass and older V1 → V2 without discarding prior actions/routing.
- [Windows source-generation contract](../../tests/ImmersiveThirdPersonActionRoutingContract.ps1) and [Lua test against unpacked actual pinned ActionRouter](../../tests/ActionRouterDormancyLuaContract.lua) cover source state, pristine/v1 composition, idempotence, SetActive, other-mod isolation and no needless type decoding. CI [#38718963646](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38718963646) passed all four jobs. In-game camera, session restore, active movement and capture A/B are still outstanding.
- Related existing A/B work: [`ImmersiveThirdPerson.cs`](../../src/G.CETProfiler.Core/Services/ModPasses/ImmersiveThirdPerson.cs) has supervisor, maintenance and pending-state gates. These need independent completeness review before marking stages A or B fully satisfied.

## Next batch selection

Stay **workload-led** (OnAction high-frequency contributors) but choose the next transform only where the original author input/state/wake graph proves it. In particular, do not assume DriveAerialVehicle's dynamically supplied exception lists are finite; do not replace CallList's localized substring selector with a guessed action table; and do not disable action observers that are their own wake trigger. Existing exact-router work on AirBackFlip, BackStepDuo, DiscardAmmoOnReload, DriveBus and AutoDropWeaponOnPickupEquip must be independently checked before adding dormancy. The Source API already has `SetActive` and can support mod-specific subscriber state changes without a new 0-Engine framework.

Source-only eligible rules entirely absent from profiler callback rows remain a separate unresolved generation-discovery path (see [LATEST-WORK](../LATEST-WORK.md)); this matrix does not mark those stages complete.
