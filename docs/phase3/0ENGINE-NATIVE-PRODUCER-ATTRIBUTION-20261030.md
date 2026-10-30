# Phase 3 — 0-Engine native producer and consumer attribution (2026-10-30)

**Status:** Native-only instrumentation commit `3932166064ce` [native CI #40995476421](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40995476421) **GREEN**, DLL SHA-256 `1f8644e885b95dc06f348173325b367bd52e7dd638949e6f333e49531b32b9ee`, matching PDB separately published. **User-requested native bump** `2.15.9-native-method-stages` → `2.15.10-zeroengine-producer-attribution`; main Profiler/Resolver build now pins this DLL by CI run and checksum, while shared package version remains `1.3.4`. The old native CI #95 is the previous compilation baseline, not the imported DLL. Main package CI and gameplay capture remain separate acceptance gates.

## Evidence and reason for the extension

User-provided `CET-20261030-031720_ITP-DORMANCY_FINISHED_G_STACK(2).zip` measured 4,633 scheduler frames with 110,068 positive allocator requests / 12,878,279 bytes inside the Scheduler frame envelope. Its root-job spans account for 84,322 requests / 7,108,683 bytes, while outside-root residual is 25,746 / 5,769,596 (zero dropped scopes). **Residual is not yet proven intrinsic 0-Engine work.** The supplied integrated `0-Engine.zip` includes the namespaced canonical Scheduler and modules. Existing ZeroEngine SourceAttribution and Callsites files are bounded samples; several source entries resolve only to `=[C]` or bare `init.lua`, which are not valid mod-owner proof.

## Implemented native-only experimental slice

The existing native `CETRuntimeProfiler.h` deep-trace return accounting now records sampled **child-exclusive** positive LuaJIT bytes and operations, plus the actual child Lua source file and function name. It reuses the existing hook and existing sampled callsite structures; no new callbacks, game hooks, Lua allocations, mod source edits, scheduler changes or GC collection controls were added.

New raw CSV outputs, emitted at capture dump only:

- `CET_Runtime_Profile_ZeroEngine_ProducerAttribution.csv` sorts sampled exclusive function allocation requests/bytes first and reports `ProducerOwner`, `FrameworkService`, source file/function/line, calls, sampled exclusive cost and proof classification. Owner comes from installed `mods/<name>/` source paths, not from the outer 0-Engine registration.
- `CET_Runtime_Profile_ZeroEngine_ConsumerCallsites.csv` reports sampled caller and child source/function, caller service/owner when proven, child source-owner, child-exclusive positive allocations and child-inclusive duration; native `=[C]`, relative ambiguous Lua modules and unresolved paths remain `UNRESOLVED`.

The existing `ZeroEngine_SourceAttribution.csv`, `ZeroEngine_Callsites.csv`, user-mod hotset slots, canonical scheduler and native method-stage CSV are preserved. The native CI workflow requires **both new file markers to be present in the compiled DLL**, not merely in the source. Tests enforce native-only source and non-additive sampled semantics.

## Critical limitations and gates

These are **bounded sampled** function/callsite exclusive allocation operations, NOT exact per-mod full-run rates or retained heaps. Native pre-Lua dispatch may allocate before Lua source frames and remains distinct owner-zero evidence. Parent inclusive and child exclusive allocation sources must never be added together. The presence of a mod-owned source or costly sample alone does not prove wasted work or safe dormancy.

Native CI requires pinned historical CET toolchain compilation, header/source contract, compiled DLL marker, SHA and matching PDB. Then test actual game world load, F10 COMBAT-POLICE-120 producer/consumer CSV population, conflicts/dropped samples, six user hotset slots, unaffected scheduler-job and frame reconciliations, and capture OFF/ON overhead before promoting an experimental DLL.

**Remaining Slice B work:** exact, full-population preparation/fanout/service-phase counters (for scheduler context construction, Health bookkeeping, event fanout, spatial/blackboard services) and their source-proven requesting mod. Native bounded deep samples alone cannot assign entire outside-root scheduler residual to a producer. Slice C wake-transition tracing and native class/subscriber attribution remain independent.

## Native browser-warning investigation and independent DLL resource identity (2026-10-30)

**Scope: native-only; not the user-facing v1.3.4 Profiler/Resolver packages.** The user's security warning applies to the compiled native artifact from native CI #96. A native identity change in MANIFEST.json did **not** change the Windows VERSIONINFO bytes of that instrumented CET DLL: the pinned CET 1.37.1 Xmake resource template was still authoritative.

The original CET 1.37.1 control build must remain unchanged. Experimental profiler-only build now uses the **existing native source patch** to give both the Xmake resource template and compiled generated resource a G-CET identity: **PE FileVersion 2.16.1.0, ProductVersion 2.16.1**, description **G-CET Native Runtime Profiler (CET 1.37.1)**. Embedded CET API and C++ version macros stay 1.37.1. Native 2.16.0 is reserved for the separate MEGAMIND provenance feature; this is version 2.16.1, not a claim to implement that feature.

Native CI verifies the actual PE version resource after linking, separately from the SHA256 and compiled attribution CSV markers; it publishes NATIVE_VERSION.txt next to the experimental DLL and checksum. Native code commits: 21b20b0fa23a plus 7f9455a619b2 (source-aware original git-tag ProductVersion handling). **The main package's pinned DLL/manifest native identity and the Profiler/Resolver v1.3.4 versions are unchanged** until separately authorized and tested.

**Security status:** a browser flag is not proven to be a false positive. New VERSIONINFO and new hash do not establish antivirus acceptance or software safety; unsigned native instrumentation can still trigger heuristic detection. Diagnose the exact browser/security-product detection and test the built DLL before any promotion. Avoid bypassing warnings or weakening protections.
