# Shared native static-location dormancy — combined-pass extension (2026-10-20)

**Authority:** user-supplied `ALL passes combined-final state-base for locational dormancy.zip`, containing the already-researched, combined optimized Lua sources. This extension copies the existing source-owned dormancy boundaries; it does not substitute new generic quest or NPC policy.

## Native-backed coarse owners (additional)

| Owner | Native id | Source-owned location and existing dormant rule | Native-owned work | Exact author work retained |
| --- | ---: | --- | --- | --- |
| Card Collection | 10035 | `Card Collection/init.lua` static `buildSpatialEntries()` over **100 cards minus those already picked**; existing 0-Engine `RegisterSpatialSet` uses 2 m radius, 1 s collection poll | Native aggregated 20 m NEAR/FAR parks the original spatial set while every uncollected site is distant; recreates it on NEAR | Original card collection, fact writes, rewards, `RemoveSpatialEntry`, PlayerReady/Invalidated lifecycle, and polling callback |
| MarmurBank | 10036 | `marmurbank/module/Bank.lua` uses `PosData.All` (three static banker positions), `RegisterSpatialSet` and `QueryNearest` against original exact 2 m banker radius | Native aggregate 10 m FAR retires spatial index and bypasses dormant nearest query | Existing timer/UI, 2 m exact nearest banker selection, menus, balance/interest, and source-known recovery path |
| Yaiba Penthouse bathtub | 10037 | `yaibaPenthouse/bathtub.lua` already has `G-CET semantic:yaiba-penthouse-distant-bathtub`, early return only with no hub, pin or pending water state | Native FAR replaces the old square-distance calculation while that identical inactive predicate holds | Original state predicate, 6 m icon and 1.8 m interaction ranges, water/mappin cleanup, forward-facing angle and original source calculation during native UNKNOWN |

For the bathtub the source distance uses the player's eye position `pPos.z + 1`; the native site is consequently anchored at `CONFIG.position.z - 1` to preserve this coordinate frame. The coarse 20 m card and 10 m banker native wake radii are **conservative supersets** of the exact original 2 m interaction radii. Card Collection's existing 1 s poll needs an advance wake before a player runs through a collectible; the original exact collection boundary remains unchanged.

All three call the existing shared `0-Engine.GCET.NativeLocationHost:attach`. No additional 10 Hz timer, native DLL, generated static manifest, or per-mod native daemon is introduced. The existing `GCETStaticLocations.dll` already allows runtime source-owned anchors and accommodates 100 entries. A missing/UNKNOWN/overrun native state keeps or restores the original Lua behavior; no source SHA lock is added.

## Eighth owner: Ziggy — existing offered-state 130 m gate

`ziggy_last_play/init.lua` already implements a source-proven
`__gcetZiggyOfferedFar()` early probe: only an unaccepted/offered,
correctly attached player with a valid session, matching identity/generation,
and no ongoing dialogue can be considered FAR when squared distance to the
fixed `C.giver.position` exceeds `130*130`. In that case the existing
controller is advanced **once per second** using accumulated `dt`. All
nearby and active quest phases remain on the original frame cadence.

Native owner **10038** registers precisely `C.giver.position` at **130 m**
through the existing shared host. The old game's position read and
squared-distance calculation are replaced with native NEAR/FAR **after**
the original phase/session/player/dialogue guards; native UNKNOWN/failure
falls through to the old exact source calculation. Journal, fighter
spawns, pickup, listening, recoveries, input and UI are unchanged.
No broad quest-phase assumptions or generated coordinates are introduced.
The source-only Resolver rule `native-location-ziggy-offered` upgrades
the already-optimized combined Lua; its native subscription shares the
existing single 10 Hz host with the other seven owners.

### Quest Runner remains on its existing rule

