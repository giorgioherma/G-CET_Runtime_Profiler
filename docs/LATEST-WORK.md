# LATEST WORK — current active development task

**Status: ACTIVE — incomplete.** Updated: 2026-10-15.
**Task:** Complete source-proven, per-mod **extended dormancy across Lua execution, Lua → native work, and native → Lua dispatch**, building on the existing 62-mod audit / 67-rule production semantic library.
**Scope:** Resolver development branch `dev/resolver-profiler-handoff`. No product-version change or permission to modify the user's live mods.
**Owner of task decisions:** This file records the user's current task direction. Existing [CORE](HANDOFF/CORE.md), [Resolver project contract](resolver/PROJECT-CONTRACT.md), [authoritative per-mod intent](resolver/DORMANCY-USER-AUTHORITATIVE-SPEC.md), [62-mod / 67-rule audit](resolver/DORMANCY-ALL-MOD-AUDIT.md), and current source/CI remain authoritative for their respective domains.

## Lifecycle of this file — mandatory

This is the **one mutable, cross-chat task document**, not a dated investigation, release snapshot, or list of every previous task.

1. Read this file at the start of **every development continuation**, after `docs/HANDOFF/CORE.md`. Its task remains active even if a conversation ends or a different capture/build is discussed.
2. **Update this same file in place as work progresses**: decisions, source-proof findings, per-mod stage coverage, completed implementation and tests, blockers, and the next unfinished step. Do not start a new task document for intermediate milestones or declare this task complete from CI alone.
3. **Keep this task here until its defined completion criteria are met** or the user explicitly changes/cancels it. Passing one CI run, improving instrumentation, or generating one pass does not close the task.
4. **After completion,** transfer durable architecture/policy into the appropriate subsystem specifications and significant final evidence/commit/CI into `docs/history/ACTIVE-STATE.md`. **Only when the next task is agreed** replace the contents of `docs/LATEST-WORK.md` with that task; keep the filename and navigation links unchanged. The Git history retains this task's progress.
5. Treat this document as the current-work pointer. Stable instructions remain in `HANDOFF`; detailed experiments remain with their subsystem. Avoid duplicate parallel latest-task documents.

## Mandatory measurement completeness and manually approved semantic admission — 2026-10-15

**If a measurement needed to improve attribution, implementation correctness, lifecycle safety or validation is missing, add the measurement.** Extend native capture, the managed Profiler, its output/report and the Profiler → Resolver handoff as necessary; make instrumentation bounded, capture-gated, tested and explicit about scope/ownership. Do not bypass a missing useful metric by labeling the work semantic. Where direct instrumentation is genuinely infeasible or would itself disturb behavior, record the concrete limitation, an alternate source/behavioral proof and an explicit validation plan. Never invent values or distribute a shared native marshalling total across unproven individual subscribers.

**Measurement completeness is an engineering obligation, not a numeric eligibility threshold.** Profile and evaluate each investigated mod/callback on both native → Lua and Lua → native sides. Source-proof the real activation, wake, pending-work and fallback paths. Once that implementation is manually reviewed and approved as safe, the Resolver must admit supported, missing semantic stages even if measured work is tiny, zero or absent in that particular capture. Explicit source-only admission must retain null/unmeasured fields, strict source identity, drift guards and postconditions. Do not relax generic AUTO's independent mechanical/measurement admission policy.

For every candidate, document (1) available measurements and their attribution scope, (2) missing useful measurements and the native/Profiler/handoff additions made to obtain them, or a concrete instrumentation limitation, (3) the source proof and manual approval basis, and (4) performed CI and gameplay validation. The rule is **measure what can improve correctness; add missing measurements; do not trivially veto an approved safe semantic rewrite over its workload score.**

## Authoritative decision — why and when to optimize

**The semantic library is a library of safe, reusable source-proven transformations, not a leaderboard of expensive callbacks.**

