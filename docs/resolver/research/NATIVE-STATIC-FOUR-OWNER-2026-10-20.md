# Native static-location source integration — 2026-10-20

**Current status:** Four source-specific Resolver semantic stages and the opt-in 0-Engine native event host are implemented on `dev/resolver-profiler-handoff`. Lua host regression and managed CI gates are maintained in the normal build. **This is not equivalent to a gameplay-verified combined overlay.** Native RED4ext DLL remains independently installed and was not changed or bundled into the Profiler.

## Architecture / bounds

- The native RED4ext Running-state coordinator owns all player-position samples and all static distance evaluations. CET **must not** call the retired `GCETStaticUpdatePlayer` writer.
- `resolver-runtime/0-Engine/modules/GCETNativeLocationHost.lua` is an optional, additive module. It does not create a periodic job until an eligible owner is attached to an available native provider.
- At most one 10 Hz 0-Engine Lua consumer drains the native subscription for all four owners. It handles complete owner snapshots, per-owner multi-site aggregation, UNKNOWN, ring overruns, registration errors, and fail-open author callbacks. This is **not one per-mod native poller**.
- `FixedZeroEngineRuntime` installs the additive bridge via the existing source-preserving 0-Engine runtime compatibility path and checks collisions for its private support module. No source-file replacement of unknown 0-Engine installations is permitted.
- Original author code always runs before the first valid native snapshot. Native outages cause the original path to resume. No false FAR from unknown native state.

## Source-specific decisions

| Owner | Native owner | Exact behavior preserved |
|---|---:|---|
| StreetGamesPoker | 10031 | Existing `poker.world_far`, `poker.world_near`, `poker.seat_near` and `poker.realtime`. Native only retires the far rediscovery lane; completed sessions, seats, chair cleanup and load/menu readiness continue. |
| tunnel_rescue | 10032 | Two 150 m source-owned sites; bypass only the redundant far-outside player-distance getter. Quest state, journal, active session, recovery, return path and user input retain original callbacks. |
| illegal_mechanic_phase1a | 10033 | Native 100 m matrix sites skip scans only when no mechanic is live or pending. Original 75 m spawn, 95 m despawn, pending resolution, entity cleanup, precise Vector4 math retained. |
| Dedka Auto Shop | 10034 | Two 200 m zones are replaced only after a valid native owner snapshot. On unavailable native service, original 0-Engine zones are recreated. Nearby 5 Hz shop detail and mount/unmount event logic remain. |

Source injectors: `src/G.CETProfiler.Core/Services/NativeLocationFourOwnersSource.cs`. Semantic admission: four `native-location-*` rules in `resolver-knowledge/semantic-library.json`. Keep the patches isolated from the original author mods: do not copy the earlier standalone Python-generated ZIP into player installations.

## Acceptance

1. **CI:** product/build contracts, semantic library exact injector parity, 0-Engine runtime packaging, and `NativeLocationFourOwnersLuaContract.lua` near/far/UNKNOWN/resync simulation.
2. **Generated pass:** test against the current installed, already-optimized mods and verify four stage markers plus one shared 0-Engine host; reject source drift. Do not claim all four have been emitted until this is checked.
3. **In game:** verify initial spawn NEAR and FAR, active poker session, chair despawn, first-ready wake, tunnel quest journey, mechanic pending spawn, Dedka UI, fast travel and save reload, and an intentional service-unavailable fail-open case. Recheck Lua GC, native-Lua boundary calls and allocation operation counts, not just FPS.

Native player tracking, including far/near and fast travel, was reported as functioning in the user's own game, but **these four integrations have not yet been tested in game**.

## Delivery update — 20 October 2026

The native bootstrap has been corrected on
`dev/native-fixed-cache-player-state` (code commit `71aace1`, native
CI75 passed). A **missing** `G-CET.locations` now enables an empty
registration index; a **present malformed** manifest still fails closed.
The native C++ regression exercises missing versus malformed and live
dynamic add/arm/update.

Resolver commit `173c197` on `dev/resolver-profiler-handoff` stages
`red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll` beside
optimized CET/0-Engine mods in the generated **game-root** ZIP. The
Resolver package imports the DLL from a successful native CI run pinned
to the exact native source commit; its checksum protects only the native
binary transfer, not arbitrary installed mod source versions. The
generated manifest records native SHA as provenance only.

No empty GCAS file is required in the normal ZIP. All four proven owners
already source-register their sites through `lookup:attach` and the
shared 0-Engine native host. No duplicate generated coordinates are
invented. The service is not installed during capture solely for
Resolver source discovery; profiler ASI restoration remains unchanged.

**Validation boundary:** successful native CI alone does not prove
combined in-game source behavior. Check the main Resolver CI artifact
and generated ZIP before declaring packaging green; run the existing
four-owner initial near/far, UNKNOWN/fallback, fast-travel and quest
lifecycle gameplay checks. The unified two-page app UI is separate
future product work, not part of this runtime packaging commit.
