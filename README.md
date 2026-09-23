# G-CET Runtime Profiler

Standalone CET/Lua runtime profiler manager for **Cyberpunk 2077**, with optional 0-Engine Scheduler attribution.

## v0.1.2-beta — guided setup + optional frame-time companion

The standalone manager is a normal **C# / .NET 8 WinForms application**. The beta keeps the CET profiler standalone while making synchronized frame-time capture easier to discover.

The port keeps the established profiler behavior and file ownership model:

```text
G-CET-Runtime-Profiler.exe
        │
        ├─ GUI mode for standalone users
        └─ headless JSON mode for TOTAL Profiler
                 │
                 ▼
        G.CETProfiler.Core
                 │
        ┌────────┼─────────┐
        ▼        ▼         ▼
      CET ASI  0-Engine  RESULTS
```

The PowerShell implementation remains under `reference/powershell-alpha6c/` as historical/reference material only. It is **not shipped in the public C# package**.

## Capture contract

The profiler owns one CET input, defaulting to **F11**:

```text
F11 #1 -> START fresh capture
F11 #2 -> STOP + AUTO EXPORT CSV
```

There is no separate F12/export action.

## Standalone workflow

1. Run `G-CET-Runtime-Profiler.exe`.
2. On **Setup**, select the Cyberpunk 2077 folder.
3. Optionally enable **Run with a frame-time capture tool** and link an existing profiler executable plus its capture/results folder.
4. Continue to **Install, Capture & Recovery**. CET / 0-Engine / Scheduler compatibility is checked there.
5. Use **INSTALL PROFILER**. The CET capture binding is preset and verified as F11.
6. In game, use F11 to start and stop the CET measurement.
7. Use **COLLECT RESULTS / CLEAR LIVE**. Known CET runtime output is copied and verified before being removed from the live game folder.
8. If a frame-time companion is configured, its latest capture is copied into the same result directory under `FrameTime/`. The external profiler's source files are never deleted.
9. Use **RESTORE ORIGINAL STATE** when finished with profiling.

### Optional frame-time companion

The companion is **not a dependency**. G-CET never configures or modifies it and never requires it for CET profiling. If the user links an executable, the capture page can launch that tool as a convenience; capture control remains owned by the external profiler.

The beta recognizes CapFrameX configuration read-only and can report its `CaptureHotKey`. **CapFrameX 1.9.1.2 Beta** is the tested reference used during development. Other profilers can be linked; when their key format is unknown the manager reports **UNKNOWN** and asks the user to verify F11 manually.

The companion executable and results paths are stored in package-local convenience settings. The capture page enables **START FRAME-TIME TOOL** only when the optional pairing is enabled and the linked executable exists. Collection copies the newest detected companion capture into the CET archive and leaves the external source untouched.

The managed installation state is stored in the game folder:

```text
bin\x64\plugins\.cet_runtime_profiler\
```

That state survives closing or restarting the manager and is the same state consumed by G's Cyberpunk 2077 TOTAL Profiler.

## 0-Engine behavior

The C# core preserves the existing three-mode behavior. The core-only choice is no longer presented as a normal setup decision: it appears as a fallback only when the installed 0-Engine cannot be integrated safely or Scheduler integration fails and rolls back.

**Existing Scheduler-integrated 0-Engine:** the user's `init.lua` remains untouched. A profiler-aware Scheduler is temporarily installed only when required, with exact backup/restore.

**Recognized unintegrated/custom 0-Engine:** the original `init.lua` is hash-backed-up and a temporary adaptive bridge is inserted immediately before its final `return Engine`. The profiler uses the separate `modules/CETProfilerScheduler.lua` filename so it does not collide with the user's Scheduler.

**Core profiler only:** 0-Engine is left byte-for-byte untouched. Native CET profiling remains available, but Scheduler attribution is skipped unless the user's own environment already exposes it.

## Restore safety

### Restore UI feedback

