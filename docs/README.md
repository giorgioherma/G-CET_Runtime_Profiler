# G-CET technical documentation

Detailed work is grouped by subsystem. [HANDOFF](HANDOFF/README.md) is reserved **only** for stable startup guidance, process and the continuation prompt—no dated experiments, version snapshots or capture reports.

| Subsystem | Documents |
| --- | --- |
| [Product](product/) | [Architecture](product/ARCHITECTURE.md), [capabilities and roadmap](product/PRODUCT-STATUS.md) |
| [Profiler](profiler/) | [C# port contract](profiler/PORT_CONTRACT.md), [callback/source audit](profiler/audits/CET-CALLBACK-AUDIT-2026-10-13.md) |
| [Native](native/) | [Pinned native build provenance](native/NATIVE_PROFILER_BUILD_PROVENANCE.md) |
| [GC and allocations](gc/) | [Research](gc/GC-RESEARCH.md), [data provenance specification](gc/DATA-PROVENANCE-SPEC.md), [native tracer](gc/G-GC-TRACER.md), [collector diagnostics](gc/GC-PHASE-ALLOCATION-DIAGNOSTICS.md), [deep-callsite research](gc/research/DEEP-CALLSITE-ATTRIBUTION-2026-10-13.md), [multi-axis attribution experiment](gc/research/UNSCOPED-MULTI-AXIS-ATTRIBUTION-2026-10-14.md), [realloc-origin and CET-ingress experiment](gc/research/UNSCOPED-REALLOC-AND-CET-INGRESS-2026-10-14.md), [live source audit](gc/audits/LIVE-CET-DATA-PRODUCTION-AUDIT-2026-10-13.md), [capture report](gc/captures/NEW-NATIVE-GC-CONTINUATION-2026-10-14.md), [third native decompression](gc/captures/THIRD-NATIVE-GC-DECOMPRESSION-2026-10-14.md), [fourth capture and PDB-resolved native producers](gc/captures/FOURTH-G-GC-PDB-PRODUCER-ANALYSIS-2026-10-14.md), [VM-state test](gc/testing/UNSCOPED-VM-CONTEXT-TEST-2026-10-14.md) |
| [Resolver](resolver/) | [Engineering contract](resolver/PROJECT-CONTRACT.md), [internal integration plan](resolver/MEGAMIND-INTEGRATION-PLAN.md), [deep reuse research](resolver/research/AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md), [measured Minimap Widgets optimization](resolver/research/MINIMAP-STeALTH-WIDGET-LOCALITY-2026-10-14.md), [native-dispatch dormancy admission](resolver/research/NATIVE-DISPATCH-DORMANCY-2026-10-15.md), [semantic soft admission and capture wake-path diagnostics](resolver/research/SEMANTIC-SOFT-ADMISSION-AND-NATIVE-TRANSITIONS-2026-10-15.md), [version-fluid Tunnel/Starman/Yaiba idle optimization](resolver/research/QUEST-AND-APARTMENT-DORMANCY-2026-10-14.md) |
| [0-Engine](0-engine/) | [Workload services](0-engine/0-ENGINE-WORKLOAD-SERVICES.md) |
| [Integration](integration/) | [Profiler summary interoperability](integration/PROFILER_SUMMARY_INTEROP_CONTRACT.md), [TOTAL integration](integration/TOTAL_INTEGRATION_CONTRACT.md) |
| [Build](build/) | [Build instructions](build/BUILDING.md) |
| [History](history/) | [Chronological development record](history/ACTIVE-STATE.md) |

GitHub-standard [README](../README.md), [CHANGELOG](../CHANGELOG.md), [RELEASE_NOTES](../RELEASE_NOTES.md) and [SECURITY](../SECURITY.md) remain at repository root. Local native/test READMEs remain beside their corresponding source.

**Maintenance:** store technical notes with their subsystem, significant decisions in History, no dated research in HANDOFF; doc-only commits must not trigger CI.
