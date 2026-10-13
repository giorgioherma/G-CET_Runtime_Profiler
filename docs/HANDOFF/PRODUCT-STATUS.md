# G-CET product overview, development progress and open work

**Snapshot 2026-10-14.** This is a **durable orientation**, not a replacement for current code/CI or the chronological [ACTIVE-STATE.md](ACTIVE-STATE.md). Read [CORE.md](CORE.md) first.

**Version cadence:** product version advances only at the user's request or a user-agreed milestone, **not** at each CI/commit. See [CORE.md](CORE.md) and [BUILD-PIPELINE.md](BUILD-PIPELINE.md). v1.2.7 remains the current version until such a decision.

## Product mission

One native/CET **Profiler** measures actual Cyberpunk 2077 modded-game work; one **Resolver** analyzes captures and the current mod sources and generates conservative, composable optimizations. The goal is real responsiveness and fewer unnecessary computations/allocations—not just lowering profiler counters. The profiler never arbitrarily rewrites gameplay; the Resolver produces a reviewable overlay ZIP instead of modifying the live mod installation.

## App/system components

| Component | Responsibilities | Status / limits |
| --- | --- | --- |
| Native CET profiler DLL | Callback inclusive/exclusive timings, rates, per-frame multiplicity, timeline/spikes, native G-GC object/allocator/collector instrumentation, deep Lua functions/edges/callsites, optional in-situ scalar read/change probe | **Compiled and hash-integrated** in v1.2.7; scalar probing must be called by instrumented Lua source. New native gameplay stability and complete parity not yet verified. |
| `G.CETProfiler.Core` | Portable app shared backend: game/CET discovery, manifest verification, transactional install/restore/rollback, 0-Engine Scheduler integration, capture collection/archive, report analysis, Resolver inputs | Implemented with broad tests; preserve existing byte hashes, bindings and ownership invariants. |
| `G.CETProfiler.App` | .NET 8 WinForms UI and JSON CLI on same backend; launcher; capture controls, optional CapFrameX pairing and HTML report generation | Working packaged app; test on user's real game stack after new native. |
| `G.CETResolver.App` | Standalone UI/CLI; consume capture + **read-only live source**; classify safe generic and measured semantic rules; compose and output a ZIP | Built and CI-tested; generated passes require in-game feature validation. |
| `resolver-knowledge/semantic-library.json` and `SemanticInjectors.cs` | Production source-proven semantic rules, version-shape matching, idempotence, optional staged 0-Engine integration | 63 production rules at this snapshot (**verify current catalog**). Not every rule has exhaustive in-game testing. |
| 0-Engine integration | Compatible Scheduler, opt-in WorkQueue/PhasePlanner/FrameListeners/StateSignals, existing state/partial installation detection | Infrastructure, **not** automatic permission to throttle every mod. |

## What is already accomplished

- Portable release layout, own `RESULTS`, native launcher, self-contained managed apps and safe installer/restore transaction; supports headless CLI.
- Real gameplay profiling: callback CPU and frequency, spikes, Scheduler and burst pressure, optional paired frametimes; native G-GC allocations and deep function/registration/callsite provenance.
- Compact HTML and machine reports: `CET_Report.html`, `CET_Summary.json`, `CET_Resolver_Input.json`, plus underlying raw CSVs. Managed bounded deep-reuse candidate section is automatic; new scalar equality section is optional.
- Resolver stages **generic first, semantic second**, recognizes already-optimized and partial current source, composes same-mod changes, emits ZIP rather than touching live mods. Semantic rules target costly off/near/far, quest lifecycle, UI/input and author-specific behaviors with source patterns rather than version IDs.
- User observed **over 50% reduction in measured CET workload on many tested mod stacks**; this is stack-specific historical evidence, not a claim of universal or new-native savings.
- NPC Outfit Manager: source-proven `npc-outfit-manager-idle-state` rule added (idle radar suppression without active automatic outfits; inactive target polling suppression; in-memory last-edited mutation revision/reference mirror). Variant/idempotence/untouched-source CI passed; real in-game pass/GC delta still unmeasured.
- New native scalar probe `CETProfilerObserveScalar(label, stringValue)` and optional `CET_Runtime_Profile_Scalar_Observations.csv`; managed ingestion and Resolver report field `scalarChangeObservations` never independently authorize rewriting. Native build compiles, Profiler v1.2.7 packages correct DLL hash.
- Build architecture now **separate**: native workflow builds only DLL; main workflow validates native import and packages both apps. [BUILD-PIPELINE.md](BUILD-PIPELINE.md).

