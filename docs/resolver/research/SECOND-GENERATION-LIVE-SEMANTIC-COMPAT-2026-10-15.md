# Resolver second-generation source-stage compatibility — 2026-10-15

## Reproduced problem

User's real `G-CET_PASS_20261015-052626.json` was generated from `CET-20261015-000909_ONACTION_DISCOVERY_IMMERSIVE_AND2` using an already optimized CET stack. It emitted **two** successful semantic source changes (`BetterLootMarkers/init.lua` and `CallList-VehicleDetailsOverlay-CET/init.lua`) and wrongly skipped **two** valid mod owners:

- **DriveBus** (`0.616396 Lua exclusive ms/s`, `54.74 onUpdate calls/s`): `Current source no longer proves semantic OnAction structure`. But the existing `DriveBus/Modules/core.lua` already contains an exact six-action `zeroEngineOnAction` 0-Engine subscriber, plus its original `Observe(PlayerPuppet, OnAction)` fallback. Existing local input action and consumer semantics must not be overwritten. `External/Cron.lua` and the residual `onUpdate` in `init.lua` are still missing the scheduled timer presence/idle guard.
- **StreetGamesPoker** (`0.04372 Lua exclusive ms/s`, `54.74 onUpdate calls/s`): `Poker world update signature changed or is ambiguous`. The live `init.lua` already implements `TryAttachPokerPass4`, `pass4Attached`, four Scheduler lanes (realtime, 0.1 s seat, 0.20 s near world, 1.0 s far world), current-original fallback and original map-pin lifecycle. Its `StreetTables.lua` already has `StreetTables.update(dt,wakeRadius)`, `wakeSq`, `dSq`, `nearAny`, all three squared-distance branches and return, without `Vector4.Distance`. This is an **already-complete** historical PASS4/PASS4C version, not a pristine mod requiring rewrite.

The prior broad catalog identity/proof correctly selected these owners but each injector expected its original, pre-optimization shape. The fix belongs in the Resolver, never in the live CET mods or in a separate restorative patch.

## Implemented central stage accounting

- `SemanticLibraryService.SourceProof` accepts a narrowly enumerated `completionVerifier` and `composablePartialStages`, used for **only** these two mod-owned source proofs. A version marker or a few matching owner-level strings do not mark the stages complete. On each ANALYZE the rules check current original/optimized live file contents; completion is authoritative, and partial upgrades can emit only still-missing files.
- `ModPasses/DriveBus.cs`: verify the single known six-action 0-Engine route and fallback before reuse; never duplicate or replace it. Verify current `Cron.HasActiveTimers` implementation separately from the guarded onUpdate; compose either/both missing files. Refuse any foreign/unrecognized `SubscribeAction` route rather than silently patching over it. Fresh original source still gets all three files in one pass.
- `PokerPass4Source.cs`: independently verify a completed controller and a completed world-sweep using their actual local structures. Accept both original generated `__gcetPoker*` and real live historical `TryAttachPokerPass4`/`pass4Attached` with `wakeSq`/`nearAny`. A source matching only one complete stage gets **one missing file**; a partially drifted stage fails closed. Also support pristine author `Vector4.Distance(pos, Vector4.new(loc.center.x, loc.center.y, loc.floorZ, 1))` as well as the previously tested scalar `math.sqrt` source in one complete pass, after confirming exactly the three distance consumers.
- `PassGeneratorService` generated manifest now labels `3 ms/s` a **soft semantic CPU priority**, consistent with the actual previously changed decision code.

## Expected next use against user's live CET

After the user installs the earlier successful BetterLootMarkers/CallList files (if desired) and reruns ANALYZE with the updated Resolver:

- DriveBus: its existing `Modules/core.lua` stays unchanged; the authoritative semantic should generate only `DriveBus/External/Cron.lua` and `DriveBus/init.lua` to gate Cron.Update when no active timer exists.
- StreetGamesPoker: historical four-lane controller and world-sweep should be reported `semantic.AlreadySatisfied=true`, without reissuing those source files.
- Already complete/installed BLM and CallList should be classified according to their independent postconditions.
- A repeated run after DriveBus's optional timer patch should recognize the rule complete and not issue a duplicate corrective pass.

**Qualification:** the new Windows binary was verified by automated original/partial/legacy-source fixture tests. There has not yet been a regenerated full pass on the user's personally installed live CET directory or an in-game A/B comparison.

## Regression and constraints

- New `tests/DriveBusPartialSemanticContract.ps1` covers pristine source → three files, existing route → **exactly two** Cron/init files, completed source, alternative historical routed callback and modified-fallback rejection.
- Expanded `tests/PokerPass4SourceGenerationContract.ps1` covers pristine → two files, controller-only and sweep-only partial upgrades, historical unmarked complete source, wake-return drift rejection, benign source variation and pristine Vector4 form.
- Existing `ResolverAccuracySemanticCompositionContract`, all other semantic compatibility suites and 0-Engine/native contracts remain in CI.
- No files in `0-Engine`, native Profiler, other mod-owned semantic sources or user-installed CET mods were modified; no version bump. A pending or verified CI link belongs in `docs/history/ACTIVE-STATE.md`, not HANDOFF CORE.
