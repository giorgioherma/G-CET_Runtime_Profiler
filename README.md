# G-CET Runtime Profiler

**v1.0.0 — first stable release**

A standalone Cyberpunk 2077 CET/Lua runtime profiler with transactional install/restore, optional 0-Engine Scheduler attribution, optional frame-time pairing, and a headless interface for G's Cyberpunk 2077 TOTAL Profiler.

## Download and run

The public release is a **portable ZIP**. No installer is required.

```text
G-CET-Runtime-Profiler-v1.0.0.zip
└─ G-CET-Runtime-Profiler\
   ├─ G-CET-Runtime-Profiler.exe
   ├─ G.CETProfiler.Core.dll
   ├─ MANIFEST.json
   ├─ VERSION.txt
   ├─ README.md
   ├─ payload\
   ├─ RESULTS\
   └─ .NET self-contained runtime files...
```

Extract the ZIP to a normal writable folder and run `G-CET-Runtime-Profiler.exe`.

The profiler is self-contained: users do **not** need to install .NET separately. Settings are created beside the EXE in the extracted profiler folder rather than being sent to Windows roaming/app-data folders.

## Requirements

Required:

- Cyberpunk 2077
- Cyber Engine Tweaks (CET)

Optional:

- 0-Engine — adds Scheduler attribution/integration where the installed layout is recognized
- a frame-time capture tool — CapFrameX is the tested/recommended companion, but another profiler or compatible CapFrameX version can be used

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
4. Continue to **INSTALL, CAPTURE & RECOVERY**.
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

## Optional frame-time companion

Frame-time pairing is convenience-only and is **not a dependency**.

CapFrameX is the development/test reference. The manager can inspect recognized CapFrameX configuration read-only to report its capture key and suggest its capture directory. Other profilers are accepted; if their key cannot be identified the UI reports it as unknown and asks the user to verify synchronization manually.

External frame-time files are always **copied only** into the collected CET result. The source files belonging to the external profiler are never moved, deleted, or modified.

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
```

## Results ownership

Standalone results remain package-local:

```text
G-CET-Runtime-Profiler\RESULTS\
```

On collection, known live CET profiler output is copied and verified into the standalone result directory, then removed from the live game folder.

External frame-time results follow the separate copy-only rule described above.

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

The public manager is a C# / .NET 8 WinForms application. The PowerShell implementation under `reference/powershell-alpha6c/` is historical/reference material only and is not shipped in the public package.

CI validates the hash-locked payload, builds the self-contained Windows package, exercises installation/collection/restore and 0-Engine integration fixtures, then produces the portable release ZIP.
