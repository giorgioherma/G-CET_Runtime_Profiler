# PHASE 3 ALLOCATION CONTROLL — 2026-10-28 generated Resolver pass, complete mod inventory

**Status:** static, source-visible audit; per-mod live dormancy and wake tests remain open. **Evidence:** `G-CET_PASS_20261028-011021.zip` / `.json` generated from fixed stock capture `CET-20261023-035144_TEST_RESOLVER_STOCK_STACK`; fresh real run `CET-20261028-022248_FIXING_RESOLVER_RESULTS_AND_GC_RESERARCH.zip`. **Branch:** `dev/resolver-profiler-handoff`.

The overlay contains **429 files** (417 CET modified/generated + 10 fixed-runtime + 2 native), covering **115 CET mod owners**. Manifest reports **1,270 transforms**, **126 frame consolidations**, **915 NATIVE_READ_REUSE occurrences**, **60 explicitly listed semantic transform records** (some have unspecified policy labels), **zero reported generation failures**. Not every original installed mod necessarily appears in this overlay. **Do not call a mod dormant because it received any of these transforms.**

Interpretation: **F** = generic frame dispatch adoption/consolidation; **A** = generic action routing; **N** = NATIVE_READ_REUSE records; these do not imply native hook removal, physical pause or reduced retained heap. **Semantic** = source-aware behavior/cadence/allocation optimization; a full sleep requires independent wake + cleanup proof. A transformed file count is not callback count. **Unproven** means no mod-owned physical dormancy/wake proof from the pass, not that no optimizations exist.

