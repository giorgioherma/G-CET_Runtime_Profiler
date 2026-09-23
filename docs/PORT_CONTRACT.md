# C# port contract

The PowerShell alpha6c implementation under `reference/powershell-alpha6c/` is a behavioral reference only. The public product must be a C#/.NET 8 WinForms application.

Port **behavior first, implementation second**. Do not redesign behavior during the port.

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
