# Extended dormancy: live-source reconciliation and additive cache stages (2026-10-15)

## Authority and scope

The user clarified that **current implemented mod behavior**, not the 62 proposed dormancy wake descriptions, is authoritative. Keep existing 0-Engine / Resolver scheduling, listeners, callback registration, mod-owned keybinds, observer/event ordering, pending jobs, per-quest discovery and save/session recovery. Adopt suggestions only when current source proves a better safe gate. Never manufacture a key (including V) or infer mission activation from the existence of an installed journal entry.

Inputs supplied in this work session:
- `EXTENDED DORMANCY.zip`: `modsLIVEnow/` (live mod tree), `G-CET_SEMANTIC_ORIGINALS_62/` (coherent original Lua trees), manifests and source report.
- `modsLIVEnowONLYourDIFF.zip`: changed-file overlay of previous optimization work; it does **not** replace the complete mod folders.

For the requested 62 mod identities: **27 have corresponding folders in the live tree**, **35 have coherent originals but no supplied live folder**, and **8 are explicitly excluded** from further dormancy edits. All 27 present live identities also have entries in the diff. The original-only group must not be assumed installed or equivalent to a partially optimized live version. Preserve prior work on all mod folders, including those outside the 62 list.

## Source-backed changes committed to the existing Resolver

### Illegal Mechanic (source rule `illegal-mechanic`)

Previously optimized HACK pulse scheduling already defers ScriptableSystem work between the author's interaction deadlines while preserving pulse visuals and interaction UI. Preserve this existing gate verbatim.

The physical NPC module loads coordinates from its mechanic matrix into fresh Lua entries; `anchorPosition(entry)` nevertheless called `Vector4.new(entry.x, entry.y, entry.z, 1.0)` every invocation. Cache that value lazily as `entry.__gcetAnchorPosition`. A matrix reload constructs fresh entries, naturally invalidating the cache. Retain the original `loadMatrix` retry, distance checks, NPC spawning, drift verification, resets, and all callbacks. **Do not cache spawned entity handles**.

The injector handles the HACK gate and position cache as independent source stages. The semantic catalog has two scoped completion proofs and allows a one- or two-file incremental pass, rather than assuming that a prior marker proves the new stage. No hardcoded input or new wake source.

Relevant paths:
- `src/G.CETProfiler.Core/Services/ModPasses/IllegalMechanic.cs`
- `resolver-knowledge/semantic-library.json`

### CyberTrials (source rule `cybertrials`)

Current live `init.lua` already owns multi-rate scheduling: 1.0 s far world discovery, 0.10 s near hub checks, 0.25 s system state, 0.10 s race state and a frame lane for active editor/race responsibilities. Existing `world.addInteraction` retains each hub's converted position in `world.interactions`. **Do not add a duplicate hub-coordinate cache**, and do not slow editor or race controls. The prefilter already returns before expensive work for mappins not owned by CyberTrials.

The remaining proven repetition is the static race icon atlas metadata: the owned `BaseMappinBaseController::UpdateRootState` callback repeatedly called `TweakDBInterface.GetUIIconRecord("ChoiceIcon.VehicleIcon")` and read its atlas path/part. Cache these scalar metadata values inside `world.init` and reuse them for owned race mappins. Do not cache mappin handles, runtime interaction registries or vehicle/NPC state. Keep the existing variant prefilter before the cache reads.

The injector is now incremental across pristine source, legacy generated source, and live PASS4 source. It preserves existing init/hubs/world stages when proven and emits only missing source changes. Resolver completion requires the owned-mappin prefilter, hub idempotence, atlas-cache postcondition, and **either** legacy world cadence or PASS4 scheduler; old markers alone no longer establish completion. The per-rule 1–3 changed-file count allowance is **only** for this source-proven CyberTrials upgrade.

Relevant paths:
- `src/G.CETProfiler.Core/Services/ModPasses/CyberTrials.cs`
- `src/G.CETProfiler.Core/Services/SemanticInjectors.cs`
- `src/G.CETProfiler.Core/Services/SemanticPassGeneratorService.cs`
- `resolver-knowledge/semantic-library.json`
- `tests/CyberTrialsSemanticCompositionContract.ps1`

## Quest state, caching and activation gates

The modded journal's installation-time existence does **not** mean a quest was accepted/started. The current Tunnel and Ziggy sources already use targeted diagnostic snapshots and cached mappin rows with state signatures and bounded retries; Tunnel's passive status cadence is already versioned beyond the earlier v4 proposal. **Do not overwrite this with the older 62-item descriptions**.

QuestRunner currently refreshes eligible triggers while idle, retains active quest per-frame updates, and separately feeds Spawner; removing idle trigger checks without a proven wake would break initiation. GigCore and Specter require coupled prerequisite/mission state handling; journal discovery must remain active where needed. No new global journal-object cache is admitted at this stage because save/session transitions, dynamic entries and progress facts require individual invalidation proof.

## Listener / trigger audit boundary

Check each proposed optimization at four levels:
1. native observer registration/call count;
2. Lua dispatch / per-frame inexpensive gate;
3. expensive downstream discovery, player/area polling and allocations;
4. exact wake transition, pending jobs, cleanup and recovery.

An early Lua return does not remove native observer dispatch. Retain all author `registerInput`, `registerHotkey`, `Observe`/`ObserveAfter`, dynamic action subscriptions, fallback and action consumption. A source-only token count is not proof of runtime work. Preserve existing explicit SKIPs: NPCD_Hotline, Shift, OverclockedJenkinsTendons, nativeInteractions, Drone Companions (Revamp), GhostVoidSystem, ImmersiveHeadInertia and AppearanceMenuMod. Minimap broad dormancy remains deferred; narrow widget reuse is independent.

## Validation and release state

- The two additive Lua source-file previews from the current live archive passed **Lua 5.4 parse-only syntax checks**. They were not executed in Cyberpunk or CET LuaJIT.
- CyberTrials' pristine-source composition contract now asserts the new icon metadata cache.
- All .NET builds, Resolver-generated ZIP proof against *actual pristine, partially optimized and already completed source*, existing CI contracts and in-game A/B captures still require confirmation before claiming release-ready.
- The 62-mod inventory remains an audit backlog; these commits implement **two narrow optimizations**, not 62 new dormancy gates. Current implementation and previous-pass working gates continue to take precedence.