`QuestRunner/init.lua` already applies the reviewed PASS5 rule:
`Manager:update` runs at 5 Hz while no quest is active and immediately
returns to frame updates for an active quest; `Spawner.Update` receives
time-accumulated updates at 4 Hz while maintaining its authored 1 s
internal scan. `module/Manager.lua` determines available quest triggers
via the dynamic `quest:isDoable()` callbacks, **not fixed map coordinates**.
`module/Spawner.lua` handles quest-owned, mutable `wnpc.pos` plus
`spawnPos`, NPC lifetime, player view angle and spawn-distance thresholds.
There is no single source-proven static-location FAR gate to copy onto
the whole framework. A native FAR assumption here could miss triggers or
delete/spawn actors incorrectly. The existing PASS5 scheduling and
`Spawner` semantics are unchanged.

## Scope not translated to static anchors

Dynamic `CourierJobs` pickup/delivery destinations, `Hidden Packages` swappable map/collection contents, active `ziggy_last_play` fight/listening quest positions, and live actor/object proximity used for UI or spawning are **not copied** into immutable native anchors. Their current optimized rules continue unchanged. The same holds for generic 0-Engine `IsNear` consumers without an existing source-proven coarse idle condition; they must not be falsely made permanently FAR.

## Contracts and validation

- Resolver source injectors: `NativeLocationAdditionalOwnersSource.cs`.
- Catalog: `native-location-card-collection`, `native-location-marmurbank`, `native-location-yaiba-bathtub`.
- Source-level regression: `NativeLocationAdditionalOwnerContract.ps1` (author source shape, LF/CRLF, original callback retention, idempotence).
- Shared Lua host regression now includes **all eight owners**, including the complete 100-card native anchor snapshot with **one** consumer.
- Native branch binary remains unchanged, and combined runtime gameplay testing is still required for save/reload, fast travel, source fallback, and actively collected/visible states. CI validates code and generated behavior contracts, not an in-game playthrough.

## Final combined-pass exclusion: GigCore and SPECTER

Reviewed against the user-supplied combined installed Lua sources on
2026-10-20, using the **existing source-owned location-dormancy rule**
without inventing a new native-location framework.

- **GigCore (`GigCore/init.lua`): NOT A STATIC-LOCATION DORMANCY OWNER.**
  Its `onUpdate` coordinates session-restoration safety, one-per-frame
  UI/PhotoMode/Phone pause state, `Marker.tickAll`,
  `VehicleDamage.tick`, `NPC.tick`, redscript bus commands (0.15 s
  reconciliation), live gig context timers and periodic reassertion.
  It does not have an authored dormant FAR/NEAR static site predicate to
  substitute. Its existing blackboard and callback reuse passes remain.

- **SPECTER (InfiniteGigs + Specter* CET modules): NOT AN ADDITIONAL
  STATIC-LOCATION DORMANCY OWNER.** Static mission checkpoint coordinates
  exist, but `InfiniteGigs/init.lua` accesses the selected
  `Slice.point`/`Slice.stageIndex` and live encounter positions while
  the GigCore context is active; it must also process active mission
  milestones, save restoration and transient actor rebuilds. The
  `SpecterM11RecoveryDev` route is a fixed authored set but is
  conditionally selected by live `stage`, `exitArmed`, dialogue/HUD,
  music and restore state. `SpecterInterceptorClean` selects a site on
  request and phases through 300 m spawn, combat and 55 m payout/exit,
  with a pre-existing 0.5 s processing lane. Its chosen site and phase
  vary at runtime. `SpecterCampaignProgression` and
  `SpecterNessaIntro` already use 0.5 s nonspatial
  metric/offer polling. The existing SPECTER M11 metric, immutable music
  contract and inactive HUD pass remain intact.

**Decision:** No native anchor, new subscriber, source injector, additional
10 Hz work, or changed game logic. Eight existing native owners remain
the complete conservative, source-proven static-location set.

## Compiled GCAS implementation (post-CI1529)

