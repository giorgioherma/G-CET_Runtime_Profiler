# G-LookupCache — native subscription and diagnostics

Status: **experimental native interface + experimental single game-state coordinator**. Main-thread-only API. This is NOT yet a production or gameplay-verified wake replacement.

## One service, independent consumers

- Every consumer has a **positive, unique Int32 consumer ID**. Its registration specifies a positive source owner ID or **0** for an all-owner aggregator (for example the CET 0-Engine integration or the REDscript bridge). Each consumer has a separate cursor over a **shared fixed 8192-event ring**; there is no per-consumer copy of every event.
- `Subscribe(consumer,owner)` does **not** implicitly establish a valid state: the new consumer must enumerate current anchors (owner-scoped `OwnerCount/OwnerAnchor`, or global `Count/AnchorAt`), inspect `State(id)` for each, then call `AckSnapshot(consumer)` **on the same game thread with no intervening `Update`**. This is the only valid first-registration handoff. All comparisons to `State==-1` are **UNKNOWN / fail-open**, never FAR.
- `Poll(consumer)` returns anchor ID **>0** for a delivered event; `PolledState(consumer)` is `1` NEAR, `0` FAR, `-1` UNKNOWN. `PolledOwner(consumer)` names its owner. `Poll==0` means no unconsumed events. `Poll==-1` means **RESYNC_REQUIRED**. A reset/session transition or a slow reader that falls >8192 events behind forces resync, never a silent skip.
- The event ring uses fixed storage and position processing emits no per-consumer allocations. Polls never remove another consumer's events. Individual owners and anchors can be tombstoned via `RemoveOwner` / `RemoveAnchor`: affected current states turn UNKNOWN; removal is idempotently irreversible within that loaded manifest. Normal source upgrades/removal are handled by a **new Resolver-generated installed-only payload**.
- `Arm(id,false)` disarms a location and emits UNKNOWN when position is known. On rearm, the next `Update` recomputes position even if stationary; the service may emit a NEAR wake. Unarmed/removed state is never interpreted as reliably FAR.
- One authoritative **RED4ext native Running-state position coordinator** now feeds the index without relying on scripts from any owner to be retired. `GCETStaticCoordinatorState()`: 0=not running, 1=running but player location unavailable (UNKNOWN), 2=valid position sample. The previous `UpdatePlayer` RTTI function is disabled (returns -1) to prevent two competing writers; never call it from CET or REDscript. Consumers must continue source-original handling when coordinator state is not 2. Do not remove an old author wake until source injection proves that the coordinator is live, native stream is consumed and exact author first-interaction/wake semantics are preserved. No self-start on proximity.
- The native plugin does **not** automatically discover or mutate mods, patch files, load YAML, call author Lua callbacks, install REDscript declarations, or remove CET `Observe` hooks. Those require independently source-proven Resolver + runtime adapters; this interface only makes their safe implementation possible.

## Native runtime owner registration

`GCETStaticAddAnchor(id,x,y,z,radius,xyOnly,owner)` registers a single source-proven static site at author initialization or editor/source epoch reload. The ID and owner must be positive Int32; the radius and coordinates obey the same manifest validation. New anchors are **unarmed**; stage-aware source code calls `Arm(id,true)` when needed. Use RemoveAnchor/RemoveOwner to invalidate obsolete entries. **Never reuse a tombstoned ID** in the loaded registry: assign a new unique ID for changed geometry, then acquire a fresh native snapshot. Source proof is required; runtime registration is not a general-purpose dynamic entity tracker. All insertion/removal work happens on the game thread and may allocate; the position evaluation and stream remain allocation-bounded.

Current opt-in integration layers are source-side bridges only: one CET/0-Engine consumer, one optional G-RED scheduler consumer and an R4 compatibility wrapper. Neither source-adapter file is included in the native CI DLL's game-install ZIP; they require tested Resolver source matching and actual runtime validation before they can retire an old author wake.

## Profiler sampling counters

`GCETStaticMetric(metric: Int32) -> Int64` returns cumulative counters since native manifest load, except metric 7/8 which are the *last valid changed-position update* snapshot. `-1` means missing/unrecognized metric or unavailable native data. Poll these outside frame-critical paths, using deltas over a time window. Counter retrieval itself is RTTI traffic, so bounded/capture-gated sampling only.

