# WORKFLOW — how and where to work

## Startup (every new chat)

1. Read [CORE](CORE.md), [../LATEST-WORK](../LATEST-WORK.md), this file and [DOCUMENTATION-RULES](DOCUMENTATION-RULES.md). Read only the relevant technical detail documents. **Do not** consume the 150k-character global work log to rediscover this task.
2. Resolve the **current GitHub branch HEAD**, tree and recent `native-static-locations.yml` workflow runs. The code, tests, generated payload and actual CI logs outrank historical notes.
3. Identify whether the next dependency is developer implementation, Windows CI or **user-only game validation**; do not repeatedly request profiler passes when a native smoke test is needed.

## Code ownership (strict)

| Change | Path / method |
| --- | --- |
| C++ spatial index, runtime IDs, tombstones | `native-static-locations/include/StaticIndex.hpp`, `src/StaticIndex.cpp` |
| Native session/position validity | `include/PositionCoordinator.hpp`, `src/PositionCoordinator.cpp` |
| RED4ext RTTI entrypoints and Running callbacks | `src/Plugin.cpp` |
| Per-consumer ring/resync | `include/TransitionJournal.hpp`, `src/TransitionJournal.cpp` |
| Runtime/compiled manifest parser | `tools/compile_locations.py`, `tools/discover_live_locations.py` |
| CET host bridge / source-matched R4 fallback | `adapters/CETStaticLocationConsumer.lua`, `adapters/LegacyR4NativeCacheBridge.lua` |
| Optional G-RED consumer | `adapters/GRedStaticLocationHub.reds` (not the main G-RED repo) |
| Interface contract | `examples/G-CET-StaticLocations.reds`, `docs/STREAM-AND-METRICS.md` |
| Tests | `tests/StaticIndexTests.cpp`, `tests/test_compiler.py`, `tests/*ConsumerTests.lua`, `tests/LegacyR4NativeCacheBridgeTests.lua` |
| This project handoff/current progress | `docs/native-static-cache/HANDOFF/`, `docs/native-static-cache/LATEST-WORK.md` |
| Durable implementation proof | `docs/native-static-cache/details/` and `history/` |

## Safe implementation loop

1. Verify the **actual existing source** and ABI; choose one bounded change with a clear expected removal of old work. Do not confuse a proposed architecture with delivered code.
2. Implement in the isolated branch; preserve game-root-only payload layout. Generic recognizers must be mod-name-independent, source-proven and idempotent; semantic exceptions are targeted, not a giant mod-specific library.
3. Add reproducible C++/Python/Lua tests, including **negative/unknown/missed-event** cases. REDscript requires an actual compiler and game-side tests, not merely textual inspection.
4. Push/commit and inspect the **matching SHA's** independent CI, both Windows and Linux jobs. On failure read the job logs, fix, rerun. Documentation-only edits outside `native-static-locations/` should not cause product/native CI.
5. For game-dependent checks, prepare an **explicitly labeled experimental isolated package** and an exact smoke-test checklist; never call a compile-only result game-stable. Don't install a location payload from sample fixture coordinates as a real mod optimization.
6. Record test gates in [../LATEST-WORK](../LATEST-WORK.md), stable technical findings in `details/`, dated closed experiments in `history/` per [DOCUMENTATION-RULES](DOCUMENTATION-RULES.md). Commit documentation before ending the work.
7. Tell the user **what was actually implemented, which CI run verified it, what remains blocked, and whether the next move is theirs**.

## Tests/commands

Local pure core: `cmake -S native-static-locations -B build/static -DGCET_BUILD_PLUGIN=OFF && cmake --build build/static && ctest --test-dir build/static --output-on-failure`.

Python: `python -m unittest discover -s native-static-locations/tests -p 'test_*.py' -v`.

Lua: `lua5.4 native-static-locations/tests/CETStaticLocationConsumerTests.lua` and `lua5.4 native-static-locations/tests/LegacyR4NativeCacheBridgeTests.lua`.

Windows SDK build uses the **pinned SDK hash in `native-static-locations/CMakeLists.txt`**. CI is `.github/workflows/native-static-locations.yml`. Product release CI is not this workflow.

## Boundary on game testing

Only the user can verify real game loads, player getter health, REDscript compilation with installed mods, first-near interaction, fast travel, save/load and gameplay equivalence. Automated tests prove the implementation contracts **only within their mocks and C++ unit boundaries**.
