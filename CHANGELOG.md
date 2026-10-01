# Changelog

All notable public changes to G-CET Runtime Profiler are recorded here.

## [Unreleased]
- Added the standalone G-CET Resolver and deployable pass generator. It inspects every measured non-infrastructure callback, applies only mechanically proven generic AUTO recipes plus source-proven production semantic rules, and emits one game-root overlay ZIP without modifying the live CET tree.
- Generic AUTO is restricted to finite OnAction routing/prefiltering and frame-dispatch consolidation. Retired structural/cadence/dormancy generator branches were removed; those recognizers may remain analysis evidence but cannot generate code.
- Refined **Optimizer Rule #1** to a risk-adjusted, system-level payback model. AUTO evaluates benefit at the level where an optimization operates (callback family/shared provider/routing layer/affected mod set), not only per individual callback. Trivial mechanically proven transforms may take small per-client wins when their aggregate measured benefit is worthwhile; bespoke semantic/timing-sensitive rewrites require substantially stronger payback. Spike/burst/frame-pacing improvements count alongside average ms/s.
- Added analysis-only shared-provider opportunity aggregation to the Resolver. It inspects only measured callbacks, recognizes a small known provider/getter catalogue in current live source, joins existing adaptive-deep callee evidence, and reports cross-stack owner/callback/source-duplication/workload/spike context without estimating savings or authorizing generation.
- Documented frame-dispatch consolidation as a first-class performance optimization: although it preserves client callback bodies/cadence, consolidating many CET-facing frame registrations into one shared 0-Engine dispatch path measurably reduced the frequency and severity of CET runtime spikes in live testing. Resolver evaluation must therefore include spike/frame-pacing pressure, not only average script work.
- The production semantic catalog is active-only and intentionally compact: every catalog entry has a corresponding source injector, requires >=3 ms/s measured runtime plus current live-source proof, and can rewrite only files that already exist in the user's installed mod. Reviewed camera, interaction-framework, HUD/world-discovery and bespoke-timing candidates remain outside AUTO.
- Removed the abandoned Advanced/manual-classification and empty exception-registry paths from the Resolver so identity-specific AUTO behavior has one authoritative home: the semantic library.
- Added optional WORLD / IDLE, DRIVING and COMBAT scenario tag inputs to the profiler control mod. Tags write start/end markers into the same F11 capture timeline, allowing preparation gaps between scenarios while keeping CET and CapFrameX on one shared capture clock.
- Added an in-game capture HUD that always shows the total capture timer while profiling and appends the currently active scenario tag. Scenario tags do not pause profiling; untagged transition/preparation time remains in the raw capture but is excluded from scenario windows.
- Resolver handoff schema 1.1 now emits per-scenario owner workload/call rates, per-scenario callback spike pressure, tagged/untagged duration, and aligned per-scenario frametime statistics/calls-per-frame when CapFrameX correlation is exact.
- Added the measurement-only `CET_Resolver_Input.json` handoff consumed by the standalone G-CET Resolver. It preserves all callback rows and adds global/family/owner shares, normalized spike pressure, owner timeline activity/burst metrics, and calls-per-frame when aligned CapFrameX data is available. The profiler remains measurement-only; optimization decisions are made by the Resolver against the collected capture plus the current live CET source.

### v1.0.0 maintenance refresh

- Fixed completed-capture detection to match the native profiler's actual `CAPTURE_START` + `PAUSE` marker pair. Real captures now deploy into the profiler's package-local `RESULTS` folder instead of being mistaken for scratch/templates and cleared.
- Added a CI regression capture that proves native marker output is recognized, archived exactly once, and preserved through restore.
- INSTALL PROFILER now shows a high-visibility warning to close Vortex or any other mod manager/installer and keep it closed until RESTORE ORIGINAL STATE completes, preventing temporary profiler files from being picked up by external deployment tools.
- Native CET profiler export is now lazy: a pre-capture or duplicate dump is a true no-op and cannot create header-only CSV shells.
- **COLLECT RESULTS / CLEAR LIVE** can clear legacy template/scratch output even when there is no completed capture.
- Template-only live output no longer blocks installation; INSTALL clears those profiler-owned scratch files automatically.
- A real completed capture is still protected and must be collected before a new unmanaged install can proceed.
- Release documentation now points to the published `.sha256.txt` asset as the authoritative ZIP checksum instead of embedding a self-staling ZIP hash.

## [1.0.0] - 2026-09-25

First stable public release and the frozen standalone CET profiler baseline.

### Standalone product

