<p align="center">
  <img src="assets/g-cet-icon.png" alt="G-CET Runtime Profiler" width="192">
</p>

# G-CET Runtime Profiler

**v1.0.0 — first stable release**

A standalone Cyberpunk 2077 CET/Lua runtime profiler with transactional install/restore, optional 0-Engine Scheduler attribution, optional frame-time pairing, and a headless interface for G's Cyberpunk 2077 TOTAL Profiler.

## Download and run

The public release is a **portable ZIP**. No installer is required.

**Canonical v1.0.0 download:** [G-CET-Runtime-Profiler-v1.0.0.zip](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/download/v1.0.0/G-CET-Runtime-Profiler-v1.0.0.zip)

GitHub **Releases** is the canonical download location for v1.0.0.

```text
G-CET-Runtime-Profiler-v1.0.0.zip
└─ G-CET-Runtime-Profiler\
   ├─ G-CET-Runtime-Profiler.exe
   ├─ MANIFEST.json
   ├─ VERSION.txt
   ├─ app\
   │  ├─ G-CET-Runtime-Profiler.App.exe
   │  ├─ G-CET-Runtime-Profiler.App.deps.json
   │  ├─ G-CET-Runtime-Profiler.App.runtimeconfig.json
   │  ├─ G.CETProfiler.Core.dll
   │  └─ .NET runtime files...
   ├─ payload\
   ├─ RESULTS\
   └─ docs\
      ├─ README.md
      ├─ CHANGELOG.md
      └─ RELEASE_NOTES.md
```

