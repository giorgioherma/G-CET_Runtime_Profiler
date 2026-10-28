# COMBAT-POLICE-60 — standard Phase 3 gameplay stress baseline

**Status (2026-10-28): STANDARD PROTOCOL + FIRST OBSERVED ~60s CAPTURE; PHASE 3 SCHEDULER DATA NOT ACCEPTED AS BASELINE.**
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
