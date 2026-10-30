# Phase 3 — 0-Engine native producer and consumer attribution (2026-10-30)

**Status:** Native-only implementation committed at `3932166064ce` on `dev/resolver-profiler-handoff`; native CI compilation and gameplay validation are separate gates. Existing proven native CI #95 (`40794412835`, source `bce9ec519be`) remains the historical baseline. Main managed native DLL pin and product version are unchanged.

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
