# Service architecture and runtime API (durable)

## Data flow

`RED4ext Running-state hook → SamplePlayer → PositionCoordinator → StaticIndex (128m XY cell map + optional XYZ test) → TransitionJournal (8192 records) → independent CET / G-RED consumers → author-proven near/far handler`.

Each area has a unique positive Int32 anchor and owner. Index entries are immutable **within a registration epoch**; the owner may register new static coordinates after loading editor JSON, but changing geometry means tombstone the old entry and use a new unique ID. Initially disarmed; stage/lifecycle decides arming. Not for moving entities.

## State and recovery

- Native coordinator `0=not running,1=position invalid/unknown,2=valid player position`; resets on running enter/exit and actual valid-to-unavailable transitions.
- Anchor state `1 NEAR,0 FAR,-1 UNKNOWN`. UNKNOWN never suppresses author wake. Unarmed/removed/missing/invalid position = UNKNOWN.
- Subscribe takes a consumer ID and 0 (all owners) or positive owner filter. Initial snapshot of relevant anchors is read, then `AckSnapshot`, on one game thread. `Poll` gives >0 ID event, 0 empty, -1 resync required; get `PolledOwner/PolledState` for the last event. Slow readers must re-snapshot.
- Native owner deletion emits UNKNOWN and tombstones entries, avoiding invalidation-by-FAR. The 100k capacity includes tombstones; never register per-frame/spawned objects.
- One native position writer. `GCETStaticUpdatePlayer` is a disabled compatibility stub returning -1, not a legitimate data source. Additional per-mod polling would defeat the objective.
- Query cell overlaps all configured wake circles; do not assume XY-only radius when original mod compared XYZ. High-speed transit can still skip an enter-and-leave pair between samples; retained author exact checks and per-source latency tests are necessary.

## Concrete implementation

C++: `native-static-locations/src/Plugin.cpp`, `PositionCoordinator.cpp`, `StaticIndex.cpp`, `TransitionJournal.cpp`; public shapes in `include/`. RTTI declarations: `examples/G-CET-StaticLocations.reds`. Manifest compiler: `tools/compile_locations.py`. Current positional export calls a game RTTI getter via `ScriptGameInstance`; SDK compile **does not validate** actual in-game RTTI name/access or returned handle.

## Native counters

`GCETStaticMetric(0..13)` includes processed positions, stationary skips, distance checks, appended/delivered events, overruns, invalid positions, recent cell candidates/tests, subscriber count, sequence number, coordinator ticks, unavailable ticks and valid-to-unknown transitions. See [native definitive stream/metric details](../../../native-static-locations/docs/STREAM-AND-METRICS.md). None directly measures saved Lua callback dispatch or FPS.

## API compatibility discipline

Compiled GCAS v1 is deterministic, little-endian, versioned, owner-scoped. RTTI methods are native; version mismatch between DLL and declarations/adapters must not silently proceed. Do not infer stable ABI from mock C++ unit tests or native CI compilation. Always use one native DLL; only one compatible script declaration may be installed.

## Fresh-arm UNKNOWN safety and temporary diagnostic service

The native registry now tracks the first valid **evaluation epoch** required by newly armed entries. An anchor armed while the player is already positioned must return `UNKNOWN=-1` until the next native `Update` actually processes that arm change, **even if its initial internal `inside` flag is false**. This prevents false FAR in initial consumer snapshots. The per-entry epoch avoids scanning every anchor to mark pending evaluations.

`PositionCoordinator::CurrentPosition` exposes the last valid position **only while known/running**; after exit, invalid sample or unavailable player it refuses to reuse stale coordinates. `DiagnosticProbe` can register at most one active developer-only high-ID anchor using that trusted position, never an author mod owner, and with no callbacks. Runtime probe operation is optional; a marker file co-located with the native DLL enables a 40 m passive automatic probe for isolated smoke packages. No file marker in Resolver payloads.
