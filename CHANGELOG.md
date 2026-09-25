# Changelog

All notable public changes to G-CET Runtime Profiler are recorded here.

## [Unreleased]

No public changes after v1.0.0.

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
- Restore is authoritative for G-CET-managed files: verified original backups are restored even if profiler-owned live files changed while profiling, so normal runtime changes cannot trap the user in a managed state.
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