- **Source proof determines permission.** Eligibility comes from the actual original/current per-mod Lua source: existing author-defined activation/deactivation states, lifecycle, exact wake triggers and required live listeners, active/pending work, input ownership, ordering and fallbacks. Wake triggers need **not appear in a profiling capture** and need **not be observed at runtime** to be established by a sufficiently complete source proof. Missing generic profiler wake-path classification is not a veto on independently proven authored semantics.
- **Apply every independently source-proven beneficial dormancy extension**, including tiny individual savings and already-low Lua CPU. No minimum `exclusiveMsPerSecond`, sampled native bytes, arbitrary contribution cutoff, or observed wake-transition-count gate for hand-authored semantics. Existing measured-callback/identity and source-safety admission contracts must not be silently discarded; investigate whether unobserved-but-applicable source-defined wake paths can be safely generated without inventing runtime evidence.
- **Profiling prioritizes investigation and verifies outcomes; it does not authorize the semantic behavior.** Count invocation frequency, registrations, native marshalling boundary samples/bytes, Lua execution/allocations, and matched before/after game behavior when available. Record each mod's measurable contribution for the library, even if small. Never invent per-mod attribution or guaranteed saved bytes from shared `OnAction` samples.
- A mod whose current Lua work is already gated can **still lack native-dispatch dormancy**. Completion of one stage or presence of an older marker is **not** completion of the others. Generate only missing, independently source-proven changes.
- Not every mod should sleep entirely. A continuously necessary observer, state watcher, wake event, menu input, queued completion, or author-requested exception remains active. **Document source-proven no-op/not-applicable decisions**; don't invent hooks or force dormancy.

## Workload-led discovery, source-led generation — confirmed user workflow

**Investigate by measured workload; generate by semantic source proof.** This is **not** a blind bulk-optimization campaign. At each iteration, choose a mod/callback from the Profiler's workload ranking (Lua CPU, call multiplicity, native marshalling/allocations, spikes and other meaningful costs), examine **both native → Lua and Lua → native** work, audit that mod's existing source and rules, and author/extend the safest A/B/C stages. Prioritize investigation by expected impact, but do not make measured impact a production-rule admission threshold.

**Rule lifecycle:**
1. **DISCOVERED:** Profiler highlights a hot callback/mod or another source investigation exposes a gap. Ranking is a queue, not an eligibility gate.
2. **SOURCE_PROVEN / IMPLEMENTED:** inspect actual mod state/wake graph and dependencies; implement source-safe stages in its authoritative injector. Preserve keybinds, event ordering, fallback and original gameplay.
3. **TESTED / CONFIRMED:** original-source one-pass generation, currently installed/partial source upgrade, idempotence, drift refusal, source postconditions, wake/suspend/resume and representative gameplay validation pass to the level available. Record which checks have passed; **do not equate CI with unperformed game tests**.
4. **PRODUCTION ENABLED:** once reviewed, confirmed to the required safety level, and entered as an enabled production semantic rule, the Resolver **must admit and generate every missing supported stage on matching source**. It must not trivially reject the rule for low Lua CPU, small native allocations, missing generic profiler wake classification, zero observed wake transitions, or absence of calls in a particular capture.
5. **ALREADY SATISFIED / SOURCE DRIFT / UNSAFE:** valid reasons to output no changed files are that all independently verified stages are already installed, applicable source/proofs no longer match, the mod is absent, or the transformation is genuinely unsafe. Expose a specific human-readable reason; never silently drop a confirmed source-safe rule because it was no longer hot after optimization.

**Capture coverage and rule authority must remain separate.** A zero-call *registered callback* is not proof of an absent feature; authored dormant states often prevent the profiler from observing a wake event. A rule must not be fabricated from missing captures or assumed ownership. Likewise, a capture with no callback entry cannot currently be treated as proof the callback exists. The existing Resolver is callback-row-driven: where a confirmed rule is installed but absent from that capture's ranked rows, an independent safe source-based candidate-selection path may be needed; **track and implement that separately instead of falsely claiming the gate is already solved**. Generic automatic optimization retains strict mechanical/observed safety requirements.

**Acceptance check:** ANALYZE and GENERATE must apply the **same** semantic policy; a confirmed production rule with supported original/live source and a genuinely missing stage must be reported as eligible and produce a pass despite sub-threshold or zero measured work. If an injector declines, identify **which actual source safety/postcondition failed**, not a profiler popularity threshold.

## Required dormancy stages — independently tracked per mod

