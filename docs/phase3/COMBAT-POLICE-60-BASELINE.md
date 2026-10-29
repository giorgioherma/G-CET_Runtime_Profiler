# COMBAT-POLICE-60 — standard Phase 3 gameplay stress baseline

**Status (updated 2026-10-29): STANDARD PROTOCOL, VERIFIED PER-JOB MEASUREMENT, AND USER-CONFIRMED CONTROLLED A/B COMBAT CAPTURE WITH MEASURED FPS/P99 DIFFERENCES. Individual changed-mod contributions are not isolated.**
This is the default repeatable *combat-stress* comparison for G-CET Phase 3 allocation instrumentation and later optimization A/B. It replaces the earlier proposal to combine 1 minute idle + 2 minutes driving + 1 minute combat in a single run for per-job allocation comparisons. That earlier mixed scenario remains a separate possible test, **not** the standard stress baseline.

## Canonical test identity and scenario

- **Scenario ID / capture title:** `COMBAT-POLICE-60`.
- **Game:** Cyberpunk 2077, current installed mod stack.
- **Location:** the user's **apartment-area police fight** (user-selected repeatably laggy scenario). The exact apartment, street, map coordinates and save slot have **not yet been supplied**; do not invent them.
- **Activity:** **one continuous 60-second police combat stress capture**. The goal is sustained combat throughout, not idle/traversal/drive time.
- **Preferred Profiler control (new):** **F10** is the default only if the `CETProfiler_Police60` binding was missing in CET bindings. It remains user-remappable. **Profiler: COMBAT-POLICE-60 (start + auto stop/export)**. Press once immediately before combat. The existing `CETProfilerControls` `onUpdate` observes native captured time and automatically calls the same Pause + Dump as F11 at the first update on or after **60.000 captured seconds**. Normal frame cadence means a few extra milliseconds/one update of overshoot are possible; it is not an operating-system hard deadline. F11 remains manual start/stop and can abort an automated run early. **One capture**, not three profiles.
- **Scenario markers:** the new timed start automatically emits `SCENARIO_COMBAT_START`; timed finish emits `SCENARIO_COMBAT_END` before exporting. It blocks manual scenario changes until the run finishes. In older captures, manual tags are optional. Per-job allocation rows are **whole-capture aggregates**, not scenario-partitioned totals.

## CapFrameX synchronization (one key, independent clocks)

For CapFrameX v1.9.1.2 beta, open **Capture** and set **Capture hotkey F10**, **Capture time 60 seconds**, **Capture delay 0 seconds**. If its per-game duration option is available, set `This game` for Cyberpunk 2077; otherwise use the global 60-second capture setting. Leave CapFrameX running with Cyberpunk detected and ready. **Do not assign CapFrameX to F11.** One F10 press is intended to trigger both CapFrameX's global key and CET's in-game F10 key. Both terminate independently after ~60 seconds; CapFrameX does not need a stop-key press or game focus change. **F11 remains the original unrestricted manual CET START/STOP key**; its manual captures are not subject to the 60-second limit.

Actual event delivery is not guaranteed by source: confirm CapFrameX starts when CET does in the first trial (record start times / markers) and that both finish, then retain their raw data. Native CET captured clock and CapFrameX capture clock can differ by start-latency or up to a frame at their own stop boundaries; preserve original timestamps and compare common overlapping interval if needed. Avoid editing CapFrameX binary or interfering with its own hotkey manager.

## Reproducibility checklist

Use the **same saved game, starting position, apartment-area approach/engagement point, weapons/loadout, difficulty, graphics preset and FPS cap**, same in-game mod stack and Resolver overlay, 0-Engine settings, game/CET and Profiler build. Record the **actual native profiler DLL SHA-256**, packaged Profiler build/CI run, Resolver artifact/JSON identity, scenario date/time and any relevant mod/config changes. This comparison must not mix captures from different builds, deployments or locations without explicitly labeling the change.

