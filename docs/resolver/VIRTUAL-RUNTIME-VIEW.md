# Resolver virtual 0-Engine and native runtime view (2026-10-23)

**Status (2026-10-23):** the virtual runtime source was integrated into `dev/resolver-profiler-handoff` at `ddbaa666` following user direction; CI #1904 is green, but *full Phase 1+2 semantic/runtime-equivalence acceptance is still open*. This document describes the architectural mechanism, not a passing full-modstack validation. See the [active preservation-recovery brief](PHASE12-PRESERVATION-RECOVERY.md).

## Ownership and timing

The Profiler still operates on the **actual** CET install and transactionally restores its temporary ASI/Scheduler integration. Profiling does not install, upgrade, or repair the gameplay 0-Engine or RED4ext player-state runtime.

Resolver ANALYZE now calls `EffectiveRuntimeView.Build(modsRoot)`. It uses `FixedZeroEngineRuntime.Prepare` to project the intended 0-Engine additions in memory and `NativeRuntimeBundle.TryLoad(false)` to inspect the CI-packaged native service. This does **not** extract/copy to the game, mutate mod files, or mark projected source transforms as installed. `G-CET_Resolver.json` includes `effectiveRuntime` with the mode, staged file list, and per-capability readiness. The callback source index, semantic admission, evidence counts, and ALREADY_SATISFIED checks still read the **physical** installed author source.

GENERATE re-composes the projection from current source instead of assuming an analysis-time file fingerprint remains valid. It fails closed if 0-Engine cannot be safely composed; native support is required when generated code/static site inventory needs it. The ZIP includes the same virtual 0-Engine files and, when packaged and available, the trusted native DLL. Native positions continue to compile from source-owned author definitions, retaining dynamic fallback. Existing author components remain authoritative unless a namespaced and source-proven integration is required.

## Runtime integration contract

A Lua support filename alone is **not** an active capability. Prior to packaging, the projected `init.lua` must actually expose frame registrar, action subscription, scheduler, workload services, and native host integration.

For older 0-Engine installs without runtime exports:

- The exported author `Engine` table remains intact. A G-CET compatibility block adds the frame and action routing contracts only if absent.
- If `Schedule.Every` is not actively wired to `Scheduler.Update`, a G-CET-owned scheduler is loaded from `modules/G-CET/Scheduler.lua` (without replacing the author's `modules/Scheduler.lua`). On the source-proven stock 0.18.6 host, current code attempts to adopt the **existing central `onUpdate`** and real host lifecycle context rather than registering a second CET update hook; other hosts may require a lazy compatibility bridge. This is **not yet equivalent to Phase 1+2 by audited runtime/gameplay proof**.
- The native location host remains opt-in and fails open to the author's callbacks if the native player/location API is unavailable.
- User-provided `modules/G-CET/` files are preserved where they implement the required API. Incomplete collisions fail closed rather than silently replacing custom code.

No author Lua source SHA is used as a version gate. The profiler ASI's binary hash and backup/restore verification remain unchanged: these are safety boundaries, not mod-version locks.

## Verification and limits

`tests/VirtualRuntimeProjectionContract` checks a stock exported-table host, repeatable projected output, required router/scheduler/native-host files, unmodified live source, temporary-profiler bridge exclusion, and missing-backup refusal.

Integration CI #1904 passed on the handoff branch, but **complete-overlay semantics and gameplay acceptance are not proven**. The audited 18:14:57 output still failed reference preservation; the [single recovery brief](PHASE12-PRESERVATION-RECOVERY.md) now tracks the specific source/worker gaps and required original-stack regeneration.