The restore confirmation is guarded from the window-activation auto-refresh path, so clicking **Yes** cannot be swallowed by a simultaneous status refresh. The capture page shows an immediate **RESTORING ORIGINAL STATE...** state, then a persistent green success or red failure result.


Installation is a persistent, hash-verified transaction.

The manager records the original CET ASI, affected 0-Engine files, CETProfilerControls state, and prior CET binding state before changing them. Restore refuses to overwrite unexpected user changes.

Pre-existing `CETProfilerControls` content is backed up and restored exactly. Legacy TOTAL Profiler 0.2.19 CET binding state is also understood during migration.

If live profiler output still exists when Restore is requested, known scripted CET output files are archived into the standalone `RESULTS/` folder before game files are restored. Exact filenames plus the manifest-owned `CET_Runtime_Profile_*` output patterns are eligible; unrelated files in the CET directory are never swept.

### Headless emergency recovery

Normal **RESTORE ORIGINAL STATE** stays intentionally strict: if any managed file is missing, changed, or has a bad backup, normal restore stops before changing anything.

The headless `--emergency-restore` command remains available for advanced/manual recovery when strict restore cannot proceed. It evaluates every managed component independently:

- components with a valid original backup and a known live state are restored;
- profiler-added files are removed only when they still match the profiler-owned version;
- changed or ambiguous files are left untouched;
- successful components do not get blocked by one unrelated failure;
- if anything is skipped, `.cet_runtime_profiler` and its backups are preserved;
- a text recovery report is written under `RESULTS/RecoveryReports/` with exact live/backup paths for manual review.

The manager also keeps a full original backup of `bindings.json` before changing the CET profiler binding. Normal restoration remains surgical so unrelated bindings changed later are not rolled back.

## Headless JSON interface

TOTAL Profiler uses the **same executable and same C# core**:

```text
G-CET-Runtime-Profiler.exe --status  --game "..." --json
G-CET-Runtime-Profiler.exe --install --game "..." [--core-only] --json
G-CET-Runtime-Profiler.exe --collect --game "..." --json
G-CET-Runtime-Profiler.exe --reset   --game "..." --json
G-CET-Runtime-Profiler.exe --restore --game "..." --json
G-CET-Runtime-Profiler.exe --emergency-restore --game "..." --json
```

Headless mode emits JSON to stdout and errors as JSON to stderr.

## TOTAL Profiler ownership boundary

This repository is the source of truth for CET profiling.

TOTAL Profiler may invoke this standalone executable, verify its status, ask it to collect a completed capture, and copy that completed result into a combined package.

TOTAL Profiler does **not** own CET installation, CET key bindings, CETProfilerControls, 0-Engine injection, CET restore, or CET's standalone result path.

See `docs/TOTAL_INTEGRATION_CONTRACT.md`.

## Public package

The Windows build is a transparent self-contained `win-x64` directory:

```text
G-CET-Runtime-Profiler.exe
G.CETProfiler.Core.dll
MANIFEST.json
README.md
VERSION.txt
payload\
RESULTS\
.NET self-contained runtime files...
```

No Python, PyInstaller, PowerShell manager, VBS launcher, UPX, or self-extracting wrapper is used.

## CI validation

GitHub Actions verifies the hash-locked native/Lua payload before compilation, builds and publishes the C# application, confirms no development manager scripts are present in the public package, then runs an integration fixture covering:

```text
adaptive 0-Engine
install
F11 binding transaction
collect + verified clear of exact outputs and scripted metadata patterns
restore exact init.lua
restore pre-existing CETProfilerControls
restore prior CET bindings
remove persistent state
core-only byte preservation
```

The native profiler payload remains version **2.11.0**, targeting the manifest-locked CET **1.37.1** binary set.

## Development reference

`reference/powershell-alpha6c/` exists only to preserve the behavior that was ported. New runtime behavior belongs in `src/G.CETProfiler.Core/`, and the WinForms/headless front end belongs in `src/G.CETProfiler.App/`.