Before F10: load the save and let startup, streaming and mod initialization settle; arrange the same police engagement and **same intended wanted level**. Do not include menu navigation, loading, fast travel, spawning/setup delays or post-fight cleanup within the measured minute if avoidable. During the capture, maintain active fighting for the full minute; use the same loadout, approximate movement path and engagement behavior. Police waves, NPC AI, spawn patterns, damage and wanted-state progression are inherently stochastic; **the test is repeatable, not deterministic**. Record deviations rather than imply identical NPC behavior. If combat ends early or is interrupted, mark the capture **NONCOMPARABLE / MIXED** or repeat; never silently treat it as 60 seconds of combat.

**Capture title convention:** `COMBAT-POLICE-60` (or `COMBAT-POLICE-60_<run label>` where the Profiler title allows it). Keep the original raw CSVs, `CET_Summary.json`, `CET_Resolver_Input.json`, full Profiler capture archive, the exact deployed Resolver pass report and CapFrameX capture if available. The user's first *actual* valid capture becomes the measured reference; do not manufacture measured numbers from this protocol.

## Scheduler runtime version gate

For a new baseline from the source-proven G-CET-owned fixed runtime, Resolver stages **both** `0-Engine/modules/G-CET/Scheduler.lua` (canonical implementation) and root `0-Engine/modules/Scheduler.lua` (thin permanent alias returning that same module). Unrecognized foreign 0-Engine roots are preserved instead of overwritten. Profiler installation must detect **namespaced** integration; it must **not** deploy an independent `CETProfilerScheduler.lua` on this optimized stack. Both Resolver files remain after normal or emergency Profiler restore. Pre-fix captures had 23 registered jobs and 0 recorded executions; **do not classify them as valid per-job allocation baselines**. Confirm post-fix data shows actual per-job calls for runnable scheduler jobs before interpreting ops/s or bytes/s. Legacy non-Resolver stacks still have a different reversible Profiler integration path.

## First validated scheduler measurement — 2026-10-28 at 19:13 CET

**User-supplied evidence:** `CET-20261028-191326_SECOND_TEST_60S_INSTRUMENTATION.zip`, original title `SECOND_TEST_60S_INSTRUMENTATION`. This is the **first accepted Phase 3 scheduler-allocation measurement reference**, replacing the two earlier **unusable zero-job captures** for per-job analysis. It is not evidence that gameplay performance improved. Combat markers confirm the test interval; the exact save/location, difficulty, wanted level, deployed DLL hash and precise start offset against CapFrameX are not independently specified/verified.

