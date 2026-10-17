# Native static-location wake replacement contract (experimental)

**Date:** 2026-10-16. **Status:** design and source-admission contract; not an implemented generic Resolver pass.

## Authoritative selection scope

- The future integrated Resolver must evaluate **only mod directories in the user's selected game installation**, derived from `<gameRoot>/bin/x64/plugins/cyber_engine_tweaks/mods`, or an explicitly verified supported mod-manager effective source tree. The supplied `modsLIVEnow` and `EXTENDED DORMANCY(4).zip` archives are **development fixtures only**, never a production installation list. Original-source archives may be used to test pristine-source composition for a mod actually present in the selected installation. Never include candidates solely because a historical archive, rule catalog, or later stack contains them. See [unified application contract](../product/UNIFIED-TWO-PAGE-WORKFLOW.md).
- For the **2026-10-16 development snapshot only**, user explicitly excluded **Dedka Auto Shop, Gambling System - Roulette and NightCityBilliards**; the next stack may include them. The archived `modsLIVEnow` snapshot contains a Dedka folder, which cannot override the user's then-current installed-source statement. In the final product, actual selected installation membership, not this historical exclusion list, determines whether to inspect a mod.
- Repeat discovery independently for a subsequent stack. No all-mod static registry; only installed, source-proven, *admitted* optimizations contribute anchors. A mod with **one** fixed location is eligible if replacing its recurring discovery has meaningful savings.

## Pass identity and suspension granularity

This is **one generic, source-structural Resolver optimization**, not a named-mod semantic rule or a registry of approved quests. A reviewed source-specific pilot is a fixture used to prove one transferable transformation family. Production admission re-derives proof for each effective installed source and source epoch, rather than recognizing names or reusing archived source hashes.

The safe suspension unit is a *location-dependent recurring execution lane*, not the mod as a whole. Disable an entire recurring callback/job only if its entire transitive side-effect/callee closure can be retired or replaced by correct source-proven wake semantics. For mixed callbacks, preserve independent remote and active work and split out only the location-dependent section if provably safe; otherwise select a narrower gate or reject. In particular, **phone/SMS, PC-terminal and quest fact listeners continue while FAR**, and their callbacks can change eligibility, rearm/disarm native anchors, or reactivate author code without the player moving. A quest starting point becoming NEAR must re-enable the original author's exact activation conditions, not trigger a synthetic quest launch. A noncancelable timer with inseparable work is not an eligible whole-job retirement just because a fixed coordinate was found.

## Admission means provable dormancy, NOT absence of an existing wake path

An authored recurring position scan **may be the wake mechanism the native service replaces**. Its existence must not disqualify the mod.

A candidate qualifies when all of the following are source-proven:
1. Coordinates used for this wake are immutable for the validity epoch, with exact XY/XYZ distance semantics, transforms, thresholds and source owner. If mutable, reject static-only generation.
2. The retiring loop's **entire downstream observable work** is identified: location discovery, UI prompts, entity housekeeping, remote activation, active quest logic, timers, subscription registration, etc.
3. Each removed proximity wake has a generated native equivalent that is ready at the right point in the session, supplies an **initial state** (including player already near), and emits every necessary enter/leave change at acceptable latency. Conservative early wake radius must contain the authored activation radius and account for fast traversal/teleportation; original exact checks can execute once near.
4. **Non-location wake** remains active, including quest facts, remote story events, hotkeys, menus, session load, API requests, spawned entities, cleanup and error recovery. Wakes are OR-composed; proximity cannot become the sole trigger unless the source proves that was already true.
5. The single player-position coordinator works **independently of the retiring subscriber**, otherwise the replacement would deadlock. A mere per-mod `State(id)` check inside the old timer is not full callback dormancy.
6. Unavailable service, uninitialized index, invalid position, stale session/generation, lost wake events, source drift or subscription failure must **fail open** to the author behavior. The fallback should not keep the obsolete full-rate poller running in healthy native mode. Native subscription cursors and snapshot reconciliation must be reliable before removing the old subscriber.
7. Source reinjection is single-pass and version-fluid: original Nexus source -> complete generated result; current live/previous optimized source -> complete upgrade; second pass -> idempotent; substantive semantic drift -> fail closed. Preserve mod-authored keys, timing, order and gameplay behavior.

