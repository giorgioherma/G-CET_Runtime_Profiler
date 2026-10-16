# Native 0-Engine source attribution — isolated experiment

**Task (2026-10-16):** Resolve cost *inside* the large 0-Engine Lua callback to actual Lua **source file + function + source-line range** and its **sampled callsite**. This is an instrumentation-only native experiment, **not** Lua-to-C code conversion, ActionRouter modification, a new scheduler, or a claim to reduce 0-Engine's actual work.

**Branch:** `dev/native-zeroengine-source-attribution`, isolated from the main branch's Lua/Resolver development. It starts from the current main-development tree, whose native sources are byte-identical to the last successful native CI #84. The branch has a **separate** Windows native rebuild workflow and uniquely named DLL/PDB artifacts. Never change the main workflow's pinned native run or `MANIFEST.json` without a validated successful native build and explicit integration authorization.

## Why earlier captures had a blind spot

The existing native profiler already supports sampled LuaJIT call/return/line tracing, deep function CPU and own allocation-request attribution, caller→callee edge information, and per-owner Scheduler job timing. However `CETRuntimeProfiler.h::MaybeRebalanceDeepHotset` explicitly had `counter->Mod == "0-Engine"` in its exclusion condition. Thus 0-Engine received aggregate CPU/GC statistics but **no selectable deep source calltree**. More Lua source modifications or GC wrappers are unnecessary for this specific question.

## Implementation

- Reserve **one independent 0-Engine deep profiling callback slot**, selection based on actual moving-window callback cost, while retaining all **six original user-mod hotset slots**. Source-neutral, capture-only, 2 s warmup, low stride and maximum **20 sampled invocations** before normal completion/spike rearm.
- Reuse existing LuaJIT debug hook `DeepTraceScope` and `DeepLuaHook`. Do not attach an additional hook, change Lua execution or ownership, manipulate GC, or alter callback/marshalling semantics.
- New **`CET_Runtime_Profile_ZeroEngine_SourceAttribution.csv`**: per sampled registered callback, real function identity and Lua debug `SourceFile`/`SourceLineStart`/`SourceLineEnd`, sampled **exclusive CPU**, calls, sampled **exclusive allocator bytes/requests**. Rows are sorted by sampled exclusive time. `SourceScope` distinguishes actual `0-Engine/` paths from **external/nested/unresolved** Lua functions; do not assign those to 0-Engine solely because the outer callback was 0-Engine.
- New **`CET_Runtime_Profile_ZeroEngine_Callsites.csv`**: sample sequence, frame, exact Lua caller file/line and parent/child function keys, sampled calls and **child inclusive** CPU. Callsite child-inclusive durations may overlap; do not sum them with own-exclusive source rows, with scheduler child timings or with parent callback total.
- Existing `CET_Runtime_Profile_Deep_*`, `Scheduler_*` and GGC reports remain as-is. They are complementary, not additional CPU or allocator cost categories. A short capture or instrumentation conflict may produce header-only deep source reports; that is missing evidence, **not proof of zero work**. No extrapolated full-capture milliseconds or guaranteed allocation savings are claimed.

## Validation

Source contract rejects reintroduction of 0-Engine exclusion, changing the six user-mod quota, or removing exact file/line/allocator outputs. Native CI compiles against the pinned CET/LuaJIT toolchain, verifies all expected DLL markers, validates PE linker 14.44, and publishes an independently hash-checked native DLL plus paired developer-only PDB. **Native CI success alone cannot establish game stability.** Before main integration, test game start/world load, 0-Engine callbacks, 5–8 minute capture, stop/export and compare normal profiler allocations/GC timing. Check populated output rows actually identify `0-Engine/modules/*.lua`; if `HookConflicts` or `Samples=0`, report failure to attribute rather than ranking them.

## Remaining limitation

This targets CPU and allocator work in Lua functions **executed underneath** 0-Engine's profiled callback. It does not directly separate hidden native CET dispatch/marshalling into arbitrary native classes; that requires separate evidence. It also does not automatically attribute every static 0-Engine function—only those reached by the bounded sampled callback(s). A later design could source-identify previously unmeasured scheduler registrations if needed, without making up per-source timings.
