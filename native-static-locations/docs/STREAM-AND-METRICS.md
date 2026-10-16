# Native shared static locations: subscriber contract + diagnostic counters

Status: **experimental native interface only**. Main-thread-only API. This is NOT yet a production or gameplay-verified wake replacement.

## One service, independent consumers

- Every consumer has a **positive, unique Int32 consumer ID**. Its registration specifies a positive source owner ID or **0** for an all-owner aggregator (for example the CET 0-Engine integration or the REDscript bridge). Each consumer has a separate cursor over a **shared fixed 8192-event ring**; there is no per-consumer copy of every event.
- `Subscribe(consumer,owner)` does **not** implicitly establish a valid state: the new consumer must enumerate current anchors (owner-scoped `OwnerCount/OwnerAnchor`, or global `Count/AnchorAt`), inspect `State(id)` for each, then call `AckSnapshot(consumer)` **on the same game thread with no intervening `Update`**. This is the only valid first-registration handoff. All comparisons to `State==-1` are **UNKNOWN / fail-open**, never FAR.
- `Poll(consumer)` returns anchor ID **>0** for a delivered event; `PolledState(consumer)` is `1` NEAR, `0` FAR, `-1` UNKNOWN. `PolledOwner(consumer)` names its owner. `Poll==0` means no unconsumed events. `Poll==-1` means **RESYNC_REQUIRED**. A reset/session transition or a slow reader that falls >8192 events behind forces resync, never a silent skip.
- The event ring uses fixed storage and position processing emits no per-consumer allocations. Polls never remove another consumer's events. Individual owners and anchors can be tombstoned via `RemoveOwner` / `RemoveAnchor`: affected current states turn UNKNOWN; removal is idempotently irreversible within that loaded manifest. Normal source upgrades/removal are handled by a **new Resolver-generated installed-only payload**.
- `Arm(id,false)` disarms a location and emits UNKNOWN when position is known. On rearm, the next `Update` recomputes position even if stationary; the service may emit a NEAR wake. Unarmed/removed state is never interpreted as reliably FAR.
- One authoritative player coordinator **outside any subscriber to be retired** must feed `UpdatePlayer`. Do not remove an old author wake until source injection proves that the coordinator is live, native stream is consumed and exact author first-interaction/wake semantics are preserved. No self-start on proximity.
- The native plugin does **not** automatically discover or mutate mods, patch files, load YAML, call author Lua callbacks, install REDscript declarations, or remove CET `Observe` hooks. Those require independently source-proven Resolver + runtime adapters; this interface only makes their safe implementation possible.

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

Metrics deliberately **do not** claim or count saved Lua allocations, native-to-Lua marshalling, original author callback invocations or FPS. To prove a Resolver optimization, combine service counters with per-source Profiler evidence: retired author registration/timer, original callback dispatches, per-callback Lua and native allocation/cpu, actual event delivery, wake latency and same-scene CapFrameX. A high counter 5 or invalid-state 6 should veto a claim that a retired author wake was equivalently replaced.

## Remaining limitations

- Large teleport/high-speed traversal can skip an enter-and-leave cycle between samples; the runtime coordinator must provide adequate cadence/segment logic before replacing original early wake.
- Removal and subscribed event handling have **not yet been in-game tested**. The current native plugin exposes functions via RED4ext RTTI, but no signed-off CET/REDscript adapter or source transformer exists.
- Owner IDs in the compiled manifest are restricted to **1..2,147,483,647** for Int32 RTTI compatibility. The Resolver must generate unique IDs with collision detection. Identical coordinates alone are never sufficient for ID identity.
