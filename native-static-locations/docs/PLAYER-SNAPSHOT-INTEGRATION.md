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


## Auto-discovery contract (native-only CI extension)

The shared 0-Engine host (owned/updated separately) may negotiate automatically
once per game session, using \`GCETPlayerApiVersion()==1\` and the
\`GCETPlayerCapabilities()\` bitmask. Required capabilities for the generic
position bridge are 1 (position), 2 (availability), 4 (change-poll),
8 (session epoch) and 16 (manifest-independent operation). The experimental
native plugin reports bitmask 31. \`GCETPlayerConsumerCount()\` is a
diagnostic query, **not** a reason to create more clients.

The optional \`CETPlayerSnapshotHub.TryAuto(Game, sharedConsumerId, fallback)\`
performs this negotiation and checks the complete native method surface.
It does NOT start a new callback, call Game.GetPlayer, read any coordinate,
or register a native subscriber just because the DLL exists. A shared host
must first bind at least one approved subscriber, then call \`:Step()\` from
its already existing update callback. If no subscriber exists, the native
Running-state callback avoids the player getter entirely (unless an independent
location anchor is already armed).

No mod needs a hand-written \`:Watch()\` call: the existing 0-Engine/Resolver
generated integration is responsible for connecting recognized source methods
to the one aggregate host. This native repository **does not** automatically
patch, intercept, or replace arbitrary mod source. Such end-to-end zero-manual
adoption requires the other agent's auto-binding/Resolver path to supply the
shared consumer and source rewrites. With the plugin absent or incompatible,
the host leaves original getters and callback behavior unchanged.

Automatic hook adoption is not synonymous with native subscription. A direct
\`Game.GetPlayer()\` call in an unmodified mod still crosses Lua/C++;
neither RED4ext nor the existence of this DLL makes that crossing disappear.
Avoid claiming removed decoding cost until a generated pass actually replaces
that call and a runtime measurement verifies fewer boundary crossings.

**Ownership priority:** existing 0-Engine provider remains authoritative unless
its integration explicitly delegates an equivalent field to the native service.
Never create two unconditional producers for the same field. An active native
location anchor can share the player's native position read with snapshot
consumers, but an empty/dormant index incurs no player read solely to advertise
capabilities.
