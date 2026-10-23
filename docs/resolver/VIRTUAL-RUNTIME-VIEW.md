# Resolver virtual 0-Engine and native runtime view (2026-10-23)

**Status:** isolated implementation on `dev/virtual-runtime-view-20261023`; target after user approval: `dev/resolver-profiler-handoff`. Do not merge automatically.

## Ownership and timing

The Profiler still operates on the **actual** CET install and transactionally restores its temporary ASI/Scheduler integration. Profiling does not install, upgrade, or repair the gameplay 0-Engine or RED4ext player-state runtime.

Resolver ANALYZE now calls `EffectiveRuntimeView.Build(modsRoot)`. It uses `FixedZeroEngineRuntime.Prepare` to project the intended 0-Engine additions in memory and `NativeRuntimeBundle.TryLoad(false)` to inspect the CI-packaged native service. This does **not** extract/copy to the game, mutate mod files, or mark projected source transforms as installed. `G-CET_Resolver.json` includes `effectiveRuntime` with the mode, staged file list, and per-capability readiness. The callback source index, semantic admission, evidence counts, and ALREADY_SATISFIED checks still read the **physical** installed author source.

GENERATE re-composes the projection from current source instead of assuming an analysis-time file fingerprint remains valid. It fails closed if 0-Engine cannot be safely composed; native support is required when generated code/static site inventory needs it. The ZIP includes the same virtual 0-Engine files and, when packaged and available, the trusted native DLL. Native positions continue to compile from source-owned author definitions, retaining dynamic fallback. Existing author components remain authoritative unless a namespaced and source-proven integration is required.

## Runtime integration contract

A Lua support filename alone is **not** an active capability. Prior to packaging, the projected `init.lua` must actually expose frame registrar, action subscription, scheduler, workload services, and native host integration.

For older 0-Engine installs without runtime exports:

- The exported author `Engine` table remains intact. A G-CET compatibility block adds the frame and action routing contracts only if absent.
- If `Schedule.Every` is not actively wired to `Scheduler.Update`, a G-CET-owned scheduler is loaded from `modules/G-CET/Scheduler.lua`. This does not replace `modules/Scheduler.lua`. Its one native `onUpdate` bridge is lazy (only after a client schedules work); its client API adds `Schedule` to the return value of `Engine.Register` when needed.
- The native location host remains opt-in and fails open to the author's callbacks if the native player/location API is unavailable.
- User-provided `modules/G-CET/` files are preserved where they implement the required API. Incomplete collisions fail closed rather than silently replacing custom code.

No author Lua source SHA is used as a version gate. The profiler ASI's binary hash and backup/restore verification remain unchanged: these are safety boundaries, not mod-version locks.

## Verification and limits

`tests/VirtualRuntimeProjectionContract` checks a stock exported-table host, repeatable projected output, required router/scheduler/native-host files, unmodified live source, temporary-profiler bridge exclusion, and missing-backup refusal.

Runtime behavior, C# compile, Lua syntax, and complete-overlay composition still require CI on the test branch and gameplay validation before approval. The new projection does not guarantee that every existing semantic worker is effective or that all 140 substantive mod-file differences from the prior ZIP comparison are reconciled.
