# Experimental native ActionRouter interest gate — scope and validation

## Observed motivation (not a performance claim)

The October 16 ~378.4s capture shows 0-Engine `onUpdate` ~21.32 Lua ms/s and `PlayerPuppet::OnAction` ~18.52 Lua ms/s (~523,350 native events, ~1,383/s). This includes nested execution and collector stalls. The earlier ActionRouter already extracts `CName.value` and avoids `GetType` when nobody subscribes. Adding another Lua-only early return will **not** eliminate native argument conversion or other CET mods' observers. GC-positive growth in a callback execution window is not an allocation instruction or a mod ownership proof.

## Specific experiment

- `gcet_native_action_gate` is a **portable C++20 DLL** with stable C ABI v1, no CET SDK, game memory reads, profiler hooks, injector or network calls.
- 0-Engine can explicitly publish a **complete current list** of its own active exact action names and presence of active `*` subscribers. The native index makes an action-name allow/skip decision without allocations on its query path (C++20 transparent heterogeneous lookup is required; C++17 would allocate a temporary string).
- Safe defaults: before a full snapshot, while rebuilding, after failed or malformed synchronization, with unknown name, or when a wildcard is active, decisions return `deliver`. Only a proven complete exact-index miss returns `skip` **for this ActionRouter subscriber**.
- Lifecycle: on every `Subscribe`, `SetActive`, `Pause`, `Resume`, `Cancel` or unsubscribe, **disarm before mutation** and rebuild from the entire authoritative subscriber set after mutation. This prevents stale native state from creating false negatives. The experimental copy of `ActionRouter.lua` has these hooks; stock ActionRouter remains untouched.
- `GCETNativeActionValues.lua` exposes `Load(explicitDllPath)`, `Disarm()`, `Refresh(router)`, `ShouldDeliver(actionName)` and `GetValues()` with opt-in `ffi.load` and safe absence fallback. It adds no frame callbacks or global Observes. The experimental ActionRouter exposes `AttachNativeValues(bridge)` and `GetInterestSnapshot()`.
- `GetValues()` includes `available`, `armed`, `wildcard`, `activeExactActionNames`, `snapshotRevision`, `decisionCalls`, `allowed`, `suppressed`, `failOpen`, and **preDispatchHook=false**. These are **decision counters**; there is no measured native marshalling saved, GC removed or FPS gain in this phase.

## Additional 0-Engine values

The optional `GCETNativeEngineValues.Read(engine, nativeActionValues)` returns an on-demand snapshot of existing `ActionRouter`, `FrameListeners`, `WorkQueue`, `PhasePlanner` and `StateSignals` `GetInfo()` tables, plus native action-interest counters if loaded. Reads are protected and unknown services stay `nil`. This is **not installed as a frame hook**: making five new GetInfo calls every frame would add avoidable Lua work and allocations. None of these scalars proves a native frame subscriber can sleep. Independently prove all wake transitions before implementing native onUpdate suppression.

## Critical host-side limitation

This is **NOT yet an early CET native pre-dispatch hook** and must not be represented as a performance fix. When invoked by Lua after CET has already delivered `PlayerPuppet::OnAction`, native-to-Lua marshalling has already happened. A later independently reviewed CET-side experiment could consult this filter *only for 0-Engine's shared OnAction subscription*, prior to converting arguments, and only if the installed CET build exposes that precise per-subscriber boundary. It must never cancel globally for other mods, suppress game-native execution, or alter original callback ordering. If CET exposes only a shared method-level hook or name decoding is unsafe, the early suppression experiment **must fail closed in development** and not ship. The read path accepts a resolved native action name; it never casts uncertain game structures or trusts an unproven RTTI class pointer.

## Fail-open vs fail-closed

Runtime **delivery is fail-open**: every uncertain case invokes the original callback. Deployment/authorization is **fail-closed**: don't enable the hook or claim gains without exact host proof, complete subscribers, separate game tests, and stable ABI. In particular a single `*` subscription prevents action-name misses from being skipped.

## Test matrix

1. Unit tests: ABI, null inputs, unarmed, incomplete snapshot, explicit matches/misses, duplicates, wildcard, disabled state and thread interleaving. Run `cmake -S . -B build -DBUILD_TESTING=ON`, `cmake --build build`, `ctest --test-dir build --output-on-failure`.
2. Windows build: `cmake -S . -B build-win -A x64 -DBUILD_TESTING=ON`; `cmake --build build-win --config Release`; `ctest --test-dir build-win -C Release --output-on-failure`. This must be compiled on Windows; the Linux `.so` is **not usable in the game**.
3. Lua bridge: test disabled FFI, absent DLL, mismatched ABI, incomplete snapshot, subscription active/inactive mutations, UI menus, session reload and native-gate load failure. No Lua runtime test is claimed from C++ CTest.
4. Before any native CET integration: audit ownership of the shared OnAction subscriber, all other mods' subscribers, fallback chains, event name identity, method pointer lifetime and hot loop thread model. Deliberately test paused/resumed action routing and original callback order.
5. Before any installation: hash/pin separately built Windows DLL and optional PDB, back up the original CET/0-Engine, compare matched gameplay, onUpdate/onAction counts, bounded native unscoped allocation/marshalling, G-GC, and per-frame statistics. Test menu/game/world load/capture-off/restore and action bindings. No native profiler or Resolver CI changes in this experiment.

## Open performance questions

The existing Lua ActionRouter exact table lookup may outperform an FFI call. Measure native-path overhead *before* including a gate after marshalling. The only compelling potential win is **avoiding the native-to-Lua transition before argument construction**, which is not implemented here. Counter values don't establish that. The 0-Engine `onUpdate` state/proximity and Scheduler work is an independent investigation and is intentionally not modified.
