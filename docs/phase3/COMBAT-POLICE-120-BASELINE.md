# COMBAT-POLICE-120 — canonical Phase 3 controlled gameplay benchmark

**Approved 2026-10-29; FIRST MEASURED 120s REFERENCE CAPTURED 2026-10-29 19:51 local filename timestamp.** Supersedes [historical COMBAT-POLICE-60](COMBAT-POLICE-60-BASELINE.md) prospectively. This is a duration and reporting change, **not a new gameplay route or different stress scenario**. The historical 60s captures are retained as valid evidence of their own configuration; they are not the comparison baseline for future 120s results.

## Reproduction and controls

- **Scenario:** one continuous **120-second sustained police combat** in the same apartment-area engagement used in the user-controlled 60s tests. Use the same starting save, player position, equipment, wanted-level setup, difficulty, graphics/FPS cap, 0-Engine settings, mod stack and deployed Profiler/Resolver identity as the new 120s baseline, except documented optimization changes. Capture only combat; if the fight ends or an unrelated transition contaminates the two minutes, note it rather than silently compare mixed intervals.
- **Single-key F10:** CET `CETProfiler_Police60` is the **legacy internal binding identifier, deliberately preserved** so existing F10 mappings survive. Its new display name is **Profiler: COMBAT-POLICE-120**. The existing CET Profiler Controls `onUpdate` uses **native captured seconds** to finish at the **first frame at or after 120.000s**, emits `SCENARIO_COMBAT_END`, pauses and dumps exactly once. At start it emits `SCENARIO_COMBAT_START`. This is bounded by frame cadence; record actual elapsed time.
- **CapFrameX:** capture hotkey **F10**, capture time **120 seconds**, delay **0**. One physical F10 press starts CET and CapFrameX, each with its own independent clock and 120s stop. Check pairing/overlap in their raw data. Keep F11 exclusively as manual, unlimited CET START/STOP (also permits early abort of an automated run). Do not assign F11 to CapFrameX.
- **Profiler capture title:** label the new run `COMBAT-POLICE-120_BASELINE` (and subsequent captures `COMBAT-POLICE-120_<variant>`) when setting the capture title. Archive the raw CSV/JSON/CapFrameX data, current generated pass report, implementation commit and native DLL checksum. Do not invent an automated title override; the title is distinct from the fixed F10 benchmark markers.
- **User-confirmed controlled A/B:** the user's standardized test is controlled. A missing written configuration field in the archive is `NOT_RECORDED`, not evidence that the run was uncontrolled. Record real deviations. One qualifying run per configuration is accepted; repeat if greater confidence is desired.

## Why 120 seconds replaces 60

The latest 60s DAV + GigCore combat capture had **zero completed GC cycles**, compared to two completed cycles in the preceding 60s capture. Source-owned DAV allocations fell sharply while global positive allocated bytes/requests increased; **this could indicate delayed major collection pressure, not a faster completed collection**. A 120s capture provides longer visibility into collection scheduling, **but cannot guarantee a complete GC cycle** and never forces collection or changes GC policy.

**Phase 3 interpretation order:** (1) frametime/hitch episodes and severity (P99/P99.9, >50/>100ms, worst frame and excess stall time), (2) GC collector time, phase timing, number of completed cycles, largest pause, object births and time-to-first-completed-cycle when timestamped, (3) positive allocator **requests/s and bytes/s**, per-mod/job allocation and unscoped owner-zero, (4) native-to-Lua argument/Next-wrapper stages, (5) average FPS/CPU. Do not add overlapping allocator windows or infer a mod owns retained heap from a GC overlap.

**Comparison rule:** compare **120s vs 120s**, with a new first accepted 120s capture as baseline. Rates per second support historical context but don't turn a 60s sample into a controlled 120s A/B. Where raw timeline telemetry supports it, also inspect **first minute vs second minute within the same 120s run** to observe GC accumulation and hitches; do not invent per-minute counts from whole-capture aggregates. Distinguish GC cycles *completed within the window* from collector work (incremental phases) that may continue between cycles and from true per-cycle CPU duration.

