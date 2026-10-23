# Phase 1+2 preservation recovery — single-source engineering handoff

**Active task: INCOMPLETE / generated patch NOT ACCEPTED (2026-10-23).**
**Branch:** `dev/resolver-profiler-handoff` (source changes merged from `dev/virtual-runtime-view-20261023` at `ddbaa666`). Continue here; do not restart development from a historical ZIP, re-merge draft PR #28, or treat a green build as semantic parity.
**Master task:** [LATEST-WORK](../LATEST-WORK.md). **Mandatory contract:** [PROJECT-CONTRACT](PROJECT-CONTRACT.md) and [HANDOFF/CORE](../HANDOFF/CORE.md).

## 1. Goal and acceptance rule

Restore the **complete effective Phase 1+2 optimization** through **one Resolver generation on supported, untouched author mod source**, composing generic 0-Engine first, native integration second, and source-proven mod-specific semantics against the final staged source. Prior manually optimized Lua and extracted coordinator files are **comparison evidence, not input prerequisites**.

**Non-negotiable:** If generated output breaks a mod or drops an optimization, repair its **existing authoritative generating logic**, then regenerate from the **untouched original mod**. Do **not** ship a second semantic that repairs the previous broken ZIP. Multiple legitimate per-mod rules/stages may coexist, but all must be owned, ordered, and fully composed in the same first pass. Upgrades from already optimized/partial source are a separate supported compatibility path; they must not become necessary to achieve the pristine result.

Keep generic transforms centralized; group authoritative mod transformations under `src/G.CETProfiler.Core/Services/ModPasses/`; use separate **owned Lua workers** where useful, without duplicate logic in `init.lua` and workers or the 200-local ceiling. Catalog rule identity and source-file ownership are different concepts: do not delete legitimate catalog rules simply to consolidate C# files. Source/version-fluid matching, lifecycle, wake proof, author fallbacks, idempotence and rejection of material source drift remain mandatory. No Lua SHA/version hardlocks (retain the Profiler's original ASI binary hash/restore safety).

## 2. Inputs, provenance and reference verdict

**Files supplied in the conversation, not tracked source fixtures in the repository:**
- **Pristine author stack:** `modsLIVEniOPT.zip` — the user's untouched CET mods, including stock `0-Engine 0.18.6`. This is the required original-source generation input; retrieve the uploaded file again in another work environment if necessary.
- **Optimized reference:** `G-CET_PHASE1+2_GC_EXTENDED_DORMANCY_NATIVE_PLAYER_CACHE(2).zip` (Phase 1+2; other numbered copies may exist). Inspect actual ZIP contents before choosing a variant.
- **Failed newly generated output:** `G-CET_PASS_20261023-181457.zip`; paired Resolver and pass JSON were provided. They are debugging evidence, not the base for further generation.
- **Additional comparison:** `G-CET_PASS8_ITPv3_DAV_HOTSCENES(1).zip` contains prior action-routing stages; not a substitute for the full Phase 1+2 acceptance reference.

**Last source-level audit (Phase 1+2 vs 18:14:57 pass):**

| Test | Verified finding |
| --- | --- |
| Inventory | Reference **426** files; generated **404**; **259** identical; **144** modified; **23** reference-only; **1** generated-only |
| Native assets | DLL and location manifest **identical**, **323/323** compiled anchors retained |
| Worker imports | **6/6** remaining worker imports resolve, but reference had **21** workers; relocation/inlining alone does not mean lost behavior |
| Missing extras | **8** reference-only supporting mod modules; determine whether original install supplies them or generation must emit them |
| Native consumers | **17** Cyberpsychos native anchors are present without their intended generated consumer |
| Implementation | **FAIL**: missing scheduled/wake work and wrong 0-Engine/action contracts |
| Dependencies | **PARTIAL**: missing semantics and mismatched contracts despite present binary/anchors |

The comparison is not a proof that every one of the 144 modified files is functionally different; it identified sufficient concrete regressions to reject the pass. Do not assume file counts must match when relocating coordinators, but prove *every* former optimization's effective implementation and dependencies. Previous manifest reported **1,313 transforms / zero generation failures** even while omitted advanced stages persisted: reported success is not proof of stage completeness.

The Phase 1+2 reference integrated `0-Engine 0.18.11-PASS4.2.1-PHASE-CADENCE-FIX`; failed output used author `0.18.6` plus an incomplete compatibility layer. It had simplified scheduler context (`inMenu=false`, `state=nil`, `generation=0`) and potentially duplicate CET update dispatchers. Hotscenes' required after-phase was accepted by a before-only router. The reference's integrated *behavior* is the target, **not necessarily its exact file layout or version string**.