| ID | Value | What it tells the profiler |
|--:|---|---|
| 0 | Changed valid player-position samples | Spatial updates actually processed (not native position provider calls) |
| 1 | Identical-position skips | Repeated calls avoided full spatial evaluation |
| 2 | Squared-distance tests | Actual spatial candidate workload, not all registered anchors |
| 3 | Native events appended | Near/far/unknown wake event volume |
| 4 | Consumer events delivered | Actual native-to-bridge delivery operations, including separate subscribers |
| 5 | Consumer history overruns | Required snapshot resets; **a nonzero value invalidates wake-equivalence until handled** |
| 6 | Invalid player-position samples | Session/position availability failures; callers must fail open |
| 7 | Candidate count, last processed update | Immediate candidate size |
| 8 | Squared-distance tests, last processed update | Immediate per-update work |
| 9 | Number of registered consumers | Duplicate subscribers / current fan-out |
| 10 | Total appended event sequence | Debug event-generation continuity |
| 11 | Native running-state tick count | Actual position-coordinator frame samples attempted |
| 12 | Coordinator ticks with no valid world position | Game player unavailable/getter failed/invalid coordinates; must fail open |
| 13 | Valid-to-unknown position losses | Number of snapshot-forcing availability transitions |

Metrics deliberately **do not** claim or count saved Lua allocations, native-to-Lua marshalling, original author callback invocations or FPS. To prove a Resolver optimization, combine service counters with per-source Profiler evidence: retired author registration/timer, original callback dispatches, per-callback Lua and native allocation/cpu, actual event delivery, wake latency and same-scene CapFrameX. A high counter 5 or invalid-state 6 should veto a claim that a retired author wake was equivalently replaced.

## Remaining limitations

- Large teleport/high-speed traversal can skip an enter-and-leave cycle between frames. Running-state callback timing, actual Game.GetPlayer/GetWorldPosition ABI, game transitions and RED4ext integration still require Windows CI plus **actual in-game testing** before retiring a source wake.
- Removal and subscribed event handling have **not yet been in-game tested**. The current native plugin exposes functions via RED4ext RTTI, but no signed-off CET/REDscript adapter or source transformer exists.
- Owner IDs in the compiled manifest are restricted to **1..2,147,483,647** for Int32 RTTI compatibility. The Resolver must generate unique IDs with collision detection. Identical coordinates alone are never sufficient for ID identity.

## Diagnostic player lookup and opt-in probe (experimental)

- `GCETStaticLastPlayerError() -> Int32`: -1 not sampled/inactive, 0 valid, 1 missing/GetPlayer failed, 2 missing RTTI class, 3 missing GetWorldPosition method, 4 getter invocation failed, 5 invalid coordinates. One RED4ext log message per failure reason per running session, with total process-wide budget of 32 diagnostic messages. The first valid position is explicitly logged.
- Explicit diagnostic only: `GCETStaticProbeHere(radius)`, `GCETStaticProbeId()`, `GCETStaticProbeRemove()`. ID/owner range is reserved high Int32 (starting at 2100000000); never generate normal source owner IDs in this range. Probe has no author callbacks. After the next native evaluation a valid active probe reports NEAR/FAR normally.
- Opt-in filename `G-CET.smoke-probe` beside `GCETStaticLocations.dll` causes **one automatically armed passive 40m anchor** at the first reliable position, logging NEAR/FAR transitions when the player moves. Intended for **smoke test overlays only**; no automatic probes in normal native or Resolver payloads.
- Newly armed anchors are **UNKNOWN until evaluated**; subscription snapshots must never interpret that pending state as FAR. This is enforced by source-agnostic native evaluation epochs and a regression test.

## Stationary player updates and one-shot edge semantics (2026-10-17)

The player's 2026-10-16 H10 native probe log validated `GetPlayer/GetWorldPosition` in the actual game and a first diagnostic `NEAR`. It also revealed that stationary fast-path `StaticIndex::Update` left the previous frame's `Changes()` list intact, allowing the diagnostic logger to repeat `NEAR` every frame and exhaust its 32-message lifetime budget. This **does not establish that subscriber journal events were duplicated**: the journal owns distinct per-consumer cursors and appends only on actual transitions.

The native code now **clears transient `Changes()` even on a stationary skip**; `Generation()`, position, known anchor state and independent journal cursors are unchanged by the stationary sample. C++ regression tests assert both preservation of `State()==NEAR` and an empty `Changes()` after the second unchanged-position tick. Native CI #29 tests this behavior. Neither the diagnostic log nor the empty-manifest probe is evidence of old author callback retirement. A separate live **FAR / re-enter NEAR** run is still required before declaring the event stream game-verified.
