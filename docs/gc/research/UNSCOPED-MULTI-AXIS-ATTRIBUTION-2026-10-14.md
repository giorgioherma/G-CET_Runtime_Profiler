# Owner-zero native attribution: multi-axis experimental capture

## Why this experiment is necessary

The second instrumentation capture (239.85 s) showed roughly **242.17 MiB** unscoped positive LuaJIT growth, with about **99.89%** observed during LuaJIT's VM_C execution state. This is a runtime execution-state classification, NOT evidence the bytes were produced by a CET mod, or even by a particular C function. The user reports a suspected in-game menu during a severe frametime outlier; that outlier must not be treated as representative combat behavior. Controlled menu-free phase comparisons are required.

## Added native evidence

All dimensions are capture-gated, additive to existing metrics, **owner registration ID zero only**:

1. **Allocation path and size**: categorize positive growth from LuaJIT allocator resize vs GC-object allocator creation, with six byte-delta tiers from <64 B to >=16 KiB. **Shapes** CSV sums exactly to owner-zero byte/operation totals.
2. **CET outer execution boundary**: three disjoint state labels — \`OUTSIDE_CET_INSTRUMENTED_BOUNDARY\` (unknown, not automatically CET internal), \`CET_FRAME_UPDATE\` (outer \`Scripting::TriggerOnUpdate\`, including nested work) and \`CET_OVERLAY_DUMP\` (profiler collection/dump entry). These are host entry contexts, NOT Lua mod ownership.
3. **Native callpath samples**: take at most 2048 bounded native unwinds, deterministic stride 1024 owner-zero allocator operations plus a sparse large-byte condition; module+RVA decoding happens at dump, not at the allocator. Samples cannot be extrapolated to whole allocations, and cannot establish Lua callsite.
4. **Capture-time distribution**: preserve exact VM-state per-second timeline; add per-second allocation-path and outer-CET-boundary timelines (1200 s + explicit overflow). All categories must reconcile to original owner-zero positive-byte and allocation-operation totals. Capture pauses/resumes use existing offset clock; second-boundary precision is approximate.

Files:
- \`CET_Runtime_Profile_GGC_UnscopedShapes.csv\`
- \`CET_Runtime_Profile_GGC_UnscopedBoundaries.csv\`
- \`CET_Runtime_Profile_GGC_UnscopedPathTimeline.csv\`
- \`CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv\`
- Existing \`CET_Runtime_Profile_GGC_UnscopedContexts.csv\` and \`CET_Runtime_Profile_GGC_UnscopedTimeline.csv\` remain.

The managed report now includes \`unscopedProducerDiagnostics\` in **both** \`CET_Summary.json\` and \`CET_Resolver_Input.json\`, plus HTML tables. Important flags: \`sourceAttribution=NOT_PROVEN\`, \`autoRewriteAuthorized=false\`, \`vmReconciled\` and \`extendedReconciled\`. Existing JSON fields, callback timing, GC phase measures and native instrumentation are retained. Legacy captures without new files still work.

## Game test — same save and unchanged mod stack

1. Use a **verified combined Profiler build**, not the native-only DLL artifact. Back up/restore through normal Manager. Load world **before** enabling capture; ensure controls, menus and NIF interaction work at baseline. Stop if there is a pre-capture crash.
2. Run an uninterrupted capture of about **4–6 minutes** if practical. Aim for **minute 0–1 idle**, **minute 1–2.5 moving/running**, and **minute 2.5 onward combat/police**. Keep CET overlay, inventory, pause and other game menus closed during measured segments. No F11 until starting/stopping. Avoid treating loading/menu frames as combat.
3. Observe if menus remain responsive **after** stop. Collect normally.
4. The capture must contain old and new CSVs, correct duplicate-free markers, stable native memory and acceptable overhead. Sum each new distinct allocation dimension against owner-zero rankings; both per-second dimensions must separately reconcile. Check native sample coverage, drops and frames \`UNKNOWN_OR_JIT_CODE\`.
5. Align allocation path/bridge peaks with game phases and collector slow steps. **Temporal overlap is not a causal ownership link.** Cluster actual sampled module+RVA paths and determine which are stable/common before source review. Compare after capture only with controlled baseline; no invented FPS or allocation savings.
6. Choose target Lua source for an opt-in scalar/change-rate probe or a source-proven elimination candidate **only after** native context and sampled path evidence identify a tractable provider or existing deep-trace owner. Do not add global caches from observations alone.

## Reproduction/CI contract

Native pipeline must compile/publish only DLL+SHA. Main CI must import actual pinned successful native artifact with verified SHA and package **both** apps at the user's current shared product version. Test \`tests/UnscopedAttributionReportContract.ps1\` checks report and Resolver JSON match, total reconciliation, mismatch rejection, bounded native-sample provenance and old-capture compatibility. Actual gameplay remains unverified until the user completes this test.

**Limitations:** Native stack unwinding can return repeated LuaJIT frames or unmapped JIT code; it is experimental capture-gated evidence with bounded overhead, not guaranteed precise callsite tracing. Large positive allocator growth is not memory retained, freed, leaked or unnecessary.