### Transformation outcomes

| Outcome | Resolver behavior |
| --- | --- |
| `REPLACE_PROXIMITY_WAKE` | Generate stable per-owner anchors, install native subscription and lifecycle adapter, move **only** verified proximity wake into the native service, **remove** the superseded Cron/scheduler/onUpdate discovery subscriber and its now-dead position reads/loops/allocations. Keep author exact interaction when awoken. |
| `SPLIT_MIXED_WAKE` | When the old callback also performs independent state/remote/recovery work, leave that work active but extract/retire only the source-proven proximity portion. Do not suppress the mixed subscriber wholesale. |
| `GATE_DOWNSTREAM_ONLY` | Native service can prove FAR but cannot yet reproduce all wake/lifecycle semantics; preserve original event/callback, remove only validated redundant native reads/downstream work. Do **not** report C->Lua dispatch eliminated. |
| `NO_GAIN` | Static positions exist but no recurring work is removed; **do not register** locations or ship data for that mod. |
| `REJECT` | Dynamic sources, unknown callbacks, missing alternative wake, or behavior drift: leave untouched. |

## Actual live-source examples in supplied snapshot

- **StreetGamesPoker** `init.lua`: `poker.world_far` scheduled every 1s only to rediscover tables while inactive. This is an intended `REPLACE_PROXIMITY_WAKE` candidate. Keep near world/seat, currently active poker, menu, lifecycle, gameplay and safety fallback. R4 Lua experiment removed far job; native must now replace its wake, not add another Lua polling loop.
- **CyberTrials**: separable `cybertrials.world_far` discovery can be replaced once the available static START anchors, session track roster and near wake are verified. Do not freeze user-added tracks or replace race, editor, menu or system-state work.
- **Killing Night** `init.lua`: 1s manual-start coordinate scan of four fixed sites is separable only if entering the native wake radius reliably drives the original **exact 1m** activation with correct initialization; active bosses, AV/Chimera and cleanup remain author-owned. R4's early return is not the eventual native full-retirement result.
- **Specter M11**: stage-1 arrival is not the only wake; remote M9 story transition must still fire without player proximity. Retire only proven stage-1 position work, never the whole quest subscriber.
- **VehicleRepairGarages, tunnel_rescue, ziggy_last_play**: evaluate each FAR scanner against active repair, remote story, save/session identity and spawned entity cleanup before selecting replace/split/gate.

## Required next implementation and tests

1. Native RED4ext service must have a live position coordinator, per-consumer cursored transition stream, initial snapshot, session epoch, owner arm/remove lifecycle, missed-event recovery and truthful health/fail-open reporting. The existing prototype has only externally-fed position updates and a most-recent-update change batch; **it does not yet meet full-wake replacement proof**.
2. Generic Resolver source recognition and transformation must operate on installed source, not on mod name. Admit a structural discovery pattern only with independently proven side-effect inventory and wake equivalence; semantic mod-owned source stages handle exceptions. Each emitted anchor must correspond to an actual retired or reduced work path.
3. Tests must cover initial near, far->near and near->far, repeated enter/exit, session/save change, fast travel, high-speed approach, runtime service missing or dying, original/live/partial input, drifts and coexistence of remote wakes. Confirm old registrations/loops really disappear from generated output. Profile native dispatch vs Lua jobs separately.
4. Never claim a source-only rewrite eliminates CET's native C event dispatch unless measured/verified at the actual native subscription boundary.

See [shared registry audit](SHARED-STATIC-LOCATION-REGISTRY.md), [R4 experiment](../0-engine/STATIC-ANCHOR-RUNTIME-EXPERIMENT.md), and [native foundation](../0-engine/STATIC-NATIVE-LOCATION-PROTOTYPE.md).