## Gates

- **Implementation:** `payload/CETProfilerControls/init.lua` contains one constant `TIMED_CAPTURE_SECONDS = 120`; all text/HUD reflects 120 seconds; legacy input ID remains; manual F11 is unchanged. No native DLL, game mod, scheduler or Resolver transformation changes.
- **Regression:** `tests/ProfilerTimedCaptureLuaContract.lua` proves F10 remains active past 60.02s and 119.99s, finishes just over 120s, emits the correct markers once, preserves no duplicate registrations and keeps F11 unrestricted.
- **Runtime/new baseline:** first real 120s user-controlled gameplay capture, CapFrameX correlation, genuine per-job data, no dropped scopes and collector/hitch metrics. **The first 120s capture now exists and is measured; source-level and in-game camera feature validation remain separate, and the exact starting save/gear are NOT_RECORDED in telemetry.** Preserve all 60s captures as historical references.

## First measured 120s reference — 2026-10-29

**User archive:** `CET-20261029-195149_FPC_TPC_AND_MINIMAP_GCRESULTS.zip`. F10 `SCENARIO_COMBAT_START` marker and CapFrameX association; CET **120.008372s**, CapFrameX **120.005s** and **4,677 presented frames**, correlation `GOOD / exactAlignment=true`, 3.496ms duration difference. User reports substantially smoother play apart from a brief heavier GC stall; whether the exact same saved gear/wanted level were used is `NOT_RECORDED`, not a reason to reject a user-controlled run. **This is the first baseline for future 120s-vs-120s comparisons, not a causal A/B for the newly optimized camera mods.**

| Whole-capture counter | Measured value |
|---|---:|
| Positive LuaJIT allocator growth (not necessarily garbage) | **397.893360 MiB = 3.315547 MiB/s** |
| Positive allocator requests | **3,491,135 = 29,090.762/s** |
| New GC-managed object births | **901,456 = 7,511.609/s** |
| Unknown/unattributed positive allocations | **196.638250 MiB = 49.420%** |
| Collector execution (includes incremental GC, not a faster completed cycle) | **2,954.3086ms / 120s = 24.6175ms/s** |
| Completed GC cycles | **2**: first observed frame end **63.514703s**, second **65.330284s** |
| Explicit Lua collectgarbage calls | **0** |
| Lua heap samples | first **620.921 MiB**, peak **780.730 MiB**, last **613.668 MiB**; last minus first **−7.253 MiB** |
| CapFrameX frame delivery | P95 **33.768ms**, P99 **46.046ms**, P99.9 **109.135ms**, worst **478.836ms**, **7** frames >100ms |
| Grouped adaptive hitch episodes | **59 / 120s = 29.499/min**; hitch excess **2,492.216ms** |

**Time structure:** The first 60s had **0 completed cycles**, **1,377.895ms** collector execution, **35** grouped hitch episodes and **1** frame >100ms; the second 60s had **2 completed cycles**, **1,576.413ms** collector execution, **24** grouped episodes and **6** frames >100ms. The narrow **63–66s** window alone had **1,469.080ms collector work**, **both completed cycles**, and two catastrophic grouped episodes: **478.836ms** worst frame near 63s; **313.458ms** worst frame near 64–65s. Together those two episodes generated **1,318.503ms = 52.90%** of full-run hitch excess. The first overlaps **444.153ms collector work bucket** and `f80h_aroused_expressions` `onUpdate` (444.744ms raw callback); the second overlaps more collector work and `AutoLoot` `onUpdate` (244.305ms raw callback) plus the 0-Engine frame lane. **Overlapping callbacks are the GC trigger site / raw wall time, not proof they allocated the garbage.** After 66s, the remaining 54 seconds had **P99 40.587ms**, only **8** frames >50ms and **no frames >100ms**, consistent with user-perceived longer smooth stretches. GC phase includes two >99ms *atomic* operations around **47.35s (116.027ms)** and **64.015s (99.821ms)** and cannot be ascribed to a mod without source proof.

