# Changelog

All notable public changes to G-CET Runtime Profiler are recorded here.

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