## 3. Current code / verification status

| Area | Existing changes on handoff branch | Not yet established |
| --- | --- | --- |
| **0-Engine** | Read-only `EffectiveRuntimeView` / `FixedZeroEngineRuntime`; intended central author `onUpdate` integration, namespaced Scheduler and phase-aware G-CET ActionRouter | Equivalence to Phase 1+2's scheduler lifecycle/context, single dispatch and action phase across the actual full stack |
| **Hotscenes** | Phase-aware router integration and safe fallback paths; source-oriented after-phase tests exist | Exact original `ObserveAfter` ordering with deployed effective runtime and in-game behavior |
| **sitAnywhere** | `ModPasses/SitAnywhere.cs` now emits frame scheduler + coordinator **in the primary pristine-source generator**; owned by `sitanywhere`, not a mismatched `sitanywhere-engine-worker` marker | Full reference parity / real-user-archive gameplay; existing separate worker rule retained for supported old partial-source upgrades |
| **Overall CI** | **#1904** on handoff, commit `285f8bbe`, **5/5 green**; both Profiler and Resolver v1.3.0 build artifacts published. Pristine-*shaped* sitAnywhere single-pass and older coordinator tests passed | No true full-stock ZIP regeneration/parity/game test has passed. CI green does not accept the patch |

Earlier red #1901/#1902 had the wrong sitAnywhere worker ownership marker; commits `2678daa4` / `285f8bbe` repaired the authoritative generator and test. The later documentation-only commit `b3d6cb57` recorded status. See [CI #1904](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40013001972), [Hotscenes/DAV integration status](HOTSCENES-DAV-INTEGRATION-STATUS.md), and [virtual runtime design](VIRTUAL-RUNTIME-VIEW.md).

**Important:** The new sitAnywhere test uses a *synthetic author-shaped fixture*, not an archive-byte-backed regression test against `modsLIVEniOPT.zip`. That distinction must remain visible.

## 4. Unrepaired semantic regressions — prioritize these four

| Priority / owner | Observed difference and likely source | Existing code to repair | Required pristine output |
| --- | --- | --- | --- |
| **P1: Repeatable Cyberpsychos** | Source generator retains early 1 Hz runtime / 5 s presentation; native fallback stage requires `TryAttachRepeatablePass4` and PASS4 watchdog anchors already present. The **17 native wake consumers** and scheduled/incremental stages are absent from the failed pass. | `ModPasses/RepeatableCyberpsychos.cs` + `RepeatableCyberpsychosNativeLocations.cs` (one mod-owned coordinated implementation; native rule remains separately identifiable as appropriate). | Build PASS4 scheduling + watchdog/wake, native consumers, safe fallback and cleanup atomically from untouched source. Test all source-owned sites, scene transitions, idempotence and partial-upgrade compatibility. |
| **P2: RICA** (`repeatable_increased_criminal_activity`) | Current primary rule emits earlier accumulator lanes; secondary `rica-engine-worker` assumes `TryAttachRicaPass4` with pre-existing **six** jobs. Reference central Scheduler and idle diagnostics were not recreated. | `ModPasses/RepeatableIncreasedCriminalActivity.cs`, `RicaEngineWorker.cs`, and relevant diagnostics/lazy-reward source-owned stages. | Generate six scheduler jobs, bounded reward/state/mappin/diagnostic/system cadence, passive diagnostics handling, rollback, source-safe fallback and owned worker **from pristine source in one pass**. |
| **P3: CyberTrials** | Previous source-level pass retained cadence/mappin optimizations but omitted **five scheduled jobs** from reference. | `ModPasses/CyberTrials.cs`, any existing same-owner helpers. | Compose five-job scheduling plus existing overlay, mappin, race/gameplay and cleanup logic without requiring an already optimized init or extra subsequent pass. |
| **P4: Dedra Combat / ITP** | Existing worker checks the *public ITP API* for `__gcetDedraWakeBridgeV2`, while generated ITP source sets `mod.__gcetDedraWakeBridgeV2 = true` on an internal table. The worker can exist yet its wake contract fail. This was **already** an issue, not necessarily a new regression. | `ModPasses/DedraThirdPersonCombatAddon.cs` and `ImmersiveThirdPerson.cs`; ensure integration is corrected at the authoritative producer/consumer boundary. | Source-proven shared wake contract, fallback, source/worker owner consistency, and verified combat-camera transitions from pristine originals. |

