# Next test: CET native-to-Lua action pre-dispatch feasibility (no suppression)

**Status: DESIGN ONLY.** This plan is for a *separate follow-up native host probe*. Do not install this standalone library as `cyber_engine_tweaks.PROFILER.dll`, do not replace CET, and do not overwrite the production 0-Engine files. No pre-Lua host hook exists in the current experiment.

## Objective
Determine whether a safe **per-subscriber, before-marshalling** boundary exists for 0-Engine's one `PlayerPuppet::OnAction` callback. If it exists, quantify the action-name miss rate and bounded LuaJIT marshalling allocation work that this one subscriber could avoid, without suppressing any action in the first experiment.

## Phase 0 — executable standalone checks
- Compile native decision library on Windows x64 with MSVC and run CTest, including fail-open incomplete/unknown/wildcard and concurrent rebuild.
- Run `gcet_native_gate_bench` for hit, miss, wildcard and disarmed lookup throughput. Standalone lookup time is **not** CET or Lua marshalling time.
- Confirm DLL C exports/ABI 1 and test optional LuaJIT `ffi.load` with explicit file path in a dedicated offline harness, not installed 0-Engine.
- Keep these binaries standalone. The `exp/native-zero-engine-hotpath-20261016` branch has a path-scoped **experiment-only** Windows workflow. It does not touch `build.yml`, `native-profiler-rebuild.yml`, the production profiler, Resolver or payload DLL.

## Phase 1 — shadow-only native CET proof, separate development step
1. Locate the exact CET `FunctionOverride` observer iteration and prove where native argument conversion occurs **relative to** the 0-Engine subscriber call. Current native patch has `ObserveAfter` scopes around `call->ScriptFunction(as_args(*apOrigArgs))`; **do not infer** that a native action name can be safely read merely from the method hash.
2. Prove and cache the identity of **only 0-Engine's own subscriber**, without dereferencing invalid class-parent RTTI pointers. Never suppress the engine's `OnAction` method or callbacks belonging to other mods.
3. Identify a supported, exact native `ListenerAction` name access path **before** constructing/pushing Lua arguments. If native decoding requires the same conversion or an unsafe object cast, stop the experiment. Unknown/unreadable names must deliver.
4. Mirror the **complete currently active** 0-Engine ActionRouter exact-action interests, including dynamic subscriber pause/resume/cancel and wildcard; on every mutation disarm before rebuild. Changes during dispatch must not create false-negative decisions.
5. **Shadow mode first**: for every incoming event, count `seen`, `wouldDeliver`, `wouldSkip`, `unknownName`, `incompleteSnapshot`, `wildcard`, elapsed native decode/lookup time, and bounded sampled pre-Lua allocation pressure. Always invoke the original Lua callback, regardless of the decision.
6. Verify untouched mod/callback order, transitions, action bindings, session reload, capture on/off, and proper restore. Compare exact marshalling boundaries without adding overlapping sampled totals. Do not count other mods' shared `OnAction` allocations as 0-Engine savings.

## Phase 2 — separately gated A/B
Only after Phase 1 proves the native name source and individual subscriber boundary, enable an **explicit opt-in** skip solely for 0-Engine's observer on complete exact misses. Compare matched stress runs: native boundary call counts and samples, number of actual skips, Lua callback invocations, G-GC bytes/cycle/step timing, frame-time percentiles, control equivalence and stability. On uncertainty, runtime **delivery** must fail open; authorization to enable skipping must fail closed.

## Decision
- **Go** only if shadow miss fraction is material, the path is truly pre-marshalling, the native decoder costs less than the measured avoided transition, and game tests show no functional regression.
- **No-go** if name decoding requires Lua conversion, if any wildcard makes all misses deliver, if exact mod ownership cannot be proven, or if there is no per-subscriber interception point.

This experiment is **not** an optimization of GC stalls or 0-Engine's `onUpdate` state/scheduler work. Keep native GC attribution and ActionRouter skip accounting separate.
