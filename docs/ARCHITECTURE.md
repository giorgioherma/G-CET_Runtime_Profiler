# Architecture

## Product boundary

`G-CET_Runtime_Profiler` is a standalone product. Its C# core owns all CET-specific behavior. The TOTAL Profiler is an external orchestrator and must not duplicate this logic.

## Projects

```text
src/
├─ G.CETProfiler.Core/
│  ├─ Models/
│  └─ Services/
└─ G.CETProfiler.App/
   ├─ Program.cs
   └─ MainForm.cs
```

### G.CETProfiler.Core

The core is the authoritative implementation for:

- game/CET path resolution;
- manifest and payload validation;
- SHA-256 and directory fingerprints;
- persistent install state;
- CET ASI replacement/restore;
- CET binding snapshot/F11 install/restore;
- 0-Engine mode detection;
- Scheduler and adaptive Scheduler deployment;
- adaptive `init.lua` bridge injection;
- CETProfilerControls backup/deployment/restore;
- live result collection and verification;
- safe rollback after failed installation.

The core has no WinForms dependency.

### G.CETProfiler.App

The application contains two front ends over the same `IProfilerService`:

```text
no arguments -> WinForms manager
CLI arguments -> headless JSON
```

There is no separate TOTAL-specific backend.

The WinForms app may also provide **optional companion conveniences** that do not belong to the CET lifecycle core: read-only detection of an external frame-time profiler, read-only start-key reporting where a known adapter exists, explicit user-triggered launch of the configured executable, and copy-only bundling of the latest external capture beside the CET archive. This does not make any external profiler a dependency, and headless/TOTAL behavior remains CET-only.

## Package-owned files

The public package owns the payload under:

```text
payload/
├─ cyber_engine_tweaks.PROFILER.asi
├─ CETProfilerControls/init.lua
└─ 0-Engine/modules/
   ├─ Scheduler.lua
   └─ CETProfilerScheduler.lua
```

These runtime payload files are hash locked. `.gitattributes` prevents checkout line-ending conversion from changing their bytes.

## Persistent transaction

A managed install creates:

```text
Cyberpunk 2077/
└─ bin/x64/plugins/.cet_runtime_profiler/
   ├─ state.json
   ├─ cyber_engine_tweaks.ORIGINAL.asi          (when replaced)
   ├─ 0-Engine.init.ORIGINAL.lua               (when patched)
   ├─ 0-Engine.Scheduler.ORIGINAL.lua          (when replaced)
   ├─ 0-Engine.CETProfilerScheduler.ORIGINAL.lua
   └─ CETProfilerControls.ORIGINAL/            (when pre-existing)
```

The state is written atomically through a temporary file. Backups are verified before live files are replaced.

A failed install attempts rollback in reverse ownership order. If rollback itself cannot complete safely, the managed state directory is retained instead of pretending the operation succeeded.

## Restore rule

Restore first validates that backups still match their recorded fingerprints/hashes and that current live files are either the installed profiler version or the known original version.

Unexpected changes cause restore to abort before destructive replacement.

Current live CET results are collected and verified before the managed game files are restored.

## Result ownership

The native profiler writes live runtime output into CET's normal directory.

The standalone manager's `Collect` operation:

```text
find exact manifest files + scripted CET_Runtime_Profile_* output patterns
        ↓
copy to package RESULTS/<timestamp>/
        ↓
SHA-256 verify every copy
        ↓
only then delete those explicitly owned live files
```

Unrelated files in the CET directory are never swept. If the GUI has an optional frame-time companion configured, its selected/latest capture is copied under `FrameTime/` after CET collection; the external source is never modified or deleted.

TOTAL Profiler does not redirect CET's path. It consumes the completed standalone CET result after collection.

## Compatibility

The C# state model is deliberately tolerant of extra JSON fields so states produced by earlier development managers can be read.

Legacy TOTAL Profiler 0.2.19 binding rollback state and pre-existing CETProfilerControls backups are supported during restore migration.


## Recovery invariant

**Rule #1: before the profiler mutates any user-owned file or directory, preserve the original state first.**

Replaced files are copied into the persistent recovery directory and hash-verified before the live copy is changed. Added profiler-owned files record that no original existed. `bindings.json` has both a full-file backup and a surgical CETProfilerControls-node snapshot so normal restore can preserve unrelated bindings changed later.

Normal restore remains all-or-nothing and prevalidates every managed component before mutation. Emergency Restore is deliberately different: it validates and restores each component independently, skips anything ambiguous, preserves the recovery state when any item is unresolved, and writes a manual-review report.
