# Active project state — 2026-10-13

## Product 1.2.5 coordinated Profiler + Resolver bump (2026-10-13)

- Advance **both** managed applications to v1.2.5 with VERSION.txt, Directory.Build.props and MANIFEST.json aligned. CI checks both executable ProductVersion and FileVersion, artifact ZIPs use v1.2.5. The pinned native 2.15.0 DLL and SHA are unchanged. No mod runtime or GC policy modifications. Version bump follows green data provenance Stage A (#1013) and updated permanent specification.
- Next engineering milestone remains the experimental bounded creation-source context and opt-in in-situ read/change probes from DATA-PROVENANCE-SPEC.md. Do not assume Stage B is implemented or gameplay verified.


## 2026-10-13 locked new objective: data-production provenance and cross-mod duplication

- **Authoritative spec:** [DATA-PROVENANCE-SPEC.md](DATA-PROVENANCE-SPEC.md). User approved the concept and requested permanent preservation plus immediate implementation. Scope: who generates bytes; how often; whether output changes; whether multiple mods independently make equivalent queries; which objects are reclaimed; safely eliminate/reuse/share with the *existing* Resolver only.
- **Stage A implemented and GREEN in CI #1013 (commit 600751e):** managed-only candidate analyzer using pinned native 2.15 function samples, grouping exact normalized **qualified** Lua source file+line+function across distinct mod registrations (bare init.lua and ./json/json.lua are ambiguous across mods and excluded); native `=[C]` stays segregated with explicit callsite confidence. A separate repeated-native-Get*/Is*/Has* name list shows which API getters were sampled across distinct mods; it is `NATIVE_API_NAME_ONLY`, NEVER proof that arguments or return values matched. Output `dataProvenanceCandidates` in CET_Summary + CET_Resolver_Input and HTML, no automatic optimizer admission, no result equivalence assumptions. See `ResultReportService.DataProvenance.cs` and `tests/HitchPressureContract.ps1`.
- Verified sample from real 470.629s capture: fully qualified `GigCore/core/bus.lua:18 Bus.get` sampled under `GigCore`, `SpecterCampaignProgression`, `SpecterM11RecoveryDev` registrations; in user-supplied GigCore 0.7.3 original code it intentionally performs fresh guarded `Game.GetScriptableSystemsContainer():Get(BUS_ID)` each invocation for offline-companion tolerance. **This is overlapping execution source, not proven repeated equivalent data. Do not cache the reds bus across callbacks without lifecycle/source proof.** Ambiguous `./json/json.lua` relative paths excluded from cross-mod claims. Other sampled native getter names across owners include GetPlayer, GetWorldPosition, GetScriptableSystemsContainer, GetQuestsSystem; names alone do not prove identical parameters or return values. Tests cover both false collisions and name-only native overlaps.
- **Stage A accepted CI:** #1013 green (all 3 jobs, managed package 1.2.4 before coordinated bump); production native remains 2.15.0. Product 1.2.5 CI must be confirmed separately.
- Later B/C/D: capture-gated bounded native execution-context provenance including unscoped owner ID 0; in-situ scalar getter read/change rate; cross-mod parameter and output equivalence with source-verified lifetime, aliasing and invalidation. Native 2.15 product pinned, experimental 2.16 pre-F11 crash unresolved.
- All existing CET ms, GC pauses, scheduler, throughput, source collection and gameplay behavior preserved. GigCore and ImmersiveFirstPerson are deferred, not permanently untouchable. CMB controls explicit GC policy; never conflate its manual collector trigger with allocation owner.


## Attribution gap / owner-0 analysis — 2026-10-13

- From the user's uploaded exact 470.629s capture, `<unattributed>` has **504.251627 MiB** (39.128% positive bytes), **3,452,096 positive allocator requests** (32.275% requests), **56,947 GC-object births** (only **3.133%** of all object births), and **12 interned-string births** (0.037% of all strings). Average positive allocation size about **153.17 bytes/request**, versus **113.55 bytes/request** for all other buckets combined.
- These distinct counters expose an important mechanism: owner-0 disproportionately records allocator growth/requests compared with GC-object creations. It MAY include VM resizing/reallocations, LuaJIT internal growth or allocations outside registered CET callbacks. **Neither 504MiB nor 3.45M ops proves 504MiB of collectible garbage**, and no sampled function rows had registration ID=0, so the current 2.15 function sampler offers no source location for them.
- Native v2.15 derives ownership from TLS current CET callback registration, restored after scoped callbacks; zero means no active attributed registration. It is not a guessed mod name. Overflow has its own bucket. No caller stacks, allocation-time categories or time distribution for owner 0 are present in current CSV. The native-v2.16 branch experienced a pre-F11 crash and stays unshipped.
- Implemented **add-only** `ResultReportService.UnknownAllocationCoverage.cs`: split unscoped ID-0, registration overflow and named scoped callbacks, displaying each one's bytes MiB/%, alloc requests/s/%, observed object births/%, string births and average positive bytes/request. The section is in HTML and `unknownAllocationCoverage` appears in BOTH summary and Resolver handoff. Proves large-byte/small-object mismatch; no new ownership claims, no reductions to raw milliseconds, no GC action, no modifications to any mod or native payload.
- Synthetic regression validates exact unscoped vs scoped shares, separate object births, no invented overflow, both report vocabularies. Next attribution improvement requires a **bounded new native capture experiment** classifying ID-0 execution contexts (Lua top-level/load, native CET internal, engine hooks, GC/VM maintenance, genuinely outside scope), with source/phase evidence and startup safety gate; cannot recover exact historical owners from ID=0 totals. Investigate cross-mod routed work separately and never assign unknown to whichever callback experienced GC.


## 0-Engine allocation production view / unchanged-value coverage (2026-10-13)

- User explicitly prioritizes allocations/MB and creation frequency over ms savings; **retain every CPU/GC timing metric**, and leave GigCore/ImmersiveFirstPerson code alone.
- Added managed-only additive 0-Engine registration allocation-flow view from existing G-GC CSV: MiB, MiB/s, operations/s, ops/call, bytes/call, calls/s, sampled native/Lua function execution source. HTML + summary JSON + Resolver input. No native or 0-Engine Lua runtime change.
- Frequent-read/rare-change identification requires observing values. Native 2.15 captures allocation rate and function samples but no return-value change counts. Mark as NOT_MEASURED/null, do not infer stable reads or callsite provenance from native sampled function names.
- Next: design opt-in in-situ primitive-read-change telemetry without calling getters again, preserving all original read rates, inputs, event order, session invalidation. Observe before optimization; don't alter GC policy.


## 2026-10-13 GC-aware report attribution (follow-up to 470.629s capture)

- From exact CET-20261013-000328 capture: **126.878541 ms/s** raw CET callback wall time (includes GC if nested); **12,568.538ms** internally measured collector time / 470.629s = **26.71 ms/s** NON-ADDITIVE. Do not subtract it to report "real CET" because collector may run outside callback; do not treat raw callbacks as strictly Lua source CPU.
- G-GC totals: **1288.714521 MiB positive growth** and **10,695,899 allocation operations**, **504.251627 MiB (39.128%) unattributed**. Other large measured attribution buckets: `0-Engine` **261.583145 MiB** (routed/scheduled client work, not necessarily framework), ImmersiveFirstPerson **102.099038 MiB**, GigCore **93.903819 MiB**. High allocations warrant source inspection, not automatic deletion. Native product 2.15 lacks validated native C callsite provenance (88.196% of sampled function allocation ops executed at `=[C]`), do not invent ownership.
- Collector phase CPU is significant: `sweepstring` **3966.9491ms**, `finalize` **2900.7163ms**, `sweep` **1730.4181ms**, `atomic` **1323.3178ms**, `propagate` **981.3057ms**. The phase `finalize` operation counter and raw global finalizers are not independently validated as actual user GC finalizer calls. Eight completed cycles, heap **418.96–772.693 MiB** and largest passive shrink **300.19 MiB**; 4,194,304 sweepstring counter operations/cycle does not prove 4.19M live strings.
- Implemented managed report-only GC exposure: preserve raw measured CET callback totals, explicitly label as WALL TIME, display independent exact collector ms/s with non-additive warning, flag individually matched collector-overlapping callback spikes (GC-CONFOUNDED vs frame overlap vs no overlap), mark `gcFreeExclusiveMs=null` instead of inventing a subtraction, update top-spike finding to warn on confound. No native tracing, measurement semantics, Scheduler timings, Resolver optimization admission or collection policy changed. Existing `HitchPressureContract` synthetic producer!=trigger exercise extended to assert truthful flags and unchanged 20ms callback time.
- Next research objective is SOURCE of 504 MiB UNATTRIBUTED (not silently assign to mod). Tier 1 producers: nested 0-Engine onUpdate / adopted callbacks, ImmersiveFirstPerson onUpdate, GigCore onUpdate. Need actual current Lua sources and bounded sampled C callsite instrumentation; experimental native 2.16 F11 world-load crash unresolved, so keep 2.15 pinned. Seek repeated read-only native getters/materialized lists/throwaway tables; balance allocation savings with native handle lifetime/alias semantics. No forced global collect on UI entry: could worsen hitches and does not stop ongoing allocation stream. Investigate UI transition as optional benchmark-only maintenance after source/GC lifetime analysis.


## 2026-10-13 capture — GC-confounded extreme callbacks, interactions and cleanup

- Exact user capture: `CET-20261013-000328_THE_QUESTS_AND_AUTOLOOT_RESOLVER`, duration **470.629 s**; 138 active callback owners; 7.43M measured invocations. Measured CET exclusive total rate **126.879 ms/s**, includes nested/overlapping collector time. CapFrameX synchronized to CET (GOOD), **669.044 ms** worst frame at **294.102 s**, mean 51.125 FPS, 103 frames >50 ms.
- At `294132.922–294707.643 ms`, `radioExt` onUpdate registration 869 measured **574.721 ms exclusive**. **Same frame** (13470) has exact native collector **638.812 ms** (85,371 incremental calls, one completed cycle); this is GC-confounded, NOT proof radioExt generated 575ms of source Lua work/allocations.
- At `424827.389–425346.767 ms`, `0-Engine` onUpdate registration 5 measured **519.378 ms**. Same frame (20934) has **573.950 ms** exact collector (95,067 incremental calls). Scheduler subjob `sitAnywhere / sitanywhere.realtime` records **519.100 ms inclusive**, so also **GC-confounded**; do not blame `sitAnywhere` or `0-Engine` as allocator owner from this callback record.
- Internal GC measured **12,568.538 ms** during 470.629s, 8 completed cycles and 20 slow-step rows. Slowest explicit single step **216.272 ms**, phase `atomic→sweepstring`; radioExt's 575ms anomaly is mostly MANY collector steps in one callback, not a single documented 575ms step. Collector `potentiallyConfoundedSpikeCount=19`; attribution is trigger/overlap not the producer.
- G-GC positive allocation sources (MiB): **504.252 unattributed**, **261.583 0-Engine callback/routed work**, **102.099 ImmersiveFirstPerson**, **93.904 GigCore**, **33.014 NPC Outfit Manager**, **31.379 Ziggy**, **22.733 Tunnel**, **20.329 AutoLoot**, **12.870 radioExt**. Positive allocations are NOT retained heap/waste; the `0-Engine` bucket is **routed/contained work**, not necessarily framework overhead. Native 2.15's 8M+ raw global-finalizer counter remains UNVALIDATED.
- Sustained non-GC workload still matters: ImmersiveFirstPerson **12.560 ms/s**, GigCore **6.984**, Tunnel **5.868** (148 recurring spikes), Ziggy **5.337**, AutoLoot **3.297**. These are capture results AFTER the latest user-installed resolver pass but may not contain newest GigCore 0.7.3/SPECTER 1.5 originals; do not infer installed mod versions from capture alone.
- User requests: hide **Illegal Mechanic developer UI** without disabling car theft, HACK, minigame or diagnostics needed for error recovery; connect `ELM_ImmersiveWay` interactions with **Dark Future**, **Flushing Etiquette**, **Wannabe Edgerunner**; prune unused runtime logs / conditional debug allocations. The capture includes ONLY profiler reports and no original source for those modules, so **no source edits performed**. The existing Illegal Mechanic Resolver semantic handles HACK pulse idle polling, NOT developer UI.
- Public mod compatibility material: The Immersive Way 1.7 already advertises optional Dark Future needs and Flushing Etiquette hygiene integration. Dark Future itself documents Wannabe Edgerunner integration. Dark Future 2.x includes its OWN Cyberpsychosis system: upstream recommends turning that system off when using Wannabe while keeping Humanity Loss. Before adding a bridge, inspect actual versions and avoid double credit for animation completion/need recovery and double cyberpsychosis. Avoid perpetual Lua polls; prefer one-shot confirmed interaction completion with error-safe source APIs.
- Logging policy pending source: remove disabled debug-formatting/hot-path print work **only when proven unused**; preserve source-error, recovery, quest-lifecycle and crash diagnostics; debug may be opt-in. Do not delete profiling captures or 0-Engine safety logs indiscriminately.
- Engineering next steps: inspect original live Illegal Mechanic / ELM_ImmersiveWay / Dark Future / Flushing Etiquette / Wannabe sources and public APIs, implement source-proven UI suppression and event bridge where author integration absent, apply logging cleanup with behavior tests. Independently investigate allocation producers and exact GC trigger attribution; maintain native 2.15 pin and no global GC tuning. No new mod-source patches or gameplay claims from this capture.


## Composable source stages across version updates (2026-10-12, CI #1001 follow-up)

- The user's 20:32 pass used older LIVE GigCore 0.7.2 and SPECTER recovery without 1.5 mission music; the Resolver did not downgrade them. It generated GigCore, recovery and Tunnel semantic changes but SKIPPED AutoLoot's radius-first stage because the world-stage detector scanned the ENTIRE logic.lua for `local closeMappins = {}`. That variable also occurs in a distinct quest-pin routine where it must be preserved.
- The engineering contract explicitly requires independently recognizing ALREADY present generic/semantic stages and adding only missing pieces, even with partly integrated 0-Engine and manually optimized mods. Marker presence alone is NOT a complete-stage proof.
- New opt-in, reusable `sourceProof.completeStages` field models independent scoped Lua-function postconditions (required/forbidden behaviors and ordered-before relations), using exact owning top-level function scopes rather than neighboring function names, versions, ZIP layout or hashes. For composite rules with this field, **all** stages must be proven for AlreadySatisfied; a partly present or legacy marker does not block a missing stage.
- AutoLoot's existing SINGLE combined rule uses two source stages: scalar world-pin selection without per-call table, and early radius rejection BEFORE both inventory queries. Inputs ORIGINAL/WorldOnly/RadiusOnly/FullyOptimized (with previous, combined or no marker) produce BOTH/only missing stage/only missing stage/no repeat. An unrelated quest pin helper's `closeMappins` remains untouched. Ambiguous or restructured author code fails closed.
- The world-pin injector no longer cuts by the next helper `logic.couldStartNewLootingCycle`; it uses the unique function's top-level closing `end`. The loot-radius rewrite is also constrained to `logic.lootInRange` and tolerates whitespace/quote differences while retaining the original item/quest/shard/visibility/transfer order.
- CI now tests original, world-only, radius-only, both, legacy/combined/unmarked stages, harmless formatting, next-helper rename, preservation of unrelated candidate lists and idempotence. Generated logic is STILL subject to gameplay validation on the user's original AutoLoot, including loot immediacy and quest/shard protections.
- No changes to native (SHA-locked 2.15.0), package (1.2.4), 0-Engine contract, other mod semantic/cadence rules, or live files.


## GigCore 0.7.3 + SPECTER 1.5 source optimization (2026-10-12)

- Source: original GigCore 0.7.3 and SPECTER 1.5; older 0.7.2/1.4.1 source+generic artifacts used as comparison, NEVER copied over updates.
- Historical capture (not of SPECTER 1.5): GigCore ~9.21 ms/s; SpecterM11RecoveryDev ~8.18 ms/s (161.49 ms max); InfiniteGigs ~0.97 ms/s. These figures are prioritization only.
- Existing version-agnostic generic frame registration, shared PLAYER getter and finite dialogue OnAction routing must regenerate from CURRENT source. Do not put mission features or newer GigCore MapPins/audio under old file overrides.
- Added gigcore-blackboard-once: reuse Game.GetBlackboardSystem() inside one menu/photo pause-state probe, retaining original per-frame refresh and all GigCore service and session startup timing.
- Added specter-recovery-prerequisite-reads: only fetch Rainbow Market/recovery completion metrics if earlier story prerequisites permit M9->M10 handoff. Retains per-frame wake and author's 0.75-second launch delay. In 1.5 only, replaces active Music.update per-frame literal contract table with immutable module-local table; 1.4.x has no music contract and only gets metric gate.
- No throttle of GigCore Marker/NPC/VehicleDamage, bridge or active-ctx service loops; no SPECTER mission loop dormancy or altered music timing. Rare recovery spike ownership remains unproven.
- Must verify CI original/compatible version/idempotence + actual gameplay reload/clock-in/phone/markers/M9->M10/music/restore. Native stays 2.15.0, package stays 1.2.4.


## Tunnel Rescue / Big Adam diagnostic + distance wake (2026-10-12)

- Live 203.47s capture CET-20261012-184809: tunnel_rescue 9.81558 ms/s exclusive, 157.005 ms peak, 99 callbacks above 5 ms, 32.42 MiB positive allocations, 201,748 allocator ops. Spikes recur every ~2.05s at the diagnostics interval. Starman/Ziggy 6.307137 ms/s, 10.9953 ms peak, 1 callback above 5 ms.
- The installed 0.8.1-dev6 v2 status shows phase outside, correctly offered quest, V 1490m away. v2 forces a full GetAllMappins on inactive future-stage missing pins. This causes recurrent full enumeration and defeats the intended sentinel.
- `tunnel-rescue-v3` reuses existing v2 source generator and introduces an optional fail-open far-outside probe, 150m squared-radius to BOTH Eli and the surface ladder, with the original controller at 0.5s when far. JournalOffer.update, Rescue.weather, status, native inputs and game state are NOT disabled. On approach, recovery, reunion, unknown save state or active visit, retain the original fast cadence.
- The existing 2s diagnostic targeted quest-pin queries remain. Full world enumeration occurs on change of stage/phase/generation/tracking/pin state, or at most once per 12s while an ACTIVE objective's pin is missing. Inactive pin misses are expected, not continuous retry requests.
- Preserve native hooks, actors, weather, teleport, shards/journal, QuietTunnel, recovery, cleanup and all inside Life/Street/Swimming frames. CI contract verifies source shape; actual full-quest gameplay and matched capture must validate results. Native 2.15.0 and package 1.2.4 unchanged.


## AutoLoot verified early-radius source composition (2026-10-12, after CI #997)

- Starting point: `dev/resolver-profiler-handoff` CI **#997 GREEN**, commit `2e7c2a73ecdb2c08ca02e241ceee9e293279fe43`. Retain package 1.2.4 and native 2.15.0.
- User supplied untouched AutoLoot 3.11.1 author archive, live `EXISTING OPTIMIZATIONS.zip` (`init.lua` input routing/frame registration; `modules/logic.lua` prior world-pin scalar selection), and gameplay-tested `AutoLoot_EARLY_DISTANCE_ONLY(1).zip` (only `modules/logic.lua`, early radius before both inventory queries).
- The previous AutoLoot v2/v3 combined cadence/input changes were rolled back. **Do not restore them**. In-game report for isolated early-radius change: much smoother keypress collection, prior shard/quest interaction bugs absent, markers still occasionally stale but shorter with Better Loot Markers present. Frame spikes remain nonzero; not an assertion of full gameplay verification.
- One `onUpdate` catalog rule must compose world-pin allocation elimination and early radius (Resolver disallows two separate matched semantic rules for the same owner/callback). Original author source gets both; pre-optimized world-pin-only source receives only the radius gate; radius-only source receives only world-pin selection. Preserve original native mappin queries, key cadence, item transfers, shard/quest protection, visibility, marker behavior and separate `OnAction` rule.
- CI test must verify generated `logic.lua` ordering, original + incrementally optimized variants, source-only output and idempotence. Re-run original/full live source in-game before calling shipped gameplay fully validated.


## AutoLoot Resolver baseline restored (2026-10-12)

- Keep package **1.2.4**, native profiler **2.15.0**, and all non-AutoLoot rules unchanged.
- Roll back bundled AutoLoot v2/v3: no combined onUpdate-triggered input/frame rewrite and **no pre-inventory early-radius guard**.
- Restore the two independent pre-v2 AutoLoot rules from commit `18172cba122d6a682c0b4c228c32a6654e6e24fa`: `autoloot` (source-proven finite OnAction routing) and `autoloot-world-pin-allocation-scan` (allocation-only map pin candidate selection), with their own evidence and idempotence checks.
- Next AutoLoot work targets **keybind-held lootInRange burst** only; preserve the clean 3.11.0 behavior for shards/read prompts, quest/ordinary pickups, instant marker updates, native visibility and existing cadence. Require isolated measured source modifications and matched gameplay checks before shipping each.
- The recent PhysX crash was isolated by user to Let There Be Flight, removed in favor of DriveAerialVehicle; do not attribute it to AutoLoot.


**Snapshot, not canonical code:** confirm branch HEAD, run results, most recent capture, and source before acting. This page intentionally separates source/CI proof from user in-game verification.

## Repository, package and release safety

- Repository: [giorgioherma/G-CET_Runtime_Profiler](https://github.com/giorgioherma/G-CET_Runtime_Profiler).
- Development branch: `dev/resolver-profiler-handoff`.
- **Managed Profiler and Resolver package version: 1.2.4**, shared via `VERSION.txt`, `MANIFEST.json` and `Directory.Build.props`; launcher version resource aligned. Building on a dev branch does **not** mean immutable public v1.2.3 release has been published.
- **Native payload remains 2.15.0**, SHA-256 `c62d57f50b1fd792ae989cb78fb54cee8d82b0370abdb51ea01617c2ddcfa23e`. CI guard explicitly covers package 1.2.3. Do not accidentally package unvalidated native 2.16.
- Last source/semantic baseline immediately before the 1.2.3/documentation updates: **CI #969 GREEN**, HEAD `ae73f48d636bcab940255db4791add4a77414ecd`. Check the **latest** [Actions build](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/workflows/build.yml) before calling current HEAD green.
- Canonical Windows build: `.github/workflows/build.yml`; standalone Resolver artifact plus portable Profiler package. Version numbers identify product, CI run numbers identify iterations. Don't bump version for each rule by default.

## What is established

- One source-proven Resolver: current live mod files; generic first, semantic second, marker/postcondition recognition; compose already partial 0-Engine integration; generated ZIP only, no silent changes to user's CET.
- User reports consistently large measured reductions, often **more than 50% CET workload** on large tested stacks after generic and semantic optimizations. Functionality was not exhaustively tested across every feature.
- Generic frame consolidation, finite input routing, enabled `PLAYER` shared-provider fallback; remaining shared-provider candidates largely analysis-only.
- 0-Engine Scheduler + `GCETWorkQueue`, `GCETPhasePlanner`, `GCETFrameListeners`, `GCETStateSignals`. Services opt-in, no automatic modifications to every mod.
- Native/managed burst and GC evidence integrated into review. Current GC evidence **does not independently authorize AUTO rewrites**.

## Recent mod-specific work and live validation gaps

### Street Games Playable Poker — unusually aggressive location optimization

- Existing `streetgamespoker` semantic generates **two files** from author source: `init.lua` four-lane PASS4 Scheduler + original fallback, and `StreetTables.lua` PASS4C 25m squared-distance near/far world wake.
- Target cadence: realtime active gameplay every frame; seat 0.10s; near world 0.20s; far world 1.0s. Preserve map pin category/registration, table spawn/despawn/trace, menu/render, author startup/shutdown.
- Version tolerance is behavioral/source-shaped (including differing delta-time parameter spelling), not exact mod/ZIP versions. Two-file generation, test fixture, version variant and idempotent rerun passed in CI #969.
- User already corrected their installed broken copy. **Acceptance still requires original author archive → current Resolver → generated two-file output**, then poker interaction and map-pin checks in game. A synthetic source-shaped test is not proof of gameplay on every version.

### Tunnel Rescue / Big Adam

- Existing `tunnel-rescue-v2` semantic generating injector was fixed to expand named callback capture groups correctly, preventing literal placeholder text being shipped as Lua.
- Preserve author's outside 150ms decision cadence, tunnel-active per-frame Life/Swimming/Street work, state transitions and cleanup, targeted quest mappins and diagnostic cache.
- User already applied a local source fix. Permanent acceptance is original mod through newly built Resolver. No separate broken-output repair rule.

### Starman / Ziggy

- `ziggy-last-play-navigation-sentinel-v3` combines the previous v2 navigation sentinel and new offered/far quest gate in **one semantic** to avoid rule conflicts.
- One 193.5s capture: `onUpdate` about **15.49 ms/s**, `onDraw` about **2.18 ms/s**, largest `onUpdate` spike **228.86 ms**; roughly **49.49 MiB positive allocation**, **423,407 allocation operations**. Quest observed in unaccepted `offered` state with 0 collected pieces. Metrics specific to that capture.
- Rule intent: cheap frame-level distance/quest-state probe; if unaccepted/offered, stable session, no dialogue and >130m from Milo, run full controller every 0.25s. Near giver, dialogue, active story/accepted state, uncertainty or failure: original per-frame cadence. Keep author's elapsed-time accounting, journal/shard discovery, dialogue, pin correction and lifecycle.
- Source-shaped CI passed with latest semantic tests, but **full quest and matched before/after reduction must still be tested in-game**.

### AutoLoot local diagnostic cleanup and dual-source admission (2026-10-12)

- The 174.5-second intensive keypress capture measured a remarkably modest **3.57 ms/s AutoLoot exclusive CET work**, about **11.16 MiB positive allocations** and **96,489 allocator operations**, despite repeated loot actions. The evidence demonstrates a good existing baseline; it does **not** validate the untested radius-first change as necessary for this outcome.
- User provided probe-installed `AutoLoot(1).zip` plus uninstrumented `init.lua` and `logic.lua`. The two uninstrumented files are byte-for-byte identical to earlier optimized `AutoLootLIVE.zip`. The probe ZIP also modified `jmels.lua` with a **temporary** `JournalManager::ChangeEntryState` diagnostic observer and bundled `modules/gcet_probe.lua` plus a CSV. That observer is NOT author functionality.
- Built `AutoLoot-CLEAN-current-passes-no-probe.zip` from probe archive with original clean `init.lua`, `logic.lua`, `jmels.lua`, removing probe and CSV; all 88 file contents match earlier optimized LIVE installation. Preserves source-generated frame registrar, action subscription/fallback, world-pin scalar selection, all original loot/quest/gameplay observers. **Still lacks radius-first preinventory guard**, which the existing v2 resolver semantic adds on eligible capture.
- Resolver production AutoLoot `autoloot-active-loot-cycle-v2` remains a single `modules/logic.lua` semantic handling both *untouched 3.11.1* (world-pin scalar + radius-first guard) and *previously optimized clean 3.11.0* (add only missing guard). Additional guard now refuses generation from files referencing temporary phase-probe observer, rather than passing instrumentation into the released source. Keep native gameplay `OnAction` fallback for installations without usable 0-Engine routing.
- Verify current CI and then test **original 3.11.1 → Resolver** and **already-optimized clean 3.11.0 → Resolver**, checking generated content, marker/idempotence and in-game money/quest loot. Do not use probe ZIP as an input to production passes or conclude radius-first has measured new gains without a matched capture.

### AutoLoot 3.11.x — key-trigger hotpath prefilter

- On 2026-10-12, inspected the user's **AutoLootLIVE** 3.11.0 with previous world-pin scalar optimization, untouched author **3.11.1**, AutoLoot Quest Guard 1.0.0, and Nearby Auto Loot 0.5.1 REDscript. The original 3.11.1 retains the same key-triggered `logic.lootInRange` scan, while preserving new author quest/autosave data/handling.
- Critical source observation: each continuous-key cycle (approximately every 0.33s) did `GetItemList(gameObj)` and often `GetItemList(gameObjOwner)` **before** the radius/frustum check. Distant registry objects therefore paid unnecessary native inventory/array work.
- Existing world-pin optimization semantic was upgraded in place to **`autoloot-active-loot-cycle-v2`**, not a competing same-callback rule. It generates the previous no-temporary-candidate-table optimization from untouched source **plus** one early radius predicate before inventories. An already-world-pin-optimized LIVE source receives **only** the missing radius predicate. Later author radius/frustum/line-of-sight, quest protections, transfer/loot action and 0-Engine input/cadence are retained.
- **CI #983 GREEN** ([run](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38220899375)), source-generation + idempotence + incremental old-v1-upgrade contract. **Not yet verified in actual gameplay or a before/after hot-key capture**; expect savings to depend on how many tracked far objects exist, not a guaranteed percentage.
- Quest Guard is independent transfer protection; REDscript Nearby Auto Loot maintains an object-attachment registry and 150ms bounded scan but uses continuous automatic looting. It is a research reference, **not** a drop-in safe replacement of AutoLoot's keybind, view, protection and other features. Do not run the competing looters together during benchmark.
- Further potential offenders when in range: repeated journal/quest mappin scans, visibility/raycast retries and short-lived hit-point arrays; **inspect allocations and feature side effects before changing**.

## G-GC evidence: top open research

- Experimental ~443-second capture reported ~**1,457.8 MiB positive allocated growth**, **12.18M allocation operations**, **2.21M object births**, **559.65 MiB (38.39%) unattributed**. Positive allocation is not retained heap; unknown and native/C source do not give proven Lua caller.
- Raw global finalizer counter anomalies remain **unvalidated**; do not use as optimization evidence until independent test.
- Known source-proven allocation example: AutoLoot temporary world-pin candidate table elimination, with unchanged author's native query/cadence. Matched actual gameplay benefit pending.
- New targets: high-frequency unnecessary full queries, repeated Lua table rebuilds, redundant large lists, cross-mod read-only shared-state opportunity **with full alias/freshness/invalidation proof**, meaningful dormant states with cheap wake, and **rare very large data producers** which may cause isolated hitches.
- Native 2.16 experimental source callsite addition previously showed a repeatable before-F11 world-load crash on same save/stack; isolated successful 443s capture does not clear release gate. Native 2.15 pinned until crash and overhead characterized.
- Distinguish collector time from triggering callback, sampled C-callsite vs executing/source owner, sustained byte pressure vs episodic spikes. Do not auto-tune collector.

## Immediate execution order

1. Verify the current 1.2.3 GitHub Actions CI run and consistency of the Profiler and Resolver version info, manifest and native SHA; no version bump implies new native instrumentation.
2. Research the **newest representative allocation-heavy capture and corresponding mod source**. Rank by positive bytes, allocation ops, object births, sampled source functions and rare spikes separately. Identify confirmed redundant work vs necessary data production and unknown provenance.
3. Seek reusable low-risk **generic** patterns first. Only admit a GC-motivated transform with source proof and broad payback, preserving existing generic+semantic composition.
4. Use existing semantic state/cadence framework for big offenders: quest dormancy, cheap proximity probes and rapid full wake. Preserve story/input/UI behavior.
5. When fresh generated mods are tested, validate Poker's pins and interactions, Tunnel's load/quest steps, Ziggy's first shard and full quest progression. Compare matched captures and report any feature regressions.

Maintain this page after any substantial pass, measurement schema change, native promotion, or gameplay validation. Do not present a prior CI number as the current build without checking it.
