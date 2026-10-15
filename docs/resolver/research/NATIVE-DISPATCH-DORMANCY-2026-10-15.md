# Native-dispatch dormancy continuation — 2026-10-15

## Source authority and correction of scope

The 62-mod dormancy list is a **checklist for investigation**, not a replacement specification for current live mod behavior. Current live CET Lua, its implemented G-CET overlays, original mod logic and existing 0-Engine services are authoritative. All exploratory Illegal Mechanic / CyberTrials caching and incremental-stage changes from the earlier session were reverted: commit `18eb97df` exactly restored the earlier source tree at `4bddd45d` (verified zero file differences). The eight explicit SKIPs remain excluded.

The performance objective is **avoid unnecessary native CET-to-Lua invocation/registration fan-out during dormancy**. An early Lua `return` can have negligible Lua-exclusive time while still incurring native observer dispatch. Therefore counting only expensive Lua callbacks is insufficient, and blindly hoisting a Lua guard is **not** native dispatch elimination.

## Existing canonical mechanism

- CET `onUpdate` and `onDraw`: the existing `0-Engine.MakeEventRegistrar` consolidates adopted mod callbacks behind shared native frame registration. Existing event/author wake behavior and original fallback must survive.
- CET `PlayerPuppet::OnAction`: the existing `0-Engine.SubscribeAction` provides shared native observation and handles for subscription pause/unsubscribe. A generic conversion requires exact action-source proof and preserves original fallback and action consumer semantics.
- Arbitrary `Observe`/`ObserveAfter` hooks cannot be assumed to support native unsubscribe or interchangeable global routing. If a mod depends on the observer to wake, never silence it without a proven independent wake source.
- Existing source markers or an `Observe` string in a file do **not** prove a live independent native hook: many are fail-safe `Observe` registrations that run only when shared action routing is unavailable. Likewise the existing 0-Engine shared native callbacks remain necessary as long as any adopted work depends on them.

## One central Resolver admission fix

Previously `CallbackResolverService.Build` completely discarded callback rows when their measured `exclusiveMsPerSecond <= 0`. This loses high-frequency native-to-Lua registrations whose Lua handler returns immediately. The central rule now also considers callbacks with **positive measured calls/sec** and recognized frame (`event` / `onUpdate` / `onDraw`) or action-observer (`OnAction`) families.

For these newly admitted zero-exclusive rows:
- Allow **only** proven `FRAME_DISPATCH_CONSOLIDATION` and source-proven `ACTION_ROUTING_*` recipes.
- Do not admit guard-hoist, sequence polling, UI query, structural source rewrites or shared-provider substitutions just because this callback was added to the report.
- Preserve prior semantic materiality thresholds, original keybind names/configuration, native hook ordering constraints and wake paths.
- Keep unrecognized callbacks, wrappers, arbitrary game methods and zero-call registrations unchanged. Accurate classification is a prerequisite for source-pass generation; it does not force every checklist item to be patched.

Current code entrypoint: `src/G.CETProfiler.Core/Services/CallbackResolverService.cs`; regression: `tests/NativeDispatchZeroLuaCostContract.ps1`, run by `.github/workflows/build.yml`. The regression uses the actual pinned 0-Engine runtime and verifies positive frame/action native routing, completed-stage recognition, reject cases, zero-cost guard immutability and read-only originals. The test fixture initially had a PowerShell source-range arithmetic bug (`@($start,$j+1)` vs `@($start, ($j + 1))`) which caused a false generator refusal; the generator itself correctly failed closed on the truncated callback source.

## Remaining scope

This is a **global Resolver admission correction**, not completion of 62 individual dormancy proofs. Continue auditing each mod for actual independently registered native callbacks, distinguishing routed fallbacks from genuinely live registrations, and only add transitions where real author activation / safe native routing can be proven. Do not add unrelated location caches (especially not to Illegal Mechanic) or incremental “optimizations” simply because the checklist mentions them.

No installed live mod files, 0-Engine runtime, native DLL, packaged version or GitHub release version were modified by the admission correction. A green managed CI contract does not establish in-game performance or correctness; compare native registration counts and frame-time A/B in matched captures before claiming savings.