Separate supported behavior to preserve while working: QuestRunner inline cadence, native reads and location subscriptions for Poker/Tunnel/Dedka/Card Collection/CyberStunts/Illegal Mechanic/Yaiba/Ziggy/VehicleMileage/f80h, and Hotscenes/DAV action ordering. Relocated inline vs external coordinators are acceptable **only after equivalent source-level semantics are shown**.

## 5. Why earlier CI missed it

Some contracts check **partial or pre-optimized source**, not a pristine author source. For example `tests/CyberpsychosWorkerCompletionContract.ps1`, `RicaEngineWorkerContract.ps1`, and `SitAnywhereEngineWorkerContract.ps1` contain PASS4-shaped fixture source with the scheduler/attachment already authored. These protect relocation and idempotence but do **not** prove creation of all missing PASS4 stages in a first pass.

The per-mod source rule can report `SEMANTIC_RULE` applied even when it installed only an earlier stage. The catalog / `SemanticPassGeneratorService` must validate **stage completion postconditions**, not merely an injector success marker, output file count, known legacy marker, or declared dependency API. Ensure generic → native → semantic order is safe, with each semantic evaluated against the **final staged source**; preserve prior optimizations rather than regenerate legacy-only outcomes.

Do **not** weaken CI checks or register corrective semantics to get green. Add original-source + partial-source + formatting-variant + drift-refusal + second-pass idempotence cases to the **existing generators'** contracts. When original sources differ substantially, fail closed with a precise reason.

## 6. Execution order and release gate

1. **Lock baseline inputs:** inventory and hash the user's actual untouched ZIP and chosen Phase 1+2 reference; retain source-only audit files/CSV if available. Use original author modules, not the failed optimized ZIP. Keep an independent pristine copy for every test.
2. **Fix actual runtime contracts:** central `onUpdate` behavior, real frame/menu/session context, `Schedule` job lifecycle, no redundant CET update callback on known 0-Engine; proper *before versus after* `OnAction` delivery and CET fallback.
3. **Repair four existing mod generation paths**, starting Cyberpsychos and RICA, then CyberTrials and Dedra. Consolidate their mod-owned C# structure **only within this set for now**; do not reorganize the remainder of the mod catalog.
4. **CI / source tests:** untouched archive or verified byte-backed fixtures → complete one-pass source/owned workers; original source remains unmodified; supported partial-source upgrade and second-run idempotence; Lua local ceiling and load/owner/dependency contracts. Keep the known generic/native and 0-Engine contract suites green.
5. **End-to-end generation:** build Resolver, analyze the original stack, generate a new pass ZIP **once**, and inspect manifest `applied/skipped` per named rule and distinct expected stage. Missing intended stage with an otherwise successful manifest = **FAIL**.
6. **Three-level preservation comparison:** **Inventory** (mods, 0-Engine, workers, auxiliary files); **Implementation** (all scheduling, native reads/location wakes, generic gates, semantic workers); **Dependencies** (0-Engine APIs, native owners, proper action phase and wake bridge). Reconcile every missing capability, not necessarily identical file layout.
7. **Gameplay validation:** test actual installed patch for affected quest/NPC events, CyberTrials races, sitAnywhere workspot recovery, DAV/Hotscenes input/menus, Dedra combat-camera wake and player/session reload; compare captures for meaningful native/Lua/GC differences. CI cannot replace these tests.

**Acceptance = all required optimization stages present and functionally/source-proven equivalent, source-safe single-pass generation from untouched mods, CI green, dependencies satisfied, and gameplay validation separately recorded.** Until then: **do not recommend installing the new ZIP in place of Phase 1+2; do not tag/release, merge speculative fixes or claim full stack optimized**.

## 7. Start a new session here

Read [HANDOFF/CORE](../HANDOFF/CORE.md), [LATEST-WORK](../LATEST-WORK.md), and **this** document. Verify live handoff HEAD and latest GitHub Actions before editing; historical run IDs above are evidence, not current status. Retrieve the original ZIP and reference from conversation attachments or ask for them if inaccessible. Inspect actual current original Lua and output for each target; **do not rely on synthetic fixtures to infer unknown author behavior**. Repair authoritative generating logic, write tests against original source, regenerate once, and report measured preservation instead of counts alone.

Maintain this brief for the active Phase 1+2 preservation scope and archive a final outcome in the subsystem/history after acceptance; do not copy the entire dated investigation into timeless `docs/HANDOFF/`.
