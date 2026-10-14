# Third native G-GC decompression capture — 2026-10-14

**Evidence:** user capture `CET-20261014-152417_THIRD_NATIVE_FOR_GC_DECOMPRESS.zip`; archived duration **238.248960s**, START at 0.000ms and one PAUSE at 238248.9604ms. New multi-axis native instrumentation `2.15.2-unscoped-native-callpaths`, packaged in Profiler/Resolver **v1.2.7**; native DLL SHA-256 `3efd05f3eb76195dff48e4e5b9a46a5380d4acf8623c6c5cadd15010033a4d52`. This is an **analysis-only** record: no installed mod, native code, GC policy or Resolver pass changed.

## Verification

- Raw `CET_Runtime_Profile_GGC_UnscopedShapes.csv`, `UnscopedBoundaries.csv`, `UnscopedPathTimeline.csv`, `UnscopedNativeSamples.csv`, preceding `UnscopedContexts.csv` and `UnscopedTimeline.csv` are all present. Both `CET_Summary.json` and `CET_Resolver_Input.json` report `HasVmTimeSeries=true`, `HasShapes=true`, `HasNativeSamples=true`, `VmReconciled=true`, `ExtendedReconciled=true`.
- Every independent owner-zero dimension reconciles to **258,546,077 positive bytes** (**246.568753 MiB**) and **1,654,279 positive allocation requests**. This is **36.55%** of 674.536661 MiB total captured positive growth; 31.10% of 5,319,126 total operations. Positive allocator growth is **not** retained heap, actual Lua garbage, or avoidable data.
- Exactly **1,615 fixed-stride native samples**, no drops, matching `floor(1,654,279 / 1024)`; all recorded sample stacks have 5–6 frames. Sampling was not exhausted; sample-associated bytes may **not** be projected to whole population.
- CapFrameX synchronized **GOOD**, 13,225 frames / 237.845s versus CET's 238.249s, mean 55.603 FPS, median frametime 17.1125ms, 12 measured frames above 100ms; highest 214.9075ms. No explicit scenario markers. Game input/menu safety **after** the stop cannot be verified from the archive.

## Owner-zero positive growth: morphology and context

| Dimension | Observed bytes | Operations | Interpretation |
| --- | ---: | ---: | --- |
| `RESIZE_GROWTH` | 257,013,325 (99.407%) | 1,630,386 | `lj_mem_realloc` accounting pathway; includes **oldSize=0** initial allocations. **Not proof of actual resizing**. |
| `NEW_GCO` | 1,532,752 (0.593%) | 23,893 | Explicit `lj_mem_newgco`-path object creation, not all conceivable allocator activity |
| `RESIZE_GROWTH / 64_255B` | 256,862,289 (99.349% of all owner-zero bytes) | 1,627,633 (98.389% of owner-zero operations) | Positive delta bucket: mean ~157.81B/operation; does not reveal old/new absolute sizes |
| `VM_C` | 258,294,061 (99.903%) | 1,651,696 (99.844%) | Execution state only, not C module/caller or Lua owner |
| `OUTSIDE_CET_INSTRUMENTED_BOUNDARY` | 258,546,077 (100%) | 1,654,279 | Not within the two installed bridge scopes **as instrumented**, not proof of work outside CET |

**Important correction to naïve morphology interpretation:** `Patch-LuaJIT-GC-Source.ps1` inserts `gcet_gc_alloc_observe(g, osz, nsz, 0)` in the LuaJIT realloc-accounting path. The `RESIZE_GROWTH` label includes `osz=0`, `nsz>0`. Therefore the 99.407% category cannot be called 99% repeated buffer expansions; it may include large numbers of newly allocated small blocks. A follow-up must separate `OLD_SIZE_ZERO` from `OLD_SIZE_NONZERO`, and record distributions of both positive delta and (bounded) absolute new size.

**Boundary coverage:** the currently instrumented `CET_FRAME_UPDATE` scope wraps `Scripting::TriggerOnUpdate`; `CET_OVERLAY_DUMP` is a different narrow host path. `OUTSIDE` alone does not distinguish other CET/game host entrypoints, callbacks, maintenance or coroutine/JIT behavior. Since all zero-scope allocations are outside these scopes, their absence inside the frame-update wrapper does **not** absolve CET mods or prove ownership by CET itself.

## Timelines by planned gameplay segments

The user planned idle 0–60s, running 60–150s, combat/police thereafter. **Phase boundaries are approximate**, because the ZIP contains no tagged scenario markers.

| Planned phase | Time | Owner-zero positive MiB/s | Owner-zero ops/s | Owner-zero ops/rendered frame | Frame rate | Complete automatic GC cycles |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Idle | 0–60s | 0.677 | 4,350 | 74.6 | 58.3 FPS | 0 |
| Running | 60–150s | 1.064 | 7,094 | 120.6 | 58.8 FPS | 2 |
| Combat | 150–238.249s | 1.248 | 8,553 | 170.2 | ~50.5 FPS | 2 |

