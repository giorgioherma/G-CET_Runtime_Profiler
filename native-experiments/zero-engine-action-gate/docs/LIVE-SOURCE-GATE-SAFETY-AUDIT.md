# Live-source gate safety audit — 2026-10-16

## Scope and provenance

Inspected user-supplied `EXTENDED DORMANCY(5).zip`, specifically `bin - modsLIVEnow/x64/plugins/cyber_engine_tweaks/mods/0-Engine/` (live source), not the Nexus originals as a presumed runtime baseline. The ZIP also contains two reference captures: `CET-CAPTURE_before_lua_to_C(BASELINE)` and `CET-CAPTURE_lua_to_C-first_round_5_mods`. These are different gameplay samples, not paired controlled A/B evidence of native gate savings.

This document is strictly an experiment audit, not a production implementation.

## Critical semantic finding: the observer serves two consumers

Live `0-Engine/init.lua`, lines 1332–1340:

```lua
Observe('PlayerPuppet', 'OnAction', function(player, action, consumer)
    if not isSessionLoaded then return end
    ActionRouter.Dispatch(player, action, consumer)
    if isPlaying and not State.inMenu then
        Events.PlayerAction:trigger(action)
    end
end)
```

Therefore **an ActionRouter exact-name miss does not authorize suppressing the complete 0-Engine observer**. The legacy `Events.PlayerAction` path is independent of router membership. Its event subscriptions and dynamic lifetimes must be audited; simply searching the shipped files for literal `PlayerAction` cannot prove nobody subscribes dynamically.

**Native gate rule:** while that event path can fire and may have listeners, never suppress the whole observer on router misses. Establish a complete live interest model or introduce a separately reviewed split path preserving event ordering and behavior, followed by fresh testing. Shadow mode always delivers.

## Snapshot and dispatch edge cases

Live `modules/ActionRouter.lua`:
- Lines 129–169: `Subscribe` accepts list or scalar action names, defaults to wildcard `*`, deduplicates per subscription, and inserts into exact/wildcard buckets.
- Lines 172–185: `unsubscribe`/`Cancel`, `SetActive`, `Pause`, `Resume` alter effective interest.
- Lines 64–90 and 189–209: compaction is deferred while dispatch is active; cancellation and active state are checked at invocation.
- Lines 193–209: unmatched exact bucket plus empty wildcard returns *inside Lua*, after CET has invoked this callback and materialized arguments.
- The legacy `PlayerAction` trigger runs **after** ActionRouter dispatch and has a separate playing/menu condition.

The native mirror must fail open during registration changes, reloads, incomplete snapshots, unknown names, unknown subscriber identity, and any potentially live legacy listeners. A bucket containing only paused or canceled subscribers must not be mistaken for an interested live subscriber when computing *would skip*, although false-positive delivery is safe.

Other live mods (including QuestRunner, marmurbank, immersive_third_person and truck/interaction UI mods) contain their own `PlayerPuppet::OnAction` observers or fallbacks. Never globally filter the engine method or another mod's callbacks.

## Capture context — not controlled gate results

| Capture | Duration | 0-Engine calls/s | 0-Engine exclusive ms/s |
|---|---:|---:|---:|
| `CET-CAPTURE_before_lua_to_C(BASELINE)` | 269.436 s | 1394.624 | 37.286174 |
| `CET-CAPTURE_lua_to_C-first_round_5_mods` | 478.854 s | 1304.282 | 35.463902 |

These differ in duration/gameplay and represent broader systems. The samples cannot isolate skipped `OnAction` calls or native-to-Lua allocations avoided. No claimed FPS improvement.

## Shadow-only host integration checklist

1. Identify exact registered 0-Engine observer (not merely class and function name) and its lifetime.
2. Prove per-subscriber decision is executed **before** per-callback argument construction. Shared argument marshalling performed once before all subscribers would eliminate expected per-subscriber marshalling savings.
3. Prove safe read of action name from original native argument metadata without falling back through Lua. Unknown/invalid names deliver.
4. Mirror router interests *and* the legacy event path, including dynamically registered subscribers, ensuring explicit disarm **before** each transition.
5. Observe decision counts separately: `routerMiss`, `legacyRequired`, `wouldSkipWholeObserver`, `unknownName`, `unarmed`, `incompleteSnapshot`. Always call Lua in shadow mode.
6. Measure native decode and decision overhead; separately sample **per-subscriber** allocation boundary, avoiding shared-method totals.
7. Only after the above passes, build optional suppression on a distinct test branch/artifact; do not touch production profiler, Resolver or CI.

## Immediate disposition

**No-go for observer suppression based only on ActionRouter exact membership.** **Go for observational instrumentation design**, with native hook implementation conditional on locating a genuinely pre-marshalling per-subscriber site. No native hook or installable CET DLL is supplied by this audit.
