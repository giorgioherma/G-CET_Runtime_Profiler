# G-CET development handoff — start here

**Every new development chat: read [CORE.md](CORE.md) FIRST.** It contains the current project identity, one-minute product overview/progress, **mandatory two-workflow CI contract**, the latest verified package/native SHA and immediate open items. Recheck GitHub HEAD/manifest/Actions for changes since that snapshot.

## Layered reading order

1. **[CORE.md](CORE.md)** — the **short primary startup context**; mandatory.
2. **[BUILD-PIPELINE.md](BUILD-PIPELINE.md)** — native builds **DLL only**; main build packages **Profiler and Resolver**. Read before any build/native/package work.
3. **[PRODUCT-STATUS.md](PRODUCT-STATUS.md)** — architecture, completed functions, what remains, practical roadmap.
4. **[PROJECT-CONTRACT.md](PROJECT-CONTRACT.md)** — non-negotiable Resolver rules, source proof, behavior, risk/payback and composition.
5. **[ACTIVE-STATE.md](ACTIVE-STATE.md)** — **historical chronological log**, substantial and sometimes describing superseded versions. Read latest sections for relevant task; do not treat older “current” values as authoritative.
6. **Deep task-specific docs**, only when relevant:
   - [DATA-PROVENANCE-SPEC.md](DATA-PROVENANCE-SPEC.md) — locked new native/managed data-producer objective.
   - [GC-RESEARCH.md](GC-RESEARCH.md) — GC evidence and research priorities.
   - [AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md](AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md) — current sampled data reuse and mod-source findings.
   - [DEEP-CALLSITE-ATTRIBUTION-2026-10-13.md](DEEP-CALLSITE-ATTRIBUTION-2026-10-13.md), [LIVE-CET-DATA-PRODUCTION-AUDIT-2026-10-13.md](LIVE-CET-DATA-PRODUCTION-AUDIT-2026-10-13.md), [CET-CALLBACK-AUDIT-2026-10-13.md](CET-CALLBACK-AUDIT-2026-10-13.md), [GC-PROVENANCE-NOTATION](GC-RESEARCH.md) and the corresponding topic report.

[**NEXT-CHAT.md**](NEXT-CHAT.md) is a copyable prompt pointing to the CORE; it deliberately does **not** freeze an old build/version.

## Maintenance discipline

**The core is current; the deep documents retain historical evidence.** After a meaningful change, update the compact core snapshot and corresponding detailed file in the **same development task**. The default is to verify branch HEAD, both Actions workflows and relevant code/captures; do not make the new chat rely on a previous assistant's prose. Report live game tests distinctly from CI.

Code/system references: [Architecture](../ARCHITECTURE.md), [native build provenance](../NATIVE_PROFILER_BUILD_PROVENANCE.md), [0-Engine workload services](../0-ENGINE-WORKLOAD-SERVICES.md), [G-GC Tracer](../G-GC-TRACER.md), [build guide](../../BUILDING.md), [production semantic catalog](../../resolver-knowledge/semantic-library.json).
