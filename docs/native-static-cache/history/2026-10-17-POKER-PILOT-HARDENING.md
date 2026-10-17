# 2026-10-17 — reviewed StreetGamesPoker pilot hardening

**Branch:** `dev/native-static-anchors-experimental`. The source archive `EXTENDED DORMANCY(7).zip` was reviewed as an external fixture only, not copied into this repository or claimed to represent the installed game.

## Source evidence

- `bin - modsLIVEnow/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/init.lua`: SHA-256 `e4594ed8eb7a1ad0a889068db0a5be258c498fab719a6f2ab13df408e6e9af84`.
- `StreetTables.lua`: SHA-256 `a79edb7eca3632531d6a35d304857592a9f61f6f7c00337ecf9e0ff8346d0099`.
- All 40 fixed table sites were found, and all nine current reviewed insertion needles occurred exactly once in the supplied source. Local reproduction of reviewed insertion text stripped back to the exact original source and parsed with the system's Lua 5.4 library. This is source syntax/placement evidence **only**; the actual game runtime and RED4ext exports were not exercised.
- Real 0-Engine Scheduler implementation in the archive confirms source job handles define `Pause()`, `Resume()` and `Cancel()`, unlike unretirable Cron timing without cancellation.

## New changes

1. Retried source-job fail-open after a failed Resume or author state restorer (rather than caching a failed fallback as successful). Owner snapshot errors still reject native gating. Retries are per owner, not per entire stack; fresh snapshots invalidate old restoration acknowledgements.
2. Added explicit CET `onShutdown` owner-detach injection in the reviewed pilot.
3. Required shared host scheduler admission ahead of lifecycle registration to avoid permanently consuming/tombstoning source IDs if the scheduler is unavailable.
4. Added native Lua and source Python regression tests. Native CI [#39056164888](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39056164888) passed Windows/Linux at code SHA `95df3e02`. Source CI [#39056375951](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39056375951) passed at code SHA `f736f578`. Experimental metadata files were moved under the game-root CET folder. Interim source CI failures came from stale test expectations and a literal newline escape in a test; the corrected regression passed.

These are experimental, source-pinned modifications, not changes to the production Resolver/Profiler. **No gameplay load, callback-dispatch performance savings, RTTI ABI verification or REDscript compile has been established by this source check.**

## Remaining gate

Take the generated full overlay + matching experimental native DLL into a controlled opt-in game smoke test, verify native/author wake equivalence, stage disarm/rearm, FAR cleanup, same-thread snapshots, unusual fast movement, shutdown and fallback, then measure original callback retirement before generalizing the proof pattern.
