# COMBAT-POLICE-60 — standard Phase 3 gameplay stress baseline

**Status (2026-10-28): USER-APPROVED TEST PROTOCOL; FIRST MEASURED BASELINE CAPTURE STILL PENDING.**
This is the default repeatable *combat-stress* comparison for G-CET Phase 3 allocation instrumentation and later optimization A/B. It replaces the earlier proposal to combine 1 minute idle + 2 minutes driving + 1 minute combat in a single run for per-job allocation comparisons. That earlier mixed scenario remains a separate possible test, **not** the standard stress baseline.

## Canonical test identity and scenario

- **Scenario ID / capture title:** `COMBAT-POLICE-60`.
- **Game:** Cyberpunk 2077, current installed mod stack.
- **Location:** the user's **apartment-area police fight** (user-selected repeatably laggy scenario). The exact apartment, street, map coordinates and save slot have **not yet been supplied**; do not invent them.
- **Activity:** **one continuous 60-second police combat stress capture**. The goal is sustained combat throughout, not idle/traversal/drive time.
- **Profiler control:** start with **F11** immediately before engaging; fight continuously; stop/export with **F11** at roughly 60 seconds. Start and end with no menus open. This is **one capture**, not three separate scenario profiles.
- **Scenario markers:** optional; `CETProfiler_CombatTag` can label the combat segment if convenient, but is not necessary for this single-scenario capture. The new per-job allocations are **whole-capture aggregates**, not scenario-partitioned totals.

## Reproducibility checklist

Use the **same saved game, starting position, apartment-area approach/engagement point, weapons/loadout, difficulty, graphics preset and FPS cap**, same in-game mod stack and Resolver overlay, 0-Engine settings, game/CET and Profiler build. Record the **actual native profiler DLL SHA-256**, packaged Profiler build/CI run, Resolver artifact/JSON identity, scenario date/time and any relevant mod/config changes. This comparison must not mix captures from different builds, deployments or locations without explicitly labeling the change.

Before F11: load the save and let startup, streaming and mod initialization settle; arrange the same police engagement and **same intended wanted level**. Do not include menu navigation, loading, fast travel, spawning/setup delays or post-fight cleanup within the measured minute if avoidable. During the capture, maintain active fighting for the full minute; use the same loadout, approximate movement path and engagement behavior. Police waves, NPC AI, spawn patterns, damage and wanted-state progression are inherently stochastic; **the test is repeatable, not deterministic**. Record deviations rather than imply identical NPC behavior. If combat ends early or is interrupted, mark the capture **NONCOMPARABLE / MIXED** or repeat; never silently treat it as 60 seconds of combat.

**Capture title convention:** `COMBAT-POLICE-60` (or `COMBAT-POLICE-60_<run label>` where the Profiler title allows it). Keep the original raw CSVs, `CET_Summary.json`, `CET_Resolver_Input.json`, full Profiler capture archive, the exact deployed Resolver pass report and CapFrameX capture if available. The user's first *actual* valid capture becomes the measured reference; do not manufacture measured numbers from this protocol.

## What to compare

- **Positive LuaJIT allocation operations/s and bytes/s**, GC pressure/phases and major allocation producers.
- **0-Engine** scheduler whole-frame envelope, root-job accounting and outside-root residual (unassigned, **not automatically intrinsic framework allocations**).
- **Individual scheduled jobs** inclusive/exclusive allocations, operations/call and CPU; nested totals are not additive.
- Shared native hook/pre-Lua marshalling, callback fanout, spikes and CapFrameX frametimes if captured.

**Interpretation:** Per-job/frame Phase 3 counters are aggregated over the entire 60-second capture, so keep almost all measured time in combat. Attribution is an execution window, **not** proof of mod-owned retained heap, garbage, or automatically safe dormancy. The user makes all human decisions on whether/how to pause a mod. Mod-origin allocations and GC remain untouched by instrumentation. Do not turn this test protocol into an automatic optimization rule.

## Status / dependencies

- Instrumentation source commit `7259194`; managed CI **#2134 green (6/6)** and experimental native rebuild **#94 green** on 2026-10-28. These prove builds/tests, **not** in-game capture correctness.
- User reports native instrumentation **installed**. Exact live ASI hash, full install/restore and in-game 60-second stress results have **not yet been inspected in this documentation**. Do not infer which package/run is live solely from the CI IDs.
- **Pending for complete reproducibility:** identify the exact apartment and save slot, wanted level, run path/engagement approach, FPS cap and chosen equipment from the first real test, then keep those settings fixed for future comparisons.
- **No requirement to make multiple different scenario captures.** One qualifying stress capture per tested configuration is the standard. Additional repeats may quantify variability when needed, but should not become a default user burden.

## Handoff rule

Every new G-CET agent/chat must treat **COMBAT-POLICE-60** as the user-chosen, canonical **Phase 3 combat-stress baseline protocol**. Read this document before interpreting or requesting a Phase 3 stress capture. Do not substitute an idle+drive+combat mixed capture, an invented location, generic 90-second fight, or an unverified measured baseline. Preserve the distinction between (1) protocol established, (2) measured reference captured and validated, and (3) per-version comparison accepted.
