# G-CET experimental native ActionRouter interest index

**Prototype, not a game-ready native patch.** Isolated from the native profiler, production 0-Engine runtime, Resolver, main CI and native CI. Read [design and safety](docs/DESIGN.md) before enabling any integration.

This C++20 library tracks the complete **active** action subscriptions owned by 0-Engine's ActionRouter and answers exact native string membership queries without allocations in the query loop. It is designed to support a future *per-subscriber native pre-marshalling* experiment. It does not intercept CET or alter game callbacks by itself; it cannot independently remove existing GC stalls.

The on-demand 0-Engine service-value reader `0-Engine/GCETNativeEngineValues.lua` exposes existing ActionRouter, FrameListeners, WorkQueue, PhasePlanner and StateSignals `GetInfo` values for manual diagnostics. It does not run every frame or authorize native suppression.\n\nBuild with CMake (MSVC x64 on Windows) and run the included CTest suite. The experimental Lua bridge `0-Engine/GCETNativeActionValues.lua` uses an **explicit path** to load the DLL by LuaJIT FFI. An additional file `0-Engine/ActionRouter.experimental.lua` is a separate modified copy of the pinned ActionRouter for snapshot publication; **do not overwrite the installed `0-Engine/modules/ActionRouter.lua`** during this development phase.

Tracked experiment uses `native-experiments/zero-engine-action-gate/` only; there are no changes to `.github/workflows/*`, `src/G.CETProfiler*`, production `resolver-runtime/0-Engine/*`, or pinned CET/DLL hashes.

Validation status: [2026-10-16 results](docs/VALIDATION.md). Windows/CET integration remains pending; the tested shared object is not a game-installable DLL.

## Isolated Windows test

This experimental branch also contains a path-scoped **standalone Windows x64** Action Gate test workflow at `.github/workflows/native-action-gate-experiment.yml`. It builds the decision library, runs native safety tests and microbenchmarks, and publishes a standalone native DLL. **Do not install it over CET**: the library does not intercept `OnAction`. Read `docs/NATIVE-CET-SHADOW-TEST-PLAN.md` for the native pre-Lua proof and shadow test required before proposing an actual performance A/B.
