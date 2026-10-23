# Hotscenes and DriveAerialVehicle — Resolver integration status

**Status (2026-10-23): INTEGRATED IN RESOLVER (SOURCE CONTRACT PASSED); GAMEPLAY/STACK ACCEPTANCE PENDING.** This is the current authoritative classification. The earlier [CI #1336 report](../EXPERIMENTAL-HOTSCENES-DAV-CI1336.md) is **historical experimental provenance** and must not be used as evidence that the Lua/source transformations are absent from the production Resolver.

## Implemented, active source stages

| Mod | Production semantic rule | Source implementation | Integrated behavior |
|---|---|---|---|
| Hotscenes | `hotscenes-ui-action-dormancy` | `src/G.CETProfiler.Core/Services/ModPasses/Hotscenes.cs` | Original `ObserveAfter('PlayerPuppet','OnAction')` phase preserved via shared 0-Engine after-phase subscription; original `REPEAT`/shard navigation kept; seven source-proven UI show/hide writers synchronize `SetActive`; direct CET fallback remains. |
| DriveAerialVehicle | `driveaerialvehicle` | `src/G.CETProfiler.Core/Services/ModPasses/DriveAerialVehicle.cs` | Original before-phase wildcard `Observe` behavior routed through the existing shared observer; active only during `Def.Situation.Waiting` and `Def.Situation.InVehicle`; source-proven situation and session transitions wake/sleep the subscriber; native `consumer:Consume`, three editable exception lists, `StorePlayerAction`, direct fallback and prior physics/cadence gates retained. |

**Catalog and dispatch:** `resolver-knowledge/semantic-library.json` marks both entries `generation.enabled=true` and `implementationStatus=ACTIVE_SOURCE_INJECTOR`. `SemanticInjectors.cs` dispatches to both implementations and supports source-proven incremental upgrades of pristine or partially optimized mod sources. Generated files remain source-dependent; this is not a guaranteed transformation of arbitrary future mod versions.

**0-Engine dependency:** ActionRouter handles opt-in `phase="after"` separately from its normal before-phase routing; `SetActive` suppresses unnecessary Lua subscriber work. This is a Lua/0-Engine listener-consolidation capability, not proof of native hook removal. The currently packaged/installed effective 0-Engine must supply compatible runtime APIs or the original CET observers must remain available as fallback.

## Verified evidence, with limits

- [CI #1880 (handoff branch)](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39891169422) — full Windows build green, including **Hotscenes and DriveAerialVehicle pristine/staged native action contract**.
- [CI #1887 (isolated virtual-runtime branch)](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39994740702) — the same specific contract and build green. That branch is still isolated; passing CI does **not** imply it was merged into the handoff branch.
- `tests/HotscenesDavActionSourceContract.ps1` verifies synthetic mod-shaped pristine/partial-source generation, exact phase markers, author fallback, DAV situation wakes, staged B→C upgrade, idempotence and source-file nonmutation. It is **not** an in-game test or byte-identical reproduction of the user's entire modstack.
- Previously generated `G-CET_PASS8_ITPv3_DAV_HOTSCENES` provides a useful optimized-stack reference. The current Resolver's **full-stack output has not yet been accepted as equivalent** to that reference; compare inventory and effective transformations, not merely ZIP file counts or timestamps.

## Outstanding, distinct work

1. **Regeneration acceptance:** Generate from the user's current modstack and capture; verify that both source rules are applied or legitimately already satisfied, fallbacks and 0-Engine dependencies are present, and the final ZIP is comparable to the optimized reference.
2. **Gameplay and behavioral acceptance:** Verify Hotscenes menu/shard navigation and wake transitions, DAV boarding/flight/landing, `consumer:Consume` exception behavior, sessions and safe fallback.
3. **Native CET pre-marshalling/whole-observer suppression — EXPERIMENTAL, NOT INTEGRATED:** A registered shared CET `ObserveAfter`/`Observe` hook can still cross native→Lua even when no Lua subscriber needs the event. The current shared router can reduce duplicate per-mod listeners and downstream decoding but cannot claim zero native crossings. Validated native-side hook filtering, lifecycle or safe pre-marshalling dispatch remains independent research. Do not treat this as part of either completed source-stage rule.

**Ownership:** This document records current Resolver integration, not approval to merge any experimental native ASI work, change mod behavior or move the isolated virtual-runtime branch.
