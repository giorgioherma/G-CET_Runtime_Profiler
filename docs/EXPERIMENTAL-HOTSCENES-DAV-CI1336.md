# Hotscenes / DriveAerialVehicle — historical native-action experiment (CI #1336)

> **Reclassified 2026-10-23: HISTORICAL EXPERIMENT, with its Lua/0-Engine Resolver source transformations INTEGRATED.** The title and filename formerly indicated an active experimental implementation; that status is superseded. Both production semantic rules are enabled and exercised by normal Resolver CI. The remaining **native CET pre-marshalling / whole-observer suppression** investigation is still experimental and is not implemented. See [current integration status](resolver/HOTSCENES-DAV-INTEGRATION-STATUS.md).

**Historical origin:** Isolated branch `dev/native-c-hotscenes-driveaerial-ci1336` from `11742dbef97db812bc5557c30650f6e3117dddc9`; successful [CI #1336](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38810915489). At the time, its v1.2.7 changes were restricted to an experimental workflow and were not authorized for automatic merging. This is retained as provenance, **not** a present-day instruction that the integrated production source passes must remain isolated.

## Native action scope

**hotscenes** (live user source `hotscenes_native_ui.lua:4156`): Preserve CET `ObserveAfter('PlayerPuppet','OnAction')` phase, exact `REPEAT` navigation, shard controller and original menu logic. It needs the observer only while the author navigation UI is shown; a closed UI playing an animation does not need that particular action observer. Source-proven UI writers, onInit/shutdown/session reset and original ObserveAfter fallback must synchronously activate/deactivate its new shared after-phase subscription. Do not take ownership of keys or menu logic. Entire mod has other observers that stay intact.

**DriveAerialVehicle** (`Modules/core.lua:431`): Preserve CET normal `Observe` phase and the wildcard action body, including native `consumer:Consume`, *all* three user-editable exception lists, and `StorePlayerAction` on every admitted action. The subscriber is needed in `Def.Situation.Waiting` (pre-mount boarding) **or** `Def.Situation.InVehicle`. All other situations may sleep, with immediate wake from original `Event:SetSituation` and `GameUI SessionStart/SessionEnd` writers. On unknown engine state keep author fallback, not a guessed action list.

## Shared 0-Engine routing change

The pinned 0-Engine ActionRouter now understands `phase="after"` opt-in while its default remains before-phase. The first after-phase subscriber installs one lazy CET `ObserveAfter` hook, with a separate indexed registry. `SetActive` gates its Lua callbacks without changing other owners. Active-count fast paths return before native `GetName` when all subscribers of a phase are asleep. A phase-after routing request with no CET API returns an inert handle so the mod can preserve direct CET fallback. The original before-phase shared hook is unchanged.

**Critical limitation:** CET's installed `ObserveAfter` hook has no validated removal API. Thus Hotscenes' new shared hook **continues crossing native-to-Lua after the first registration**; subscriber sleep reduces Lua decoding/work, but does NOT eliminate that native C crossing. For true zero native crossings, native CET hook lifecycle or a verified native-side filter remains a separate research stage. DriveAerialVehicle replacing its *own* per-mod `Observe` with the **existing** shared before hook can remove its additional direct native listener (one native crossing per action), even though the shared 0-Engine observer stays. Validate event consumption ordering and gameplay.

## Historical build and present-day validation

The original isolated branch used `.github/workflows/native-c-hotscenes-driveaerial.yml`, Lua/router contracts and synthetic mod-shaped fixtures. Its experimental v1.2.7 Resolver was **not** an official Profiler or release. This provenance is retained for historical traceability.

**Current source integration:** `src/G.CETProfiler.Core/Services/ModPasses/Hotscenes.cs` and `DriveAerialVehicle.cs` are dispatched by `SemanticInjectors.cs`. Production catalog rules `hotscenes-ui-action-dormancy` and `driveaerialvehicle` both have `generation.enabled=true` and `implementationStatus=ACTIVE_SOURCE_INJECTOR`. Normal packaging runs `tests/HotscenesDavActionSourceContract.ps1` to validate pristine and partially transformed source, generated callbacks/fallbacks, idempotence, and on-disk source preservation. This test passed in [handoff CI #1880](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39891169422) and [virtual-runtime test CI #1887](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39994740702).

**Limit of proof:** Passing CI establishes source generation and contracts, not exact user-stack regeneration, in-game menu/flight/input behavior, observed CPU/GC reductions, or prevention of the remaining CET native→Lua shared observer crossing. `ObserveAfter` hook removal and safe native pre-marshalling filtering remain research only. Do not mistake consolidation of per-mod listeners for complete native-dispatch dormancy.

**Classification:** historical CI report, not an active integration task. The maintained current-status reference is [Resolver Hotscenes/DAV integration status](resolver/HOTSCENES-DAV-INTEGRATION-STATUS.md).