- Native CET capture **60.017046 s**; `SCENARIO_COMBAT_START` at **0.0998 ms**, `SCENARIO_COMBAT_END` at **60016.9383 ms**, `PAUSE` at **60017.0465 ms**.
- CapFrameX v1.9.1.2 **60.016 s**, **1,898 frames**, **31.625 FPS mean**, **P99 70.0636 ms**, **233.3367 ms max**. Duration mismatch about **1.4 ms**. Report shows `sync.quality=GOOD`, relative-key-clock alignment; this alone does **not** independently establish a sub-frame absolute start offset.
- **1,899 scheduler frames**, **23 registered jobs**, **20 jobs with executions**, **10,614 total invocations**. Three dormant Poker jobs with zero calls are explicitly recorded; **0 dropped/unpaired scopes**. The previous capture `CET-20261028-170533_60S-BASELINE_CAPTURE_AGREED_STANDARDISED_TEST.zip` recorded 1,971 frames, 23 jobs, **zero job invocations and zero allocation counts**. Thus the scheduler instrumentation coverage defect is resolved.
- Scheduler frame positive LuaJIT growth: **6,368,040 B / 46,052 allocation operations** (about **6.07 MiB** or **767.3 ops/s**). Root-job exclusive: **1,609,420 B / 24,819 ops** (about **1.53 MiB**, **413.5 ops/s**). Outside-root residual: **4,758,620 B / 21,233 ops** (about **4.54 MiB**, **353.8 ops/s**). The two partitions reconcile to the frame envelope; **residual is UNASSIGNED, NOT demonstrated intrinsic 0-Engine cost**, and native/profiler bridge overhead is not subtracted. These counters are positive allocated-byte growth, **not retained heap**.
- Scheduler job CPU totals ~**919.335 ms / minute (15.318 ms/s)**. Largest job allocations by exclusive ops: `RICA/rica.state` **7,009 ops / 362,040 B / 60 calls**, `RICA/rica.rewards` **4,800 ops / 302,880 B / 240 calls**, `RepeatableCyberpsychos/cyberpsychos.presentation_worker` **4,459 ops / 332,400 B / 476 calls**. Not yet a transformation authorization.
- **Performance issue remains**: `RICA/rica.state` had a **167.2304 ms single scheduler job** (also a 19.5034 ms spike); the `RepeatableCyberpsychos` presentation worker regularly reached ~6–7 ms. Investigate whether the RICA spike is reproducible before optimizing. Correlation with frametime spikes does not prove single-job causality across clocks.
- Captured deep-callsite CSV contains **589 rows (811 field occurrences)** referencing `G-CET\\Scheduler.lua` in `Deep_Callsites.csv`, with **none referencing `CETProfilerScheduler.lua`**. Confirms canonical foldered scheduler source reached while no separate adaptive file was observed in those text traces. Hash/package install provenance still needs independent validation when interpreting installation status.

**Acceptance:** Phase 3 per-job measurement **WORKING**, with accurate numeric source evidence, no dropped scopes and whole-frame/root/residual accounting. **Do not** describe the old zero-job runs as performance improvements or regressions; they were instrumentation coverage failures. This one capture is an attribution baseline, not sufficient alone to demonstrate FPS gains, authorize dormancy or establish A/B causality. Repeat identical COMBAT-POLICE-60 setup for later before/after changes.

## User-confirmed controlled Phase 3 comparison — 2026-10-29

**CONTROLLED A/B (user-confirmed):** The user explicitly confirms the Phase 3 combat captures used the established controlled `COMBAT-POLICE-60` scenario. Do **not** call the sessions uncontrolled solely because their save/position/gear details were not all independently enumerated in the ZIP reports. The distinction is between the controlled gameplay procedure (user-confirmed), the observed configuration-level performance differences (measured), and attribution to any *single* changed mod (not isolated).

| Metric | 2026-10-28 19:13 first valid measurement | 2026-10-29 14:47 Dedra / CyberTrials / Sit Anywhere test |
| --- | ---: | ---: |
| CET captured duration | 60.017046 s | 60.0057 s |
| CapFrameX captured duration | 60.016 s | 60.004 s |
| CapFrameX average FPS | 31.625 | 34.498 (**+9.1%**) |
| CapFrameX P99 frametime | 70.0636 ms | 50.736 ms (**−27.6%**) |
| CapFrameX recorded frames | 1,898 | 2,070 |
| Registered scheduler jobs / executed jobs | 23 / 20 | 27 / 21 |
| Dropped scheduler scopes | 0 | 0 |

The latest test archive is `CET-20261029-144740_DEDRA_SITANYWHERE_CYBERTRIALS.zip`. The new runtime shows **zero** calls and **zero** positive LuaJIT allocation ops for each targeted paused `dedra.active`, `cybertrials.realtime`, and `sitanywhere.realtime` job; corresponding quiet/wake probes continue at their approved bounded cadence, and the user confirms that all three mod features work. Dedra's immediately preceding same-scenario FPP combat record `CET-20261029-034947_NO_DEDRA_PLZ.zip` had **2,182 `dedra.active` calls / 169,922 positive allocation ops** over its 60s interval. This supports targeted before/after dormancy and a **controlled scenario-level** performance comparison. It does not isolate how much of the overall FPS/P99 change belongs to each mod or establish the activation-transition timing and every lifecycle edge case. Keep hook ingress distinct from paused scheduled workers.

