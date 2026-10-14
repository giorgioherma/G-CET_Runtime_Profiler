# PASS3 live-stack consolidation — paused work checkpoint (2026-10-14)

Status: investigation and scoping only. **No new Resolver rule, regression test or runtime optimization has yet been committed for this task.** Do not claim the planned passes were implemented.

## User goal and authoritative input

- Current live stack with automatic Resolver passes: `modsLIVEnow.zip`.
- Earlier manually optimized changed-files-only overlay: `G-CET_0-Engine_ORIGINAL_PASS.zip`. **Not** a complete historical stack; do not interpret folder difference as net-new mod count.
- Capture: `CET-20261014-211612_PASS3_FIXED_QUEST_ACTIVE_AND_NATIVE_ADDITIONS.zip`.
- Exact phases: 0–60s idle; 60–150s movement/jumping/dashing; >=150s combat.
- User wants source-proven consolidation of native pure override allocation paths (dominant RTTI hash `9658066991423262154`), 0-Engine `onUpdate` and `PlayerPuppet::OnAction`, ImmersiveFirstPerson `onUpdate`, GigCore `onUpdate`, and eligible new independent event observers (e.g. CallList). Preserve gameplay and measure CPU + allocations.
- All new Resolver rules must be **as version-fluid as possible**: behavior/control-flow source proofs rather than SHA/version/line/indent; original + live already-optimized source; partial-stage upgrades; whitespace/comment/quote variation; idempotence; fail closed on behavior drift.

## Source observations established before pause

1. `src/G.CETProfiler.Core/Services/SemanticInjectors.cs` is the actual production injector (not the Resolver.App folder). `resolver-knowledge/semantic-library.json` contains 65 active production entries. `SemanticScopedStages.cs` has stage postcondition helpers.
2. `SemanticInjectors.Apply` currently treats legacy marker presence as a skip for most single-stage rules. Existing `tunnel-rescue-v3` and `ziggy-last-play-navigation-sentinel-v3` are explicit upgrade exceptions. New stages for already-optimized mods must have independent source-level completion checks and upgrade handling.
3. Existing `RouteExactOnActionObserver` extracts an exactly proven `PlayerPuppet::OnAction` body and subscribes known action names via 0-Engine `SubscribeAction`, with direct Observe fallback. It assumes a single unambiguous observer; assess runtime order/semantics before reuse.
4. Existing rules already cover `autoloot`, `quest-tracking-toggle`, `immersive-head-inertia`, `gigcore-blackboard-once`, and `driveaerialvehicle`. Do not duplicate these blindly.
5. Live ImmersiveFirstPerson already contains a route to the shared OnAction facility when present (with native fallback). Do not add a duplicate registration; focus on measured per-frame work and any proven absent routing/adoption in other mods.
6. `docs/0-engine/0-ENGINE-WORKLOAD-SERVICES.md` confirms shared GCET workload services are **opt-in** and do not globally intercept new mod callbacks. Updating Profiler by itself does not update live mods.
7. `tests/GigCoreSpecterSourceContract.ps1` is a useful regression pattern: run Resolver CLI against fixtures, inspect generated ZIP, confirm originals untouched, apply pass to staged copy, rerun and assert idempotence. Extend to original/live/drift fixtures.
8. Native RTTI hash and bridge population are **not** proof that all corresponding allocations are safely avoidable. Do not patch CET's global wrapper/Override next() semantics or cache native objects without source/lifetime proof. Allocation categories can overlap callback attribution, so do not sum them.

## Next implementation sequence

1. Re-open/re-inspect **actual archive contents** and PASS3 capture in the new session; prior chat had preliminary counts but had not established every source-level candidate and runtime relation.
2. Verify which high-frequency source paths are already optimized, choose a small **unoptimized** observed target with a safe semantic transformation. CallList remains a candidate, not an approved rule; prove the action names, handler bodies, ordering and fallback from both archives.
3. Add one new catalog entry and corresponding injection method (or a distinctly marked new stage to an existing rule), plus original/live/format-variant/idempotence/semantic-drift fixtures. Update `enabledInjectorCount` only if adding an actual new active entry.
4. Run applicable build and semantic contracts; verify generated pass composition. Only then commit. Do not bump product version merely for this work.
5. Expand one source-proven target at a time; keep native method-hash attribution analysis separate from speculative CET-wide marshalling changes.

## Repository status at pause

This task involved **read-only repository inspection** before this checkpoint. No new optimization code or tests were created. Previous native DLL integration and v1.2.7 packaging were completed in earlier commits; they are separate from the pending source consolidation work.