| Stage | What must be source-proven and optimized | What does **not** count as completion |
| --- | --- | --- |
| **A. Lua execution / state-processing dormancy** | Suspend idle Lua polling, quest/scene/area scans, timers, UI/draw, unnecessary computation and per-feature processing; preserve activation, discovery, pending work, state transitions and cleanup. | An early return that accidentally loses wake events or assumes a journal entry is an active quest. |
| **B. Lua → native engine-work dormancy** | Avoid proven unnecessary native getters, entity/RTTI/blackboard queries, marshalling, duplicate construction and reads performed *by idle Lua logic*; retain essential wake/discovery reads at the source-proven minimal safe cadence. | Reducing Lua exclusive CPU but still executing unnecessary native reads; a speculative cache or shared query without safe invalidation. |
| **C. Native → Lua observer-dispatch dormancy** | Eliminate or consolidate unnecessary independently registered native callbacks, and suppress optional downstream subscriptions while inactive **when CET/0-Engine mechanics and the mod's wake graph allow it**. Preserve required activation/wake listeners and fallback. Where source-proven, use existing shared native observer and semantic per-subscriber activation/deactivation. | Merely inserting an early Lua return after CET already marshalled and invoked the native callback; or claiming a sleeping Lua subscriber eliminates the *shared* bridge's remaining native → Lua transition. |

**Native boundary reality:** Removing a mod's independent `Observe` registration or otherwise safely disabling native delivery can avoid its individual native → Lua invocation. Consolidating several observers into **one** 0-Engine observer can reduce duplicate native crossings, but that shared crossing and its argument conversion may still be required. A dormant subscriber behind the shared crossing suppresses only its own downstream dispatch until a native-level skip is separately proven and implemented. Do **not** mislabel all three results as “native hook eliminated.” If the native registration cannot be safely removed, keep it and document the unavoidable boundary.

**The semantic state/wake model—not allocation samples—is the source of truth** for when any A/B/C stage can sleep and resume. Build and extend these stages **inside each mod's existing** `src/G.CETProfiler.Core/Services/ModPasses/<Mod>.cs` (with existing documented specialist handling where applicable), retaining catalog identities in `resolver-knowledge/semantic-library.json`. Shared generic routing belongs in centralized generic services/0-Engine, never duplicated in mod rules. Do **not** create a second dormancy framework.

## Preserve user-approved per-mod behavior

Follow [DORMANCY-USER-AUTHORITATIVE-SPEC.md](resolver/DORMANCY-USER-AUTHORITATIVE-SPEC.md) **per mod**; that file is user intent, not automatic proof. Follow [DORMANCY-UNKNOWN-SOURCE-AUDIT.md](resolver/DORMANCY-UNKNOWN-SOURCE-AUDIT.md) for still-uncertain source behavior. The broad [DORMANCY-ALL-MOD-AUDIT.md](resolver/DORMANCY-ALL-MOD-AUDIT.md) is inventory/triage, not proof that 67 rules are implemented across all stages.

Specific invariants:
- Preserve the mod's configurable keybinds and input definitions **within the owning mod**. Never move ownership to 0-Engine, invent parallel bindings, hardcode `V`, or change consumed-input behavior.
- Preserve quest acceptance/start vs auto-created journal offers, progression prerequisites, proximity/interaction discovery, author event ordering, native observers required for wake, active gameplay responsiveness, scene/dialogue, saved state, hot reload/reinitialization, pending `Cron` work, cleanup, fallback and failure isolation.
- Dynamic or localized action interest requires actual source proof; do not replace `DriveAerialVehicle` dynamic exception lists or CallList localized matching with guessed exact action names.
- Retain the user's explicit **SKIP** dispositions unless the user changes them. Source-only reviews and the 67-rule catalog do not revoke SKIPs. `Minimap Widgets` broad dormancy remains uncertain; retain already-safe narrow optimizations.
- Respect source drift and cross-mod dependencies: no incorrect lifetime caches, global observer shutdown, speculative patching or silent changes to gameplay.

## Source and generation contract

For each mod with a candidate source-proven extension:

