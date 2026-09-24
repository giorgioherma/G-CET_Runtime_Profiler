# TOTAL Profiler integration contract

`G-CET-Runtime-Profiler` is a standalone product first. TOTAL Profiler consumes the exact standalone release unchanged.

TOTAL must not:

- maintain its own CET install/restore implementation;
- inject its own 0-Engine bridge;
- own CETProfilerControls;
- write CET key bindings itself;
- redirect CET's standalone result folder;
- delete standalone CET result archives after collection.

TOTAL may:

- call the standalone managed headless endpoint under `app/`;
- verify that CET is installed and bound to F11;
- ask CET to collect the completed capture;
- copy the completed CET result into TOTAL's combined `Raw/CET` package;
- consume the standalone `CET_Summary.json` for compact CET interpretation and/or read the preserved raw files under `Data/`;
- correlate CET + GRSP + CapFrameX;
- generate TOTAL's combined HTML / ZIP.


## Dependency rule

TOTAL should consume a versioned standalone release of this repository and invoke `app/G-CET-Runtime-Profiler.App.exe` headlessly. The root `G-CET-Runtime-Profiler.exe` is intentionally GUI-only. The files used by TOTAL must remain byte-identical to the standalone package for that release.

Combined-capture orchestration and correlation belong in TOTAL. The standalone GUI's optional frame-time companion is only a convenience layer: it is not part of the CET headless dependency contract and TOTAL must not rely on it.

The standalone CET result interpretation also remains CET-owned. This now includes CET + recognized CapFrameX interpretation when a copied CapFrameX JSON is present under `FrameTime/`: synchronization quality, frametime/stall statistics, CET-window overlap, callback-spike overlap and Scheduler-burst overlap. TOTAL may reuse the published `CET_Summary.json` and preserved raw data, but should not maintain a second fork of CET-specific ranking/Scheduler/CapFrameX interpretation logic. TOTAL's report adds GRSP and other cross-profiler context; it does not replace the standalone CET diagnosis.

If TOTAL needs a new CET lifecycle capability, that capability should first be exposed by the standalone core/headless contract rather than implemented as a TOTAL-only fork.


## Recovery behavior

TOTAL must expose the standalone manager's strict `--restore` and partial `--emergency-restore` semantics rather than recreating CET cleanup logic. A failed strict restore is not a dead end: Emergency Restore may recover independently safe CET-owned components while leaving ambiguous user changes untouched and reporting them for manual review.