The root EXE is a small native launcher. The self-contained WinForms application and .NET runtime live under `app\`, keeping the public root readable without hiding the whole application inside one large opaque executable. `payload\` remains separate because those exact profiler/integration files are hash-verified before deployment.

Extract the ZIP to a normal writable folder and run `G-CET-Runtime-Profiler.exe`.

The profiler is self-contained: users do **not** need to install .NET separately. `G-CET-Runtime-Profiler.settings.json` is created beside the root launcher on first use rather than being sent to Windows roaming/app-data folders. The launcher forwards both GUI and headless commands to the same application under `app\`.

## Requirements

Required:

- Cyberpunk 2077
- Cyber Engine Tweaks (CET)

Optional:

- 0-Engine — adds Scheduler attribution/integration where the installed layout is recognized
- a frame-time capture tool — CapFrameX **1.9.1.2 Beta** is the tested/recommended reference build, but another frame-time profiler can be used

Neither 0-Engine nor a frame-time profiler is required for a normal CET profiler run.

## Capture contract

The profiler owns one CET input, defaulting to **F11**:

```text
F11 #1 -> START fresh capture
F11 #2 -> STOP + AUTO EXPORT
```

There is no separate export key.

## Standalone workflow

1. Run `G-CET-Runtime-Profiler.exe`.
2. On **SETUP**, select the Cyberpunk 2077 folder.
3. Optionally link a frame-time profiler executable and its results folder.
4. Continue to **INSTALL -> CAPTURE -> RESTORE**.
5. Review the status list and use **INSTALL PROFILER**.
6. When the page reports **PROFILER IS READY!**, optionally launch the frame-time tool and then start Cyberpunk.
7. In game, press F11 to start and F11 again to stop/export.
8. Return to the manager and use **COLLECT RESULTS / CLEAR LIVE**.
9. When finished profiling, use **RESTORE ORIGINAL STATE**.

### Status language

The second page uses one shared status convention:

- ✅ confirmed good / completed
- ⚠️ optional, pending, unknown, or degraded-but-usable
- ❌ blocking error requiring attention

Optional components never make the core CET profiler unavailable by themselves.

The final v1.0.0 UI keeps status prose white and colors only the semantic markers themselves. Disabled actions are visibly gray; enabled forward actions use the cyan G-CET accent, while **RESTORE ORIGINAL STATE** uses magenta when available.

## Optional frame-time companion

Frame-time pairing is convenience-only and is **not a dependency**.

CapFrameX **1.9.1.2 Beta** is the development/test reference used for v1.0.0. The manager can inspect recognized CapFrameX configuration read-only to report its capture key and suggest its capture directory. The CapFrameX releases link intentionally points to the upstream release page rather than claiming that the upstream release-page label matches the executable's internal version. Other profilers are accepted; if their key cannot be identified the UI reports it as unknown and asks the user to verify synchronization manually.

External frame-time files are always **copied only** into the collected CET result. The source files belonging to the external profiler are never moved, deleted, or modified.

When the copied companion is a recognized CapFrameX JSON capture, the standalone report automatically adds a second evidence layer: average/median/P95/P99/max frametime, slow-frame counts, CPU Active and GPU Active readings, shared-F11 relative-timeline synchronization, CET 50 ms-window overlap, recorded callback-spike overlap, 0-Engine Scheduler-burst overlap, a synchronized CET/frametime timeline, and a table of the worst rendered-frame events. Hold **Shift** and use the mouse wheel over the timeline to zoom in/out around the pointer; the existing Full capture / Around worst frame controls remain available. CapFrameX `Info.CreationDate` is treated as record/save metadata rather than a capture-start clock. Other/custom profiler files remain preserved under `FrameTime\` even when their schema cannot be interpreted automatically.

## 0-Engine behavior

0-Engine compatibility is resolved from the installed structure/capabilities rather than from an assumed version number.

### Existing Scheduler-integrated 0-Engine

The user's `init.lua` stays untouched. If its Scheduler is not already profiler-aware, the original Scheduler is copied and verified before the profiler-aware Scheduler is temporarily deployed.

### Recognized unintegrated/custom 0-Engine

The original `init.lua` is copied and hash-verified before a temporary adaptive profiler bridge is inserted. The profiler uses `modules/CETProfilerScheduler.lua` so it does not collide with a user's existing `Scheduler.lua`.

### Core-profiler fallback

If the 0-Engine structure cannot be integrated safely, the manager can run CET profiling in core-only mode. 0-Engine remains byte-for-byte untouched and Scheduler attribution is skipped.

## File safety and restore

Installation is transactional.

Before a pre-existing user file or directory is modified, its recoverable original state is copied and verified. Unknown/unowned files are not swept.

Managed state is stored in:

```text
bin\x64\plugins\.cet_runtime_profiler\
```

**RESTORE ORIGINAL STATE** restores the original CET/0-Engine/binding state and removes profiler-owned temporary integration files.

If live CET profiler output exists, known profiler output is archived before restore. Unrelated files in the CET directory remain untouched.

A strict headless emergency-recovery command is retained for advanced/manual recovery:

```text
G-CET-Runtime-Profiler.exe --emergency-restore --game "..." --json
G-CET-Runtime-Profiler.exe --report  --capture "RESULTS\<capture>" --json
```

`--report` rebuilds `CET_Report.html` and `CET_Summary.json` from an already collected result. This is useful after a frame-time companion has been added/copied into `FrameTime\` and is also covered by CI.

## Results

Standalone results remain package-local:

```text
G-CET-Runtime-Profiler\RESULTS\<capture>\
├─ CET_Report.html
├─ CET_Summary.json
├─ Data\
│  ├─ Runtime\
│  ├─ Scheduler\
│  └─ Metadata\
└─ FrameTime\              # only when a companion capture is copied
```

**Open `CET_Report.html` first.** Collection turns the existing native profiler data into a human-readable CET diagnosis: the bulk of measured CET work, call volume, callback hotspots shared across mods, heavy CET timeline windows, recorded callback spikes, and 0-Engine Scheduler pile-ups where Scheduler attribution is available. If a recognized CapFrameX capture was paired, the same report also shows actual rendered frametime and synchronized CET/stall evidence.

The report is a presentation layer, not a replacement for the raw data. Every verified native profiler file is preserved under `Data\`, and `CET_Summary.json` exposes the same condensed findings for automation and future higher-level tooling.

0-Engine is treated specially in the report because it can carry client work. Scheduler job timing is shown as work measured **inside** 0-Engine and is never added again to normal CET owner totals.

On collection, known live CET profiler output is copied and hash-verified before any live profiler file is removed. Report generation happens only after the raw capture is safe; if presentation fails, the verified native data remains archived and a `CET_Report_Error.txt` diagnostic is written.

External frame-time results follow the separate copy-only rule described above. CapFrameX is treated as the rendered-frametime/CPU-GPU-active evidence layer; CET remains the script-side workload layer. The report never subtracts one measurement domain from another and does not turn timing overlap into automatic causation.

## Headless JSON interface

The same executable and the same C# core are used by higher-level orchestration:

```text
G-CET-Runtime-Profiler.exe --status  --game "..." --json
G-CET-Runtime-Profiler.exe --install --game "..." [--core-only] --json
G-CET-Runtime-Profiler.exe --collect --game "..." --json
G-CET-Runtime-Profiler.exe --reset   --game "..." --json
G-CET-Runtime-Profiler.exe --restore --game "..." --json
G-CET-Runtime-Profiler.exe --emergency-restore --game "..." --json
```

## TOTAL Profiler boundary

This repository is the source of truth for the standalone CET profiler.

G's Cyberpunk 2077 TOTAL Profiler consumes the **exact standalone release unchanged**. TOTAL may orchestrate it, query status, trigger lifecycle actions through the public interface, copy/read completed results, and correlate them with other profiler outputs.

TOTAL does not maintain a separate CET profiler binary or TOTAL-specific CET package.

See `docs/TOTAL_INTEGRATION_CONTRACT.md`.

## Technical notes

The native profiler payload remains version **2.11.0**, targeting the manifest-locked CET **1.37.1** binary set.

The public manager is a C# / .NET 8 WinForms application shipped behind a small native launcher, with the self-contained managed app/runtime isolated under `app/`. Both Windows executables carry the final multi-size G-CET application icon. The retired PowerShell manager has been removed from the live tree; its history remains available through Git.

CI validates the hash-locked payload, builds the self-contained Windows package, and exercises installation/collection/restore plus 0-Engine integration fixtures. Versioned GitHub Releases provide the public portable ZIP and matching SHA-256 file.
