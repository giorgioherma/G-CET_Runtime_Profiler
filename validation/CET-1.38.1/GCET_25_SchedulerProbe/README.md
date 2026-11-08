# GCET_25_SchedulerProbe — optional CET 1.38.1 Scheduler test

**Temporary diagnostic CET mod, never part of the public profiler runtime.** It uses the 0-Engine interface from the original 0.18.6 source: `GetMod("0-Engine")` then `Engine.Register(...).Schedule` (or `Engine.Schedule`). It never changes 0-Engine, old 2.31 mods, CET native code or gameplay.

## Installation

1. In the existing CET 1.38.1 manager, install the profiler with **0-Engine integration** enabled, not core-only mode. Leave the genuine 0-Engine host installed. If integration is refused as unsafe, stop: never hand-patch 0-Engine.
2. Copy just the `GCET_25_SchedulerProbe` folder into `Cyberpunk 2077/bin/x64/plugins/cyber_engine_tweaks/mods/`. Do not replace `0-Engine/init.lua`.
3. Keep incompatible 2.31 **0-Engine-dependent gameplay mods** disabled.
4. Load a save, press **F11** to capture **60–120 seconds in world gameplay** (not menus), press F11 to stop and COLLECT RESULTS.
5. Upload the capture ZIP and then delete `GCET_25_SchedulerProbe` from the game.

## Expected Scheduler jobs

All five jobs use owner `GCET25SchedulerProbe`; three register at mod initialization, and the one-shot and cancel test register at each F11 capture start.

| ID | API | Expected behavior |
| --- | --- | --- |
| `frame10` | `EveryFrames(10)` | Repeating every 10 gameplay frames |
| `timed1` | `Every(1.0)` | Repeating roughly once per game second |
| `once3` | `After(3.0)` | Fires once after capture start |
| `cancel5` | `EveryFrames(5)` | Exactly eight calls, then self-cancels |
| `paused4` | `EveryFrames(4)` | Initially inactive; resumes at capture start, then pauses after six calls |

The fixture emits cumulative counters at the start of capture and every ~5 seconds using the native profiler's `CETProfilerMark`. See `GCET25_SCHED_V1_F10_..._T1_..._O3_..._C5_..._P4_..._E_...` in `CET_Runtime_Profile_Markers.csv`. Use differences between snapshots to compare independent Lua calls with the native Scheduler's capture-window counts. The first checkpoint is a **baseline**, not zero.

Check real registrations, native begin/end timing, owner and labels, no duplicate execution, and preservation of 0-Engine's original update function. If no Scheduler API is present, the fixture prints a diagnostic and refuses to substitute one. The regression test `tests/CET25SchedulerProbeContract.lua` exercises the real canonical Scheduler implementation with mocked CET native entry points; only the in-game capture proves integration with the actual installed stack.

**The pinned ASI remains unchanged.** This probe is removed before public Nexus packaging.
