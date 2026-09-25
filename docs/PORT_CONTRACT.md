# C# port contract

The retired PowerShell alpha6c implementation is preserved only in Git history; the live tree and public package use the C# manager exclusively.

**v1.0.0 implements this contract in C#/.NET 8.** The reference scripts are no longer part of the public runtime package.

Future changes should preserve these behavioral invariants unless the standalone profiler contract is intentionally versioned.

## Invariants

- Guided two-page standalone workflow: SETUP, then INSTALL -> CAPTURE -> RESTORE.
- One CET input, default F11.
- F11 #1 starts a fresh capture.
- F11 #2 stops capture and automatically exports CSV results.
- No separate F12 export action.
- CET owns its own binding and its own result lifecycle.
- Native profiler live output remains in the normal CET location until `Collect`.
- `Collect` copies + verifies exact manifest outputs plus explicit scripted `CET_Runtime_Profile_*` metadata/status/temp patterns into standalone package `RESULTS/`, then clears only those owned files.
- Unrelated files in the CET directory are never swept.
- Optional GUI-only frame-time pairing is not a dependency: known tools may have their START key detected read-only; a linked executable may be launched on explicit user action; companion results are copy-only and external originals are never deleted.
- Install/restore state is game-side under `bin/x64/plugins/.cet_runtime_profiler/` and survives application restart.
- Exact original CET ASI / 0-Engine files are backed up before changes and hash-verified.
- Existing Scheduler-integrated 0-Engine uses the known scheduler path.
- Recognized unintegrated/custom 0-Engine gets the temporary profiler bridge + `CETProfilerScheduler.lua` path.
- Core-only mode leaves 0-Engine completely untouched and is presented as a fallback only when normal Scheduler integration is unavailable or fails safely.
- Restore uses the verified original backups for G-CET-managed files.
- CET keybinds are user-controlled. F11 is seeded only when no existing `CETProfiler_Toggle` binding exists, and restore leaves the current binding untouched.
- Legacy TOTAL 0.2.19 binding/controls rollback state must remain recoverable during migration.
- Standalone GUI and headless/CLI mode call the same C# core service.

## Headless contract used by TOTAL

```text
G-CET-Runtime-Profiler.exe --status  --game "..." --json
G-CET-Runtime-Profiler.exe --install --game "..." [--core-only] --json
G-CET-Runtime-Profiler.exe --collect --game "..." --json
G-CET-Runtime-Profiler.exe --reset   --game "..." --json
G-CET-Runtime-Profiler.exe --restore --game "..." --json
G-CET-Runtime-Profiler.exe --emergency-restore --game "..." --json
G-CET-Runtime-Profiler.exe --report --capture "RESULTS\<capture>" --json
```

The JSON contract should remain stable enough for TOTAL Profiler to consume without knowing CET internals.


## Recovery invariant

Every user-owned file or directory that the profiler replaces or edits must have recoverable original state recorded **before** the mutation. Added profiler-only files are tracked as additions rather than pretending an original existed.

Normal restore validates the integrity of all required original backups before restoring the managed transaction. Emergency restore remains a component-by-component recovery path for missing or corrupt recovery material.
