# Resolver: version-fluid Lua compatibility (2026-10-20)

**Status:** Implemented on `dev/resolver-profiler-handoff`; source/CI regression coverage added. Runtime gameplay and the player's current install are separate acceptance steps.

## Locked design rule

**No installed Lua file is admitted or rejected because its entire contents match (or fail to match) a recorded SHA-256.** An exact file fingerprint cannot establish a version-fluid behavioral contract.

- Existing author/custom source is authoritative. A compatible module is preserved byte-for-byte; source-proven additive WorkQueue capture instrumentation is the only current exception.
- If the installed module does not expose a recognized optional G-CET service, the shared `0-Engine` bootstrap handles that with `pcall(require, ...)` and `type(module.New)` instead of aborting the pass or forcing a bundled implementation over the user's file.
- A missing or changed native-location host version is not a blocker when the `attach` capability exists.
- Exact source ranges and behavior-bearing semantic anchors are still checked **against current source at generation time**. If a particular safe transformation cannot be proven, that transformation is skipped; the Resolver may still generate other passes.
- SHA-256 values can still be emitted as provenance in generated pass manifests. They do not authorize a source rewrite or choose a version.
- Native CET/ASI package verification and byte-for-byte integrity verification of **backups during restore** are distinct safety checks. Removing them would risk installing an incompatible binary or restoring corrupted files; they are not Lua version acceptance tests.

## Places repaired

1. `FixedZeroEngineRuntime.cs`: eliminate installed support-module SHA allowlists and the SHA-classified known `init.lua` upgrade path. Upgrade current source structurally; preserve private ActionRouter, Health, Scheduler, WorkQueue, PhasePlanner, FrameListeners, StateSignals, WorkloadProbe, and native location host implementations if already installed.
2. `PassGeneratorService.cs`: discard exact capture-vs-live SHA vetoes and SHA-keyed shared-provider deduplication. For cross-file interaction UI, prove actual guard/pending/idle-reset structure on the live file instead of requiring the cross-file module's original fingerprint.
3. `BlackboardPsmDefinitionCacheSource.cs`: source-proven 12 PSM definitions with native-callsite allocation evidence, no pinned module SHA or absolute source-line allowlist.
4. `ZeroEngineAllocationBatchSource.cs`: source-proven SpatialHash and Lifecycle optimizations without exact baseline/radius-only/release SHA values or fixed allocation evidence line numbers.
5. `ZeroEngineService.cs`: recognize customized profiler-aware Scheduler sources by the expected integration API instead of the packaged reference SHA; preserve current bytes when already aware.
6. `NativeLocationFourOwnersSource.cs`: check for a callable shared-host `attach` interface rather than `version == 1`.

## Required regression coverage

- Alter a compatible `GCETWorkQueue.lua` and prove the author extension survives optional profiler instrumentation.
- Place an unfamiliar existing WorkQueue at the same path and prove it is preserved and does not block unrelated safe pass generation.
- Place customized reference-compatible and unknown private ActionRouter sources at the same path; preserve each rather than overwriting them.
- Test altered but structurally equivalent BlackboardCache, SpatialHash, and Lifecycle sources. Confirm changed behavior-bearing anchors are rejected without partial edits.
- Confirm original lifecycle/player, source-boundary, and native snapshot/fallback behavior contracts remain green.

**Do not use exact source hashes as future semantic-library admission selectors.** Prefer matching the narrow executable behavior being changed, with independent proof of each wake/fallback obligation.