## Current research / not completed

1. **Game validation of v1.2.7 native DLL**: world load before/after capture; pause/stop; existing G-GC/deep data completeness; identical-save comparisons; no profiler-specific crash, user-defined restore and acceptable overhead.
2. **Actual scalar observation sources**: native API exists, but no real selected getter callsites have been opted in yet. A zero/absent CSV does not prove values are stable.
3. **NPC Outfit Manager**: real generated pass on user's live original mod, editor closed vs active, automatic rules activated while live, deferred `Cron.After`, attachments/detach, save/load, outfit look restore, both performance and GC comparisons. The current cache is **not a full persistent working outfit autosave**.
4. **Better Loot Markers + AutoLoot**: candidate cross-mod **immutable marker display summary** with per-entity inventory revision/invalidation; transfer and protected-item logic must still read fresh inventory. Not implemented as a shared provider; don't claim a safe cache yet.
5. **0-Engine + ImmersiveFirstPerson** cross-mod scalar snapshot remains less certain, behind the first two mod targets. Don't recommend redundant blackboard cache blindly—0-Engine already batches state.
6. **Native G-GC provenance**: bounded sampling, legacy owner-0 gaps and exact producer/caller attribution remain research; avoid confusing `__index` and native getter calls with Lua table creation.
7. **Version-fluid rule durability**: expand real author-version fixtures. Fail closed when source structure diverges; no blanket guarantee across future mod updates.

## Deep historical records

- [ACTIVE-STATE.md](ACTIVE-STATE.md) — timeline of captures, decisions, CI and code changes (large; **older snapshots are historical**).
- [GC-RESEARCH.md](GC-RESEARCH.md) and [DATA-PROVENANCE-SPEC.md](DATA-PROVENANCE-SPEC.md) — GC strategy and locked requirements.
- [AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md](AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md) — sampled deep producer/caller data, exact NPC/AutoLoot/BetterLootMarkers source audits and safe cache boundaries.
- [CET-CALLBACK-AUDIT-2026-10-13.md](CET-CALLBACK-AUDIT-2026-10-13.md), [LIVE-CET-DATA-PRODUCTION-AUDIT-2026-10-13.md](LIVE-CET-DATA-PRODUCTION-AUDIT-2026-10-13.md) and [DEEP-CALLSITE-ATTRIBUTION-2026-10-13.md](DEEP-CALLSITE-ATTRIBUTION-2026-10-13.md) — source/capture measurements and attribution.
- [PROJECT-CONTRACT.md](PROJECT-CONTRACT.md) and [../ARCHITECTURE.md](../ARCHITECTURE.md) — architecture/invariants.

## What changes which document

- **Current build/product, cross-chat operating rules:** update `CORE.md` and `PRODUCT-STATUS.md` only as needed.
- **Build pipeline, pins, release artifacts:** update `BUILD-PIPELINE.md` (and `BUILDING.md` if needed) and current snapshot in `CORE.md`.
- **Per-capture hypothesis, source finding or deep experiments:** update the subject-specific deep research document. Append only the significant decision/result to `ACTIVE-STATE.md`.
- Do not inflate the short startup file with raw profiler tables or repeat historical CI runs as the current run.