Resolver's game-root ZIP now includes **both**
`red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll` and
`red4ext/plugins/G-CET-StaticLocations/G-CET.locations`. The latter is a
little-endian GCAS v1 binary generated from the currently installed author
sources. Fixed owners use `NativeLocationHost:attachCompiled` and no longer
register Lua coordinate arrays. The DLL's existing GCAS reader loads the
file on plugin initialization. Missing/unsupported source coordinates are
never invented, and the Lua author fallback remains available.

The eight semantic mod families remain Poker, Tunnel Rescue, Illegal
Mechanic, Dedka, Card Collection, MarmurBank, Yaiba bathtub and Ziggy.
Illegal Mechanic no longer uses **one aggregate** native owner: Resolver
reads its installed `zzz_IllegalMechanicTypes.reds` registry and assigns
individual owner IDs `1003301 + registry index` for each fixed NPC
location. The supplied October 20 registry has **18 mechanics**; all
18 original discovery shapes plus the 75 m spawn / 95 m despawn
thresholds fit inside a **110 m** native wake sphere. Lua FAR can park
only that site's unspawned/unpending NPC supervisor; live/pending actors
retain the author's position and cleanup behavior. The original
REDscript discovery logic remains authoritative.

The compiled host marks manifest anchors as immutable across CET reload:
shutdown **disarms** compiled anchors, never removes them. Any dynamic
legacy anchors are still removed. This is an in-place source-composition
change, not a new binary API; the pinned native DLL is unchanged and
the Resolver product version stays `1.2.11`.

**CI1527 failure:** test constructed a sample REDscript registry but
did not stage the associated semantic owner, so the compiler correctly
emitted zero records. CI1528 and CI1529 succeeded after the fixture was
corrected; CI1529 also verifies compiled-anchor reload lifecycle.
Contract verification does **not** replace runtime gameplay testing or
guarantee that every pristine input mod receives all chained semantic
stages in a single pass. Inspect the generated JSON for each
`native-location-*` rule and the `compiledOwnerCounts` table before
installing. A missing owner must be investigated rather than described
as generated.

**Migration:** users may restore pristine author source files and rerun
Resolver's existing semantic generation. Old aggregate native v1/v2
Illegal Mechanic source is explicitly rejected by its v3 injector
rather than silently rewritten. The complete original mod must be
installed (including `r6/scripts` registry and JSON configuration)
while Resolver generates the native binary, since an overlay-only ZIP
does not carry those original input files.


## Source-structural capability and independent-stage correction (CI1550)

The October 20 generated pass demonstrated **five** native-location source
hooks, with Poker, Dedka, and MarmurBank skipped because previously
optimized source changed *shape*. The pass also included a **legacy
GCETNativeLocationHost.lua without attachCompiled** (same source and
generated SHA in its JSON manifest), so manifest placement alone did not
establish a working native owner.

Fixed by the following independent, source-proven capabilities:

1. **Location geometry** is compiled separately from the callback hook.
   Source-constant author coordinates still qualify if the mod's staged
   callback rewrite fails, while unsupported/unproven geometry is refused.
   Poker table locations, Dedka's entrance positions, and banker PosData
   use current installed source, not a reference file, mod version or
   source SHA. Wake radii are read from the author's current discovery
   behavior and conservatively expanded: Poker +5m, Dedka +10m,
   MarmurBank source nearest-query radius +5m.
2. **Source-hook registration** is verified independently. Poker proves
   far/near/seat scheduler IDs, pause/resume transitions and realtime
   activity. Dedka proves its two keyed author zones, helper
   RegisterZone and enter/exit lifecycle, retaining restorable handles.
   MarmurBank proves spatial register/unregister and nearest-query
   lifecycles without relying on log messages. Unchanged callbacks and
   active gameplay are untouched. Ambiguous changes reject only that
   native-location semantic rule.
3. **Idempotence** is based on the behavioral postcondition for each
   structural stage. A complete stage skips; a partially present stage
   fails closed; absence of one native implementation does not invalidate
   other semantic transformations in the same mod.
