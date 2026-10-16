# G-LookupCache — consumers and source proof

## Three experimental bridges

1. **CET hub**: `native-static-locations/adapters/CETStaticLocationConsumer.lua`. One unique native journal consumer; owner callbacks `onNear`, optional `onFar`, **mandatory `onFallback`**. Called by **existing** 0-Engine host tick; never creates independent per-mod onUpdate hooks. Snapshot then transitions; errors unsubscribe/resnapshot/fallback. Does not arm or inject mod source automatically.
2. **G-RED hub**: `adapters/GRedStaticLocationHub.reds`. Optional `GRedRuntime` source overlay with one `ScheduledJob` shared across owners, registered only with active listeners. `GCETStaticLocationListener` retains near/far/unavailable hooks; source owner handles arms separately. Current code **not compiled with a real REDscript mod stack**; validate native declarations and scriptable system lookup/lifecycle. It is not added to `giorgioherma/G-REDruntime_Framework` production core.
3. **R4 compatibility**: `adapters/LegacyR4NativeCacheBridge.lua`. Wraps source-specific existing R4 `GCETStaticAnchors` overlay. Translates author-provided static positions to native `AddAnchor` and `Arm`, reads native state/stream, preserves old R4 Lua worker as fallback, handles per-source invalidation and source epoch reload. **Only for source-matched R4 mods**; cannot be dropped into an arbitrary live CET stack. Additional `QueryNearby` parity work was added and tested in mock.

## Generic versus source-specific

The eventual **generic Resolver** must find static source coordinates / bounded source epochs, the exact original gate and distance semantics, independent wake/side effects, ownership, mutation/invalidation, and measurable recurring cost. Generic means *not keyed on a named mod*, **not** proof-free. Match existing source via exact structure/known safe abstraction, compose with other passes, recognize original/previously optimized states, and be idempotent; reject substantial source drift.

For any one owner, choose `REPLACE_PROXIMITY_WAKE`, `SPLIT_MIXED_WAKE`, `GATE_DOWNSTREAM_ONLY`, `NO_GAIN`, or `REJECT` as set out in [the Resolver contract](../../resolver/NATIVE-STATIC-WAKE-REPLACEMENT-CONTRACT.md). Register-only is **NO_GAIN** if no author work is retired; do not sell it as optimization.

## Active gameplay/fallback requirements

- Entering a broad native wake radius should wake the original exact interaction code, not automatically start gambling, a mission or an entity interaction.
- Preserve stage/fact gates, remote quest activation, active NPC/vehicle maintenance, UI requests, save load and fast travel; if the old callback has mixed responsibilities, split it.
- Native absent, UNKNOWN, overflow, invalid loaded source, stream error or wrong owner ID → safe author fallback. Healthy native mode should retire the now-redundant *far discovery* work, not keep a full-rate shadow poller.
- `modsLIVEnow` / R4 archives are fixtures. The actual installed game-root CET path is `bin/x64/plugins/cyber_engine_tweaks/mods`. REDscript source lives under effective `r6/scripts`, subject to proven mod-manager overlay resolution. Development snapshot exclusions (Dedka, Roulette, Billiards) do not become permanent product exclusions.
- No owner pass is accepted until source-state compatibility, first-near/leave, rearm, source reload, loss recovery and representative gameplay have been shown. Use the original-fallback and fully-disabled baselines in the same scene.

## Real performance proof

Compare before/after callback registrations and author timer jobs, Lua CPU and allocation scope, native index updates/dispatch, baseline overhead of the shared service, REDscript cost where applicable and CapFrameX frametimes. Native event counts are **not** the number of saved Lua/C++ crossings. Workload reduction without gameplay-equivalent wake is not a valid pass.