| Mod | Files | F | A | N | Applied dormancy/allocation stage (when present) | Wake source / audit result |
|---|---:|---:|---:|---:|---|---|
| 0-Engine | 13 | 0 | 0 | 0 | PSM definition caching; spatial row reuse; lifecycle ID reuse | Shared framework; cannot sleep if subscribers active |
| AppearanceMenuMod | 14 | 2 | 1 | 39 | UI target identity split; preset/idle GC fastpath | Panel visibility, target/editor changes; no hard sleep proved |
| AutoLoot | 4 | 2 | 0 | 5 | Exact action routing; early world pin/distance rejection | Loot context, combat/quest state; no hard sleep proved |
| BetterLeveling | 1 | 1 | 0 | 0 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| BetterLootMarkers | 3 | 2 | 0 | 3 | Immersive-mode-off guard; category allocation fastpath | Immersive mode setting/marker writer |
| CallList-VehicleDetailsOverlay-CET | 1 | 2 | 0 | 0 | Lazy display query on input | Existing input |
| CanyonFixes | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Card Collection | 2 | 1 | 0 | 2 | Static collection native location worker | Zone proximity and collected state, verify |
| CET_NPC_Body_Tweaks | 1 | 2 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| CET_player_underwear_removal_extended | 1 | 1 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| ChallengingBreachMinigame | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| ClaimHackTestBench | 2 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| ConflictBegone | 2 | 2 | 0 | 7 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| CourierJobs | 1 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| CustomHackingSystem | 1 | 0 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| CustomQuickslots | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| CyberStunts-StuntJumps-CET | 2 | 2 | 0 | 4 | Fixed-jump native proximity worker | Near jump zone, launch, reset, verify |
| CyberToasted-CET | 1 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| CyberTrials | 9 | 2 | 2 | 25 | Five-lane scheduler, closed-overlay provider guard | Race entry/exit and overlay; realtime EveryFrame never-pause |
| cyberware-ex-keybinds | 1 | 0 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| DamageScaling | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| DeceptiousQuestCore | 2 | 0 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Dedka Auto Shop | 4 | 2 | 1 | 13 | Coarse zone/near mechanic detail, native worker | Zone, interaction, recovery |
| Dedra's Truck Simulator | 4 | 2 | 1 | 14 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| DedraJNCJoytoy | 7 | 2 | 1 | 18 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Dedrameditate_mod | 1 | 1 | 0 | 2 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Dedrapanamgoondate | 7 | 2 | 1 | 17 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| DedraThirdPersonCombatAddon | 2 | 1 | 0 | 4 | Event-driven ITP camera-combat wake, frame pause support | FPP zero-callback FAILED (20,329); diagnose ITP bridge/event/pending cleanup |
| DisappearanceOf8ug8ear | 5 | 0 | 0 | 4 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| DriveAerialVehicle | 12 | 2 | 0 | 23 | Active vehicle/flight-state gate | Enter/exit, user flight input, transitions; frame-sensitive when active |
| DriveBus | 9 | 2 | 0 | 15 | Action routing plus timer dormancy | Action, bus/timer state, deadline |
| ELM_ImmersiveWay | 9 | 2 | 1 | 18 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Enhanced Vehicle System | 2 | 0 | 0 | 10 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| EnhancedVehicleCamera | 2 | 2 | 0 | 8 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Exodus - Arasaka Training | 2 | 2 | 0 | 0 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| f80h_aroused_expressions | 5 | 1 | 0 | 13 | Active allocation reduction, native NPC attachment gating | NPC attach/detach, scene/activity |
| f80h_s_enc_nud_patch | 3 | 1 | 0 | 11 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| FenixRebirth | 3 | 1 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| freefly | 4 | 2 | 1 | 10 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| GammaControls | 2 | 2 | 0 | 7 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| GigCore | 21 | 2 | 0 | 42 | Invocation-local native provider reuse | Gameplay quest; no sleep proven |
| gtaTravel | 5 | 2 | 0 | 9 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Gwnet | 5 | 1 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Hangout_Romances_add-on | 1 | 0 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Hidden Packages | 4 | 1 | 0 | 19 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| hotscenes | 10 | 2 | 1 | 21 | After-phase UI/navigation guard | Menu/UI change and scene cleanup |
| illegal_mechanic_phase1a | 6 | 2 | 1 | 4 | Idle-fast guard, position reuse, native NPC location worker | Zone, NPC/spawn, interaction/dropoff transitions |
| immersive_third_person | 2 | 2 | 0 | 7 | V3 action routing, V4 FPP fastpaths, existing TPP toggle bridge | CET toggle, session and perspective; essential FPP work continues |
| ImmersiveFirstPerson | 4 | 2 | 0 | 11 | Realtime active with slow state probe | Author first-person movement, input; not hard dormant |
| ImmersiveGigs | 2 | 0 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| ImmersiveVDialogueExpanded | 3 | 1 | 1 | 7 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| InertiaReturns | 1 | 0 | 0 | 0 | Transition-local velocity getter | On-ground transition; no sleep proved |
| InfiniteGigs | 6 | 1 | 1 | 7 | Empty-queue zero-allocation fastpath | Job queue mutation preferred, observer/source audits pending |
| Jig Jig Revolution | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| jnvepenthousenorth | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| JoytoysOfNightCity | 5 | 1 | 0 | 9 | Bridge sentinel, active scene split | Dialogue/scene enter-exit |
| JoytoysUploader | 1 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Killing Night | 1 | 2 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| licksclubjukebox | 4 | 1 | 0 | 7 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| LucySpawns | 2 | 0 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| luxeGlenSwitcher | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| MakeAllVehiclesUnlockable | 1 | 0 | 0 | 0 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Milestone_Perk_Rewards | 1 | 1 | 0 | 4 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Minimap Widgets | 1 | 1 | 0 | 6 | Timer-first/mappin prefilter, widget/time reuse | HUD/mappin state, timers; no frame detach proven |
| MissingPersons | 7 | 2 | 0 | 21 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| MissingPersons_read_shard_addon | 1 | 0 | 0 | 4 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| MMS from Rogue | 1 | 0 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| nativeInteractions | 30 | 2 | 0 | 49 | Indexed mappin multi-rate + no-pins marshalling guard | Pin/scene/interaction signals; multi-rate fallback |
| nativeSettings | 1 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| nativeSettings_side_menu_add_on | 1 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| NCFlood | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| negotiableaffection | 1 | 1 | 0 | 3 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Night-night_City | 1 | 2 | 0 | 2 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| NightCityArmored | 3 | 1 | 1 | 10 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| NightCityInteractions | 2 | 0 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| No Crowd panic from stealth activity | 1 | 0 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| NonCanonRomancesEnhanced | 2 | 0 | 0 | 6 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| NPC Outfit Manager | 1 | 2 | 0 | 5 | Empty-rule idle, edit-event, bounded string/ID, GC fastpath | Rule edits, outfit/equipment, UI |
| OutdoorVForest | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| PacificaTyphoon | 7 | 2 | 0 | 21 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Phantom_Customizer | 3 | 2 | 0 | 4 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| PhotoModeNPCWardrobe | 2 | 2 | 0 | 4 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| PrivateYacht | 3 | 2 | 0 | 8 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| QuestRunner | 11 | 1 | 1 | 23 | Active-frame idle-fast | Quest queue/status, UI |
| QuestTrackingToggle | 1 | 2 | 0 | 4 | Exact action routing | Existing action |
| QuickStash | 1 | 1 | 0 | 0 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| RABlueMoonLucyDependency | 1 | 0 | 0 | 2 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| radioExt | 5 | 1 | 0 | 8 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| rarobottruck_1 | 2 | 2 | 1 | 4 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| react_to_horn | 1 | 1 | 0 | 2 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| repeatable_cyberpsychos | 8 | 1 | 0 | 9 | Native proximity worker, multi-rate runtime/presentation | Mission stage, native vicinity, reward lifecycle; 22.6/s watchdog and reward remain |
| repeatable_increased_criminal_activity | 6 | 1 | 0 | 7 | Validated multi-rate, lazy getters | Quest/mission status and distance |
| SDH0_Ferrari458Spider | 5 | 1 | 0 | 18 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SDH0_FerrariF40Zephyr | 5 | 1 | 0 | 18 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| Separate FPS Limits | 2 | 0 | 0 | 10 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SilentSilencersAndKnives | 1 | 0 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SimpleLocationManager | 4 | 1 | 0 | 9 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| sitAnywhere | 10 | 2 | 2 | 17 | Hard-dormant work-body guard | Workspot, scanner, timer, UI; realtime EveryFrame never-pause |
| Specialized Ripperdocs | 1 | 1 | 0 | 0 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SpecterB34S7Dev | 4 | 1 | 1 | 9 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SpecterCampaignProgression | 1 | 1 | 0 | 3 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SpecterInterceptorClean | 3 | 1 | 0 | 5 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| SpecterM11RecoveryDev | 2 | 1 | 0 | 3 | Prerequisite short circuit, static table reuse | Quest prerequisite; no hard sleep |
| SpecterNessaIntro | 1 | 1 | 0 | 2 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| sq021_randy_pc_scene_patch | 1 | 0 | 0 | 2 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| StealthRunner | 3 | 0 | 0 | 11 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| StreetGamesPoker | 9 | 2 | 1 | 18 | Four-lane scheduler, hidden HUD native getter guard | Poker seat/UI; realtime EveryFrame never-pause |
| tunnel_rescue | 13 | 2 | 0 | 21 | Quest-distance/dedicated diagnostics, native worker | Actual accepted stage, near zone, pending cleanup |
| UndergroundFightClub | 2 | 1 | 0 | 0 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| VehicleMileage | 8 | 1 | 0 | 17 | Fuel station native location worker | Nearby pump, vehicle/fuel state |
| VehicleRepairGarages | 1 | 1 | 0 | 8 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| WeaponGroundFix | 1 | 1 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
| yaibaPenthouse | 4 | 1 | 1 | 2 | Distant bathtub UI getter elision, native worker | Apartment proximity, UI interaction, transition |
| ziggy_last_play | 3 | 2 | 0 | 7 | Quest proximity + mappin sentinel, native worker | Quest actual stage, proximity/mappin diagnostics |
| zzzz_NPCGroupStudio | 1 | 2 | 0 | 1 | Generic/shared provider/read changes only; no separate source-aware dormancy claim | Independent wake not established; inspect author input/event and remaining per-frame work |
 
