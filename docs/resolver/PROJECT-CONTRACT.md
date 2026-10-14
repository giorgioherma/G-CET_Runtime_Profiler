# G-CET engineering contract

> **Startup/build contract:** [CORE.md](../HANDOFF/CORE.md) is the first-read summary, [BUILD-PIPELINE.md](../HANDOFF/BUILD-PIPELINE.md) is the authoritative two-workflow procedure (native CI = DLL only; main CI = Profiler + Resolver packages), and [PRODUCT-STATUS.md](../product/PRODUCT-STATUS.md) is the current capabilities/remaining-work overview. The details below are the durable Resolver engineering contract.

## Goal and historical results

G-CET Runtime Profiler measures heavily modded Cyberpunk 2077 CET/Lua; G-CET Resolver applies proven optimizations **to original/current mod source**. This project began with compatible 0-Engine integration and semantic optimization of measured big offenders, then systematic testing of essentially all Nexus mods carrying CET tags in the user's collection. The user repeatedly measured **over 50% lower CET workload** on tested stacks. That's valuable observed progress, *not a guarantee of universal correctness or performance*.

We are extending the same Resolver with burst/hitch and G-GC allocation evidence. The objective is to discover more reusable generic rules and worthwhile semantic rules **without degrading author behavior**. The internal decision-maker is not a separate player-facing optimizer.

## Product version policy (explicit user instruction)

**Version increments are not tied to CI frequency.** Keep the current shared Profiler/Resolver version across normal commits, tests, native DLL rebuilds, diagnostics and packaging reruns. Increase it **only** when the user explicitly requests it or agrees that a substantial project milestone warrants a release. A new native hash/run pin is not, by itself, a product version milestone. When authorized, synchronize `VERSION.txt`, `Directory.Build.props` and `MANIFEST.json.packageVersion`; otherwise identify builds by commit SHA and Actions run ID. See [BUILD-PIPELINE.md](../HANDOFF/BUILD-PIPELINE.md). Do not automatically replace an official release/tag.

## The single ordered pipeline

1. **Capture real work.** Profiler collects exclusive CPU time, call rates, spikes, bursts, Scheduler workload, G-GC allocation counts/bytes/births, and optional frametime evidence. Record native revision, capture duration, source stack and coverage.
2. **Resolve current source.** Read live original mod files and dependencies. Detect installed generic changes, semantic postconditions, 0-Engine integration, manual modifications, and partial states. Identity/version naming alone never proves code behavior.
3. **Apply broadly safe generic transforms first to staged files.** Examples: callback consolidation, finite OnAction routing, source-proven `PLAYER` provider reuse with author fallback. Never assume all caches, queries or allocations are safe to deduplicate.
4. **Apply eligible semantic transformations second to staged generic output.** Cadence, dormancy, quest/event wake, near/far activity and bespoke heavy hotpaths use full source proof and lifecycle analysis. A generic and semantic pass may act on the **same mod**; each must recognize what is already satisfied.
5. **Compose rather than override.** Check exact postconditions across files. A partially 0-Engine-integrated mod can receive **only missing proven pieces**. Avoid double-injecting scheduler lanes or silently rolling back previous work. Fail closed for ambiguous partial states.
6. **Generate an overlay ZIP** containing only user-present source files and allowed 0-Engine additions. Resolver doesn't alter the live installation; user deliberately applies it.
7. **Validate behavior and performance.** Original source → generated output; syntax/contract/marker tests; cross-version structural variants; idempotent second run; gameplay; comparable before/after capture.

**Important:** If a generated pass breaks a mod, repair the **existing generating logic** and retest from the **untouched original mod**. A separate semantic that repairs the old broken output is not an acceptable permanent solution.

## Generic rules: risk-adjusted, system-level payoff

We actively look for **widely applicable** generic improvements. Lower risk permits lower payback; risk close to zero plus broad use can justify many small wins.

- Measure benefit at the natural unit: callback family, shared provider, many independent owners, 0-Engine dispatch group or complete mod stack—not only one cold-looking callback.
- Account for mean exclusive CPU, number of engine calls, allocation operations/bytes, bursts and frame-pacing spikes. Frame consolidation may improve pacing without removing logical Lua callback work.
- Mechanically proven transforms may be admitted with small individual benefit; moderate-risk transforms need clear aggregate benefit. Bespoke timing/lifecycle changes need substantial offender-level payback.
- Enforce source receiver identity, read-only semantics, no-escape/no-alias proof, freshness, native-handle lifetime, valid invalidation, errors/fallback and event ordering. Do **not** automatically cache arbitrary Game APIs.
- `PLAYER` is the first enabled safe shared-provider family; other source-recognized providers remain analysis-only until source proof and tests support generation.
- A high G-GC allocation counter is a research lead, **not** permission to lower admission thresholds or generate a rewrite.

## Semantic library: system patterns, not version-specific mod patches

Semantic is for significant hot owners needing state-aware cadence/dormancy, interaction/quest activation triggers, near/far scans, high-cost source-specific logic and complete feature preservation.

Production catalog entries must have an implemented source injector, measured eligible runtime, current-source proof, postconditions, idempotence and source-version variation testing. The standard admission floor is **3 ms/s exclusive**; explicitly justified rule-local lower floors must be safe and should not weaken global generic policy.

**Version adaptability:** prefer author method shapes, source control flow, state transitions, module relationships, register/cleanup patterns, API call semantics, and stable behavioral source anchors. Don't hardcode ZIP/version number, injected registration ID, exact line number, a particular whitespace form, or incidental local variable names when behavior-equivalent versions can be matched. Name-independent matching needs strong unique fingerprints. An incompatible structural change must fail closed rather than invent a patch.

