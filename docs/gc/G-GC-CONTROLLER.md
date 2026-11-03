# G-GC Controller — approved lightweight heap-pressure assistance design

**Status (2026-10-27): DESIGN APPROVED, NOT IMPLEMENTED.** This is the current intended G-GC controller, recovered from the October 25–26 design decisions and clarified by the October 27 gameplay/GC captures. It supersedes the older **research-only** instruction in [GC-RESEARCH.md](GC-RESEARCH.md) that says never automatically tune GC; that restriction remains true for the **existing** Profiler/Resolver until the separately tested controller is actually implemented. Do not represent the design as shipped or green-CI validated. Phase 1+2 source preservation remains the active implementation task in [LATEST-WORK](../LATEST-WORK.md).

## 2026-11-03 correction — Resolver-owned native installation, not standalone Lua

**User-confirmed binding decision:** The experimental standalone `G-GC_EXPERIMENTAL_ADAPTIVE_SLIDERS_v0.1.zip` was the **wrong architecture** and must not be used, packaged, promoted or treated as a production prototype. A manually installed `G-GC/init.lua` file with hardcoded calibration values is specifically rejected.

**End-to-end ownership:** A real selected Profiler/CapFrameX capture feeds the existing `CET_Resolver_Input.json` → Resolver ANALYZE → G-CET Resolver evidence → GENERATE composition. Resolver derives that player's measured hitch tolerance and approximate collection-pressure context, then writes a small versioned configuration and deploys **one separately verified persistent G-GC controller through the G-CET installation flow**. Its runtime is **native-owned**, independent of the temporary capture-only profiler ASI; a CET overlay UI may present intensity/frequency sliders but must never become a second GC execution owner. Runtime settings are adjustable while playing and are constrained by profile-derived headroom, cost and pressure gates. On each admitted new capture/Resolver generation, replace the capture-derived configuration instead of reusing earlier values. Missing CapFrameX data or incompatible/absent controller binaries are explicit blockers, never grounds for guessed default calibration or an incomplete ZIP.

**Runtime goals:** Use an approximate observed GC pressure band (median and slack), rather than exact natural-trigger prediction or a mandatory time-to-trigger estimator. Pressure decides **whether** to assist; reserved frametime headroom and measured step cost decide **how much**; intensity adjusts effort and frequency controls minimum attempt spacing. High gameplay load takes precedence and assistance backs off rather than causing continuous lag. Protect safety around loading, transitions and collector atomic steps; never promise an individual `collectgarbage("step")` operation is a hard-millisecond bounded primitive. The target is smoother gameplay and heap slope, not maximal GC work or zero spikes.

**Current implementation gate:** The main repository's `ResolverService` now attaches `gGcCaptureCalibration` from the **selected** handoff (not a fixed historical capture) with status, actual hitch tolerance, median observed slow-step heap, p25/p75, explicit uncertainty and **installAuthorized=false**. This is evidence plumbing only. The current repository does not yet contain a built, independently tested persistent native G-GC controller/Updater-owned deployment mechanism; therefore **no G-GC runtime is generated or installed by that stage**. Source/build/runtime/restore must be implemented and proven before enabling install. Native operations must not be owned by the temporary Profiler ASI, which restores the official CET binary afterward.

**Earlier proposals below are historical where they conflict with this decision.** In particular, `init.lua` + `threshold.lua` as a complete controller is superseded, and 5–8 natural cycles or exact time-to-trigger must not be a prerequisite for a pressure/headroom-first experiment.

## Purpose and non-goals

The objective is **preventing avoidable frame stalls caused by accumulated LuaJIT garbage/heap pressure**, with the least practical runtime overhead. The primary control signal is **live heap pressure and predicted time remaining until the natural LuaJIT collection-trigger region**. Collector and CapFrameX evidence establish a *latency budget* for safe assistance; they do **not** mandate constant higher GC effort.

Upstream source optimizations (eliminating pointless allocations, duplicate work and excessive Lua→native polling, dormancy, caching) remain the first line of defense. The controller is a small **incremental helper**, not a substitute for fixing data producers, not a replacement collector, not a forced full-GC scheduler and not Choom Memory Booster. Do not introduce aggressive global collection, synchronous full collections or high-frequency monitoring that itself worsens frame pacing.

## Installation, data ownership and configuration — approved

