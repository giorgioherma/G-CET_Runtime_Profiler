# CET approved architecture

CET owns its Profiler, standalone lifecycle/restore, Lua Resolver, native integrations and one canonical 0-Engine Scheduler. GCO consumes the owner outputs; it must not duplicate them. [Product architecture](product/ARCHITECTURE.md), [build pipeline](HANDOFF/BUILD-PIPELINE.md), [engineering contract](resolver/PROJECT-CONTRACT.md), [0-Engine services](0-engine/0-ENGINE-WORKLOAD-SERVICES.md) and [integration contracts](integration/) hold technical details.

## Shared production
- One Scheduler.Demand controller orchestrates service/dependency activation, cadence, deadlines and release, not one scheduler per mod. No-consumer work is unnecessary only if no internal, cleanup, sync-read, historical or measurement dependency requires it. No arbitrary polling or fabricated defaults.
- Share equivalent producer work with explicit ownership, receiver, lifetime, freshness and phase proof. Fastest active consumer governs required cadence, but independent exact phase deadlines use their union. Same frame alone is not a mutable gameplay state validity barrier; distinguish strict synchronous and explicitly compatible coarse consumers.
- Demand planning is available before required host reads, preserving existing clock advancement and callback order. Getter-only consumers use on-demand reads; events activate/invalidate only relevant dependencies. Preserve legacy direct/retained state, source drift fallbacks and correct transition history.
- One native owner/ABI. Only intentionally bundled compatible native capability is emitted. Native compilation and Profiler/Resolver package CI are separate; preserve exact production provenance. Source, fixture, compiled DLL, generated real stack and loaded gameplay evidence are distinct.
- Resolver composes generic 0-Engine, native integration, generic finalization and final semantics on staged live sources. Preserve approved existing behavior, all partial states and source-aware admission. No corrective pass.

[Phase 3 evidence and benchmarks](phase3/PHASE3-ALLOCATION-CONTROLL.md) and [120s protocol](phase3/COMBAT-POLICE-120-BASELINE.md) apply when measuring. Research on dormancy, observer dispatch and historical pass preservation remains available but does not set the current assignment. See [CURRENT-TASK](../CURRENT-TASK.md).
