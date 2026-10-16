# Shared Static Locations — standalone RED4ext experiment

This module is deliberately scoped to **fixed world-space locations only**, independent of the game-side CET/Lua and REDscript script runtimes. It is **not** a production optimizer yet.

## Built and tested

- Single shared C++ spatial hash with 128-metre XY grid, squared XY/XYZ distance, per-owner stable integer IDs, exact-position unchanged early return, owner-controlled armed state, transition-only change list, invalid-position fail-open, and session reset. Threading is deliberately excluded; call on the game main thread.
- Compiler converts a **source-proven installed-only JSON manifest** into a small, versioned, fixed-record little-endian file (`GCAS` v1); YAML may be used as a review/export format but is not interpreted in game. Invalid IDs, duplicates, coordinates, nonfinite numbers, unsupported metrics and radii fail closed.
- RTTI global native functions exposed for future REDscript and CET adapter consumption: `GCETStaticReady`, `Count`, `Arm`, `UpdatePlayer`, `State`, `ResetSession`, `ChangeCount/Id/Near`. No new per-mod hooks. A single external coordinator must supply the authoritative game position. `State==-1` means unknown, therefore **run original author path**.
- Dedicated branch + independent Windows RED4ext CI and Linux core/manifest CI. This is **not** an extension of the existing Profiler native build.

## Critical limitations

- **No source recognizer or generated mod subscriptions yet.** Resolver must first prove that original player reads and location loops are purely discovery work and that proximity is not an unsound replacement for remote activity. Static coordinates alone never activate a quest.
- The sample JSON is test data only, not actual StreetGamesPoker or Specter coordinates. The Windows artifact intentionally ships no sample `G-CET.locations`; it must not be auto-installed as an optimization.
- The initial native API exposes the *most recent update batch*; a future subscribed consumer must reconcile generation and state after missed ticks or save loads. Do not claim durable per-subscriber delivery until implemented. It performs neither hook interception nor actual script callback retirement yet.
- Zero native allocations on `Update` are a design target after index construction and reserve. Runtime measurements and allocator instrumentation have not yet proven that target in-game.
- Native binary and REDscript declarations are compiled separately. ABI, game load, CET RTTI bridge and authored mod behavior require a real game test before integration.

## Manual build/test

`cmake -S native-static-locations -B build/static -DGCET_BUILD_PLUGIN=OFF && cmake --build build/static && ctest --test-dir build/static --output-on-failure`

`python -m unittest discover -s native-static-locations/tests -p 'test_*.py'`

`python native-static-locations/tools/compile_locations.py native-static-locations/examples/sample-installed-only.json /tmp/G-CET.locations`

Windows x64 plugin: `cmake -S native-static-locations -B build/static -A x64 -DGCET_BUILD_PLUGIN=ON`, then `cmake --build build/static --config Release`. The RED4ext SDK is pinned to `f9ad5334362a8a0815baf58f47615697485feb52` for reproducibility.

## Delivery policy

The correct **eventual delivery is as an opt-in Resolver payload**, including both the RED4ext DLL+SHA and that installed stack's generated location file + exact compatible source adapters. It should not be included in the normal Profiler payload and must not silently install code on the player's live mods. Native CI builds only the DLL; Resolver CI would import a pinned successfully built DLL after the native API and mod-owned wake tests pass.

## Independent wake stream (experimental revision)

The core now includes `TransitionJournal` with a fixed 8192-event ring, independent consumer cursors/owner filters, an explicit initial-state snapshot handoff, reliable RESYNC_REQUIRED on lost events, per-owner/anchor removal, and 11 inexpensive native workload metrics available to an **optional, bounded** profiler sampler. See [subscriber and metrics contract](docs/STREAM-AND-METRICS.md) for the complete API/state machine. Linux core and manifest tests validate this code; game integration and actual retirement of the source-author callback remain **unimplemented**.

A future generic Resolver recognizer must infer source-proven static coordinates *and* equivalence of the author wake path. If the old timer/position getter only discovers proximity, its registration and redundant checks can be removed after native wake replacement is proven; if it also runs independent quest, entity or recovery work, the Resolver must split that work. Do not select mods from historical/original-source archives merely because a directory exists there: use the user's present `modsLIVEnow` scope and explicit exclusions.