**Example: Street Games Playable Poker.** This is an unusually aggressive location optimization—not merely slowing `onUpdate`. The desired existing semantic emits **two coordinated source edits**: `init.lua` scheduler lanes (active realtime; near seat 10 Hz; near world 5 Hz; far world 1 Hz) and `StreetTables.lua` (25 m squared-distance wake signal). Preserve chair spawn/despawn/trace semantics and exact map-pin/category registration. Original mod input must yield this complete result. No obsolete one-file or repair-only rule.

**Example: Tunnel Rescue / Big Adam.** The known bug was the generating injector emitting literal named-regex placeholders as Lua. Preserve source-level corrected callback substitution, outside 150 ms decision cadence, inside realtime life/swimming/street execution, cleanup, quest mappins and diagnostics. The user's installed local mod was already corrected; permanent fix is the original→Resolver path.

**Example: Starman / Ziggy.** The v3 semantic combines bounded navigation-mappin diagnostics and main controller gate in one rule. Stable unaccepted `offered` state, V >130 m from the giver, no dialogue and valid session → cheap frequent proximity probe/full controller 4 Hz. Nearby/accepted/uncertain or dialogue states → original per-frame updates. Preserve elapsed dt, shard discoverability, journal, full mission progression and teardown. Do not replace it with generic blind 4 Hz.

### Quest journal lifecycle is not runtime activity

Many modded quests create/offer their journal entry as part of initialization or registration; some may remain progression-locked or untouched. **A journal entry, tracked/registered mappin, quest-offer fact or flag named `questArmed` does not on its own prove accepted, started or currently active gameplay.** Tunnel is an observed counterexample: its journal-offer routine sets `questArmed=true` before acceptance, defeating the earlier quiet-status gate and producing periodic diagnostic JSON spikes. This distinction applies as a **general admission rule**, not a claim that every quest mod uses the same registration sequence.

For every quest dormancy pass, audit the mod's source-defined state transitions and distinguish `registered/visible`, `offered`, `progression-locked`, `accepted/started`, `currently interacting`, `recovering/cleaning up`, and `completed` as supported by that particular mod. Determine which transitions or proximity checks need immediate wake. Keep journal availability, mission discovery, activation, dialogue, timers, UI and recovery semantics intact; optimize only source-proven work that is unnecessary in passive states. Spikes absent from captures are encouraging evidence for existing gates, **not proof of correctness for every future quest stage**. Avoid sweeping rewrites of other quest gates without separate source proof and measurements.

## 0-Engine runtime contract

The existing Scheduler and 0-Engine mod integration may be part of a generated eligible pass, never a blind bulk rewrite of user runtime files. GCET opt-in modules:

| Service | Appropriate use | Critical limit |
| --- | --- | --- |
| `Engine.WorkQueue` | Spread **separable** safe operations over multiple frames | Soft budget cannot preempt one expensive Lua/native function |
| `Engine.PhasePlanner` | Defer noncritical bounded-lateness maintenance | No quest/input/render deadlines or order-sensitive handlers |
| `Engine.FrameListeners` | Pause/resume adopted frame callbacks when lifecycle is known | Resume can alter listener ordering |
| `Engine.StateSignals` | Subscribe to cached state transitions (vehicle, combat, menu, district) | No new polling loop; generations invalidate on world/session change |

These are **infrastructure APIs**; existing mods do not become automatically dormant simply because the services exist. Measurements of nested service work are not added to parent CET callback time. Protect foreign files, manual framework modifications, and current 0-Engine integration.

## Mandatory correctness gates

- **Source**: actual original author files + staged generic result, owner/dependency graph, variant signatures, complete transformation and no unknown file replacement.
- **Feature behavior**: startup/onInit, map pins, quest objectives/shards, dialogue, combat, input/UI, timers, close/shutdown, reload/session generation, forced failure and fallback.
- **Data**: avoid mutable table sharing, stale native userdata handles, changed ordering/equality/tie behavior, escaped temporary tables or hidden API side effects.
- **Composition**: generic before semantic, marker/postconditions, multi-file atomicity, partial 0-Engine state, second-run idempotence.
- **Generation**: no literal regex placeholders, valid Lua local budget, only permitted live source files, consistent manifest and install/restore semantics.
- **Tests**: actual original + compatible variant, synthetic fixture, generated diff, source syntax, CI, in-game gameplay and matched captures.
- **Reporting**: distinguish measured, inferred, prospective and unverified. No invented speedup, GC ownership or time saved.

## Code locations

- `src/G.CETProfiler.Core/Services/CallbackResolverService.cs` — callback classification and generic/semantic decisions.
- `src/G.CETProfiler.Core/Services/SemanticLibraryService.cs` — identity, source proof, postconditions, already-satisfied/partial treatment.
- `src/G.CETProfiler.Core/Services/PassGeneratorService.cs` — pass composition and output.
- `src/G.CETProfiler.Core/Services/SemanticPassGeneratorService.cs` — semantic generation admission and staging.
- `src/G.CETProfiler.Core/Services/SemanticInjectors.cs`, other specialized injectors — source transformations.
- `resolver-knowledge/semantic-library.json` — *production* semantic rules, not speculative candidates.
- `resolver-runtime/0-Engine/` — pinned/safe runtime additions.
- `tests/*.ps1`, `.github/workflows/build.yml` — regression and build/release contracts.

For GC-focused work, continue with [GC-RESEARCH.md](../gc/GC-RESEARCH.md). For **current build and validation state**, consult [CORE.md](../HANDOFF/CORE.md) and [BUILD-PIPELINE.md](../HANDOFF/BUILD-PIPELINE.md); [ACTIVE-STATE.md](../history/ACTIVE-STATE.md) contains dated historical context.
