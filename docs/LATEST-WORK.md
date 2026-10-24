# LATEST WORK — Phase 1+2 optimization preservation recovery

**Active task: INCOMPLETE — no real-stack output acceptance yet.** Updated **2026-10-24**.  
**Repository / branch:** `giorgioherma/G-CET_Runtime_Profiler` / `dev/resolver-profiler-handoff`.  
**Shared Profiler + Resolver version:** **1.3.1** (do not bump without explicit user authorization).  
**Latest verified implementation CI:** [main build #1970](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40095214923), commit `b14d6ad9`, **5/5 jobs successful**; both `G-CET-Resolver-v1.3.1` and `G-CET-Runtime-Profiler-v1.3.1` Windows artifacts published. Changes after that run were documentation-only; check current HEAD and Actions anew before implementing.

**Authoritative detailed task:** [Phase 1+2 preservation-recovery handoff](resolver/PHASE12-PRESERVATION-RECOVERY.md).  
**Mandatory engineering contracts:** [HANDOFF/CORE](HANDOFF/CORE.md), [Resolver PROJECT-CONTRACT](resolver/PROJECT-CONTRACT.md), [BUILD-PIPELINE](HANDOFF/BUILD-PIPELINE.md).  
**Prior work, not the active task:** [archived native/Lua callback and 0-Engine allocation investigation](history/NATIVE-LUA-CALLBACK-LATEST-WORK-ARCHIVE-20261024.md). Its experimental GO/NO-GO and runtime gaps were not resolved merely by moving the notes.

## Objective and hard acceptance rule

Restore the **effective complete Phase 1+2 optimization behavior** through **a single composed Resolver generation from supported untouched author mod sources**. Phase 1+2 is the authoritative *optimization* reference, not necessarily an identical Lua layout, number of generated workers, or replacement for updated original gameplay/audio code.

Required transformation order: **generic 0-Engine → native integration → final source-aware semantic stages**, all operating on the current staged source. A generated pass must preserve existing optimizations and source-mod quest/gameplay, dialogue, UI/input, scheduling, native wake, timers, session/player lifecycle, cleanup, fallbacks and dependencies.

**When output loses an optimization or breaks a mod, repair the existing authoritative generating logic.** Do not add a repair-only semantic, duplicate a Lua coordinator/inline implementation, require prior optimized output, weaken source proof, or use Lua SHA/version/line-number locks. A previously optimized stack is a comparison or compatibility input only. Maintain compatible partial-source upgrades, idempotence, formatting variants and source-drift refusal. Profiler binary verification/restore safety is separate and must remain intact.

## Source inputs and reference reconciliation

- **Pristine source:** the user's untouched CET mod stack (historically `modsLIVEniOPT.zip`; the separately supplied `modsLIVEstock-NoOptimizations.zip` must be inspected before treating it as the exact same baseline), with stock author 0-Engine. Preserve an unmodified original during generation.
- **Optimization reference:** the Phase 1+2 `G-CET_PHASE1+2_GC_EXTENDED_DORMANCY_NATIVE_PLAYER_CACHE` archive; select and hash the actual supplied variant before comparison.
- **AudioXL caveat:** six reference mod files contain contaminated older AudioWare/AudioXL author gameplay/audio baselines. Compare their **G-CET optimization behavior** against the untouched Nexus originals **plus the corrected AudioXL source**, not the wrong-base gameplay bodies. The change from old `AUDIO_SYSTEM` to appropriate corrected AudioXL integration is not automatically a lost optimization. The separately provided corrected AudioXL files and required author modules must be source-reconciled; do not copy contaminated reference Lua wholesale. Resolver does not produce separate REDscript AudioXL files.
- **Old generated ZIPs, including `G-CET_PASS_20261024-033804.zip`, predate the latest fixes.** They are debugging/comparison evidence, never accepted output or prerequisites for new generation.

## What is implemented and actually verified

The following fixes are in the **existing generating implementations**, not in additional corrective semantics:

| Area | Established generator repair / preservation requirement | Evidence boundary |
| --- | --- | --- |
| **f80h_aroused_expressions** | `ModPasses/F80hActiveGcSweep.cs` composes original-source PASS8 0.50-second empty-runtime Cron dormancy, correct disabled → inactive → RNG → busy/Cron ordering, runtime reset, native LookAt wake suppression during safe OnDetach, and existing tracked/pending/pre-active/audio/GC ownership and fallbacks. | Actual original/corrected-source structural audit + synthetic first-pass, partial, idempotence and drift contracts; no complete fresh user-stack output or gameplay proof. |
| **ImmersiveVDialogueExpanded** | Existing generic UI/Cron pipeline emits the source-proven hidden-UI/pending-input guard and timer dormancy in `init.lua` **together with** the required `modules/Cron.lua` `Cron.HasActiveTimers()` source change. Preserve timer/no-op semantics and input reset. | Original-like multiline Cron/UI admission, fixtures and CI verified; new real output pending. |
| **rarobottruck_1** | Existing generic cross-file onDraw transform recognizes bare `ui.update()` and optional `pcall(require)`/no-op UI fallback, guards hidden UI **without dropping pending input cleanup**. | Source shapes and fixtures verified; new real output pending. |
| **Earlier restoration** | Catalog/source admission repaired for AutoLoot 3.11.1 world-pin/early-radius, NPC Outfit radar caching/passive look-at, and Native Interactions no-pin guard; pristine native-read worker generation repaired for Panam/Hotscenes; authoritative 0-Engine Scheduler/ActionRouter namespaced under `modules/G-CET`. | Prior contracts/CI green; end-to-end reference parity and remaining native reads still open. |
| **Prior mod-owned work** | Cyberpsychos six-job/native site consumers, RICA six jobs, CyberTrials five jobs + owned Cron helper, sitAnywhere, Dedra/ITP bridge and Hotscenes action-phase corrections have source-shaped generation contracts. | Do not mistake synthetic fixture CI for full archive-byte or gameplay equivalence. |

**Verified build:** [#1970](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40095214923) passed all five jobs, including `F80hNativeWakeContract.ps1` and `GenericUiSequenceContract.ps1`. The original and corrected AudioXL files were statically audited for the three repaired paths. **Not verified:** regenerated current-binary ZIP against the complete real untouched stack, all effective Phase 1+2 stages, LuaJIT runtime, gameplay stability, or matched performance. **A green build, a transform count and zero generation failures are not acceptance.**

## Next execution — in order

1. **Re-establish exact input provenance.** Inspect/hash the actual provided stock ZIP, corrected AudioXL replacement sources/modules, chosen Phase 1+2 reference and current GitHub HEAD. Do not silently equate archives with similar names. Keep the author original read-only.
2. **Run the latest v1.3.1 Resolver once** against the cleaned untouched stack; retain the output ZIP, Resolver JSON, pass JSON and per-rule applied/skipped/deferred reasons. Do not preapply historical passes. Refuse unknown source or partial-stage ambiguity with a precise diagnostic.
3. **Verify the three newly restored paths in real generated files:** (a) f80h PASS8 lifecycle/Cron/OnDetach ordering and native/GC coexistence; (b) ImmersiveVDialogueExpanded `init.lua` + `modules/Cron.lua` emitted atomically with correct hidden UI/input semantics; (c) rarobottruck `onDraw` guard and optional-module fallback. Check both original → complete one-pass output and partially optimized input → safe, idempotent output.
4. **Conduct the full three-level Phase 1+2 preservation audit:** **Inventory** — optimized mod coverage, 0-Engine framework files, existing author auxiliary modules and source-owned workers; **Implementation** — native reads, dormancy/wake, scheduler jobs, UI guards, action phases, per-mod semantic behavior and fallback; **Dependencies** — every worker import/owner, appropriate 0-Engine APIs, G-CET Scheduler/ActionRouter ownership, native DLL/location owner and consumer, and correct mod/bridge registration. Equivalent inline logic may replace an older worker **only when behavior, fallback and ownership are proven**; file count alone is not a verdict.
5. **Prioritize remaining gaps:** Native Interactions **manager/world/session player/quest reuse** and its no-pin observer composition; **QuestRunner** extracted worker/UI-path and source-aware staged preservation. Recheck AutoLoot, NPC Outfit, Panam/Hotscenes local-limit fallback, Cyberpsychos/RICA/CyberTrials/sitAnywhere, Dedra–ITP, Hotscenes/DAV action phase, and native-location users as their actual output warrants. No source-stage may disappear merely because capture workload is low or missing when approved source-only admission applies.
6. **Validate actual generated Lua and lifecycle/dependencies.** Check LuaJIT-compatible syntax/bytecode where possible (Lua 5.3 static parsing is not LuaJIT proof), source-drift refusal, worker import resolution, local-variable budget, single 0-Engine dispatch, job cancel/rearm/reset, input cleanup, player/session transitions, native owner availability, idempotence, and fail-open author fallbacks.
7. **Report real-stack implementation/source equivalence separately from in-game acceptance.** Only the user/gameplay run can validate mission state, race/workspot/dialogue/UI operation, reload, wake transitions, startup, and native/GC/runtime performance under matching conditions. Retain Phase 1+2 as the reference until preservation is independently shown; do not recommend replacing it as accepted or tag/release solely because CI is green.

## Required report and completion gate

For each missing or changed reference capability, record **mod, original author source, Phase 1+2 reference behavior, generated behavior/file owner, required framework/native dependency, evidence category, and final status**. Distinguish `IMPLEMENTED`, `CI/SOURCE-CONTRACT TESTED`, `REAL-STACK SOURCE-EQUIVALENT`, `GAMEPLAY-VALIDATED`, and `UNRESOLVED`. Avoid invented performance or ownership claims.

**Task is complete only when all required optimizations are present and source-safe from untouched originals in one pass; equivalent generated implementation and dependencies are verified across the real stack; required CI is green; and gameplay/runtime validation is explicitly recorded (including any remaining limits).** Until then, remain **INCOMPLETE** and continue updating *this* `docs/LATEST-WORK.md` in place after material findings. Keep detailed investigation and dated evidence in the focused [Phase 1+2 recovery document](resolver/PHASE12-PRESERVATION-RECOVERY.md) / [engineering history](history/ACTIVE-STATE.md); keep `docs/HANDOFF` timeless. Documentation-only commits must not trigger routine CI.
