# Native Fixed Cache — shared player snapshot (experimental)

Baseline: isolated G-HoneyTrap/StaticLocations CI61, commit 48244f9.
Branch: dev/native-fixed-cache-player-state. **No Resolver, Profiler or live 0-Engine source is modified.**

## What it owns

One RED4ext Running-state player world-position sample is used by BOTH the
pre-existing fixed-location index and the new opt-in native player snapshot.
When neither location anchors nor player-snapshot consumers are active, the
Running callback makes **zero** player RTTI getter calls.

The player snapshot also works when G-CET.locations is absent: it does not
depend on the compiled static-anchor manifest. It stores only x/y/z floats,
known/unknown, revision, session epoch and a fixed 64-slot consumer table.
It never caches a player/vehicle/blackboard object, RedScript reference,
CET userdata, or pointer across game updates.

The native player getter is still once per **active Running update**, not once
per position change. Native emits events only when a scalar changes.
This can reduce duplicated CET reads only when a source-approved consumer
stops its original getter and uses this value instead.

## REDscript/CET ABI (experimental)

Use ONE logical consumer ID per shared host, not one per mod:

- GCETPlayerSubscribe(consumer:Int32, fields:Int32) -> Bool
- GCETPlayerUnsubscribe(consumer:Int32) -> Bool
- GCETPlayerPoll(consumer:Int32) -> Int32 bit mask
- GCETPlayerStatus() -> Int32: 0 not Running; 1 unknown; 2 valid
- GCETPlayerEpoch() -> Int32
- GCETPlayerRevision() -> Int32
- GCETPlayerX/Y/Z() -> Float; read ONLY when Status()==2

Field subscription mask: 1=position, 2=availability, 3=both.
Poll output: 1=position changed, 2=availability changed, 4=first
snapshot required, 8=session epoch changed, 16=updates coalesced,
0=no change, -1=invalid consumer. Coalesced delivery is the latest
state, NOT a lossless history of every position or availability edge.

Subscribe then Poll, check Status, read fields. A newly subscribed consumer
is not entitled to suspend the author reader until Status==2 and the
source-level semantic and freshness requirements are met. UNKNOWN,
load/teleport invalidation, missing ABI, or callback failure: fail open.

Examples/G-CET-StaticLocations.reds declares the native interface;
adapters/CETPlayerSnapshotHub.lua is an optional one-consumer fan-out.
The adapter requires an existing host's tick: no new CET onUpdate, onDraw,
Observe, Cron job or vehicle hook is installed. Native calls are main-thread
only; no background Lua polling.

## 0-Engine ownership (audited from working October 20 files)

Current 0-Engine init.lua already owns a frame state update and a shared
player position (which Proximity + SpatialHash consume), plus an orientation/yaw
read. Lifecycle holds its own player entity; BlackboardCache2 polls 12
PlayerStateMachine values with a fast unchanged-state early exit and
pre-resolved definitions; DerivedState throttles expensive queries; the
StateSignals wrapper fans out native-backed **existing** event transitions for
vehicle, combat, menu and district; shared game-system providers cache by
player epoch. The frame/action dispatchers consolidate CET callbacks.

**Do not duplicate these in the RED4ext extension.** First attach native
as an opt-in provider behind 0-Engine's existing state API, and use the native
position only when the old GetWorldPosition source read is actually bypassed.
Do NOT add another unconditional player poll to 0-Engine, nor create
new native vehicle/combat/blackboard sampling while the existing BB/event
owner already provides those states. Preserve 0-Engine's GetWorldOrientation
and yaw semantics until explicitly replaced; a raw x/y/z snapshot alone
cannot substitute for a Vector4 userdatum and orientation.

Possible attachment sequence:
1. Source matcher proves callback expects world-space player coordinates
   and tolerates the last native Running-state sample (or retains the old
   immediate getter for timing-critical paths).
2. In the same pass, the source stops its redundant player-position lookup,
   consumes the shared native snapshot, and retains original fallback on
   unknown/stale/unsupported runtime.
3. One 0-Engine aggregate host calls Hub:Step() on its ALREADY EXISTING tick;
   all approved owners Watch() its change-only events.
4. Preserve independent active/remote/gameplay work. Only suspend the
   particular proven redundant player polling body, not arbitrary mod jobs.
5. A/B native RTTI queries, Lua boundary crossings and allocator/GC effects;
   native alone is NOT evidence of saved CPU.

## Tests and boundaries

- Native independent C++ tests: no-demand dormant updates, subscription
  capacity, repeated same-position skips, first valid state, invalidation,
  session epochs, event coalescing, 0,0,0 as a valid position.
- CET adapter simulated Lua tests: single shared subscription, fail-open,
  first snapshot, callback delivery, unsubscribe and no idle polling.
- Existing CI61 location-anchor/transition tests are preserved.
- Game ABI, native-Lua visibility, load/teleport, frame-relative freshness and
  net optimization require an IN-GAME pass before production admission.
- This branch does not ship a new Resolver pass or modify any installed mod.
