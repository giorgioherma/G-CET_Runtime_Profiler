# Shared Static Locations — standalone RED4ext experiment

This module is deliberately scoped to **world-space locations fixed within each source-owned registration epoch**, independent of the game-side CET/Lua and REDscript script runtimes. It is **not** a production optimizer yet.

## Built and tested

- Single shared C++ spatial hash with 128-metre XY grid, squared XY/XYZ distance, per-owner stable integer IDs, exact-position unchanged early return, owner-controlled armed state, transition-only change list, invalid-position fail-open, and session reset. Threading is deliberately excluded; call on the game main thread.
- Compiler converts a **source-proven installed-only JSON manifest** into a small, versioned, fixed-record little-endian file (`GCAS` v1); YAML may be used as a review/export format but is not interpreted in game. Invalid IDs, duplicates, coordinates, nonfinite numbers, unsupported metrics and radii fail closed.
- RED4ext RTTI exports the native index/state/owner subscription API and `GCETStaticCoordinatorState`. The **single native Running-state callback** now reads the game player's world position and updates the index once per running-state tick, with enter/exit snapshot invalidation. A failed/absent player getter changes state to UNKNOWN. The old `UpdatePlayer` method is a disabled compatibility stub returning -1: CET/REDscript mods **must not** feed position separately. `State==-1` means unknown, therefore **run original author path**. The native game callback still needs actual runtime validation.
- Dedicated branch + independent Windows RED4ext CI and Linux core/manifest CI. This is **not** an extension of the existing Profiler native build.

## Critical limitations

- **No source recognizer or generated mod subscriptions yet.** Resolver must first prove that original player reads and location loops are purely discovery work and that proximity is not an unsound replacement for remote activity. Static coordinates alone never activate a quest.
- The sample JSON is test data only, not actual StreetGamesPoker or Specter coordinates. The Windows artifact intentionally ships no sample `G-CET.locations`; it must not be auto-installed as an optimization.
- The independent subscriber event ring, snapshots, resync and owner tombstones are implemented and core-tested. Source-authored wake replacement, complete CET and REDscript consumer adapters, native PlayerPuppet getter validation and game save/fast-travel proof remain outstanding. It does **not** yet retire original mod hooks; no claims of measured player performance improvement.
- Zero native allocations on `Update` are a design target after index construction and reserve. Runtime measurements and allocator instrumentation have not yet proven that target in-game.
- Native binary and REDscript declarations are compiled separately. ABI, game load, CET RTTI bridge and authored mod behavior require a real game test before integration.

## Manual build/test

`cmake -S native-static-locations -B build/static -DGCET_BUILD_PLUGIN=OFF && cmake --build build/static && ctest --test-dir build/static --output-on-failure`

`python -m unittest discover -s native-static-locations/tests -p 'test_*.py'`

`python native-static-locations/tools/compile_locations.py native-static-locations/examples/sample-installed-only.json /tmp/G-CET.locations`

Windows x64 plugin: `cmake -S native-static-locations -B build/static -A x64 -DGCET_BUILD_PLUGIN=ON`, then `cmake --build build/static --config Release`. The RED4ext SDK is pinned to `f9ad5334362a8a0815baf58f47615697485feb52` for reproducibility.


## Native owner-supplied registration (experimental)

Alongside the compiled GCAS v1 manifest, `GCETStaticAddAnchor(id,x,y,z,radius,xyOnly,owner)` allows an author adapter to register **source-proven locations once** without knowing locations during Resolver manifest creation. This covers fixed positions loaded from editable JSON, owner-created location tables, and source epoch changes. New anchors are **unarmed**; the owner adapter must explicitly call `GCETStaticArm(id,true)` when that wake is applicable. Remove obsolete anchors with `GCETStaticRemoveAnchor`; reloaded coordinates need a **new unique ID** (tombstones prevent identity reuse until the service reloads). The C++ index updates spatial cells during registration, not per-frame, with ownership-aware snapshots and unknown-state recovery. Repeated new registrations consume up to the 100,000-entry capacity, including tombstones; churny spawned entities must not use this static service.

The initial source-matched R4 Lua prototype has its own experimental bridge `adapters/LegacyR4NativeCacheBridge.lua`. It can substitute native proximity checks behind the existing 0-Engine static-anchor API **without introducing a new CET per-mod update hook**, and uses the original R4 Lua work path as fail-open fallback. It is **not** automatically installed or enabled. CET consumer and G-RED scheduler bridges are also maintained under `adapters/`, but they are not yet the integrated production Resolver transforms. See [adapter contract](adapters/README.md).

## Delivery policy

The correct **eventual delivery is as an opt-in Resolver payload**, including both the RED4ext DLL+SHA and that installed stack's generated location file + exact compatible source adapters. It should not be included in the normal Profiler payload and must not silently install code on the player's live mods. Native CI builds only the DLL; Resolver CI would import a pinned successfully built DLL after the native API and mod-owned wake tests pass.

## Independent wake stream (experimental revision)

The core now includes `TransitionJournal` with a fixed 8192-event ring, independent consumer cursors/owner filters, an explicit initial-state snapshot handoff, reliable RESYNC_REQUIRED on lost events, per-owner/anchor removal, and 14 inexpensive native workload metrics available to an **optional, bounded** profiler sampler. See [subscriber and metrics contract](docs/STREAM-AND-METRICS.md) for the complete API/state machine. Linux core and manifest tests validate this code; game integration and actual retirement of the source-author callback remain **unimplemented**.

A future generic Resolver recognizer must infer source-proven static coordinates *and* equivalence of the author wake path. If the old timer/position getter only discovers proximity, its registration and redundant checks can be removed after native wake replacement is proven; if it also runs independent quest, entity or recovery work, the Resolver must split that work. For production installations, enumerate the currently effective game-root CET/REDscript sources; historical `modsLIVEnow` archives remain developer fixtures only.

## New developer-only live probe

The native plugin logs bounded coordinator diagnostics using RED4ext's own logger: a running-state registration/entry, first valid player position or the exact failure stage, and session exit. This has no per-frame logging. The passive self-probe requires a separate explicit marker `G-CET.smoke-probe` next to the DLL; it creates a radius-40m anchor once a native position is valid and logs only its NEAR/FAR changes. **Do not ship this marker in a normal Resolver-generated optimization overlay.** The test does not call any author Lua/REDscript handler and does not optimize gameplay.

An additional source-correctness invariant is now tested: after `Arm(true)` a site stays UNKNOWN until the next native position evaluation, avoiding a false FAR snapshot before the nearby wake has been processed.