## Explicit hot wake / physical-pause gaps

- **Dedra:** All latest gameplay stayed in FPP; its generated `GCET_Semantic/dedra_camera_wake.lua` ran **20,329** update callbacks / **449.68s** ≈ **45.21/s** with **9.63 ms/s** exclusive CPU and **0.292 MiB/s / 2,879 allocation requests/s**. Steady FPP post-cleanup target **zero**. Its worker contains real `Pause/Resume` logic, but runtime result proves no physical dormancy. `__gcetWakeDiagnostics()` required to identify missing bridge, API subscription, pending cleanup, perspective or registrar state. No extra corrective semantic.
- **sitAnywhere:** generated worker schedules `sitanywhere.realtime` `EveryFrame({pause="never"})`; author work-body guards do not detach scheduler. Actual fresh capture ~45.21 executions/s. Need independent scanner/workspot/UI/timer wake before removing polling.
- **CyberTrials:** generated `cybertrials_engine_worker.lua` owns `world_far` 1 s, `world_near` 0.1 s, `system_state` 0.25 s, `race_state` 0.1 s, and `realtime` EveryFrame, all `pause="never"`. Avoid cutting essential active racing; inactive overlay/track needs source-proven wake.
- **StreetGamesPoker:** `world_far`, `world_near`, `seat_near`, `realtime` scheduled with `pause="never"`; realtime ~45.21/s. Prefer game/seat interaction start-stop + owed timers, preserve HUD exit/recovery.
- **repeatable_cyberpsychos:** worker `runtime_combat_reward`, `runtime_watchdog`, `runtime`, `presentation`, `scheduler` all `pause="never"` (different cadences); fresh reward/watchdog each ~22.6/s. Prove when stage, nearby activity or pending reward makes execution necessary.
- **repeatable_increased_criminal_activity:** six worker jobs `diagnostics_worker`, `rewards`, `state`, `mappins`, `diagnostics`, `system` are `pause="never"`; exact observed activity and independent wake still need verification.
- **0-Engine:** shared frame emitter and action hook can remain registered even after all child bodies return. Changes in its attribution may reflect work transferred from client mods; add per-job allocation scopes before assigning all costs to the host.

