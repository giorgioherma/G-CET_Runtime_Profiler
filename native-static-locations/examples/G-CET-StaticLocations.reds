// Experimental declarations ONLY. Not player-ready. Main-thread-only calls.
// The Resolver must install precisely one compatible declaration when admitted.
public native func GCETStaticReady() -> Bool;
// 0 not running, 1 position UNKNOWN, 2 valid native position feed.
public native func GCETStaticCoordinatorState() -> Int32;
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
