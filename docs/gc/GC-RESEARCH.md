# GC research — the remaining major opportunity

> **Historical research, not current controller policy (2026-10-27).** This document's prior prohibition on automatic GC policy changes describes the existing capture-only/attribution phase. The newer user-approved, **still unimplemented** lightweight predictive heap-pressure assistance design is maintained in [G-GC-CONTROLLER.md](G-GC-CONTROLLER.md). Its implementation and gameplay validation have not started; no prior profiler measurements authorize arbitrary full collections or uncontrolled GC tuning.

## Current experimental next step — bounded unscoped producer evidence (2026-10-14)

Following the clean 239.85 s capture showing ~242 MiB unattributed positive growth and ~99.89% of this VM_C, instrumentation was expanded **in one native + managed package milestone**. [Engineering design and game test](research/UNSCOPED-MULTI-AXIS-ATTRIBUTION-2026-10-14.md).

[Native #38517977314](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38517977314) and [combined #38519218557](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38519218557) both succeeded. Both Profiler and Resolver remain product **v1.2.7**. New exact owner-zero allocation-path and size breakdown, bounded sampled native callpaths, CET outer-boundary and per-second timelines should narrow source search; extra source/provenance evidence and gameplay proof are still required before GC optimization passes. Both Summary and Resolver JSON emit reconciliation and `autoRewriteAuthorized=false` for these observations.

The user's note that a large previous frame may have been a **menu frame** is a real confound; the next capture will intentionally avoid menus during idle/movement/combat segments.

## Important correction: recorded length ≠ intentional capture window (2026-10-13)

The user reports pressing STOP after approximately **7–9 minutes** and then playing another couple of minutes. The native `CET_Runtime_Profile_Markers.csv` contains **CAPTURE_START=0** and **PAUSE=698202.1602ms** with no earlier PAUSE or RESUME; the last GC frame ends 698202.1602ms, and CapFrameX independently spans 697.743s. The stop was **not successfully recorded at the intended time**, mechanism undiagnosed. The **11m38.202s** and all metrics in the following section describe the **actual recorded interval**, NOT a clean intentional single gameplay scenario.

First 7m: **6 completed automatic GC cycles and 10,215.6ms collector work**. First 8m: **6 cycles and 11,664.9ms**. First 9m: **8 cycles and 13,525.2ms**. Last 2m38s–4m38s (depending on actual attempted stop) therefore includes **3–5 GC cycles** and ~**6.31–9.62s** measured collector wall. This is from raw GC Collector CSV and does not require any inferred attribution.

**Critical limitation:** Native 2.15 GGC rankings, registration allocations, sampled-object lifetime/reclamation, and function provenance totals are **whole-recording aggregates**; there is NO evidence permitting re-derivation of these per owner for the first 7–9 minutes. Whole 698s owner-0 1.095 MiB/s and 99.826% reclaimed samples remain recorded facts, but **are not valid clean intended-window evidence or A/B baselines**. Avoid comparing their apparent similarity to the earlier 470s capture as definitive stationary/waste-stream proof. A time-binned producer trace, and stop acknowledgment diagnostics, should be priorities for next instrumentation. Keep GC timing non-additive.


## Long v1.2.5 capture — CONTAIN_THE_DATA (2026-10-13)

Source: user-uploaded `CET-20261013-031413_CONTAIN_THE_DATA.zip` compared to `CET-20261013-000328_THE_QUESTS_AND_AUTOLOOT_RESOLVER.zip`. Read the raw nested CSV plus CET_Summary and CET_Resolver_Input; figures below are observed, not modeled garbage savings.

- **698.202 s vs 470.629 s**. Active mods 139 vs 138. Capture-level CET exclusive callback wall **138.964 ms/s vs 126.879 ms/s**, 17,008 vs 15,784 calls/s; the longer test differs in gameplay/mod workload (e.g. yaibaPenthouse newly active), so this is **not an A/B regression claim**.
- **11 native incremental GC cycles completed vs 8 previously**; **19,834.020 ms** internal collector time (**28.407 ms/s**, previous **12,568.538 ms = 26.706 ms/s**). The raw collector frame CSV is not additive to callback wall. **Zero explicit GC full/step calls** and no full native collector operations, thus ordinary LuaJIT automatic collector activity; CMB forced full collections not observed in this specific run. No dropped collector frame buckets.
- Collector phase sums: sweepstring **6,061.654 ms**, finalize phase **4,902.642 ms**, sweep **2,839.918 ms**, atomic **1,835.684 ms**, propagate **1,680.405 ms**; the first two phases are **55.28% of all measured collector wall**, not evidence that C/CET user finalizers ran. Largest measured GC frame bucket **516.141 ms**, maximum indivisible step **208.326 ms**. At **101.66s** GC **353.975ms** overlapped AutoLoot callback spike **298.408ms**; **236.07s** GC **516.141ms** overlapped BetterLootMarkers **472.337ms**; **350.62s** GC **211.496ms** overlapped ImmersiveFirstPerson **208.600ms**; **398.32s** GC **262.549ms** overlapped 0-Engine **263.587ms**. These are near-exact clock coincidences; **no GC-free callback millisecond estimate, no allocation-source assignment, and no extra GC time summing is authorized**.
- All owners total **2,042.068 MiB positive allocator growth**, **16,434,283 positive allocation requests**, **2,603,535 observed GC-managed object births**, **59,247 interned strings**, over 698s: respectively **2.925 MiB/s**, **23,538 requests/s**, **3,729 births/s**. None is retained heap or confirmed waste.
- **Unscoped registration 0** **764.241 MiB = 37.425% positive bytes**, **5,229,772 requests = 31.822%**, **143,515 GC object births = 5.512%**, **23 new strings**, means **1.095 MiB/s** and **7,490.3 requests/s**, mean positive operation **153.23 bytes**. Compared to old run (1.071 MiB/s, 7,335.1 requests/s, 39.128% bytes), both rates remain similar across captures. The count of births is disproportionately small relative to positive reallocations; attribution cause NOT KNOWN.
- **Scoped bucket** **1,277.827 MiB** (62.575% positive bytes), **11,204,511 ops**; 0-Engine registered scopes **376.003 MiB = 0.5385 MiB/s**, **3,297,281 ops** = 4,722.5/s, **640,018 births**. These are work executed in registration contexts, possibly routed other mods' work, not proof of framework-generated allocations.
- **Object lifetime sampling:** 54,145 sampled GC objects, **54,051 reclaimed samples** (99.826% of these samples), **3.262 MiB of sample-tagged freed object size**, **3,076 sampling collisions**. Never extrapolate 3.262 MiB to whole-heap reclaimed memory. Examples of *creation-to-reclamation* mean observed times: owner0 **44.48s**, 0-Engine **44.72s**, GigCore **44.24s**, ImmersiveFirstPerson **42.58s**, NPC Outfit Manager **46.49s**; these are NOT creation-to-last-use times and are focused/biased samples. Older capture had 36,336 / 40,300 reclaimed samples (~90%).
- Report heap snapshots span **432.158–790.113 MiB**, 100 recorded checkpoints, 38 observed shrink events; an intermittent heap-size drop does not identify its allocator. CapFrameX alignment is GOOD, ~50.10 FPS mean, with 52 frames over 100ms; some worst frames coincide with callback/collector intervals, but not every hitch is GC-caused.
- **v1.2.5 Stage A works and is strictly review-only:** 1,680 registration rows, 2,278 sampled function rows, **30,265 sampled allocation ops** (tiny bounded subset of 16.4M overall); **27,642 = 91.33%** sample ops are C-native function frames and **ALL 2,278 function rows have `NOT_RECORDED_LEGACY` Lua caller resolution** in native 2.15. The candidate report identifies **84 Lua function source sites**, **ONE qualified cross-mod site** (GigCore/core/bus.lua:18 get, 172 sampled calls under GigCore/SpecterCampaignProgression/SpecterM11RecoveryDev/SpecterNessaIntro), **39 shared native Get*/Is*/Has* API names**, e.g. GetPlayer, GetEntityID, GetWorldPosition, GetScriptableSystemsContainer, GetQuestsSystem. All native getter overlaps are **API-NAME ONLY**, not identical callers, receivers, parameters, data results or shared-cache eligibility. No result value stability/freshness or avoidable-MiB savings is presently measured.
- Next must be **experimental bounded capture-gated native execution/producer context**, especially distinguishing unscoped owner-0 allocator growth, plus **opt-in in-situ callsite/parameter/scalar-value equality probes** targeting named getters. Preserve production 2.15 SHA until experimental same-save world-load/without-F11, capture, restore, no-overhead tests pass; previous experimental 2.16 caused world-load crash before F11. Never patch GigCore bus getter or global-cache based on same-source overlap alone.


## Attribution gap / owner-0 analysis — 2026-10-13

- From the user's uploaded exact 470.629s capture, `<unattributed>` has **504.251627 MiB** (39.128% positive bytes), **3,452,096 positive allocator requests** (32.275% requests), **56,947 GC-object births** (only **3.133%** of all object births), and **12 interned-string births** (0.037% of all strings). Average positive allocation size about **153.17 bytes/request**, versus **113.55 bytes/request** for all other buckets combined.
- These distinct counters expose an important mechanism: owner-0 disproportionately records allocator growth/requests compared with GC-object creations. It MAY include VM resizing/reallocations, LuaJIT internal growth or allocations outside registered CET callbacks. **Neither 504MiB nor 3.45M ops proves 504MiB of collectible garbage**, and no sampled function rows had registration ID=0, so the current 2.15 function sampler offers no source location for them.
- Native v2.15 derives ownership from TLS current CET callback registration, restored after scoped callbacks; zero means no active attributed registration. It is not a guessed mod name. Overflow has its own bucket. No caller stacks, allocation-time categories or time distribution for owner 0 are present in current CSV. The native-v2.16 branch experienced a pre-F11 crash and stays unshipped.
- Implemented **add-only** `ResultReportService.UnknownAllocationCoverage.cs`: split unscoped ID-0, registration overflow and named scoped callbacks, displaying each one's bytes MiB/%, alloc requests/s/%, observed object births/%, string births and average positive bytes/request. The section is in HTML and `unknownAllocationCoverage` appears in BOTH summary and Resolver handoff. Proves large-byte/small-object mismatch; no new ownership claims, no reductions to raw milliseconds, no GC action, no modifications to any mod or native payload.
- Synthetic regression validates exact unscoped vs scoped shares, separate object births, no invented overflow, both report vocabularies. Next attribution improvement requires a **bounded new native capture experiment** classifying ID-0 execution contexts (Lua top-level/load, native CET internal, engine hooks, GC/VM maintenance, genuinely outside scope), with source/phase evidence and startup safety gate; cannot recover exact historical owners from ID=0 totals. Investigate cross-mod routed work separately and never assign unknown to whichever callback experienced GC.


## 2026-10-13 add-only 0-Engine allocation frequency and value-stability coverage

- User scope: leave GigCore / ImmersiveFirstPerson untouched; retain **all existing milliseconds, spikes and GC timing metrics**. Focus on 0-Engine-generated/routed data: **MiB**, MiB/s, allocator ops/s, per-callback frequency, ops/call, bytes/call; do not change scheduler/input frequency or GC policy.
- Current 470.629s capture: 0-Engine registered `onUpdate` #5 **206,480,929 B = 196.92 MiB**, **1,501,074 allocator ops**, **24,067 calls (~51.138/s)**; `OnAction` #1107 **66,717,291 B = 63.626 MiB**, **796,215 allocator ops**, **601,860 calls (~1278.84/s)**. Routed callback allocation does not prove framework-owned allocation: nested client work must be separated by source before any optimization.
- Added managed-only `ResultReportService.AllocationStreams.cs`: reads existing native 2.15 CSV records, preserves CPU/GC output; HTML table of the measured 0-Engine registration routes and their allocation rates/per-call counts; bounded sampled function table exposing Lua/native execution source and allocation samples; same `zeroEngineAllocationFlow` analysis block added to **both** `CET_Summary.json` and `CET_Resolver_Input.json` (review evidence only, not source proof or rewrite authority).
- The user's key extra question is **where reads are frequent but returned data changes rarely**. Current 2.15 capture DOES NOT record return-value equality or change events for sampled native functions. The new block truthfully reports `valueChangeStatus=NOT_MEASURED_BY_NATIVE_2_15` and `valueChangeRate=null` everywhere: fast access + allocation cannot establish stability.
- Next instrument specifically *proven scalar reads* at existing call sites (no new native getter calls) with a bounded, opt-in read count / changed-value count and source/caller identity; avoid mutable native handles or retaining game objects. Validate per-phase (menu/scene/quest) and reset on session changes. No broad interception, GC tuning, or unwarranted cached game-state assumptions.
- Historical G-GC 2.15 function sampler is bounded and often reports native `=[C]` execution without the Lua callsite: `__index`, `jobEnd`, `GetQuestsSystem` are investigative leads, not proven repetitive unchanged values. Source hotspots should be inspected under the relevant routed callback before modifying behavior.


## 2026-10-14 native attribution iteration — ready for gameplay testing

- Source commit `997cde051cfeaed6cae74ad4eb64d70a5c7672e0`; native CI [#38496930001](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38496930001) SUCCESS, actual DLL SHA `72cb0f09827fcd86a07e6e510145de6907c7eb9f2e0d68f5381333857b381976`; integration `d3ab2a7908834d54e937b0872f60ff60b6bc72ab` via main [#38497725639](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38497725639) SUCCESS, **both apps packaged v1.2.7**, no version increment.
- Additive **exact within observed LuaJIT allocator sites** owner-zero growth counters by `g->vmstate`: INTERP, C, GC, EXIT, RECORD, OPT, ASM, UNKNOWN. This is a **VM-state execution classification, not Lua source/provenance or mod ownership**, and cannot establish duplicated data, safe caching, or GC cause.
- Add bounded **one-second capture timeline**, fixed 1200s plus overflow bucket, no Lua debug hooks/getters/GC tuning, capture-gated. Normal GGC owner-zero metrics remain in their prior rows and **must reconcile** with new category totals. Manager already captures wildcard `CET_Runtime_Profile_*.csv`; manifest explicitly includes both.
- Files: `CET_Runtime_Profile_GGC_UnscopedContexts.csv` and `CET_Runtime_Profile_GGC_UnscopedTimeline.csv`; raw CSV not a new primary managed HTML scoring field. Source only committed after proof-of-concept; native CI proves compilation, NOT game-load/safety or bounded overhead.
- Next: use **new package** and reproduce a brief normal capture. Compare owner-zero byte/ops sum with categories; timeline with GC slowdown interval. Then use context evidence to select specific safe producer callsites for opt-in scalar sampling. Avoid owner0 guesses.
- [Reproduction checklist](testing/UNSCOPED-VM-CONTEXT-TEST-2026-10-14.md).

## Mission

**Stop unnecessary data from being generated and fed into LuaJIT garbage collection.** This improves upstream Lua mod work and may also lower downstream collector pressure. The user's remaining major concern is that, despite successful callback consolidation, we have not sufficiently questioned whether the highest-volume **data producers are needed at all**.

This is not a GC-only optimizer, nor a project to cosmetically lower garbage counters by retaining stale tables. GC traces add information to the **existing** Resolver; source proof still decides whether an optimization is generic, semantic, or review-only.

## New field evidence: 2026-10-13 GC trigger-vs-producer split

470.629s G-CET/G-GC/CapFrameX synchronized capture `CET-20261013-000328_THE_QUESTS_AND_AUTOLOOT_RESOLVER` strongly validates the trigger-site confounder:

| Frame/time | Callback exclusive | Exact collector in SAME frame | Implication |
| --- | ---: | ---: | --- |
| 13470 / ~294.13s | radioExt onUpdate 574.721ms | 638.812ms / 85,371 incremental calls | Repeated collector steps likely inside callback; NOT evidence radioExt produced backlog |
| 20934 / ~424.83s | 0-Engine onUpdate 519.378ms; sitAnywhere frame job 519.100ms INCLUSIVE | 573.950ms / 95,067 calls | Nested scheduler and collector durations overlap; do not double-add or assign generator owner |
| 6288 / ~139.92s | 0-Engine routed OnAction 199.677ms | 200.966ms / 36 calls | Single native atomic→sweepstring 199.641ms phase operation directly overlaps |
| 7550 / ~168.62s | f80h aroused expressions OnDetach 193.436ms | 268.900ms / 24,918 calls | Hook likely observed collector stall, source ownership unproven |

Exact collector totals **12,568.538ms**, 8 cycles, 20 individually logged slow operations; `spikeCoincidences` already labels **19** callbacks potentially GC-confounded. For radioExt specifically, no 575ms individual GC phase was recorded; multiple incremental steps inside that frame are responsible for large collector time. Source script may still do work, but the callback's measured elapsed time is not independent proof of expensive script code.

G-GC allocation positive MiB rankings: unattributed 504.252; 0-Engine/Routed 261.583; ImmersiveFirstPerson 102.099; GigCore 93.904; NPC Outfit Manager 33.014; Ziggy 31.379; radioExt 12.870. These positive allocated byte counts are not live heap and do not identify a specific duplicate table. Preserve UNKNOWN allocations and invalid global-finalizer counters. Recommend presentation mark GC-CONFOUNDED at spike level and separately show frame-level exact GC, source-allocated bytes and material callback work; no naïve arithmetic subtraction from exclusive callback values. Target demonstrable upstream data producers, not innocently interrupted callback owners.

## Real evidence and limitations

An extended experimental capture from 2026-10-12 (about **443 s**) measured approximately **1,457.8 MiB positive LuaJIT allocator growth**, **12.18 million allocation operations** and **2.21 million GC object births**. About **559.65 MiB (38.39%)** remained unattributed. Some global finalizer invocation counts are inconsistent with sampled finalizers and explicitly **UNVALIDATED**. These figures represent that one capture/runtime only; positive allocator growth is **not retained memory**, and neither count alone proves that a particular allocation was unnecessary.

The known-good **product native 2.15.0** does not expose every experimental source-caller field of 2.16. The experimental native 2.16 build caused a repeatable before-capture world-load crash on the same save/stack in another test and was rolled back. Any newer 2.16 data must be labeled experimental and its fields may not be retroactively inferred for legacy 2.15 captures. Native upgrades need same-save startup testing and measured overhead, not merely CI compilation.

### Evidence vocabulary: keep separate

| Metric | What it indicates | Do not claim |
| --- | --- | --- |
| Allocated positive bytes | Cumulative positive realloc/new growth | Retained heap, RSS or collectible waste |
| Allocation operations | Requests counted by instrumentation | Number of GC objects or duplicate tables |
| Object / string births | Newly observed GC-managed objects | Every allocator operation; complete object graph |
| Sampled source functions | Partial per-function allocation evidence | Exhaustive attribution or exact responsible line |
| Reclamation sample | Sample-tagged objects reclaimed | Total freed bytes or total GC work |
| Collector phase/slow step | Native GC wall time within capture | The overlapping CET callback created its garbage |
| Callback/Scheduler CPU and bursts | Time of measured execution and spikes | Additive collector + nested job costs |
| Unattributed / native C source | Scope/attribution limits | Lua caller identity inferred from proximity |
| Large infrequent episode | Possible load spike or expensive one-off | Sustained average bottleneck |

**Never silently reassign unattributed bytes.** Separate executing callback owner, source/library owner, observed callsite, and confidence. Specter/GigCore and other mod pairs can call each other's functions. A single sampled caller is *not* ownership proof. Suspicious raw finalizer counters stay out of optimization decisions until independently validated with LuaJIT fixtures.

## Research tracks, ranked by likely opportunity

### 1. Eliminate throwaway tables and structures (often a promising generic rule)

Inspect hot loops for fresh candidate lists/tables on every invocation, temporary result copies, repeated concatenated strings and one-shot sort/filter constructions. Where a list is used only to choose nearest/best, source-prove a scalar selection with **identical** filter order, tie handling, one-item behavior, range checks, return shape and side effects. Trace every alias/escape and all callers; a table that another function mutates cannot be deleted blindly.

**Existing lead:** AutoLoot world-pin candidate table elimination (a source-proven allocation-reducing semantic pass). It retains the author's native mappin query/filter and original cadence. Gameplay before/after allocation and CPU gains need direct measurement; only then generalize the pattern for other source-compatible mods.

### 2. Reduce repeated read-only engine queries (generic when mechanistically safe)

Repeated Game getters, player position/orientation, component access, mappin/world lists, state snapshots and system read paths may generate transient Lua data *even when the logical answer hasn't changed*. Rank repetitions per measured callback and aggregate across the stack.

Prove the getter's receiver/source, side effects, native userdata validity, identity, freshness requirements, error behavior, and the narrowest safe cache scope: one expression → one callback → one frame → one well-defined 0-Engine generation. The currently enabled shared-provider family is **PLAYER**, using the existing 0-Engine lifecycle-managed cache and fallback to the author getter. Other provider families remain **analysis-only** until admitted with stronger source and matched runtime proof.

### 3. Deduplicate heavy lists across mods (high-upside research, NOT yet an enabled rule)

The desired win is **one safely shared data production instead of N mods fetching/building nearly identical megabyte-heavy tables**.

Investigate with all of these gates:

1. Identify the same measured source/provider called from multiple owners. Record exact query parameters, filters, transformations, order and relative timing, not merely the same method name.
2. Establish **semantic equivalence**: same inputs, same data freshness, same output shape, same errors, no mutation/alias/identity differences. Confirm whether consumers need separate copies.
3. Decide lifetime: same invocation, same frame, event-scoped, game-generation-scoped, or periodic snapshot. Establish explicit invalidation triggers (load/unload, quest state, district, journal, entity generation, UI changes).
4. Store **plain stable scalar IDs/immutable snapshots** where correct; do not hold native entity/mappin pointers or transient handles across unsafe boundaries. Never expose a writable shared table that lets one mod corrupt another's view.
5. Measure cache generation cost, live retained memory, subscribers/invalidation overhead, source CPU, total ops/bytes and collector pressure. Shared retention can *increase* heap usage; a smaller allocation counter by itself is not success.
6. Build one opt-in source-proven generic contract only if the broad safe conditions really recur. Otherwise use reviewed mod-specific semantic state reuse.

Do not invent a global cache or new 0-Engine polling loop to force a general rule.

### 4. Dormancy, frequent tiny probes, rapid full wake (typically semantic)

Many quest, world-scan and UI controllers work every frame while the player is nowhere near the relevant feature. We expect major wins from understanding the mod's **actual state machine**, not simply dividing all calls by N.

Model initial state, offered/unstarted, active gameplay, transitions, timed events, quest proximity, destruction, reload/session generation, UI/dialogue and teardown. Define the cheapest safe high-frequency wake probe; gate the heavy controller only while provably inactive; propagate elapsed time correctly. If probe fails or state is uncertain, fall back to original cadence.

- **Ziggy / Starman**: current measured big offender, 130 m distance + stable offered/unaccepted phase → full update at 4 Hz while far; full-rate when near, dialogue, active, transition or uncertainty. Preserve first shard, journal and story quest.
- **Poker**: far 1 Hz, near world 5 Hz, seat 10 Hz, active gameplay per frame, 25 m squared-distance wake. This is an aggressive *two-file* optimization, not just a simple cadence filter.
- **Tunnel Rescue**: outside 150 ms author decision loop; inside active per frame, full cleanup and diagnostic quest-mappin semantics.

Any new semantic rule requires substantial measured payback. A new generic state read can be considered separately only when broadly safe and mechanically proven.

### 5. Identify rare very large producers and burst systems

The user explicitly wants to know **what system produced a huge load even when it runs seldom**. Build a separate ranking from sustained top-bytes/ops: heavy single episodes, large temporary table bursts, expensive mappin enumeration, game-load resource/state initialization, quest entry, UI rebuilds, and expensive periodic maintenance. Relate CET spikes, allocation peaks and GC phases by timestamp but never assume causation.

If independent subtasks exist, `WorkQueue` or `PhasePlanner` could amortize work over frames with source-proven deadlines and correct ordering. They cannot interrupt a single huge Lua/native operation; delaying immediate quest/UI work is unacceptable. Some spikes are legitimate necessary first-time work and should be reported, not blindly eliminated.

## Desired Resolver data and candidate reporting

For each ranked candidate, retain:
- **Identity:** capture/time window; owner, registration, executing source owner, actual file/callsite or UNKNOWN, mod version/source fingerprint, active/idle/game state and source proof.
- **Costs:** exclusive ms/s, calls/s, max spike/burst, positive bytes total and per second, allocation ops total and per second, births and observed type, sampled reclaim and coverage, nested Scheduler/GC context and any dropped/unknown samples.
- **Mechanism:** exact query/data structure/temporary table, repeat rate, cross-mod duplicates, consumers, mutations/escapes, invalidation/freshness needs, source/version compatibility, existing generic/semantic/0-Engine treatment.
- **Judgment:** expected savings as a **hypothesis**, risk and confidence, generic broad impact vs semantic hot spot vs review only, error fallback, postconditions, rollback and matched gameplay test.
- **Outcome:** source-generated patch diff and complete-state markers, baseline/after capture, actual measured change, gameplay failures or passes, remaining open questions.

Priority is **broad almost-zero-risk rules first**, then significant high-rate heavy producers and independently important bursts. Keep separate views for total bytes, bytes/s, allocation ops/s, births/s, rare max episode and unattributed amounts. An 8 MB one-off can be interesting even if mean CPU is low; millions of 64-byte throwaways can be critical even if no single request stands out.

## Experimental gates / blockers

- Independently validate global finalizer counters and GC sample coverage; preserve `NOT_RECORDED_LEGACY` rather than manufacturing caller IDs.
- Characterize unattributed allocations only where source can be demonstrably recovered (init, native bridge, finalizer, true UNKNOWN). Avoid intrusive all-alloc stack walking.
- Benchmark tracer overhead vs same save/gameplay without new native tracing; no product promotion of experimental 2.16 without solving/reproducing the world-load regression.
- Verify that fewer allocations actually correspond to less CPU/GC overhead or fewer hitches, not to leaked long-lived caches.
- **Do not automatically tune LuaJIT GC, force collection, or infer allocations from heap drops.**

## Immediate plan for the next chat

Read the newest capture and exact original/deployed mod sources. Produce ranked top-bytes, top-ops, top-births and rare-spike reviews with confidence annotations. Identify 3–5 specific source-proven opportunities, prefer new **reusable generic families**, and only write high-payback semantic when the quest/activity lifecycle is fully understood. Check existing rules for overlap and make all generation version-tolerant and idempotent. Run regression/CI and matched in-game measurements.
