# Shared static-anchor runtime — experimental live-source overlay (2026-10-16)

**Status: implemented locally, Lua syntax + isolated synthetic tests passed; NOT a Resolver-built production pass, NOT in-game or CI verified.** Source is the exact user's `EXTENDED DORMANCY(4).zip` / `bin - modsLIVEnow` (not the 62 pristine Nexus source set or `bin - ONLY DIFF ALL PASSES`).

## Contents and deployment scope

A single six-file experimental drop-in overlay, separately supplied to the user as `G-CET_Shared_Static_Location_Cache_EXPERIMENTAL.zip`:
- modified `0-Engine/init.lua`: exports `Engine.StaticAnchors` backed by existing `SpatialHash.Update`; it introduces no extra native position read or hook;
- modified `0-Engine/modules/SpatialHash.lua`: shared service invoked from existing position update; replaces ordinary spatial sets' per-poll `nowActive={}` with reusable epoch-marked active entries, deferring enter callbacks until grid iteration finishes;
- new `0-Engine/modules/GCETStaticAnchors.lua`: owner/source registry, dedupe, 256m common cell index, squared 3D checks, 6-frame / >15m-movement poll, once-per-transition owner near/far notifications, source invalidation, session reset and unavailable fallback;
- modified `StreetGamesPoker/init.lua`: register immutable author-loaded table centres, retire its separate 1s far-discovery scheduler only if shared registration succeeds, preserve near world/chair jobs and all actual poker/UI activity;
- modified `VehicleRepairGarages/init.lua`: register source-authored garage coordinates, bypass far+idle `ProcessRepairState` native vehicle probes if safe, retain mounted vehicle/mappin path, active repairs, post-load refresh, safety lift and session state;
- modified `tunnel_rescue/init.lua`: register only `L.rescue.surface` / `brother` from already loaded JSON (the two sites are world-space, not origin-relative). Replace direct player-position/distance probing during source-proven far states only, retain quest facts, remote wake, TRQHooks, rescue, recovery and cleanup.

All author locations remain authoritative. Game native handles and quests are not cached as immutable anchors. No proximity signal auto-starts a quest. The native frame hook is unchanged. Native C savings must not be claimed.

## Evidence and limitations

The six generated Lua files passed a Lua 5.4 parser. Isolated executable contract using the actual `GCETStaticAnchors` and `SpatialHash` modules passed: duplicate / conflicting-source rejection, 3D proximity owner transitions, coexisting stock spatial observer, invalidation / source replacement, save/session reset, callback-failure fail-open, and 10,000 synthetic steady-state frame updates with a reused position object (0.0 KiB measured incremental Lua heap in that test). This is **not** a LuaJIT G-GC measured result or proof of net savings; the shared worker is new CPU/GC tax to measure with comparable captures.

The experimental ZIP contains the exact changed live source files, `manifest.json` with input/output SHA256 per file, `docs/UNIFIED-LIVE-DIFF.patch`, an installation/recovery and gameplay verification guide, and standalone `tests/shared_static_contract.lua`. It is separate from GitHub CI and was not installed into the user's game.

## Non-negotiable next steps

1. Test actual CET loading, save changes, fast travel, rapid vehicle approach, and all Poker/Garage/Tunnel first interactions, ongoing activity, recovery and shutdown. Restore originals if any transition fails.
2. Profile matched baselines and experimental runs. Include the shared spatial worker's CPU, allocations, native reads, mod-owned update invocations, LuaJIT allocator pressure and p95/p99 frametimes. No game measurements exist yet.
3. Move this source-reviewed runtime experiment into owner-scoped, version-fluid `ModPasses/*.cs` plus 0-Engine infrastructure generation. Prove **pristine original → full one-pass**, current partial/live → upgrade, formatting drift and idempotence; keep the three author sources individually owned. Existing Resolver code was intentionally **not modified** by this experiment.
4. Extend owners only after independent author activation/remote-wake and mutable-source invalidation proofs, as in [shared registry contract](../resolver/SHARED-STATIC-LOCATION-REGISTRY.md). Ziggy/CyberTrials next; mixed pointpacks/quests later. Do not pause active native subscribers without complete independent wake.

References: [LATEST-WORK](../LATEST-WORK.md), [workload services](0-ENGINE-WORKLOAD-SERVICES.md), [source audit](../resolver/SHARED-STATIC-LOCATION-REGISTRY.md).
