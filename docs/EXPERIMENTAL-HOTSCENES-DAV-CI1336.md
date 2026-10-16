# Experimental native action dormancy: isolated CI #1336 branch

**Owner:** `dev/native-c-hotscenes-driveaerial-ci1336`. Base commit `11742dbef97db812bc5557c30650f6e3117dddc9` is the exact source of successful CI [#1336](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38810915489), not the moving main development branch. **Never auto-merge or cherry-pick** changes to `dev/resolver-profiler-handoff` or `main` without explicit user instruction. Product stays v1.2.7. Changes must remain isolated to this branch and its separate experimental workflow.

## Native action scope

**hotscenes** (live user source `hotscenes_native_ui.lua:4156`): Preserve CET `ObserveAfter('PlayerPuppet','OnAction')` phase, exact `REPEAT` navigation, shard controller and original menu logic. It needs the observer only while the author navigation UI is shown; a closed UI playing an animation does not need that particular action observer. Source-proven UI writers, onInit/shutdown/session reset and original ObserveAfter fallback must synchronously activate/deactivate its new shared after-phase subscription. Do not take ownership of keys or menu logic. Entire mod has other observers that stay intact.

**DriveAerialVehicle** (`Modules/core.lua:431`): Preserve CET normal `Observe` phase and the wildcard action body, including native `consumer:Consume`, *all* three user-editable exception lists, and `StorePlayerAction` on every admitted action. The subscriber is needed in `Def.Situation.Waiting` (pre-mount boarding) **or** `Def.Situation.InVehicle`. All other situations may sleep, with immediate wake from original `Event:SetSituation` and `GameUI SessionStart/SessionEnd` writers. On unknown engine state keep author fallback, not a guessed action list.

## Shared 0-Engine routing change

The pinned 0-Engine ActionRouter now understands `phase="after"` opt-in while its default remains before-phase. The first after-phase subscriber installs one lazy CET `ObserveAfter` hook, with a separate indexed registry. `SetActive` gates its Lua callbacks without changing other owners. Active-count fast paths return before native `GetName` when all subscribers of a phase are asleep. A phase-after routing request with no CET API returns an inert handle so the mod can preserve direct CET fallback. The original before-phase shared hook is unchanged.

**Critical limitation:** CET's installed `ObserveAfter` hook has no validated removal API. Thus Hotscenes' new shared hook **continues crossing native-to-Lua after the first registration**; subscriber sleep reduces Lua decoding/work, but does NOT eliminate that native C crossing. For true zero native crossings, native CET hook lifecycle or a verified native-side filter remains a separate research stage. DriveAerialVehicle replacing its *own* per-mod `Observe` with the **existing** shared before hook can remove its additional direct native listener (one native crossing per action), even though the shared 0-Engine observer stays. Validate event consumption ordering and gameplay.

## Build/validation procedure

This branch adds a separate `.github/workflows/native-c-hotscenes-driveaerial.yml` with Lua syntax/router contracts and isolated source invariants. CI green proves source/runtime tests only; not game behavior or native savings. Next implementation stage is source-proven mod-specific Resolver transforms in the current mod-owned code, with original/live/partial/idempotence checks, no extra keys or mods, then a game test. Prevent expensive full build trial-and-error: run targeted contracts first, and run the main packaging CI only after those pass. This document is an active engineering task.
