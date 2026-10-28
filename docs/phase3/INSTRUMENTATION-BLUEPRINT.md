# PHASE 3 ALLOCATION CONTROLL — instrumentation blueprint and execution ledger

**2026-10-28.** Successor to Phase 2 **DORMANCY EXTENDED**. Active branch `dev/resolver-profiler-handoff`. First instrumentation slice source commit `7259194`; managed CI #2134 and native rebuild #94 passed. **Following canonical scheduler fix #2142 and package consolidation #2143, a real 60.017-second capture now verifies 20/23 executing jobs, nonzero per-job allocations, accounting reconciliation and zero dropped scopes; see [first validated measurement](COMBAT-POLICE-60-BASELINE.md#first-validated-scheduler-measurement--2026-10-28-at-1913-cet).** Exact live ASI hash remains unverified. No mod wake or optimization is authorized by telemetry alone. The user makes the human decision whether a mod can safely sleep.

## User-approved reproducible gameplay stress baseline

**`COMBAT-POLICE-60`** is the canonical Phase 3 stress benchmark: one **60-second sustained police fight at the user's apartment area**, **F10 starts both CET and CapFrameX** with independent 60-second auto-stop/export, **F11 remains unlimited manual**, same save, position, wanted level, loadout, mod stack, settings and FPS cap. It replaces the four-minute mixed idle/drive/combat proposal for aggregate per-job comparisons. **First valid scheduler-allocation capture received 2026-10-28**; exact apartment/save details are not established. Read [the complete benchmark protocol](COMBAT-POLICE-60-BASELINE.md) before interpreting or planning A/B. No obligation to split into three profiles.

## Experimental RC far-state gating, not quest disable

The primary pristine-source rule for `repeatable_cyberpsychos` now reuses already generated compiled native boss-site far/near notifications. All 17 sites explicitly far => pause the two alternating frame-job handles between sweeps, keep cheap native 1Hz RuntimeTick and scheduler semantics, and run a full Lua safety sweep every 30s. Near/pending reward or global replay-count transition wakes the normal 17-site worker. Presentation replaces pointless 5s distant full walks with aggregate-count fingerprint and a 30s fallback. Missing native location evidence or invalid scheduler pause handles keeps the old behavior. RC is *not* fully event-only; this is bounded work reduction preserving lifecycle.

RICA remains unchanged until reliable body/completion reward/cooldown wake is proven. Its 167ms state job is a single outlier, not proof of a 167ms periodic tick. New runtime tests and A/B captures are required before claiming savings.

## Single canonical Profiler scheduler asset

Package exactly one profiler-aware Scheduler under `payload/0-Engine/modules/G-CET/Scheduler.lua`; do not ship redundant `payload/0-Engine/modules/Scheduler.lua` or `payload/0-Engine/modules/CETProfilerScheduler.lua`. Keep distinct **live runtime paths**, not duplicate packaged byte sources: existing G-CET namespaced installs are adopted untouched; a legacy integrated author Scheduler still receives a temporary hash-verified replacement to the author-owned root path and is restored; a legacy adaptive install deploys the same byte source as its temporary `modules/CETProfilerScheduler.lua`. CI verifies canonical checksum, excludes old payload duplicates, and exercises all three install/restore branches. The temporary Resolver root alias for recognized fixed runtimes remains a separate compatibility issue until all clients can use the namespace directly.

## Canonical scheduler fix and next capture

**User-authored architectural correction (2026-10-28):** Resolve the prior 23 jobs/zero executions by measuring the actual source-owned namespaced scheduler. Resolver generates `modules/G-CET/Scheduler.lua` and, only for source-proven G-CET-owned fixed runtimes, a **persistent** root `modules/Scheduler.lua` alias returning the same cached namespaced module (not duplicated implementation). Unrecognized foreign hosts retain their original root module; their own namespaced G-CET integration must remain non-destructive. The Profiler installer detects that source-proven existing integration and does not add or tick `modules/CETProfilerScheduler.lua`. The actual canonical file, alias, and 0-Engine host remain byte-identical through profiler install/restore, retaining Resolver dependencies. Reversible native ASI and ProfilerControls transaction still works. Non-namespaced originals still use the old adaptive/integrated flow. Tests cover the alias and managed lifecycle. **Source change is not gameplay validation**: new 60-second paired F10 test must prove nonzero job calls when jobs are active, meaningful allocation operations/bytes and exact archive/restore.