4. **0-Engine host capability** is separate from the 0-Engine source
   version. Resolver retains the installed module and uses source proof
   to add the missing attachCompiled API, legacy attach-to-precompiled
   dispatch, and safe immutable-anchor shutdown handling. This is a
   surgical additive adaptation; existing Scheduler, Health, ActionRouter
   and customized module code remain authoritative. Unknown host layouts
   fail closed rather than receiving a guessed overwrite. Reapplying the
   bridge is byte-for-byte idempotent.

No mod-version or source-SHA admission gate was added to these stages.
SHA values in manifests remain provenance, not native-location
compatibility decisions. This does not remove integrity protections
for profiler original-state restoration or unrelated generic capture
consistency.

CI1544/1545 validated structurally changed Poker/Dedka/MarmurBank hook
source and independently compiled pre-native locations. **CI1550
completed all five jobs successfully**, including legacy customized
0-Engine native-host migration and its idempotence check:
https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39452851455

**Runtime qualification:** CI does not constitute successful gameplay
validation of all eight owners in the user's precise combined stack.
Generate a new Resolver pass and inspect both native hook transform rows
and compiled owner counts; never mistake the 20261020-151906 legacy
pass for a valid new-native installation.

## Resolver stage-graph acceptance contract (PASS0–PASS11, October 20)

The user's PASS0–PASS11 ZIPs are **incremental, overlapping overlays**, not
independent complete mod releases. Preserve the tested installed optimized
baseline. Every source transformation must be admitted **per behavior stage**;
source hashes and author release labels are provenance, never mod-version
admission gates. A marker alone is not proof of stage completion.

Order and invariants:

1. **Current-source inventory and proof** — compare live source with each
   stage's behavioral postconditions; classify independently as SATISFIED,
   APPLICABLE, or UNSUPPORTED. Never infer from a whole-mod marker.
2. **Lua and 0-Engine source composition** — produce missing PASS0–PASS10
   optimizations in the staged source graph, preserving prior edits. Do not
   refuse independent work merely because another stage already touched the
   file. PSM immutable-key cache, SpatialHash poll radius, SpatialHash
   grid-row reuse and Lifecycle same-update entity ID are separate stages.
3. **Player-state/source ownership** — distinguish 0-Engine Blackboard PSM
   definitions from native player position, typed Vector4 and live entity
   receiver identity. Require valid epoch, freshness/phase, typed return and
   original getter fallback before substituting a live player read.
4. **Native capability resolution** — evaluate existing 0-Engine source and
   native ABI by capability, not release or SHA. Preserve customized modules;
   add only independently proven missing APIs. Native player getter/position
   validity is separate from static owner-index availability.
5. **Compiled static locations** — extract author geometry independently of
   wake callback rewrites, require source-proven radius beyond the largest
   discovery/streaming boundary, and map each mechanic to its own owner.
6. **Native subscription and fallback** — subscribe only when both compiled
   geometry and compatible runtime/owner callbacks are proven. Otherwise
   retain the original discovery, listeners and lifecycle; do not pretend
   the presence of a DLL alone proves an active optimization.
7. **Final pass validation** — verify each retained or newly applied stage,
   per-stage unresolved reasons, 0-Engine baseline preservation, binary
   manifest/host capability consistency, and no duplicate anchors on reload.

Mandatory matrix for each stage: pristine original, radius-only / row-only /
other partially optimized variant, mixed variants across multiple files,
already-satisfied source, author-update formatting / nonsemantic changes,
changed gameplay semantics (refuse only that stage), and second-pass
idempotence. Source-only contracts verify the known variants; real game
behavior remains separately pending.

2026-10-20 implementation progress: The SpatialHash generator no longer
classifies a row-only stage as completion of the poll-radius snapshot.
It verifies all three existing row calculations, adds a missing radius
snapshot independently and preserves row work without duplication. The
PSM cache also validates all twelve keyed reads and lifecycle entrypoints
before calling its existing marker complete. CI regression for mixed
SpatialHash states was added. **This is partial progress, not a claim that
all PASS0–PASS11 stages are already fully version-fluid.**
