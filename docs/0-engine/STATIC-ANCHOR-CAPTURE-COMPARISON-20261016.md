# Shared static-anchor R4 — first paired archive review (2026-10-16)

**Verdict: mixed, scenario-confounded evidence; structural far-job removal is confirmed, some owner callback costs improve, net FPS/worker-tax gain is NOT established.** This document interprets **user-uploaded captures** only; no source edits, native changes or new Resolver passes.

## Actual inputs

- Earlier **without location cache** `CET-20261016-001659_CAPTURE_5-10_LUA_TO_C(2).zip`: 378.437 s, exact CapFrameX synchronization, 23,056 measured frames, baseline before the shared-location experiment. Title refers to previous 5–10 Lua-to-C work and therefore does **not** certify an otherwise identical code stack.
- Later **with R4 experimental location cache** `CET-20261016-042949_CACHED_LOCATIONS_REMOVED_EXPENSIVE_CHECKS.zip`: 391.675 s, exact CapFrameX synchronization, 20,381 measured frames. R4 was a source-live overlay against `EXTENDED DORMANCY(4).zip`.
- Compared `CET_Summary.json`, `CET_Runtime_Profile_ByMod.csv`, `...ByModKind.csv`, `...GGC_Registrations.csv`, `...Scheduler_ByJob.csv`, `...Spikes.csv` and the CapFrameX frame-time JSONs; all rate calculations use each capture's **actual duration**, and per-frame costs divide by capture mean FPS. Profiler measurements remain correlation-only where appropriate.

## Headline numbers

| Metric | Before cache | With cache | Qualification |
| --- | ---: | ---: | --- |
| Measured CET owner-exclusive workload | 131.687 ms/s | 121.432 ms/s | Lower partly because frame-driven invocation falls at lower FPS |
| Mean FPS | 60.929 | 52.092 | **Worse** in cached run; not a controlled same-scene A/B |
| Frame median | 15.811 ms | 18.497 ms | Worse |
| Frame p95 | 19.968 ms | 23.850 ms | Worse |
| Frame p99 | 25.916 ms | 29.700 ms | Worse |
| GPU active mean | 6.689 ms | 8.405 ms | +25.6%; **different scene/render load probable** |
| CPU active mean | 16.174 ms | 18.943 ms | Workload not matched |
| Collector time | 27.784 ms/s | 24.668 ms/s | GC still completes 6 cycles in both |
| 0-Engine measured owner-exclusive | 40.104 ms/s | 37.856 ms/s | Per-frame **0.658→0.727 ms** (+10.4%), not net win evidence |
| 0-Engine scheduled jobs, nested | 6.059 ms/s | 10.259 ms/s | Poker realtime alone **0.225→4.409 ms/s** |
| Allocator registration positive bytes, all owners | 665.428 MB | 625.773 MB | Exact sums, not retained heap; do not assign unknown bytes to mods |

**Outlier:** cached capture includes a **472.254 ms** callback owned by `0-Engine Observe MenuScenario_HubMenu::OnSelectMenuItem`; its origin is a menu selection/native observer, **not traced to the static-anchor worker**. Cached CapFrameX max 549.780 ms, collector maximum frame bucket 505.752 ms; noncached respective 297.399 ms and 195.476 ms. These one-offs warrant separate attribution; neither proves the cache caused a hitch.

## Source-target owner callback efficiency

| Mod | Exclusive µs/callback before→with | Positive allocation operations/callback before→with | Evidence and cautions |
| --- | --- | --- | --- |
| tunnel_rescue | 32.84 → 25.94 | 2.204 → 1.357 | Improvement compatible with suppressed far probes |
| illegal_mechanic_phase1a | 24.07 → 20.96 | 2.278 → 0.615 | Strong far/NPC discovery reduction consistent with design |
| ziggy_last_play | 38.26 → 34.54 | 5.639 → 5.237 | Modest normalized benefit |
| Hidden Packages | 3.93 → 3.95 | 0.077 → 0.004 | Very small owner CPU but allocation operations substantially reduced |
| VehicleRepairGarages | 3.01 → 3.27 | 0.446 → 0.410 | Little absolute difference |
| CyberTrials | 11.61 → 12.21 | 0.275 → 0.237 | Discovery job retired; total callback cost barely changes |
| SpecterM11RecoveryDev | 51.13 → 53.47 | **14.000 → 14.000** | No overall callback allocator gain shown; stage-specific skip not independently observed |
| Killing Night | 9.87 → 9.99 | 0.576 → 0.572 | Tiny share; Cron source wake not independently counted |
| ELM_ImmersiveWay | 8.85 → 8.96 | 0.000 → 0.052 | Provider registration only; no gain expected, additional activation callbacks possible |
| StreetGamesPoker | 11.93 → 25.28 | 0.500 → 2.080 | **Different gameplay activation**; cannot treat as regressions caused by cache without matched idle/near time |

**Direct scheduler evidence:** `StreetGamesPoker/poker.world_far` ran **379** times baseline and **zero** cached. `CyberTrials/cybertrials.world_far` ran **378** times baseline and **zero** cached. Those per-owner periodic discovery jobs genuinely disappeared. However cached Poker activates `poker.world_near` **1,298** times and `poker.seat_near` **2,588** times, compared with **0/0** baseline. `poker.realtime` average execution changed from 3.70µs to 84.63µs (total 0.225→4.409 ms/s). This is a **different near-table / table-interaction scenario** and largely accounts for the 0-Engine scheduler increase. Poker changes gameplay phase and often input/streaming load; no controlled regression inference is possible.

## Interpretation and next acceptance

1. **Confirmed architectural result:** two obsolete far scheduler jobs are gone and the shared engine gate is running. Some far-idle adapter costs decrease (especially Illegal Mechanic, Tunnel, Hidden Packages).
2. **NOT demonstrated:** R4 as a whole improves FPS, total 0-Engine overhead, or native hook count. In the observed cached recording mean/p95/p99 are **worse**, and performance pressure on the GPU is materially higher. Registration-owned allocations are not exact native-to-Lua marshaling, and no observed method attribution proves a specific GetWorldPosition count reduction. No native C callback-registration reduction established.
3. **Proposed minimal controlled test:** same save, game/graphics/clock conditions, initial location, similar field of view and route, approximately 3–4 minutes per condition. Compare **R4 installed vs full rollback to pre-cache live files**, *far from all poker/quest sites* as the idle baseline, then a brief equal approach to one location to check wake correctness. Run Profiler+CapFrameX the same way. Include near/far activity indication or route annotation, session restart, and identical game mods. Cross-check 0-Engine per-frame cost/allocator/G-GC vs combined target mods and their own scheduler jobs. Avoid claiming native-C removal from plain Lua short-circuits.
4. **Safety:** Do not remove authored Cron/OnUpdate/scene/remote wake jobs simply to push averages lower. Verify interactions and transitions first.

This report supersedes any inference of a quantifiable aggregate gain from R1–R4 synthetic Lua tests. It does not change the runtime patch or production CI.