## Capture-wide allocation issues to audit, not presumed bugs

Fresh session ranked **unattributed 1.309 MiB/s / 10,935 ops/s**, **0-Engine 0.345 / 3,429**, **Dedra 0.292 / 2,879**, **ImmersiveFirstPerson 0.195 / 1,492**, **GigCore 0.147 / 1,187**, **ITP 0.105 / 408**, **f80h 0.076 / 1,363**. A callback making **~50 allocations per frame** at 45–60 FPS produces **~2,250–3,000 allocation operations/s**, regardless of their small average byte size. This is a *frequency* priority independent of CPU and hitches. Others worth explicit inactive-state checks include Ziggy, VehicleRepairGarages, InfiniteGigs, JoytoysOfNightCity, NPC Outfit Manager, ELM_ImmersiveWay, Yaiba and VehicleMileage. Compare actual state before declaring the operations waste.

**Important:** positive allocator growth in a callback's active window is not retained heap, GC objects born, nor exact producer causality for native pre-marshalling or shared 0-Engine tasks. Historical results are not matched A/B gameplay.

## What a later agent must add per mod

For **every** row, populate: author activation invariant (including quest offered vs accepted), actual currently attached CET host registration(s), 0-Engine worker/job IDs, direct native observer/fallback distinction, wake sources and timing, pending cleanup and state lifetime, min safe poll cadence, observed inactive/active calls/s + ops/s + MiB/s + object/string births, native pre-dispatch owner/class+method quality, proof of no lost behavior, stale/partial-source support, and live pass/fail evidence. Until that is measured, mark **WAKE UNPROVEN**; do not convert a plausible trigger into a completed optimization.
