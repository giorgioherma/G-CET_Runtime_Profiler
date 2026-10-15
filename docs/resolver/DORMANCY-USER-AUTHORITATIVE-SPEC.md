# Authoritative user dormancy specification — 62 mods

This document records user-specified intent. It is **not** evidence that source patches have been implemented or that guessed triggers are safe. Check every trigger against pristine and live Lua files, and preserve source semantics. All changes are Resolver-generated overlays, never direct edits to installed mods.

## Global behavioral contract

- Preserve the original listener and exact wake/keybind/event lifecycle, including pending work and replay needed at wake. Inactive downstream work should sleep; native listener registration may remain if necessary for wake.
- Separate listener registration, observer dispatch, Lua handler invocation, heavy polling, and allocation. Measure each; don't call a Lua early return native-hook elimination.
- Never treat a modded quest merely present in the journal as actively started. Gate on actual mission activation/state and preserve mission triggers.
- Cache static location tables only when all runtime providers, including other mods, are discovered and their invalidation/registration behavior is understood. Do not freeze dynamic markers.
- For periodic location discovery, keep the required cheap wake scan. For input, preserve author keybinds and any action consumption, routing order and fallback.
- Check pristine, partially optimized and fully optimized sources for each eligible rule. Skip on meaningful version drift, tolerate equivalent formatting and known source variants.

## Per-mod requirements

| # | Mod | Required condition / disposition |
|---:|---|---|
| 1 | GTA - JoyRide | Horn interaction while in car; citywide joytoy tracking only needed while in vehicle. |
| 2 | Combat | Combat-start event wakes; dormant outside combat. |
| 3 | repeatable_cyberpsychos | Wake on cyberpsycho defeat. |
| 4 | CyberTrials | Wake on trial start; investigate static location cache and external location providers in live stack. |
| 5 | StreetGamesPoker | Dormant unless playing; cache fixed locations. |
| 6 | NightCityBilliards | Wake when close to vanilla pool table. |
| 7 | NPCD_Hotline | SKIP |
| 8 | GameEntityExaminerTool | Fully dormant until defined keybind. |
| 9 | GiveMeEverything | Dormant until CET UI use or keybind. |
| 10 | Gambling System - Roulette | Dormant unless in use. |
| 11 | DriveAerialVehicle | Dormant outside flight; listener recognizes entry into flying vehicle. |
| 12 | illegal_mechanic_phase1a | Dormant when inactive; cache static locations. |
| 13 | Shift | SKIP |
| 14 | gambling-system-blackjack | Dormant except casino gameplay. |
| 15 | QuestRunner | Dormant outside active quest; activation trigger requires source proof. |
| 16 | Alternative Midair Movement | Dormant when grounded, wake airborne. |
| 17 | trainSystem | Dormant outside train use; source proof needed. |
| 18 | NightCityPizza | Dormant outside jobs; examine keybind subscription. |
| 19 | OverclockedJenkinsTendons | SKIP |
| 20 | songsdeck | Review source for safe dormancy. |
| 21 | DynamicOutfitsJudy | Keep cached outfits and Judy spawn wake listener. |
| 22 | FovSentinel | Review source; uncertain. |
| 23 | FenixMantisBlade | Review source; uncertain. |
| 24 | EasyTrainer | Static trainer locations may be cached; wake near trainer; preserve native interaction. |
| 25 | TeleportGatewaySystem | NO NEW TRIGGER GIVEN; verify existing wake conditions without guessing. |
| 26 | DiscardAmmoOnReload | Only process on reload. |
| 27 | advanced_settings | Only active in main/settings menu. |
| 28 | Auto Ammo Crafting (I need more bullets) | Retain loot-pickup detection listener. |
| 29 | DriveBus | Process for bus driving; consider required wake when bus in traffic. |
| 30 | QuestTrackingToggle | Only toggle on configured trigger. |
| 31 | sitAnywhere | Wake when scanner is active. |
| 32 | repeatable_increased_criminal_activity | Wake on completed criminal activity. |
| 33 | Dedka Auto Shop | Dormant outside use; cache static PC/physical locations. |
| 34 | marmurbank | Dormant outside bank; retain deposit-exit event; do not continuously poll vanilla ATMs. |
| 35 | immersive_third_person | Dormant while disabled; wake CET keybind. |
| 36 | ImmersiveFirstPerson | Dormant while disabled; wake CET keybind. |
| 37 | nativeInteractions | SKIP |
| 38 | Minimap Widgets | POSSIBLE SKIP; source review before changes. |
| 39 | AutoLoot | No loot-processing activity until CET keybind/activation; preserve required pickup/loot events. |
| 40 | BetterLootMarkers | Needs area scanning for loot; do not unconditionally shut down scanner. |
| 41 | GiveCraftMat | Dormant until CET UI or configured keybind. |
| 42 | Simple Notepad - CET | Dormant unless UI open. |
| 43 | DedraPalmjetQuickslot | Unknown; source review. |
| 44 | BackStepDuo | Unknown; likely conditional dormancy; source review. |
| 45 | NightCityAlliesMissions | Dormant while missions/system inactive; preserve activation. |
| 46 | GoodFeelings | Unknown; source review. |
| 47 | AirBackFlip | Dormant while grounded. |
| 48 | AutoDropWeaponOnPickupEquip | Pickup event is trigger; no per-frame work. |
| 49 | Drone Companions (Revamp) | SKIP |
| 50 | GhostVoidSystem | SKIP |
| 51 | Straight Edged Controls | Preserve control listeners; review downstream dormant work. |
| 52 | ImmersiveHeadInertia | SKIP |
| 53 | JoytoysOfNightCity | Dormant outside interaction if source supports it. |
| 54 | tunnel_rescue | Dormant outside active mission; journal-created/open != active mission. |
| 55 | ziggy_last_play | Dormant outside active mission; journal-created/open != active mission. |
| 56 | AerialRace | Only active in aerial vehicle and race region; investigate static race locations. |
| 57 | AppearanceMenuMod | SKIP |
| 58 | GigCore | Sleep inactive mission work where source-proven; retain discovery/wake. |
| 59 | SpecterM11RecoveryDev | Coordinate with GigCore; prerequisite/recovery event wake. |
| 60 | NPC Outfit Manager | Fully dormant until CET keybind/use. |
| 61 | yaibaPenthouse | Dormant when away from apartment; wake on proximity/entry. |
| 62 | CallList-VehicleDetailsOverlay-CET | Only active from vanilla garage key (user binds V); preserve key wake. |

## Explicit skips and open decisions

Explicit SKIP: NPCD_Hotline, Shift, OverclockedJenkinsTendons, nativeInteractions, Drone Companions (Revamp), GhostVoidSystem, ImmersiveHeadInertia, AppearanceMenuMod. Minimap Widgets is an uncertain possible skip. TeleportGatewaySystem has no new direction. Several others explicitly request source review; they must not be treated as proven dormant on inference alone.

## Separate main branch

The standalone profiler on `main` has its own release pipeline. Port only compatible, non-optimizer instrumentation and reporting improvements independently; do not merge the development Resolver or deep auto-optimizer.
