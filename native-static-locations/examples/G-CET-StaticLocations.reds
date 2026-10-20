// Experimental declarations ONLY. Not player-ready. Main-thread-only calls.
// The Resolver must install precisely one compatible declaration when admitted.
public native func GCETStaticReady() -> Bool;
// 0 not running, 1 position UNKNOWN, 2 valid native position feed.
public native func GCETStaticCoordinatorState() -> Int32;
// -1 not sampled/inactive; 0 VALID; 1 no player; 2 no RTTI class;
// 3 no world-position getter; 4 getter invoke failed; 5 invalid coordinates.
public native func GCETStaticLastPlayerError() -> Int32;

// EXPLICIT developer-only active probe: marks one radius around the LAST valid
// player position. No quest/callback connection. Unique high-range tombstoned ID;
// this does NOT make the cache a gameplay optimization.
public native func GCETStaticProbeHere(radius: Float) -> Int32;
public native func GCETStaticProbeId() -> Int32;
public native func GCETStaticProbeRemove() -> Bool;
public native func GCETStaticCount() -> Int32;
public native func GCETStaticArm(id: Int32, armed: Bool) -> Bool;
// Compatibility signature only: native coordinator owns the position; returns -1.
public native func GCETStaticUpdatePlayer(x: Float, y: Float, z: Float) -> Int32;
public native func GCETStaticState(id: Int32) -> Int32;
public native func GCETStaticResetSession() -> Void;
public native func GCETStaticChangeCount() -> Int32;
public native func GCETStaticChangeId(index: Int32) -> Int32;
public native func GCETStaticChangeNear(index: Int32) -> Bool;

// Independent event streams. consumer is a unique positive ID; owner=0 means
// all owners (for one CET or REDscript aggregator). A consumer must read the
// initial state of every relevant owner anchor and AckSnapshot, on the SAME
// game thread without an interleaved Update, before polling. If Poll returns
// -1, fail open and resynchronize. 0 means no event. Positive means anchor ID.
public native func GCETStaticSubscribe(consumer: Int32, owner: Int32) -> Bool;
public native func GCETStaticUnsubscribe(consumer: Int32) -> Bool;
public native func GCETStaticNeedsSnapshot(consumer: Int32) -> Bool;
public native func GCETStaticAckSnapshot(consumer: Int32) -> Bool;
public native func GCETStaticPoll(consumer: Int32) -> Int32;
public native func GCETStaticPolledState(consumer: Int32) -> Int32;
public native func GCETStaticPolledOwner(consumer: Int32) -> Int32;
// For owner=0 global subscribers, iterate Count / AnchorAt for snapshot.
public native func GCETStaticAnchorAt(index: Int32) -> Int32;
public native func GCETStaticOwnerCount(owner: Int32) -> Int32;
public native func GCETStaticOwnerAnchor(owner: Int32, index: Int32) -> Int32;
// Runtime registration for source-proven (including editor-reloaded) sites.
 // Fresh unique IDs for replaced sites; adding does not arm an anchor.
public native func GCETStaticAddAnchor(id: Int32, x: Float, y: Float, z: Float,
                                       radius: Float, xyOnly: Bool, owner: Int32) -> Bool;
public native func GCETStaticRemoveAnchor(id: Int32) -> Bool;
public native func GCETStaticRemoveOwner(owner: Int32) -> Bool;

// Opt-in diagnostics for Profiler sampling. IDs:
// 0 changed valid positions, 1 stationary skips, 2 distance tests,
// 3 recorded changes, 4 delivered events, 5 stream overruns,
// 6 invalid player samples, 7 last candidates, 8 last distance tests,
// 9 consumer registrations, 10 appended events, 11 running-state ticks,
// 12 unavailable player ticks, 13 valid-to-unknown losses.
// Does not count saved CET C hooks.
public native func GCETStaticMetric(metric: Int32) -> Int64;


// Experimental independent native scalar player snapshot, available even
// without a G-CET.locations manifest. ONE ID per shared host, not per mod.
// Subscribe field bits: 1 position, 2 availability.
// Poll: 1 position, 2 availability, 4 snapshot, 8 session, 16 coalesced.
// 0 unchanged; -1 invalid ID. UNKNOWN is never safe as FAR/dormant.
// Version 1: stable capability handshake for zero-per-mod-configuration
// discovery by an EXISTING shared host. Capabilities bits:
// 1 position, 2 availability, 4 change-poll, 8 session epoch,
// 16 player snapshot independent of the static-location manifest,
// 32 native typed Vector4 player-position getter, 64 on-demand/frame or forced-live API.
public native func GCETPlayerApiVersion() -> Int32;
public native func GCETPlayerCapabilities() -> Int32;
public native func GCETPlayerConsumerCount() -> Int32;
public native func GCETPlayerSubscribe(consumer: Int32, fields: Int32) -> Bool;
public native func GCETPlayerUnsubscribe(consumer: Int32) -> Bool;
public native func GCETPlayerPoll(consumer: Int32) -> Int32;
public native func GCETPlayerStatus() -> Int32;
public native func GCETPlayerEpoch() -> Int32;
public native func GCETPlayerRevision() -> Int32;
public native func GCETPlayerX() -> Float;
public native func GCETPlayerY() -> Float;
public native func GCETPlayerZ() -> Float;

// Typed snapshot, not three independently marshalled scalar reads.
// Call only when GCETPlayerStatus()==2 AND update-phase freshness is proven.
// On UNKNOWN or incompatible callback phase, retain original authored getter.
// No implicit rewrite authorization; optional ABI v1 bit 32.
public native func GCETPlayerVector4() -> Vector4;

// On-demand getter uses ScriptGameInstance.GetFrameNumber when available;
// unknown frame identity forces an actual engine read for every request.
// Forced-live getter always calls the engine. Valid() describes the immediately
// preceding native getter call on the main thread; never use the zero sentinel.
public native func GCETPlayerVector4Live() -> Vector4;
public native func GCETPlayerVector4Valid() -> Bool;
public native func GCETPlayerPositionCacheHits() -> Int64;
public native func GCETPlayerPositionEngineReads() -> Int64;