**Historical 60-second, contextual rates (NOT matched 120s A/B):** Against the 2026-10-29 17:14 `DAV_GIGCORE` capture, 120s **Minimap Widgets** measured **0.125694MiB/s, 922.98 ops/s, 76.49 GC births/s** vs prior **0.156754MiB/s, 1,174.7 ops/s, 118.4 births/s**, consistent with a useful reduction but not single-mod causality. **Immersive Third Person** measured **0.089381MiB/s, 332.54 ops/s, 9.94 births/s** vs prior **0.097502MiB/s, 351.8 ops/s, 29.9 births/s**, also favorable. **Immersive First Person** measured **0.168730MiB/s, 1,273.33 ops/s, 157.64 births/s** vs **0.144525MiB/s, 1,069.9 ops/s, 142.7 births/s**, not an unambiguous reduction per second. Framerate also changed from **33.962→38.973FPS**; therefore investigate **ops/frame** and hot callsites before attributing IFP behavior to the optimized scratch source. **Global positive growth and requests/s are higher than the prior 60s**, so do NOT claim a measured stack-wide allocation reduction from this cross-duration comparison.

**Next steps:** Preserve this ZIP, raw GC buckets, CapFrameX run and time-aligned evidence; verify FPP FreeLook/crouch/aiming and TPP toggles/restore and Minimap pin visibility in actual gameplay. Match future captures to **this 120s baseline**, inspect the first-minute and second-minute view and 63–66s GC cluster, and isolate sustained producer sources rather than patching a GC-overlapped callback or changing GC parameters to hide the stall. Native-to-Lua redesign is still unimplemented.

<!-- PHASE3_120S_AUTOLOOT_F80H_COMPARISON_20261029 -->
## First post-baseline 120s comparison — AutoLoot + f80h, 2026-10-29

**Capture:** `CET-20261029-204841_F80F_AUTOLOOT.zip`, CET **120.001091s**, CapFrameX **120.001s**, **4,284 frames**, `sync.quality=GOOD`, `exactAlignment=true`; compare the earlier `195149_FPC_TPC_AND_MINIMAP_GCRESULTS` **120.008372s / 4,677 frames**. Both report **139 active owners**, and both are F10-combined CapFrameX recordings. These are whole-*configuration* observations, not isolated individual producer causal tests; first/last heap and gameplay frame rate differ. User reports reduced noticeable allocation pressure and longer smooth stretches.

| Metric (120s capture total unless /s) | Earlier 19:51 | New 20:48 | Change |
|---|---:|---:|---:|
| Positive LuaJIT allocations, MiB | 397.893360 | 356.083752 | **−10.51%** |
| LuaJIT allocator requests | 3,491,135 | 3,161,250 | **−9.45%** |
| GC object births | 901,456 | 858,998 | **−4.71%** |
| Positive allocations, MiB/s | 3.315547 | 2.967338 | **−10.50%** |
| Requests/s | 29,090.762 | 26,343.510 | **−9.45%** |
| GC object births/s | 7,511.609 | 7,158.252 | **−4.70%** |
| Unattributed scope-zero allocated MiB | 196.638250 | 173.570500 | **−11.73%** |
| AutoLoot allocated MiB | 10.469672 | 4.953659 | **−52.69%** |
| AutoLoot requests | 83,469 | 44,447 | **−46.75%** |
| AutoLoot GC births | 14,859 | 13,882 | **−6.58%** |
| f80h_aroused_expressions allocated MiB | 3.849519 | 2.481037 | **−35.55%** |
| f80h requests | 67,307 | 42,012 | **−37.58%** |
| f80h GC births | 30,100 | 21,764 | **−27.69%** |
| Collector execution, ms | 2,954.309 | 3,257.809 | **+10.27%** |
| Completed collector cycles | 2 at 63.515/65.330s | 2 at 47.232/49.182s | **Earlier, not fewer** |
| Largest single recorded collector step | 116.033ms | 149.494ms | **+28.84%** |
| Largest collector frame bucket | 444.153ms | 280.442ms | **−36.86%** |
| Lua heap first / peak / last | 620.921 / 780.730 / 613.668 MiB | 700.604 / 785.506 / 627.081 MiB | Starts +79.683 MiB |
| CapFrameX P99 | 46.046ms | 57.549ms | **+24.98%** |
| CapFrameX worst frame | 478.836ms | 319.466ms | **−33.28%** |
| Frames over 100ms | 7 | 16 | **Worse** |
| Grouped hitch episodes | 59 | 73 | **Worse** |
| Grouped hitch excess | 2,492.216ms | 3,756.244ms | **+50.72%** |
| FPS average | 38.973 | 35.700 | **−8.40%** |

