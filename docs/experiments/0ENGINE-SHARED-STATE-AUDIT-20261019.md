# 0-Engine shared-state source/capture audit — 2026-10-19

Status: **investigation; one isolated SpatialHash Stage A experiment; no production Resolver/Profiler/native changes.**
Evidence: user-provided `captures.zip` (baseline 20261019-204343 and Blackboard Stage A 20261019-222748) and user-provided `HANDOFF CHAT FILES.zip` (active `modsLIVEnow` and 62-original collection). Active optimized source takes precedence over original author sources.

## Confirmed existing result
The 12 PlayerStateMachine key snapshot in `BlackboardCache2.lua` is **confirmed** by the user's measured ~95% reduction in that exact allocation measurement. The method count/cadence remained 11 `GetInt` and one `GetBool` per update. Different capture locations prevent attributing general FPS to that change. Retain the existing source-verified Resolver stage.

## Baseline vs Stage A — bounded Lua/C positive allocations
These are sums **within sampled C-frame callsite rows only**, never complete-capture allocation totals and not direct A/B comparisons because capture durations/locations differ.

| Source | Pre-Stage A bounded sample bytes | Stage A capture bytes | Sampled callsite interpretation |
| --- | ---: | ---: | --- |
| `BlackboardCache2.lua` | 105,000 | 5,664 | 12 PSM definition lookups eliminated from frequent `__index` callsites; separately user-confirmed reduction |
| `DerivedState.lua` | 21,021 | 39,886 | camera-system and camera-forward native getters; single corresponding read per update, so no within-module redundancy proven |
| `SpatialHash.lua` | 21,120 | 25,280 | dynamic positions and eligible-poll `math.ceil` calls |
| `Lifecycle.lua` | 20,150 | 23,777 | `GetEntityID`, `id.hash`; redundant same-frame identity retrieval may be narrower future study |

`DerivedState.lua`: Stage A-capture `GetCameraSystem` line 125: 59 sampled calls / 10,797 own positive bytes; `GetActiveCameraForward` line 127: 59 / 4,661. There is no source-proven redundant second camera call to remove within `DerivedState.Update`. Cross-frame cached camera userdata would need explicit invalidation and frame-transition proof: **no patch**.

## Isolated candidate: SpatialHash poll-cell radius
Original `SpatialHash.lua` source SHA256: `ec9d6a8d3960ceb81b5588024786dfffecf290443415727c44231be159fd4b56`.

Line 272 `math.ceil(cfg.pollRadius / gridSize)`: pre-Stage A 50 sampled calls / 3,200 own positive bytes; Stage A capture 59 / 3,776. Private per-set `gridSize` and `pollRadius` are snapshotted at registration and have no exposed setter. For positive numeric grid sizes, precompute the clamped cell radius at registration, store `cfg.pollCellRadius`, and use it on eligible polls. Keep the original calculation as fallback for unsupported configurations. Marker: `G-CET SPATIAL_POLL_RADIUS_SNAPSHOT_STAGE_A_V1`.

Experimental single-file SHA256: `8f0af7d1d52a630628e1f12b75e4df54edc0f0f28ef607a2776e03367ee33adf`. Archive contains **only** `0-Engine/modules/SpatialHash.lua` with a matching pristine rollback archive. Both are created externally from the uploaded source. No change to spatial entry positions, spatial query semantics, native calls, polling cadence, callbacks or persistent entity userdata. Lua 5.3 syntax passed; deterministic two-set behavioral harness produced identical results for poll events, nearest/within query, count and reset, with `math.ceil` calls shifted from 0 registration + 6 polling calls to 2 registration + 0 polling calls. LuaJIT/gameplay acceptance **PENDING**. No runtime/FPS/allocation saving claimed yet; the CPU payoff may be small.

This candidate is **NOT wired into automatic passes**, since final admission requires approval, an exact-source hash, allocator-callsite evidence, idempotence, drift/postcondition testing and real A/B gameplay. Do not substitute the hand-built overlay for a source-authorized Resolver stage.

## Shared-position and NPC-state investigation
Current 0-Engine takes `player:GetWorldPosition()` once in `init.lua`, passes the identical position to `Proximity.SetPlayerPos` and `SpatialHash.SetPlayerPos`, and then runs both services. `Proximity` accesses x/y/z separately for each qualifying zone; `SpatialHash.Update` reads the current position once per qualifying set. Cross-set scalar snapshots could remove repeated userdata reads but might alter behavior if a callback updates position mid-loop: **defer until same-update mutation and consumer boundaries are proved**.

A code-pattern-only inventory of current optimized mod Lua files (119 folders with relevant patterns, not a measured runtime count) found 480 textual `GetWorldPosition` occurrences, 383 `GetEntityID` occurrences and 701 `IsAttached`/`IsDefined` occurrences. Some refer to the player, some to different NPCs, and some may appear in comments or inactive paths: **never multiply these into saved calls or allocations**. Notable source inventory leads are `hotscenes` (82 positions, 139 validity occurrences, 33 IDs), `tunnel_rescue` (56 positions, 42 validity, 5 IDs), `AutoLoot` (13 positions, 22 validity, 6 IDs), `ziggy_last_play` (16 positions, 10 validity, 5 IDs) and `AppearanceMenuMod` (78 positions, 75 IDs). Author originals were retained for behavioral comparison but should not replace active optimized source. Do not add a global NPC entity cache from these source counts. Next proof requires identical receiver/identity, parameters, timing, actual call frequency, lifetime and full invalidation across despawn/reload.

## Lag attribution independent of allocation experiment
Stage A capture:
- 0-Engine `PlayerPuppet::OnAction` frame 8732: 159.8648 ms registered callback spike, with LuaJIT atomic-to-sweepstring GC step 159.8203 ms inside the same registration and time window.
- Frame 10611: 181.5117 ms `OnAction` spike with 179.4816 ms atomic GC step inside that registration/time window.
- `nativeInteractions` frame 2445: 105.5483 ms callback spike with 105.4690 ms atomic GC step.
These prove **temporal nesting**, not who allocated the garbage. Do not attribute collector wall time to normal Lua code or claim the listed callback/mod caused the collection. Other major spikes remain (`radioExt`, `ziggy_last_play`, `NPC Outfit Manager`), with causality still open.

## Next gates
1. Same-save A/B for isolated SpatialHash and its exact `ceil` callsite; verify cell transitions, callbacks, session/reload and 0-Engine version compatibility.
2. After user approval, integrate into `PassGeneratorService` as a one-source evidence-gated stage similar to `BlackboardPsmDefinitionCacheSource`. Source SHA, marker idempotence, drift refusal, postcondition tests. Do **not** bump product version/native DLL.
3. Compare precise receiver/timing of candidate NPC reads in the active mod stack; favor within-update scalar reuse, no cross-frame userdata retention.
4. Attribute major GC pauses to allocation origin evidence, not the callback selected when the collector happened.
