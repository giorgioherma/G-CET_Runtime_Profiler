<!-- PHASE3_ALLOCATION_CONTROLL_20261028 -->
**Active research:** [PHASE 3 ALLOCATION CONTROLL](phase3/PHASE3-ALLOCATION-CONTROLL.md), successor to **Phase 2 DORMANCY EXTENDED**, with a [115-mod 2026-10-28 generated-pass dormancy audit](phase3/RESOLVER-PASS-20261028-MOD-AUDIT.md). Covers wake-trigger maps, inactive allocation frequency/size, native CET marshalling and reproducible tests.


# G-CET technical documentation

**Active cross-chat task:** [LATEST-WORK.md](LATEST-WORK.md) — the single editable work-in-progress document, kept until completed and replaced with the next agreed task. **Current source-preservation handoff:** [Phase 1+2 single-pass recovery](resolver/PHASE12-PRESERVATION-RECOVERY.md) (inputs, discrepancies, existing generators, tests and acceptance). [Live-expanded semantic owner A/B/C matrix (62 originals + 10 tracked additions)](resolver/EXTENDED-DORMANCY-STAGE-MATRIX.md) tracks source-proof and per-mod status. Detailed research belongs in subsystem docs; [HANDOFF](HANDOFF/README.md) remains reserved **only** for stable startup guidance, process and the continuation prompt—no dated experiments, version snapshots or capture reports.

| Subsystem | Documents |
| --- | --- |
| [Product](product/) | [Architecture](product/ARCHITECTURE.md), [capabilities and roadmap](product/PRODUCT-STATUS.md) |
| [Profiler](profiler/) | [C# port contract](profiler/PORT_CONTRACT.md), [callback/source audit](profiler/audits/CET-CALLBACK-AUDIT-2026-10-13.md) |
| [Native](native/) | [Pinned native build provenance](native/NATIVE_PROFILER_BUILD_PROVENANCE.md) |
| [GC and allocations](gc/) | [G-GC controller — approved design, not implemented](gc/G-GC-CONTROLLER.md), [Research](gc/GC-RESEARCH.md), [data provenance specification](gc/DATA-PROVENANCE-SPEC.md), [native tracer](gc/G-GC-TRACER.md), [collector diagnostics](gc/GC-PHASE-ALLOCATION-DIAGNOSTICS.md), [deep-callsite research](gc/research/DEEP-CALLSITE-ATTRIBUTION-2026-10-13.md), [multi-axis attribution experiment](gc/research/UNSCOPED-MULTI-AXIS-ATTRIBUTION-2026-10-14.md), [realloc-origin and CET-ingress experiment](gc/research/UNSCOPED-REALLOC-AND-CET-INGRESS-2026-10-14.md), [live source audit](gc/audits/LIVE-CET-DATA-PRODUCTION-AUDIT-2026-10-13.md), [capture report](gc/captures/NEW-NATIVE-GC-CONTINUATION-2026-10-14.md), [third native decompression](gc/captures/THIRD-NATIVE-GC-DECOMPRESSION-2026-10-14.md), [fourth capture and PDB-resolved native producers](gc/captures/FOURTH-G-GC-PDB-PRODUCER-ANALYSIS-2026-10-14.md), [VM-state test](gc/testing/UNSCOPED-VM-CONTEXT-TEST-2026-10-14.md) |
| [Resolver](resolver/) | [Engineering contract](resolver/PROJECT-CONTRACT.md), [Hotscenes/DAV integrated source status and native research boundary](resolver/HOTSCENES-DAV-INTEGRATION-STATUS.md), [internal integration plan](resolver/MEGAMIND-INTEGRATION-PLAN.md), [deep reuse research](resolver/research/AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md), [measured Minimap Widgets optimization](resolver/research/MINIMAP-STeALTH-WIDGET-LOCALITY-2026-10-14.md), [native-dispatch dormancy admission](resolver/research/NATIVE-DISPATCH-DORMANCY-2026-10-15.md), [native OnAction outgoing allocation metric](gc/research/NATIVE-ONACTION-OUTGOING-METRIC-2026-10-15.md), [semantic soft admission and capture wake-path diagnostics](resolver/research/SEMANTIC-SOFT-ADMISSION-AND-NATIVE-TRANSITIONS-2026-10-15.md), [second-generation DriveBus/Poker source-stage compatibility](resolver/research/SECOND-GENERATION-LIVE-SEMANTIC-COMPAT-2026-10-15.md), [version-fluid Tunnel/Starman/Yaiba idle optimization](resolver/research/QUEST-AND-APARTMENT-DORMANCY-2026-10-14.md) |
| [0-Engine](0-engine/) | [Workload services](0-engine/0-ENGINE-WORKLOAD-SERVICES.md) |
| [Integration](integration/) | [Profiler summary interoperability](integration/PROFILER_SUMMARY_INTEROP_CONTRACT.md), [TOTAL integration](integration/TOTAL_INTEGRATION_CONTRACT.md) |
| [Build](build/) | [Build instructions](build/BUILDING.md) |
| [History](history/) | [Chronological development record](history/ACTIVE-STATE.md) |

GitHub-standard [README](../README.md), [CHANGELOG](../CHANGELOG.md), [RELEASE_NOTES](../RELEASE_NOTES.md) and [SECURITY](../SECURITY.md) remain at repository root. Local native/test READMEs remain beside their corresponding source.

**Maintenance:** store technical notes with their subsystem, significant decisions in History, no dated research in HANDOFF; doc-only commits must not trigger CI.