**Critical targeted callback evidence:** `AutoLoot onUpdate` 57,743→19,714 allocator requests (−65.86%), 7.489→2.090 positive MiB, 446.896→51.603ms total exclusive CPU; `f80h onUpdate` 26,326→731 requests (−97.22%), 1.442→0.043 positive MiB and 584.850→24.929ms exclusive CPU. These raw callback CPU totals include changing GC overlap and number of rendered updates (4,678→4,285), so not all improvement is intrinsic Lua work, and **callback CPU is not collector time**. Native attachment/detach f80h still produces most of its remaining own allocation requests: `OnGameAttached` 18,590 and `OnDetach` 22,691, while `onUpdate` is 731. No native/detach work was intentionally disabled by the new source generator.

**Collector timing and experience:** First cycle observed **47.231503s**, second **49.181880s**. All **3,257.809ms** of recorded collector execution and both completed cycles occurred in the **first 60 seconds**; none in 60–120s. In contrast the earlier baseline had 1,377.895ms in minute 1 and 1,576.413ms in minute 2, with both completed cycles at 63–66s. The new 45–50s burst has about 1.56 seconds of collector work (frame-bucket inclusion); largest new frametime spike **319.466ms at ~46.93s**, overlapping large collector activity and `BetterLootMarkers` raw callback. Other >100ms frames occur at ~25.72s, ~47–49s and ~93.47s (last one no recorded GC). Do NOT blame `BetterLootMarkers`, `0-Engine`, `nativeInteractions` or another overlapping callback for creating the collector's garbage; attribution is to shared timing until a specific allocator site is proven. The second half still has **3 frames >100ms** and is **not** wholly hitch-free. Different starting heap (new 700.604MiB vs 620.921MiB) and gameplay mix make early completed cycles **not evidence that allocation reduction failed**. Explicit forced Lua collection calls remain **zero**.

**Remaining named GC object-birth contributors:** `0-Engine` 78,171 (shared infrastructure; do not attribute all to intrinsic scheduler), `GigCore` 42,830, `SpecterM11RecoveryDev` 38,565, `InfiniteGigs` 31,793, `NPC Outfit Manager` 25,727, `ELM_ImmersiveWay` 25,712, `ziggy_last_play` 25,299, f80h 21,764, `ImmersiveFirstPerson` 20,254, `nativeInteractions` 19,616. The **unattributed scope-zero** is larger than any named mod: **173.5705 MiB / 1,413,137 allocator requests / 270,658 births**, and is not a distinct mod or proof of waste. Further prioritization should inspect author source and per-call behavior; no new native-to-Lua intervention authorized by this capture.

**Decision:** AutoLoot/f80h source-GC optimization shows strong target-specific and positive total allocation savings. New 120s run does **not** show improved GC-stall *frequency* or frame-tail distribution. Keep the 19:51 capture as the first reference; compare subsequent 120s against both appropriately, investigate the time-clustered collector phases and next source-owned GC objects. **Gameplay equivalence of proximity/expressions and loot behavior is still user verification**, even after green source CI.
