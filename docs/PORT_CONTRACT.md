# C# port contract

The PowerShell alpha6c implementation under `reference/powershell-alpha6c/` is a behavioral reference only.

**v3.0.0-alpha8 implements this contract in C#/.NET 8.** The reference scripts are no longer part of the public runtime package.

Future changes should preserve these behavioral invariants unless the standalone profiler contract is intentionally versioned.

## Invariants

- Same visible standalone Manager design and workflow.
- One CET input, default F11.
- F11 #1 starts a fresh capture.
- F11 #2 stops capture and automatically exports CSV results.
- No separate F12 export action.
- CET owns its own binding and its own result lifecycle.
- Native profiler live CSVs remain in their normal CET location until `Collect`.
- `Collect` copies + verifies them into standalone package `RESULTS/`, then clears live CSVs.
- Install/restore state is game-side under `bin/x64/plugins/.cet_runtime_profiler/` and survives application restart.
- Exact original CET ASI / 0-Engine files are backed up before changes and hash-verified.
- Existing Scheduler-integrated 0-Engine uses the known scheduler path.
- Recognized unintegrated/custom 0-Engine gets the temporary profiler bridge + `CETProfilerScheduler.lua` path.
- Core-only mode leaves 0-Engine completely untouched.
- Restore refuses unsafe overwrites when live files changed unexpectedly.
- Restore returns the previous CETProfilerControls binding state.
- Legacy TOTAL 0.2.19 binding/controls rollback state must remain recoverable during migration.
- Standalone GUI and headless/CLI mode call the same C# core service.

## Headless contract used by TOTAL

```text
G-CET-Runtime-Profiler.exe --status  --game "..." --json
G-CET-Runtime-Profiler.exe --install --game "..." [--core-only] --json
G-CET-Runtime-Profiler.exe --collect --game "..." --json
G-CET-Runtime-Profiler.exe --reset   --game "..." --json
G-CET-Runtime-Profiler.exe --restore --game "..." --json
```

The JSON contract should remain stable enough for TOTAL Profiler to consume without knowing CET internals.


## Recovery invariant

Every user-owned file or directory that the profiler replaces or edits must have recoverable original state recorded **before** the mutation. Added profiler-only files are tracked as additions rather than pretending an original existed.

Strict restore prevalidates the whole transaction. Emergency restore is allowed to make partial progress, but only component-by-component after that component's own backup/ownership checks pass. Anything uncertain stays untouched and is listed for manual review.
