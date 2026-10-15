# Semantic soft admission and native-dispatch evidence — 2026-10-15

## Intent and authority

User capture: `CET-20261015-000909_ONACTION_DISCOVERY_IMMERSIVE_AND2` (269.436 seconds, 54.741 average FPS). The source-of-truth stack is the live CET mod Lua plus the original sources and prior composed optimizations; this report is a scoped investigation, not a new dormancy spec.

**Separate three decisions** instead of applying the generic dormancy classifier to everything:

1. **Hand-authored semantic source injectors:** pre-reviewed mod-specific implementations, selected from the 67-rule production library; current mod identity, source anchors, postconditions, and injector-local safety validation must still pass. An **actual per-callback measured invocation rate** is required. The default 3 ms/s is now a *soft CPU prioritization boundary*, not a veto; low Lua time is not evidence that the safe authored transformation lacks value.
2. **Generic native CET-to-Lua dispatch consolidation:** original/current callback registration, exact action selectors, fallback/wake behavior, and mechanical source proof must still authorize the transform. The caller/handler crossing can cost native time even when Lua exclusive time is zero. **Measured Lua time must never be presented as measured native-dispatch savings.**
3. **Generic inferred dormancy or cadence changes:** `HARD_DORMANT`/`DISCOVERY_DORMANT` and heuristic wake writers remain **evidence only**. They do not authorize suspending quest activation, discovery, area scanning, input routing, or cleanup without independent complete author-state and wake proofs.

### What the Profiler already hands off

`CET_Resolver_Input.json` already preserves all 1,449 registered callback records with owner, kind, target, registration ID, source file/range, invocation count/rate, Lua-exclusive time, spikes, frame multiplicity and deep profile evidence. It also carries registration-matched G-GC allocation operations/bytes with attribution caveats. The managed Profiler need not be changed, and the native DLL need not be rebuilt, to apply the current eligibility improvement.

The Resolver now writes distinct `semantic.admissionLane` and `runtime.nativeDispatch` evidence and uses the **same `SemanticAdmissionPolicy`** when reporting rule readiness and when generating a pass. This prevents a low-cost semantic rule being displayed as eligible but silently dropped by the pass generator.

The Profiler cannot infer author-specific quest state or gameplay activation just from callback frequency, GC bytes, and absent scenario markers; there were **zero tagged scenario intervals** in this capture. Do not invent transition witnesses, or infer an absent wake path from a source classifier miss. Hand-authored mod semantics and source proofs remain authoritative.

## Capture-specific breakdown

- 455 non-infrastructure ranked callbacks; 222 families.
- 337 classified `HARD_DORMANT` and 26 `DISCOVERY_DORMANT` by the **heuristic evidence classifier**, 363 total; 59 have its `CompleteWakePathProven` flag and 304 do not. 92 belong to the remaining classes. **304 is not a count of broken/insecure implementations.**
- 33 callbacks match authored semantic rules; 29 match their current-source anchors; only 6 have the generic wake-path proof flag. **27 of the 33 lack that separate heuristic flag**, including already-complete hand-authored hotpath optimizations. These are not 27 illegally generated dormancy passes.
- 123 callback outcomes `ALREADY_SATISFIED`; semantic postconditions/stage composition and generic frame consolidation must be distinguished from future idle-body improvements.
- Five measured `PlayerPuppet::OnAction` observers each receive ~1,269.455 invocations/s. These are high-frequency native-to-Lua callback candidates; real-time observer registration counts and action-name/source interest still govern safe routing.
- 72 `onUpdate` callbacks (~3,941.28 invocations/s combined) and 54 `onDraw` callbacks (~2,955.96 invocations/s combined). Rates include work that is legitimately active; they do not prove waste or native timing.
- In the **current optimized live stack**, four hand-authored rules are source-matched but not yet marked satisfied and fall **below 3 ms/s**, so this change makes them candidates for source-safe one-pass generation:

| Owner | Callback | Lua exclusive ms/s | Invocations/s | Authored rule |
|---|---|---:|---:|---|
| DriveBus | onUpdate | 0.616396 | 54.740 | drivebus |
| BetterLootMarkers | onUpdate | 0.060668 | 54.740 | better-loot-markers |
| StreetGamesPoker | onUpdate | 0.043720 | 54.740 | streetgamespoker |
| CallList-VehicleDetailsOverlay-CET | PlayerPuppet::OnAction | 0.261309 | 1269.455 | calllist-action-lazy-display-query |

A candidate **is not a guaranteed applied file**: authoritative injectors may reject source drift, incomplete controls, or an already implemented stage. Do not patch installed sources just to manufacture a new pass. Re-run ANALYZE with the updated Resolver, then generate and check the ZIP manifest. No new profiler capture is required for this admission-policy change.

## Safety and interpretation

- Generic wake-path proof is a *separate* classifier from source proof of an authored rule, and authored hotpath transformations need not ever suppress their whole observer. Never bypass mod-source anchors or injector postconditions to satisfy the user's "hand-made" directive.
- Source-specific wake data cannot be fabricated from total registration counts. For truly new automatic dormancy transitions, demand a complete source/event writer graph or bounded explicit scenario-tagged evidence; an inactive journal entry is not proof that a quest is active.
- Preserved source hash preconditions prevent applying stale analyzed decisions after the live source changes between analysis and generation; this is not a version-number pin.
- Native-dispatch, Lua-exclusive execution, and G-GC positive allocations are different measurements. Allocation overlap does not prove a callback produced the bytes; attribution stays registration-and-owner scoped.
- The existing default 3 ms/s diagnostic materiality display is unchanged; it is no longer a hard semantic generation threshold.
- This change does **not** complete all 62/64 checklist dormancy gates, patch 0-Engine, change the Profiler DLL, or claim frame-time improvement. Use existing original/live/partial/idempotent/source-drift contract tests and validate game behavior separately.
