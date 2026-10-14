# G-GC unscoped allocation-origin and CET ingress experiment — 2026-10-14

**Status:** Native + combined CI **verified**; game runtime/overhead capture **pending**. [Native #38582048697](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38582048697) SUCCESS; native `2.15.3-unscoped-realloc-context`, DLL SHA `5c3d5dfad9e9131c1aae53f700985dfdd7b9d417067044687fbf32f8b78b0e11`, developer PDB SHA `30ccf88c4f2d9515f05729b9473bc613f1d36392d54c29f2f8b4b48f9a658450`. [Main #38584208545](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38584208545) all four jobs SUCCESS with both player artifacts **v1.2.7**. No production mod changes, GC policy edits, Resolver optimization passes or product version bump.

## Why this pass

Third native capture `CET-20261014-152417_THIRD_NATIVE_FOR_GC_DECOMPRESS.zip` was a clean ~238.249s capture. Owner zero recorded 258,546,077 positive LuaJIT allocator growth bytes (246.569 MiB), 1,654,279 positive requests, with ~99.4% in the legacy `RESIZE_GROWTH` path and 100% outside the previously instrumented `TriggerOnUpdate` / dump host boundaries. Those labels were too broad to prove either repeated resizing or a concrete producer. See [capture analysis](../captures/THIRD-NATIVE-GC-DECOMPRESSION-2026-10-14.md).

## Implemented additive source instrumentation

1. **Old-size-zero vs existing realloc:** `gcet_gc_alloc_observe` now forwards the original old/new sizes to a bounded owner-zero native accumulator. For the legacy `RESIZE_GROWTH` accounting only, split `oldSize==0` and `oldSize>0` into two independently reconciled matrices of six positive-delta tiers and six **absolute new-allocation-size tiers**. Keep legacy `RESIZE_GROWTH`, `NEW_GCO`, VM states, per-second path data and owner-zero byte/operation accounting unchanged.
2. **New file:** `CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv` records `POSITIVE_DELTA` and `ABSOLUTE_NEW_SIZE` partitions; each sums to the **legacy realloc path only**, never to all owner-zero bytes. The per-second `UnscopedPathTimeline.csv` adds `Dimension=REALLOC_ORIGIN`, likewise a subset of realloc path, not extra bytes.
3. **Sampling:** existing bounded native stack CSV appends `ReallocOrigin`, `OldSize`, `NewSize`. The original stack columns and default 2048-sample bound remain intact. `NOT_REALLOC` belongs to the separate `NEW_GCO` path.
4. **CET host ingress:** retain original `OUTSIDE_CET_INSTRUMENTED_BOUNDARY`, `CET_FRAME_UPDATE`, `CET_OVERLAY_DUMP`; add `CET_DRAW_DISPATCH`, `CET_LIFECYCLE_DISPATCH` (hook/tweak/init), `CET_OVERLAY_EVENT_DISPATCH`. All use capture-gated native-only `BridgeScope` around pinned `Scripting::TriggerOn*` entrypoints. **No extra Scope over `FunctionOverride::next()`** or modification of its prior SEH safety rule. Uninstrumented host pathways can still land in `OUTSIDE`.
5. **Reports and tests:** managed Summary + Resolver JSON and HTML include reconciled split diagnostics, plus exact totals and `sourceAttribution=NOT_PROVEN`, `autoRewriteAuthorized=false`. `tests/UnscopedAttributionReportContract.ps1` includes consistent origins, mismatch fail-closed and old-native capture compatibility. The new CSV is in `MANIFEST.json.liveResultFiles`.
6. **Symbols:** build already copies matching profiler PDB when produced; native CI now **requires** the PDB and uploads a separate developer-only artifact, with its SHA256 and the paired DLL SHA256. Main packaging still pins and imports **only** the compiled DLL and its checksum, keeping symbols out of player distributions. Old sample RVAs cannot be named using a newly linked or nonmatching PDB.

## Exact reconciliation contracts

- Existing owner-zero VM-state, path/size, boundary and one-second totals each reconcile to original owner-zero bytes/operations.
- New `POSITIVE_DELTA` and `ABSOLUTE_NEW_SIZE` rows and `REALLOC_ORIGIN` per-second rows **each independently equal** the `RESIZE_GROWTH` aggregate (and not owner-zero total because `NEW_GCO` is excluded).
- A mismatch sets `reallocOriginsReconciled=false` and `extendedReconciled=false`; a legacy capture without new file retains its previous report contract. Sampled native bytes must not be extrapolated.
- Every new context is a host execution scope, **not** mod ownership; a VM state, positive allocator growth or PDB-resolved native function is not proof that a Lua source allocation is redundant.
- No new Lua calls, GC scheduling, collection thresholds, app features or raw nested timing math.

## Build and game checkpoints

1. Follow **latest** native source CI to terminal success, confirm matching PDB and DLL artifacts with SHAs, and inspect compiled feature/CSV markers.
2. Pin that exact successful native run and its DLL SHA in `MANIFEST.json` and main CI. Preserve synchronized product `1.2.7`; use a distinct native instrumentation identity to prevent mixing old/new builds. Verify both normal Profiler and Resolver artifacts.
3. Same save/unchanged stack: world load with capture off; 4–6 minutes idle → running → combat **without menus**; pause/stop; post-stop menus/input/sleep selector; normal collection.
4. Reconcile new dimensions and original owner-zero totals. Compare rate by activity window. Symbolicate with **that exact build's** PDB; do not use its RVA mapping to assert old captured RVAs unless DLL identity matches.
5. Only after concrete native/host or Lua source evidence identifies a tractable provider, inspect live Lua source and choose a **source-proven**, reversible producer optimization. Do not convert these diagnostics into an automatic Resolver cache rule.

**References:** [multi-axis attribution predecessor](UNSCOPED-MULTI-AXIS-ATTRIBUTION-2026-10-14.md), [data provenance contract](../DATA-PROVENANCE-SPEC.md), [HANDOFF core](../../HANDOFF/CORE.md).