- G-GC is **shipped with the G-CET Updater and generated/configured by Resolver** for the user's installation. It is independent of Choom Memory Booster; CMB is deliberately removed from the live test and source baseline.
- Proposed source/generated Lua layout: `bin/x64/plugins/cyber_engine_tweaks/mods/G-GC/init.lua` and `threshold.lua`, or an equivalent correctly owned packaged layout proven during implementation. A single controller owns the incremental assistance; do not duplicate GC helpers inside individual optimized mods.
- A **Profiler + CapFrameX capture determines the user's actual frametime stall/hitch budget**. Resolver derives the configured threshold from the latest admitted capture and **overwrites the exact configuration value on every generation**. No historic thresholds, historical config database, hardware benchmark or hardware detection pass is required.
- The earlier design proposed a conservative calibration formula `hitch_threshold_ms = max(25 ms, 1.5 × median CapFrameX frametime)` (a prior example produced 25.2045 ms). Treat this as the *design-era proposal to verify against the currently admitted profiler hitch semantics*, **not** as proof that a particular hardcoded 25 ms threshold is universally valid or that a shipped generator exists. The latest authoritative threshold must come from the actual Profiler/Resolver evidence; distinguish ordinary P95/P99 frametime reporting from the stall boundary selected for control.
- **Runtime must not poll CapFrameX, scan hardware, replay profiler logs, maintain a historical capture database or infer the user’s hardware.** Its only runtime job is lightweight heap/collector observation and assistance informed by the generated scalar configuration. Missing or invalid profile evidence must be reported, not silently replaced with fabricated calibration.

## Runtime decision model — approved direction

1. **Observe very cheaply.** Use bounded/low-frequency observations of the current LuaJIT heap, changes over elapsed time, and the collection-cycle progress or observed natural trigger regions that can be read safely from the supported LuaJIT/CET environment. Avoid per-frame allocations, busy waits, repeated expensive native getters and unbounded history.
2. **Characterize unassisted natural disposal.** Learn where LuaJIT naturally begins collecting from a rolling median over approximately **5–8 valid natural cycles**. The trigger region is an empirical, adaptive heap observation, not a universal fixed-MiB limit. **Do not train this baseline on forced or controller-assisted cycles** and do not equate a heap drop with provenance or reclaimed garbage from any specific mod. When too few valid cycles exist, use a conservative, explicitly uncalibrated state.
3. **Estimate pressure from live growth.** Compute a bounded/smoothed *recent net heap growth rate* from observations, separate from the Profiler's **cumulative positive allocator bytes**. Rising gameplay load accelerates it; idle/light gameplay should lower or flatten it. If growth is flat/negative, no predicted near-term pressure is asserted. Forecast, when meaningful:
   `time_to_pressure ≈ max(0, learned_natural_trigger_heap − current_heap) / max(recent_positive_net_heap_growth_rate, epsilon)`.
   The controller should never interpret positive allocation request totals as retained/live-heap growth.
4. **Adapt incremental assistance only when it matters.** Maintain a very light baseline/maintenance mode; increase bounded incremental assistance progressively as remaining time shrinks, heap grows toward the learned trigger region, and predicted risk approaches the configured latency safety margin. Reduce assistance when pressure eases. Require conservative maximum effort, hysteresis/cooldown, and no execution storm. The exact safe helper primitive, time slicing, cadence and intensity bounds must be validated **before implementation is considered ready**; do not invent a `collectgarbage` API guarantee from this design.
5. **Minimize visible latency, preserve LuaJIT safety.** Respect ordinary collector progress, session/loading transitions, explicit user/game lifecycle and fallback behavior. Do not claim a helper can interrupt a single long indivisible atomic/sweepstring/finalize step; aim to reduce the likelihood/backlog of large natural stalls, then verify empirically. Fall back to ordinary LuaJIT behavior if the controller cannot establish safe heap/collector state.
6. **Use evidence to retune, not heavyweight runtime profiling.** Later Resolver runs may regenerate a new exact threshold from better captures. No permanent in-game detailed allocation profiler is required merely to dispatch the lightweight helper.

**Important distinction:** the generated **frametime latency threshold** is not the same variable as the **learned natural GC heap-trigger region**. The first bounds acceptable *visible stall cost*; the second plus *recent growth rate* predicts *when heap pressure will become urgent*. Assistance responds to the second while respecting the first.

## October 27 evidence and implications (observation, not control-policy proof)

Two captures were **entirely first-person gameplay**. Both began with approximately **20 seconds of light movement/idle** followed by more demanding gameplay, including combat. Locations and activities differ, so aggregate averages are not matched A/B comparisons. Their within-capture phase changes are useful for **gameplay-dependent pressure** and should be inspected in time-aligned bins rather than inferred from whole-capture rates alone.

