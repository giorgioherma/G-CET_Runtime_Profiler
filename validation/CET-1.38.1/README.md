# CET 1.38.1 / Cyberpunk 2.5 — callback validation fixture

This temporary mod is **not part of the public profiler runtime**. It independently counts its own Lua callbacks so we can compare actual mod execution with the public G-CET native CSV.

## Install for the game test

1. With the game closed, install the packaged G-CET profiler using its existing GUI or CLI.
2. Copy the supplied `GCET_25_Validation` directory into `bin/x64/plugins/cyber_engine_tweaks/mods/`.
3. Launch Cyberpunk 2.5, load a save, and enable a manual profiler capture (existing F11 toggle).
4. Play for 2–3 minutes: walking/movement, vehicle entering or driving, and combat if practical.
5. Press the profiler toggle to stop/export, then collect through the G-CET manager.
6. The fixture records bounded `GCET25_V1_*` counter snapshots in the native `CET_Runtime_Profile_Markers.csv`. Use the first marker after F11 as baseline; compare snapshot deltas to capture-scoped profiler results.
7. Remove this fixture mod after testing.

The fixture never edits a gameplay value. Its `Override.PlayerPuppet::IsInCombat` hook calls the original method and returns its original result.

## Checks

- `event.onInit`, `event.onUpdate`, `event.onDraw`, `event.onShutdown`: lifecycle/events.
- `Observe.PlayerPuppet::OnAction`: high-frequency engine callback.
- `ObserveAfter.PlayerPuppet::OnAction`: after observer attribution.
- `ObserveDelayed.PlayerPuppet::OnAction`: registered after three update seconds to test deferred runtime registration.
- `Override.PlayerPuppet::IsInCombat`: non-mutating pass-through and `next()`/exclusive-time integrity.

`Registered=yes` means the registration API returned without a Lua error. CET can silently reject an unresolved engine method, so a positive callback count and absence of CET registration errors are required to prove execution. For the override, enter combat so it is exercised.

This fixture counts full mod runtime, whereas native profiler counts only **capture-active** callbacks. Consequently exact numerical count equality requires capture to be active throughout the interval, or a capture-state-aware comparison. The authoritative first test is positive attribution, target name, time correctness and no duplicate hook execution. Do not compare raw counters across different start/stop intervals.

## Acceptance

- Normal world load on official CET control and on profiled CET.
- Original input/combat behavior unchanged.
- Each exercised callback attributed to `GCET_25_Validation`, its kind and exact script target in `CET_Runtime_Profile_Detail.csv`.
- Mod's own counts must be >= profiler's capture-limited counts, with time windows reconciled before claiming precise equality.
- No callback appears duplicated (a single runtime registration produces a single native counter row per target/kind).
- `Override` times exclude downstream original native `IsInCombat` work.
- No stale context pointers or duplicate observers on reload.
- Native event/timeline/spike/Scheduler CSV formats and existing controls stay unchanged.

This fixture does not prove game-wide overhead or all third-party mod behavior. Report those as separate gameplay checks.

## Independent 0-Engine Scheduler probe

For the final 0-Engine integration test, use the standalone `GCET_25_SchedulerProbe/` CET mod, **not** incompatible 2.31-dependent gameplay mods. Read [the Scheduler probe instructions](GCET_25_SchedulerProbe/README.md). It creates three initial Scheduler jobs and two capture-scoped jobs, emits independent native counter checkpoints, and tests cancellation, pause/resume, repeat capture and cleanup. It does not edit 0-Engine files. This is a manual test fixture and **must be removed before publishing to Nexus**.