- Finalized the .NET 8 WinForms standalone manager and portable ZIP distribution.
- Standardized the two-page **SETUP -> INSTALL -> CAPTURE -> RESTORE** workflow.
- Added package-local persistent settings for the Cyberpunk folder, optional frame-time executable, optional frame-time results folder, and pairing preference.
- Added the final dark G-CET visual shell, multi-size Windows application icon, themed dialogs, and restrained cyan/magenta action accents.
- Finalized action-state visuals: unavailable actions are gray, available forward actions are cyan, and **RESTORE ORIGINAL STATE** is magenta when available.
- Finalized status rendering so normal status prose remains white and only the semantic markers are colored:
  - ✅ confirmed good / completed
  - ⚠️ optional, pending, unknown, or degraded-but-usable
  - ❌ blocking error requiring attention

### CET profiling

- F11 is preset and verified as the CET profiler capture key.
- F11 starts a fresh measurement; the second F11 stops and automatically exports.
- Known CET profiler output is copied and verified before live files are cleared.
- Unrelated CET files are never swept.
- Added human-readable adaptive diagnosis for sustained workload, call volume, shared callback pressure, heavy windows, recorded callback spikes, and repeated presence in the heaviest CET windows.

### Result presentation

- Added `CET_Report.html` as the human-first starting point for every collected capture.
- Added `CET_Summary.json` as a compact machine-readable interpretation layer for standalone automation and future TOTAL integration.
- Organized verified native output under `Data/Runtime`, `Data/Scheduler`, and `Data/Metadata`.
- Added a dedicated 0-Engine Scheduler section that keeps client-job attribution separate from normal 0-Engine owner totals and surfaces multi-job single-frame pile-ups plus common cadence groups.
- Added recognized CapFrameX JSON interpretation: frametime distribution, slow-frame counts, CPU Active/GPU Active summaries, shared-F11 relative-timeline synchronization, CET/stall overlap, exact recorded callback-spike overlap, exact Scheduler-burst overlap, and worst-frame evidence.
- Added an interactive synchronized CET/frametime timeline. Hold **Shift** and use the mouse wheel to zoom around the pointer.
- Added `--report --capture <folder>` so an already collected result can be rebuilt after optional companion data is added.
- Report interpretation remains evidence, not proof of causation; CET and frame-time measurement domains are not added or subtracted.

### 0-Engine

- 0-Engine remains optional.
- Existing Scheduler-integrated layouts use the verified Scheduler replacement path only when needed.
- Recognized unintegrated layouts use a transactionally backed-up adaptive init bridge and uniquely named `CETProfilerScheduler.lua`.
- Core-profiler fallback leaves 0-Engine byte-identical when safe integration is unavailable.
- Pre-existing user files/directories are copied and verified before mutation.

### Frame-time companion

- Frame-time pairing remains optional.
- CapFrameX **1.9.1.2 Beta** is the tested reference build for v1.0.0.
- The upstream CapFrameX releases link is kept separate from the tested-version statement because upstream release-page labeling may not match the executable's internal version.
- Other frame-time profilers remain supported.
- External profiler results are copy-only and are never deleted or modified at source.

### Recovery and integration

- Installation and restore are transactional.
- Restore confirmation/activation race fixed.
- CET capture keybinds are user-controlled: F11 is seeded only when no existing binding exists, custom bindings are preserved, and restore never rolls them back.
- All failure/warning dialogs shown after async profiler operations use the existing 500 ms UI-settle path so a disabled/white transition cannot be frozen beneath the modal.
- Headless emergency recovery remains available for advanced/manual recovery.
- Standalone/headless behavior is the same implementation consumed by TOTAL Profiler.
- TOTAL integration contract requires the exact standalone release, not a TOTAL-specific variant.

### Packaging and maintenance

- Replaced the giant single-file bundle with a small native root launcher plus a normal self-contained .NET application under `app/`.
- Public root is intentionally limited to the launcher, `MANIFEST.json`, `VERSION.txt`, `app/`, `payload/`, `RESULTS/`, and `docs/`.
- Managed assemblies, native .NET runtime files and framework plumbing are isolated under `app/`.
- Non-English .NET satellite resources and public PDBs are excluded from the portable package.
- Both Windows executables use the final multi-size G-CET application icon.
- Removed retired PowerShell manager code and other dead/obsolete runtime/UI plumbing from the live tree.
- Removed obsolete absolute frametime start-offset fields and consolidated relative CapFrameX/CET timing around the shared capture-key model.
- CI verifies the payload hashes, package layout, launcher handoff, install/collect/restore lifecycle, emergency restore, 0-Engine resolver/handoff behavior, report generation, and portable artifact creation.
- Main-branch CI refreshes the canonical GitHub release for the version declared in `VERSION.txt`, including the portable ZIP and SHA-256 file.

## Pre-1.0 development

The 0.x beta builds were development/pre-release iterations leading to the stable v1.0.0 product. They are superseded by v1.0.0.