| Metric | 12:50 capture | 15:09 capture | Interpretation |
| --- | ---: | ---: | --- |
| Duration | 320.752 s | 351.007 s | Different scenes/activity |
| CET callback wall | 147.975 ms/s | 155.693 ms/s | GC can be nested; not GC-free CPU |
| Separate LuaJIT collector | 33.989 ms/s | 40.564 ms/s | **Non-additive** to callbacks |
| Positive LuaJIT allocation growth | 4.252 MiB/s | 4.454 MiB/s | Not current heap growth or all garbage |
| Allocation ops | 41,179/s | 42,748/s | Distinct from object births |
| Avg FPS | 58.509 | 61.067 | Different work; cannot attribute |
| P99 frame | 29.600 ms | 30.220 ms | Tail remains costly |
| Worst frame | 344.252 ms | 230.964 ms | Severe stalls persist |

- In the 15:09 capture, **nine ~164–212 ms collector slow steps** occurred and the **~230.96 ms worst frame temporally overlapped a ~211.65 ms collector step**. Such timing strongly suggests collector contribution to that stall, not a proof of which allocation producer made it necessary. Recorded collector `pause=200`, `stepmul=200` are *observed settings*, not approved values for a controller to overwrite.
- That capture recorded **eight completed ordinary incremental cycles**, with **zero observed full collections and zero explicitly instrumented collection requests**. Analyze the raw slow-step/state/heap-before/after/threshold/debt timing to understand **what triggers natural disposal**, whether a reliable trigger region can be learned, and where pressure predicts future stalls. Natural GC can involve many steps, so do not assume a single instantaneous trigger point is always measurable.
- G-GC accounted **1,563.535 MiB cumulative positive allocations**, with **35.605% / 556.696 MiB unscoped**. These are **not retained memory**. No owner is allowed to inherit unscoped bytes without source proof. Sampled reclamation and heap checkpoints cannot prove individual retained-heap contributions or an actual memory leak.
- The heavy callback/allocation rankings remain useful *separately* for source optimization. Top **attributed** allocations in 15:09 were DedraThirdPersonCombatAddon **218.385 MiB / 0.622 MiB/s**, GigCore **113.202 MiB**, ImmersiveFirstPerson **94.492 MiB**. The user did **not** engage third-person view in either capture. Dedra nevertheless ran **21,435 onUpdate calls (~61.07/s), 17.327 ms/s exclusive** and was the leading identified allocated-byte producer. Immersive Third Person and Dedra must have their runtime wake handshake, pending camera cleanup and source-owned frame ownership checked; do not claim full dormancy or remove needed work on the basis of first-person camera alone.
- The 0-Engine Scheduler registered **23 jobs, each reporting zero executions** in both captures. This is an *execution/telemetry verification gap* (dormant jobs vs instrumentation vs defect), not proof all jobs are broken. Prove active/dormant runtime ownership before drawing conclusions about GC relief from scheduled work.

## Acceptance and implementation gates

- **Do not start implementation solely by applying a 25 ms scalar or by forcing more collection.** First characterize collector trigger/disposal behavior and time-binned heap pressure across light and combat intervals using the existing measurements, distinguishing observed natural cycles, sampling gaps and controls.
- Specify one authoritative lightweight G-GC owner and its runtime API/capabilities; no Choom dependency, no duplicate ownership with 0-Engine, and no dynamic per-mod semantic GC scheduler.
- Test idle/low pressure (no boost), sustained combat/high pressure (bounded predictive assist), transitions/loading, burst allocations, controller disabled/unavailable fallback, long natural GC steps, repeated sessions and failure of trigger estimation. A continuous baseline probe must demonstrably cost very little.
- Evaluate **whole-game** frametimes/CapFrameX episodes and catastrophic stalls, LuaJIT heap trajectory, collector step/phase timing, CPU overhead, allocation rates and gameplay correctness against repeatable same-save/same-route unassisted control. Improvements are hypotheses until measured; excessive controller CPU, altered gameplay timing, memory retention, or changed GC safety are blockers.
- Keep upstream source optimization opportunities distinct: review top allocated-byte/ops/birth owners, identify **unnecessary** production using actual author source and lifetime/escape proof, then repair authoritative generators only. Legitimate large allocations are not automatically defects.
- Follow G-CET fixed stage order, original→partial→idempotent/unsafe drift tests, framework/native dependency checks and CI. **This is documentation only; no controller generated code, product bump or gameplay validation has been completed.**

## References

- [Active runtime capture and preservation status](../LATEST-WORK.md)
- [GC research / producer provenance (historical)](GC-RESEARCH.md)
- [G-GC tracer mechanics](G-GC-TRACER.md)
- [Collector phases and slow-step diagnostics](GC-PHASE-ALLOCATION-DIAGNOSTICS.md)
- [Data provenance engineering specification](DATA-PROVENANCE-SPEC.md)