Positive owner-zero byte activity rises with gameplay even **per rendered frame**, not merely because more frames were rendered. Temporal co-variation does not assign mod ownership or demonstrate how many operations can be removed. The preceding ~239.85s second-instrumentation capture recorded ~242.17 MiB unscoped positive bytes (~1.010 MiB/s); this one ~1.035 MiB/s, a ~2.5% difference in aggregate rates, **not controlled A/B**. Both show VM_C dominance.

## Bounded native call stacks

All sampled operations are tagged VM_C and `OUTSIDE_CET_INSTRUMENTED_BOUNDARY`. These tags apply to **the sampled subset** and should not be equated to all operations. All common first two frames are LuaJIT/CET-module RVAs; `cyber_engine_tweaks.asi` stack frames are not proof that the code was initiated by CET itself.

The following four **third-stack-frame** groups contain **1,563 of 1,615 sampled events (96.78%)**:

| CET module RVA | Sampled events | Notes |
| --- | ---: | --- |
| `+0x8927a` | 627 | Recurs in each activity window, relatively less dominant in combat |
| `+0x11c30e` | 385 | Multiple following native/game/JIT continuation paths, relatively more common in combat |
| `+0xfd016` | 288 | Two principal continuation families, relatively increases during combat |
| `+0x93f33` | 263 | Another stable CET-module stack family |

There is no validated source-level symbol/function map for these RVAs in this archive. A small number of stacks end in `UNKNOWN_OR_JIT_CODE`; that is not a resolved Lua line. Samples deliberately skip 1023/1024 operations. Their relative distribution is useful for prioritizing the *exact native build's* symbolication, not for calculating per-family MiB or exact producer percentages.

## GC and prior source provenance

- Automatic LuaJIT collector: **4 completed incremental cycles**, **489,055 incremental calls**, **6,655.804ms** measured internal collector time, no explicit/full GC calls in this capture. Collector wall time is **nested**, not additive to total callback CPU.
- Largest individually captured indivisible steps: **192.569ms** at ~188.502s (observed in `illegal_mechanic_phase1a` callback context), **182.714ms** at ~77.387s (`0-Engine`), **169.217ms** at ~218.939s (owner-zero trigger context), **163.759ms** at ~109.132s (owner-zero trigger context). Every one is `atomic → sweepstring` at its start. These are **collector trigger contexts, not allocation producer attributions**.
- Phase times: `sweepstring` ~2,036.522ms, `finalize` ~1,711.011ms, `sweep` ~822.450ms, `atomic` ~708.259ms, `propagate` ~540.755ms (subset of collector timing, not additive to callback time).
- Scoped G-GC top positive allocation buckets: `0-Engine` 136.305 MiB / 1,196,506 ops (routed work not necessarily framework-produced); `ImmersiveFirstPerson` 59.599 MiB; `GigCore` 51.399 MiB; `yaibaPenthouse` 25.524 MiB. These are **not** labels for any of the unscoped bytes.
- Deep sampler: **34,066/34,066 callsites matched**, 3,529 function rows, no matched-caller ambiguity; source-sampled allocations still do **not** establish owner-zero Lua callsites. Opt-in scalar observation status: `NOT_CAPTURED`; no real value stability/invalidation evidence. Prior source-checked investigations (e.g. NPC Outfit Manager, licksclubjukebox liveness, BetterLootMarkers/AutoLoot inventory overlap) remain separate, not proven owner-zero causes.

## Engineering decision and next work

**No new source optimization or GC-policy change is authorized by this capture alone.** Repeated native stacks and small growth requests are promising *investigation targets*, not evidence of redundant or safely cacheable Lua data.

1. **Try offline symbolication first**, using debug symbols / exact linker map matching native SHA `3efd05f3...` for the four dominating CET-module RVA families. Never borrow a map from a non-identical DLL to name these frames. If symbols cannot be recovered, record that gap explicitly.
2. In **one bounded additive instrumentation update**, distinguish old-size-zero small mallocs from true old-size-nonzero positive resizes; retain 6 existing delta tiers and all legacy counters. Extend host-boundary context to selected high-frequency CET ingress paths (outside `TriggerOnUpdate`), distinguishing host scope from mod callback ownership; do not add Lua getters/hooks or any GC policy.
3. Only when a native family maps to a tractable host/service path or to existing source-located deep trace should a **selected opt-in producer scalar/change-rate probe** or a source-proven redundant-materialization elimination be considered. Check receivers, inputs, return freshness, mutation/aliasing and lifecycle before any shared cache.
4. Repeat gameplay load-before-capture, idle/run/combat without menus, post-stop menus and sleep/dialogue UI, restore, profiler overhead and the full reconciliation suite. This ZIP cannot certify post-stop input/menu health.
5. Retain Profiler and Resolver **v1.2.7**. Do not edit installed mods, make speculative Resolver passes or bump product version as part of this report.

See [multi-axis instrumentation contract](../research/UNSCOPED-MULTI-AXIS-ATTRIBUTION-2026-10-14.md), [GC research](../GC-RESEARCH.md) and [development contract](../../HANDOFF/CORE.md).