### Hitch-tail, GC and native handoff analysis of the controlled A/B

**Primary Phase 3 optimization objectives:** reduce excessive frametime spikes and hitch episodes, remove avoidable LuaJIT allocator requests and bytes, and reduce native→Lua ingress/materialization where a source-proven safe design exists. Average FPS is **secondary** and insufficient to judge these objectives. P99 / P99.9 and event severity, actual GC collector time, object births, per-job operation frequency, and owner-zero native argument conversion/wrapper allocation require separate treatment.

**Paired tail result:** P99 frametime **70.0636 → 50.7361 ms (−27.6%)**, supporting improved ordinary tail frame pacing and the user's perception; **worst single frame worsened from 233.3367 to 471.1733 ms**. New archive records **70 adaptive hitch frames across 30 episodes (~30/min)**, 338 frames ≥33.3ms, 22 ≥50ms, 7 ≥100ms; original baseline per-threshold episode counts were not available in the current review, so **the count/frequency difference is NOT measured**. Do not misstate the 30 episodes as a proven reduction.

**Latest 60s GC/allocator:**
- LuaJIT collector **2,717.573ms / 60s (45.289ms/s)**, two completed cycles, largest recorded incremental step **94.524ms**, including **94.486ms atomic→sweepstring** within a VehicleMileage callback. Its placement does not prove VehicleMileage caused the pressure.
- Positive LuaJIT allocated-growth **167.106 MiB**, **1,517,482 allocation requests (~25,289/s)**, **413,833 observed GC object births**. Unknown/owner-zero: **84.690 MiB (50.68% of bytes)** and **685,413 requests**; cannot silently assign these to mod heap, a direct source producer, or retained memory.
- Experimental native pre-dispatch PURE `OnAction`: **57,187 calls, 29,565,679 B / 171,561 allocator requests** before the Lua subscriber. Separate native stage windows for root `WrapNextOverride` closure (**63,177 calls / 11,504,661 B / 125,734 requests**) and chain closure (**63,177 / 9,492,917 B / 125,734 requests**) are **non-additive** with measured owner-zero allocation; no safe closure reuse/lifetime proof or net savings claim exists.
- Worst frame **471.173ms** aligns with a **379.872ms f80h_aroused_expressions** callback; major **VehicleMileage 94.650ms** and **nativeInteractions 95.996ms** callback spikes also occur. These callback walls are **potentially GC-confounded**, not proven source CPU or per-mod garbage causality; **181.842ms** frame has no matching recorded CET callback spike.

**Interpretation:** The controlled A/B has a clear P99 improvement and valid physical pause evidence for Dedra, CyberTrials and Sit Anywhere while retaining catastrophic hitch, collector and native handoff pressure. For follow-on work, prioritize **spike severity/frequency + GC + positive allocator operations/bytes + native marshalling**; report average FPS separately, and never convert correlated outliers into automatic source rewrites. The proposed further hotspot sequencing remains source-/wake-proof-gated.

## Next A/B experiment — RC dormant sweep, RICA preserved

On the first valid 2026-10-28 capture, the player did not engage a cyberpsycho but RC still ran roughly 1,900 alternating frame jobs and 476 presentation steps, while RICA emitted 240 reward polls and a single 167.23ms state-job outlier. RC's previous primary semantic treated **global available replay count** as justification to scan all 17 bosses 1Hz. That does **not** prove the player is anywhere near one.

**Experimental source-owned RC change:** native compiled 17-site `Watchdog.nativeFar` evidence is the safe distant signal (all sites must be explicitly `true`; otherwise fail-open); every native 1Hz `RuntimeTick`, the 10s scheduler decision and save-load settings/reconciliation remain. The alternating combat/reward and watchdog frame jobs are now **actually paused** outside their bounded 17-site sweep and resumed together for near/pending/replay-count-change work; distant/unchanged 17-site sweeps have a 30s safety audit. Distant presentation runs only on aggregate replay-state/signature changes or a 30s safety audit; nearby/failure paths retain the previous cadence. The worker's pause API is strictly required on attach; missing support rejects the optimization and falls back to original source callback.