1. Inventory **all existing rules and installed stages first**, including partial/earlier G-CET output. Map source-defined `ACTIVE`, `DORMANT`, `WAKE_PENDING`, and cleanup / pending-job transitions **only where those states exist in the real mod**. Record the actual author wake listener(s), event writers and required state checks. Do not impose these as synthetic global states.
2. Independently classify stage **A / B / C** as `UNREVIEWED`, `SOURCE_PROVEN_MISSING`, `ALREADY_SATISFIED`, `IMPLEMENTED_NEEDS_GAME_TEST`, `VERIFIED`, `NOT_APPLICABLE_SOURCE_PROVEN`, or `BLOCKED_SOURCE_PROOF`. The status must have real source anchors/postconditions, not just a marker or profiler inference.
3. Extend the **existing authoritative per-mod injector**, keeping earlier compatible stages intact and updating stage-completion recognition. Implement complete optimizations from **pristine original Nexus source in ONE generated pass**. Also compose correctly against the user's **current optimized/partially optimized source**, emitting only genuinely missing files/stages.
4. Make the change version-fluid: stable source/control-flow proofs; accept equivalent formatting/known variants; **fail closed** on substantive behavior drift. Never rely on a previous pass marker as a prerequisite or count of completed optimization.
5. Test **pristine → complete one pass**, current/live partial → missing stages only, idempotence, harmless formatting differences, unknown source drift refusal, fallback, activation/deactivation/wake/cleanup and no lost events or input ordering. Keep supported files read-only until the generated overlay is deliberately applied by the user.
6. Report **measured pre/post deltas by metric and scope** when captures exist; otherwise label `NOT_MEASURED`. Capture exact native boundary totals and bounded class/method samples separately; measure registration/observer/subscriber counts independently. Never attribute all `OnAction` bytes to one mod or extrapolate sample counts to savings.

## First extended native-dispatch implementation — batch 1

