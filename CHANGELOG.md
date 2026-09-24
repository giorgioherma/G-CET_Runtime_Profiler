# Changelog

All notable public changes to G-CET Runtime Profiler are recorded here.

## [Unreleased]

### Packaging

- Cleaned the portable ZIP root: the .NET runtime and managed assemblies are bundled into `G-CET-Runtime-Profiler.exe` instead of spilling dozens of runtime files beside it.
- Public root is now intentionally limited to the EXE, `MANIFEST.json`, `VERSION.txt`, `payload/`, `RESULTS/`, and `docs/`; package-local settings are still created beside the EXE on first use.
- CI now fails if DLL/runtime clutter or any unexpected file/directory leaks back into the public root.

### Result presentation

- Added `CET_Report.html` as the human-first starting point for every collected capture.
- Added `CET_Summary.json` as a compact machine-readable interpretation layer for standalone automation and future TOTAL integration.
- Organized verified native output under `Data/Runtime`, `Data/Scheduler`, and `Data/Metadata` instead of presenting a flat CSV pile.
- Added direct CET-side findings for sustained workload, call volume, callback hotspots, shared callback boundaries, heavy timeline windows, recorded callback spikes, and recurring presence in the heaviest CET windows.
- Added a dedicated 0-Engine Scheduler section that separates client-job attribution from the normal 0-Engine owner total and surfaces multi-job single-frame pile-ups plus common cadence groups.
- Result interpretation is downstream of verified raw collection: a report failure cannot invalidate or discard the native capture.
- Added recognized CapFrameX JSON interpretation directly to the standalone CET report: frametime distribution, ≥25/33.3/50/100 ms stall counts, CPU Active/GPU Active summaries, F11 start-clock synchronization, CET-window/stall overlap, exact recorded CET callback-spike overlap, exact Scheduler-burst overlap, and an interactive synchronized timeline.
- Added worst-frame evidence that shows the aligned CET 50 ms window, largest CET owner, callback spike and Scheduler burst where available, while keeping CapFrameX frametime authoritative for the rendered-frame measurement.
- Added `--report --capture <folder>` so an already collected result can be rebuilt after optional companion data is added.
- CapFrameX interpretation remains correlation evidence only: CET and frame-time domains are not added/subtracted and overlap is not labeled as automatic causation.

## [1.0.0] - 2026-09-23

First stable public release.

### Standalone product

- Finalized the .NET 8 WinForms standalone manager and portable ZIP distribution.
- Added package-local persistent settings for the Cyberpunk folder, optional frame-time executable, optional frame-time results folder, and pairing preference.
- Standardized the two-page SETUP → INSTALL/CAPTURE/RECOVERY workflow.
- Added a final readiness panel with consistent status semantics:
  - ✅ confirmed good / completed
  - ⚠️ optional, pending, unknown, or still usable
  - ❌ blocking error only

### CET profiling

- F11 is preset and verified as the CET profiler capture key.
- F11 starts a fresh measurement; the second F11 stops and automatically exports.
- Known CET profiler output is copied and verified before live files are cleared.
- Unrelated CET files are never swept.

### 0-Engine

- 0-Engine remains optional.
- Existing Scheduler-integrated layouts use the verified Scheduler replacement path only when needed.
- Recognized unintegrated layouts use a transactionally backed-up adaptive init bridge and a uniquely named `CETProfilerScheduler.lua`.
- Core-profiler fallback leaves 0-Engine byte-identical when safe integration is unavailable.
- Pre-existing user files/directories are copied and verified before mutation.

### Frame-time companion

- Frame-time pairing remains optional.
- CapFrameX is the tested/recommended companion without becoming a dependency.
- Other frame-time profilers remain supported.
- External profiler results are copy-only and are never deleted or modified at source.

### Recovery and integration

- Restore confirmation/activation race fixed.
- Strict restore preserves ambiguous/changed files instead of overwriting them.
- Headless emergency recovery remains available for advanced recovery.
- Standalone/headless behavior is the same implementation consumed by TOTAL Profiler.
- TOTAL integration contract requires the exact standalone release, not a TOTAL-specific variant.

## Pre-1.0 development

The 0.x beta builds were development/pre-release iterations leading to the stable 1.0.0 product. They are superseded by v1.0.0.