## First uploaded ~60s capture: scheduler coverage unresolved

User-provided `CET-20261028-135900_NEW_INSTRUMENTATION - STANDARD 60s capture BASELINE.zip`: native capture duration 60.945835 s, 966,622 CET callback invocations, CapFrameX correlated; Phase 3 CSV exists. **Scheduler_ByJob: 23 registrations, all 0 executions. FrameworkAllocation: 1,928 measured frames but all 0 bytes/operations.** No dropped spans. These zeros are not acceptable evidence that 0-Engine or mods allocate nothing. Managed report now marks `UNVERIFIED_ZERO_JOB_INVOCATIONS_WITH_FRAMES` when registered jobs have zero invocations amid scheduler frames. Establish whether jobs were actually paused/never dispatched, or whether the bridge is failing, before using per-job allocation measurements. Existing G-GC and CapFrameX evidence remains useful.

**Timed capture control:** new `CETProfiler_Police60` keybind auto-starts, tags COMBAT, stops/dumps at first `onUpdate` reaching native captured 60 seconds, and uses no additional event hook. F11 remains manual. Exact 60.000 s termination cannot be guaranteed below one rendered update; record the actual elapsed seconds.

## Work backwards from decisions

For each of the 35 semantic owners, capture **actual work**, not an inferred dormancy eligibility score:

| Decision input | Evidence | Provenance |
|---|---|---|
| Which callback and scheduler job still executes? | registration ID, mod, source, job ID, calls/s and CPU | existing CET callback and Scheduler instrumentation |
| Which job allocates repeatedly? | **own** positive LuaJIT ops and bytes per invocation/s, plus inclusive nested envelope | new native Scheduler Begin/End counters; job ownership is a measured execution envelope |
| What costs belong to shared 0-Engine? | framework sampled source + scheduler frame total, outermost job inclusive total, outside-root-job residual | existing bounded `ZeroEngine_SourceAttribution` plus new framework CSV; **residual is not by itself proven intrinsic 0-Engine** |
| Why did a job not sleep? | original-source evaluated wake guards, pending cleanup, pause/resume intent and physical subscriber attachment | planned capture-only transition ledger; emit from existing decision sites, never invent new polling |
| Which native ingress continues before Lua guards? | method/class hash, calls, pre-dispatch ops/bytes, actual listener fanout if measured | existing bounded native boundary evidence; targeted extension only when still ambiguous |
| What are actual allocation producers? | bounded source function/callsite samples and GGC object/string births | already implemented; do not extrapolate sample totals or confuse bytes with retained heap |

**No automatic dormant/not-dormant conclusion.** The report may show "unmeasured" or "unknown"; it must not claim callbacks are safe to remove. Preserve native fallback, author input, scene/quest ordering, pending cleanup and session lifecycle.

## Execution order and deliverables

### Slice A — scheduler per-job allocation + 0-Engine scheduler residual (SOURCE IMPLEMENTED; TESTS PENDING)

Reuse existing native `CETProfilerSchedulerJobBegin` / `CETProfilerSchedulerJobEnd`, fixed thread-local counters, positive LuaJIT allocator snapshots and nesting. Add to the existing `Scheduler_ByJob.csv` **appended columns**: inclusive bytes/ops, child-subtracted exclusive bytes/ops and per-job rates. No change to existing CSV column meanings.

New `CET_Runtime_Profile_Scheduler_FrameworkAllocation.csv`: exact Scheduler.Update frame envelope bytes/ops, **outermost measured job spans**, the outside-root-job remainder, and dropped/unpaired scope count. This is *not* the entire 0-Engine cost and the remainder includes profiler bridge work. It is never summed with callback GGC totals.

Report both streams in HTML, `CET_Summary.json` and `CET_Resolver_Input.json`, with legacy-not-measured behavior. Distinguish inclusive and exclusive jobs and detect frame/root reconciliation failures. Preserve existing source attribution, CPU, GC, native and CapFrameX reports. Add an independent synthetic report contract for nested jobs, wrong totals and legacy CSV.