- **Mod #35: `immersive_third_person`.** Kept its existing two-rule semantic model. Extended the existing `immersive-third-person-action-routing` injector with **v2 source-controlled subscriber dormancy**, not a new independent optimization framework: register the shared action subscriber in its original active state, then immediately pause it through `SetActive(false)` when the mod begins in FPP (falling back to the old active behavior on older runtimes without `SetActive`); on the mod's own `applyTppMode` → active, `restoreFppMode` → paused, `updateSessionGuard` session gap → paused. The existing mod-owned CET toggle/keybind remains authoritative, old 0-Engine/Observe fallback stays operational. The shared native observer still exists; this stage suppresses only this mod's unnecessary downstream fanout.
- **Source proof:** inspected available real `PASS4D_ITPP_init.lua` (259 KB, not a fabricated profiling wake record); its three top-level author state writers are exactly the three described. A new state writer or changed route shape fails closed. Source-completion verifier checks all three transitions rather than treating old routing v1 as v2 complete.
- **Compatibility:** pristine original → v1 routing and v2 dormancy in a **single pass**; already-v1 installed source → only v2; fully v2 → idempotent; drift → refuse. Tests in `tests/ImmersiveThirdPersonActionRoutingContract.ps1`. The pinned 0-Engine `ActionRouter.Subscribe` already supports `active`, `SetActive`, `Pause` and `Resume`—no runtime expansion required yet. `tests/ActionRouterDormancyLuaContract.lua` checks disabled subscriber, resumed action delivery, other-mod isolation and unchanged low-level action-type decoding. Added to the existing zero-engine Lua CI lane.
- **Status:** SOURCE IMPLEMENTED and [CI #38718963646](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38718963646) **GREEN (all 4 jobs)** for shared action dormancy, pristine + v1 upgrade, idempotence, drift and Lua runtime. **Gameplay verification remains pending.** There is no measured native byte saving or claimed elimination of the one shared native/Lua entry. See [working 62-mod stage matrix](resolver/EXTENDED-DORMANCY-STAGE-MATRIX.md).
- **Source-only rule discovery (now implemented and CI-verified):** `SemanticLibraryService` accepts an explicit per-catalog-rule `generation.sourceIndependentAdmission` opt-in, resolves one installed mod identity, and applies existing live-source anchors and the author-specific stage-completion verifier without relying on callback capture rows. Resolver emits `semanticSourceOnlyCandidates` separately from `callbackFamilies`. Generation consumes these only via the existing mod-specific injector, never generic AUTO, and reports `admissionEvidence=SOURCE_PROVEN_NO_CAPTURE` with `runtimeMsPerSecond=null` rather than fabricating a measurement. The first opt-in is **ITPP action dormancy only**, not all 67 rules. **CI [#38720069775](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38720069775) GREEN — all 4 jobs**, including empty callback capture, one-pass generation, old v1 upgrade, source-completion and drift checks. The packaged native DLL and product version remain pinned unchanged.
- **Next rule-review intake:** inspected the existing mod-owned injectors for EasyTrainer, DriveBus, DiscardAmmoOnReload, AirBackFlip, BackStepDuo, AutoDropWeaponOnPickupEquip, BetterLootMarkers, Straight Edged Controls and CallList. Marked nine corresponding [A/B/C matrix](resolver/EXTENDED-DORMANCY-STAGE-MATRIX.md) rows as **existing partial** where a stage is already implemented, not fully native-dormant. Their individual author wake/source lifecycles still need proof before C is extended.
- **Remaining:** all other per-mod A/B/C source re-audits and eligible stages, additional deliberate source-only opt-ins where original source proof supports them, actual game wake/session transitions and comparable native/Lua capture A/B. This task is NOT complete.


## Follow-on implementation — DriveBus source-only admission (2026-10-15)

- Extended the **existing** `drivebus` production semantic rule with explicit `generation.sourceIndependentAdmission=true`; no new action routing, wake assumptions, subscriber pause or native dispatch removal. Its already-reviewed three-file injector still supplies exact action/fallback proof and active-timer gate, with source completion/idempotence for prior routed/partial forms.
- Extended `tests/DriveBusPartialSemanticContract.ps1` to run DriveBus generation with an **empty callback capture**, demanding a single source-only candidate, three authored staged files, no synthetic callback family, `SOURCE_PROVEN_NO_CAPTURE` and null measured runtime. Existing zero-call registered, pristine, partial, historical and drift tests remain.
- **CI status: pending verification at time of this update.** This is an admission/coverage extension, **not** proof of further stage-C dormancy or in-game saving. Native measurement-gap review and source-defined bus activity/wake lifecycle are still needed before considering a new native-subscriber pause. Product version and pinned native unchanged.

## Current verified state versus unfinished task

**Already implemented before this task was restated (do not repeat or roll back):**
- Existing semantic source transformations and 0-Engine action routing, plus extensive Lua-side gates across the catalog.
- Soft 3 ms/s materiality policy for pre-reviewed authored semantic rules with measured callback invocations and source-proof admission. Profiler generic dormancy wake classification is evidence only.
- Partial/current-source semantic composition improvements (e.g. DriveBus's existing routed `OnAction` can coexist with a still-missing `Cron` guard; StreetGamesPoker's already-completed lanes are recognized).
- Dedicated `nativeMarshallingAllocationPressure` evidence in summary → Resolver, separate from Lua CPU. Experimental native class-hash capture built successfully as an isolated artifact, **not** promoted to the regular Profiler DLL; native game validation remains separate. This identifies RTTI **class+method envelope**, **not** the allocation owner mod or per-subscriber savings.
- CI-proven managed measurement plumbing and existing semantic injectors do not demonstrate complete A/B/C dormancy coverage or observed native dispatch elimination in gameplay.

**Current implementation of the source-led admission decision (2026-10-15):**
- The shared `SemanticAdmissionPolicy` now admits a production-enabled, source-proven semantic rule with a **registered callback row** even if the capture reports zero calls and zero exclusive Lua CPU (`SEMANTIC_AUTHORED_UNMEASURED`); the same admission evaluator is used in ANALYZE and GENERATE.
- The Resolver preserves such zero-observed-work rows **only when the mod's current source and production rule independently qualify**. Generic AUTO still excludes arbitrary zero-call registrations and retains its mechanical source/runtime proof.
- The DriveBus production regression now forces `callsPerSecond=0` and `exclusiveMsPerSecond=0`, verifies the `SEMANTIC_AUTHORED_UNMEASURED` reporting lane, and verifies complete pristine-source pass generation. Existing zero-Lua native-dispatch tests retain their unsafe/generic exclusions.
- **Verified:** [Main CI #38714129254](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38714129254) succeeded in all four jobs, including DriveBus and existing semantic/native-dispatch contracts, at code commit `e53c1d8`. Product version 1.2.7 and pinned native DLL unchanged; CI is not in-game validation.
- **Not yet implemented:** Independent source-based candidate enumeration for rules whose callbacks are **entirely absent** from a given capture, plus the outstanding A/B/C per-mod native-dispatch stages. This acceptance improvement is not a claim the full extended-dormancy task is done.

**Still required — THIS TASK IS OPEN:**
- [ ] Audit the **67 production semantic rules covering the 62 user mods** against existing actual pristine and live sources; preserve explicit SKIPs, unknowns and genuine always-on work. Don't mistake every unmatched rule/callback for a missing optimization.
- [x] Create the [full 62-mod A/B/C review matrix](resolver/EXTENDED-DORMANCY-STAGE-MATRIX.md), with explicit user SKIPs and unreviewed stages kept distinct from missing stages. **Populating source-proven final classifications remains OPEN**; `ALREADY_SATISFIED` for existing Lua-only work must not close C.
- [ ] Extend every **eligible, source-proven** per-mod semantic rule with its *missing* A/B/C stages regardless of measured size. Where C cannot eliminate native crossing, document whether shared consolidation, downstream subscriber gating, or `NOT_APPLICABLE_SOURCE_PROVEN` is the safe result.
- [ ] Ensure ANALYZE and GENERATE select/compose every individually reviewed production semantic stage on supported source regardless of low/zero observed workload. **DONE for registered zero-work callbacks and explicitly opted-in source-only ITPP**, CI-confirmed; **OPEN for auditing/enabling the remaining catalog source-only rules** and per-mod A/B/C stage completeness. Reject only for specific source/version/safety/completion reasons; keep zero false savings and no independent registration pollution.
- [ ] Cover pristine/partial/live/formatting/drift and lifecycle contracts in CI; run full Profiler+Resolver CI after code changes (and independent native CI only for native-source changes).
- [ ] Validate representative native observer/consolidation effects and in-game wake/feature behavior against comparable captures and real game runs; measurements evaluate improvement but do not authorize semantic source behavior.
- [ ] Close the task only after the per-mod matrix is resolved (verified or justified source-proven no-op/blocked), generated overlays cover all safe stages, tests pass and remaining game-validation limits are explicitly recorded.

## Next execution order

1. Read the source of the current 67 catalog rules, `ModPasses`, actual original/live test inputs and [per-mod source index](resolver/MOD-PASS-SOURCE-INDEX.md); **do not start another data-collection project** as a prerequisite to applying known source-proven semantics.
2. Produce/update the A/B/C per-mod completion matrix **inside this file or a linked Resolver working matrix**, so it survives chat boundaries. Clearly separate what's already installed from what is only specified; inspect existing `OnAction` action router and subscribe/unsubscribe mechanics first.
3. Implement safe missing native-dispatch stages alongside each mod's existing Lua dormancy source stage, prioritizing source readiness, not allocation magnitude. Small proven optimizations belong in the library.
4. Run tests and CI, inspect emitted ZIP/manifest against pristine and live sources, then collect game validation evidence without claiming savings prematurely.
5. Keep updating `docs/LATEST-WORK.md` until the complete extended-dormancy task is closed. New tasks replace this document **only after completion or explicit user redirection**.

## Relevant authority and evidence links

- [Handoff and implementation invariants](HANDOFF/CORE.md)
- [Per-mod user intent (62 mods)](resolver/DORMANCY-USER-AUTHORITATIVE-SPEC.md)
- [67-rule / native action audit](resolver/DORMANCY-ALL-MOD-AUDIT.md)
- [Unknown source and source-backed wake review](resolver/DORMANCY-UNKNOWN-SOURCE-AUDIT.md)
- [Native marshalling metric and capture class attribution](gc/research/NATIVE-ONACTION-OUTGOING-METRIC-2026-10-15.md)
- [Native-dispatch / admission analysis](resolver/research/SEMANTIC-SOFT-ADMISSION-AND-NATIVE-TRANSITIONS-2026-10-15.md)
- [Build and release guardrails](HANDOFF/BUILD-PIPELINE.md)
- [Engineering history](history/ACTIVE-STATE.md)