**RICA not made fully dormant:** It owns a persistent native stronghold scheduler and pending body/completion rewards. Its `rica.state` 201.98ms included a 167.23ms *single-call spike*, not representative steady cost. The author original checks rewards at 1Hz; the primary pristine generator's existing 0.25s lane now uses native pending-body-site and completion-serial hints to skip most **empty** heavy reward checks, but forces a complete 1Hz fallback and immediately processes any pending/unknown status at 0.25s. Leave native Tick, 1s settings and mappin lanes intact. Do not describe this as event-only processing. Replay, body and completion rewards still require gameplay validation. Full notes: [LATEST-WORK](../LATEST-WORK.md).

## What to compare

- **Positive LuaJIT allocation operations/s and bytes/s**, GC pressure/phases and major allocation producers.
- **0-Engine** scheduler whole-frame envelope, root-job accounting and outside-root residual (unassigned, **not automatically intrinsic framework allocations**).
- **Individual scheduled jobs** inclusive/exclusive allocations, operations/call and CPU; nested totals are not additive.
- Shared native hook/pre-Lua marshalling, callback fanout, spikes and CapFrameX frametimes if captured.

**Interpretation:** Per-job/frame Phase 3 counters are aggregated over the entire 60-second capture, so keep almost all measured time in combat. Attribution is an execution window, **not** proof of mod-owned retained heap, garbage, or automatically safe dormancy. The user makes all human decisions on whether/how to pause a mod. Mod-origin allocations and GC remain untouched by instrumentation. Do not turn this test protocol into an automatic optimization rule.

## Status / dependencies

- Native instrumentation source commit `7259194`; managed CI **#2134 green (6/6)** and native rebuild **#94 green**. User-supplied real capture `CET-20261028-135900_NEW_INSTRUMENTATION - STANDARD 60s capture BASELINE.zip` ran **60.945835 seconds**, 966,622 CET callbacks, 1,928 0-Engine scheduler frames, with CapFrameX pairing. **However, all 23 registered scheduler jobs reported 0 Calls, all new per-job alloc counters were zero, and frame envelope was 0 bytes/ops.** Thus this capture is usable for existing callback/G-GC/frametime investigation but **does not validate Phase 3 scheduler allocation instrumentation or provide a Phase 3 per-job numerical baseline**. Do not interpret arithmetic `Reconciled:true` as meaningful execution coverage. Cause of all-zero scheduler job execution has not been established (runtime inactivity/paused registrations vs bridge failure).
- User has installed experimental native instrumentation and provided a real in-game ZIP with new Phase 3 CSVs (confirming capture/export). The exact live ASI hash and full installation/restore test are not verified from that ZIP. The new timed CET binding and #94 native artifact are being packaged together for consistent future captures.
- **Pending for complete reproducibility:** identify the exact apartment and save slot, wanted level, run path/engagement approach, FPS cap and chosen equipment from the first real test, then keep those settings fixed for future comparisons.
- **No requirement to make multiple different scenario captures.** One qualifying stress capture per tested configuration is the standard. Additional repeats may quantify variability when needed, but should not become a default user burden.

## Handoff rule

Every new G-CET agent/chat must treat **COMBAT-POLICE-60** as the user-chosen, canonical **Phase 3 combat-stress baseline protocol**. Read this document before interpreting or requesting a Phase 3 stress capture. Do not substitute an idle+drive+combat mixed capture, an invented location, generic 90-second fight, or an unverified measured baseline. Preserve the distinction between (1) protocol established, (2) measured reference captured and validated, and (3) per-version comparison accepted.