**Validation not yet done**: native source compile, unchanged stable/pinned DLL, isolated experimental artifact, same-save start/world load *before* capture, stop/pause/resume/reload, probe enabled/disabled overhead, file archive + restore, 120 s idle and 90+90 s active/recovery captures. A green managed CI cannot prove native source compiles or world loads.

### Slice B — true 0-Engine internal breakdown (PENDING)

Expand the existing limited Scheduler.Update residual and bounded `ZeroEngine_SourceAttribution` with **source-level boundaries**, only where needed: frame emitter, Scheduler scan/dispatch, ActionRouter filter and fanout, FrameListeners/StateSignals, NativeLocationHost, and shared-provider/cache work. Preserve nested attribution and method marshalling as a separate ingress layer; do not double count children or claim the shared host should disappear. Stage B has not been implemented by Slice A.

### Slice C — dormancy transition evidence (PENDING)

Attach capture-only transition events to *existing* pausable frame handles and source-owned semantic wake/guard decisions:
`REGISTERED`, `SIGNAL_RECEIVED`, `GATE_EVALUATED`, `ACTIVE`, `PAUSE_REQUESTED`, `DETACH_CONFIRMED`, `RESUME_REQUESTED`, `ATTACH_CONFIRMED`, `CLEANUP_PENDING`, `FALLBACK_ACTIVE`, `CANCELLED`.
Record owner ID, handle/job ID, timestamp/frame and bounded numeric reason/state fields **only on transitions** plus one startup snapshot. Do not poll to record evidence. Use Dedra's already established bridge/linked/perspective/cleanup flags first. Then sitAnywhere, CyberTrials, StreetGamesPoker, repeatable_cyberpsychos, repeatable_increased_criminal_activity. A Lua no-op or early return is not physical detachment.

### Slice D — selective native dispatch context (PENDING/CONDITIONAL)

Use existing native pre-Lua marshalling evidence first. If it still cannot distinguish a high-frequency ingress, target the proven method/class and subscriber chain; never apply a blanket native unsubscribe or restructure CET hooks. This is conditional, not a prerequisite for A–C.

## Testing and release boundaries

- Profiler installs only its own temporary probes and restores the **exact pre-profile install**, retaining all Resolver output mods and original gameplay files. File backup/restore, conflict validation and emergency restore are part of the existing Manager transaction. No permanent probe in generated optimization output.
- **Mods' allocation/GC behavior is observed and never changed by diagnostics.** No GC policy change, caching or source rewrite in these instrumentation slices.
- Reuse existing GGC native counter streams; experimental additions must not allocate Lua tables/strings per observation. Do not start a separate "profiler heap" project without measurements showing a problem.
- Same-save capture A/B with exact DLL hash and world-load test; normal capture, paused/resumed, no-0-Engine and partial-old-runtime paths; verify no startup crashes and previous metrics unchanged.
- All telemetry additive; `NOT_MEASURED` for legacy native 2.15 and incomplete files. Guard reconciliation and drops. No speculative per-mod native-marshalling ownership, retained heap claims or savings.
- **Native build gate:** compile experimental code via native-only pipeline, validate isolated crash-free gameplay first; don't replace pinned stable native v2.15 or bump product v1.3.3 without explicit approval. Main CI compiles managed report and tests fixture, but cannot stand in for native runtime validation.
- Current Phase 1+2 preservation recovery remains open. Any Resolver source mutation must target authoritative original-source generators, not install a corrective output patch.

## Current execution checkpoint

- Added native scope/counter source and additional CSV fields; added native scheduler frame-envelope CSV.
- Added managed report reader for the new fields and HTML/Summary/Resolver JSON output.
- Added synthetic contract for nested inclusive/exclusive, frame accounting drift, and legacy missing telemetry.
- **Completed:** managed CI #2134 (6/6) and experimental native rebuild #94. **Pending:** game boot/capture/restore validation with the actual live binary, first `COMBAT-POLICE-60` capture, true 0-Engine source-phase ownership and wake transition ledger. No mod optimization or product release claimed.
