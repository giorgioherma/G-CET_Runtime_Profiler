# Complete semantic dormancy triage

Scope: 67 production rules across distinct mod identities; all originals are available. Classification is based on the production policy, not yet proof of a full source retrofit.

## Invariants

- Preserve author wake triggers, hotkeys, native listeners, action consumers and processing order.
- Early Lua return does not unregister native CET hooks. Count native registrations separately from subscriber dispatch and Lua work.
- Retain the exact listener that wakes dormant features. Dynamic action lists and localized matches require explicit proof before routing.
- Test original, partial and full optimization source, semantic drift, one-pass output, fallback and wake latency.
- Standalone Profiler main needs independent attribution/instrumentation changes, without importing Resolver features.

## All semantic entries by owner

| Mod identity | Rule IDs | Review lane |
|---|---|---|
| GTA - JoyRide | gta-joyride | Dormancy candidate |
| Combat | combat | Conditional/non-dormancy |
| repeatable_cyberpsychos | repeatable-cyberpsychos | Dormancy candidate |
| CyberTrials | cybertrials | Conditional/non-dormancy |
| StreetGamesPoker | streetgamespoker | Conditional/non-dormancy |
| NightCityBilliards | nightcitybilliards | Dormancy candidate |
| NPCD_Hotline | npcd-hotline | Dormancy candidate |
| GameEntityExaminerTool | gameentityexaminertool | Dormancy candidate |
| GiveMeEverything | givemeeverything | Input listener review |
| Gambling System - Roulette | roulette | Dormancy candidate |
| DriveAerialVehicle | driveaerialvehicle | Dormancy candidate |
| illegal_mechanic_phase1a | illegal-mechanic | Dormancy candidate |
| Shift | shift | Conditional/non-dormancy |
| gambling-system-blackjack | blackjack | Dormancy candidate |
| QuestRunner | questrunner | Dormancy candidate |
| Alternative Midair Movement | alternative-midair-movement | Dormancy candidate |
| trainSystem | metro-system | Dormancy candidate |
| NightCityPizza | nightcitypizza | Dormancy candidate |
| OverclockedJenkinsTendons | overclocked-jenkins-tendons | Dormancy candidate |
| songsdeck | songsdeck | Input listener review |
| DynamicOutfitsJudy | dynamic-outfits-judy | Dormancy candidate |
| FovSentinel | fov-sentinel | Conditional/non-dormancy |
| FenixMantisBlade | fenix-mantis-blade | Conditional/non-dormancy |
| EasyTrainer | easytrainer, easytrainer-background-dormancy | Dormancy candidate |
| TeleportGatewaySystem | teleport-gateway-system | Dormancy candidate |
| DiscardAmmoOnReload | discard-ammo-on-reload | Input listener review |
| advanced_settings | advanced-settings | Dormancy candidate |
| Auto Ammo Crafting (I need more bullets) | auto-ammo-crafting | Conditional/non-dormancy |
| DriveBus | drivebus | Input listener review |
| QuestTrackingToggle | quest-tracking-toggle | Input listener review |
| sitAnywhere | sitanywhere | Dormancy candidate |
| repeatable_increased_criminal_activity | repeatable-increased-criminal-activity | Conditional/non-dormancy |
| Dedka Auto Shop | dedka-auto-shop | Conditional/non-dormancy |
| marmurbank | marmurbank | Dormancy candidate |
| immersive_third_person | immersive-third-person, immersive-third-person-action-routing | Input listener review |
| ImmersiveFirstPerson | immersivefirstperson | Conditional/non-dormancy |
| nativeInteractions | nativeinteractions | Dormancy candidate |
| Minimap Widgets | minimap-widgets, minimap-stealth-widget-locality | Conditional/non-dormancy |
| AutoLoot | autoloot, autoloot-world-pin-and-early-distance | Input listener review |
| BetterLootMarkers | better-loot-markers | Conditional/non-dormancy |
| GiveCraftMat | give-craft-mat | Dormancy candidate |
| Simple Notepad - CET | simple-notepad-cet | Dormancy candidate |
| DedraPalmjetQuickslot | dedra-palmjet-quickslot | Input listener review |
| BackStepDuo | backstep-duo | Input listener review |
| NightCityAlliesMissions | nightcity-allies-missions | Conditional/non-dormancy |
| GoodFeelings | good-feelings, good-feelings-hard-draw | Input listener review |
| AirBackFlip | air-backflip | Input listener review |
| AutoDropWeaponOnPickupEquip | auto-drop-weapon-dynamic-routing | Input listener review |
| Drone Companions (Revamp) | drone-companions-revamp | Input listener review |
| GhostVoidSystem | ghost-void-system | Conditional/non-dormancy |
| Straight Edged Controls | straight-edged-controls-input-dormancy | Dormancy candidate |
| ImmersiveHeadInertia | immersive-head-inertia | Input listener review |
| JoytoysOfNightCity | joytoys-of-night-city-bridge-active-split-idle-facts-v2 | Dormancy candidate |
| tunnel_rescue | tunnel-rescue-v3 | Dormancy candidate |
| ziggy_last_play | ziggy-last-play-navigation-sentinel-v3 | Dormancy candidate |
| AerialRace | aerial-race-active-idle-split | Dormancy candidate |
| AppearanceMenuMod | appearance-menu-mod-squeeze | Dormancy candidate |
| GigCore | gigcore-blackboard-once | Conditional/non-dormancy |
| SpecterM11RecoveryDev | specter-recovery-prerequisite-reads | Conditional/non-dormancy |
| NPC Outfit Manager | npc-outfit-manager-idle-state | Dormancy candidate |
| yaibaPenthouse | yaiba-penthouse-distant-bathtub | Dormancy candidate |
| CallList-VehicleDetailsOverlay-CET | calllist-action-lazy-display-query | Input listener review |

## Native OnAction owners to check separately

- 0-Engine: attribute subscribers; preserve shared native wake hook.
- DriveAerialVehicle: current source returns cheaply outside Waiting/InVehicle but still has native Observe. Dynamic exception lists and consumer:Consume prevent guessed exact-action matching.
- immersive_third_person: exact-action router injector exists; runtime subscription and keybind wake still need proof.
- nativeSettings and hotscenes: outside the production semantic catalog; require their own original-source audit.
- CallList: localized substring matching; do not replace with guessed raw action-name list.

Review lane counts: {"Dormancy candidate":31,"Conditional/non-dormancy":16,"Input listener review":15}. This document is an inventory/triage, not a claim of completed source modifications.
