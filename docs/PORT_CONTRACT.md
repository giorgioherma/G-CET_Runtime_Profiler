# C# port contract

The retired PowerShell alpha6c implementation is preserved only in Git history; the live tree and public package use the C# manager exclusively.

**v1.0.1 implements this contract in C#/.NET 8.** The reference scripts are no longer part of the public runtime package.

Future changes should preserve these behavioral invariants unless the standalone profiler contract is intentionally versioned.

## Invariants

- Guided two-page standalone workflow: SETUP, then INSTALL -> CAPTURE -> RESTORE.
- Two independent CET capture controls: unrestricted manual F11 and optional F10 COMBAT-POLICE-120 auto-stop. Existing user assignments remain authoritative.
- F11 #1 starts a fresh capture; F11 #2 stops and automatically exports CSV results.
- F10 starts a new 120-second native-clock capture and automatically stops/exports; a second press while capturing does not restart it.
- The live capture HUD shows elapsed time; scenario/heap checkpoints share the existing profiler callback and marker API.
- No separate F12 export action.
- CET owns its own binding and its own result lifecycle.
- Native profiler live output remains in the normal CET location until `Collect`.
- `Collect` copies + verifies exact manifest outputs plus explicit scripted `CET_Runtime_Profile_*` metadata/status/temp patterns into standalone package `RESULTS/`, then clears only those owned files.
- Unrelated files in the CET directory are never swept.
- Optional GUI-only frame-time pairing is not a dependency: known tools may have their START key detected read-only; a linked executable may be launched on explicit user action; companion results are copy-only and external originals are never deleted.
- Install/restore state is game-side under `bin/x64/plugins/.cet_runtime_profiler/` and survives application restart.
- Exact original CET ASI / 0-Engine files are backed up before changes and hash-verified.
- Existing Scheduler-integrated 0-Engine uses the known scheduler path.
- Recognized unintegrated/custom 0-Engine gets a v3 composed bridge + temporary `CETProfilerScheduler.lua`; the original onUpdate handler and Scheduler tick use one CET registration. Obsolete v2 bridges must be restored first.
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

## CET native rebuild/ASI pin policy (mandatory, 2026-11-08)

The portable Cyberpunk 2.5 CET Profiler consumes the SHA256-verified, already-compiled ASI pinned in `.github/workflows/cet-2.5-profiler-package.yml`. All edits to Lua, 0-Engine Scheduler, C#, installer, UI, reports, tests, validation fixtures and documentation **MUST reuse it**, with zero native CET rebuilds. Native rebuilds are **manual only** and require a real change in the native generating inputs; the workflow checks those inputs against the pinned ASI's source commit before invoking the CET builder.

For an intentional C++/native-source revision: batch changes, run source-level checks, dispatch one native build, read its actual resulting SHA256, then update the packaging artifact run, source SHA and expected binary hash together. Package CI must block stale native source and incorrect binary hashes; it must never compile CET as a fallback. No automatic native build on push. This rule applies regardless of gameplay acceptance status and does not change the original CET restore and install contracts.

See [the exact branch pin and workflow rule](CET-1.38.1-PUBLIC-PORT.md#native-asi-reuse-contract--2026-11-08).
