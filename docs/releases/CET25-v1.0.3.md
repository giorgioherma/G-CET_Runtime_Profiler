# G-CET Runtime Profiler v1.0.3 — Cyberpunk 2077 2.5 / CET 1.38.1

**LATEST RELEASE for Cyberpunk 2077 2.5 with Cyber Engine Tweaks 1.38.1.**

**Not for Cyberpunk 2077 2.31 / CET 1.37.1.** That configuration uses the [previous v1.0.2 release](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/tag/v1.0.2). The two versions use different native CET binaries; do not interchange the ASIs.

## 0-Engine and advanced profiling

**0-Engine remains a dependency for advanced 0-Engine Scheduler/job attribution.** The basic CET callback CPU profiler works without 0-Engine, but per-job Scheduler timing, job labels and owner attribution require a recognized, correctly integrated 0-Engine host.

**We tested 0-Engine successfully in Cyberpunk 2.5.** The actual game capture verified the original 0-Engine callback was running alongside the profiler bridge, and all five independent Scheduler probe jobs appeared under their correct owner. This confirms the tested 0-Engine installation and integration, not blanket compatibility for every 0-Engine-dependent gameplay mod from Cyberpunk 2.31.

If 0-Engine's installed source is unsupported or unsafe to integrate, the profiler provides a **core-only mode** that leaves 0-Engine untouched and omits advanced Scheduler attribution.

## What changed

- Ported the profiler's native CET hooks to CET 1.38.1 / Cyberpunk 2.5, preserving upstream deferred hook registration and current native ABI.
- Fixed incorrect `<unknown>` callback ownership and added capture-gated, cached native owner lookup.
- Fixed 0-Engine integration: original onUpdate and the profiler Scheduler are composed into one dispatch rather than replacing the author's callback or introducing a second ticking Scheduler.
- Added the in-game capture timer HUD, manual unlimited **F11** capture/stop/export and separate automatic 120-second **F10** capture.
- Retained heap/scenario checkpoints, transactional installation, capture collection, native source/byte integrity checks, recovery and restoration.

## Validation completed on November 8, 2026

- Windows CET 1.38.1 native compile succeeded: [native build #42437606920](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42437606920). **Native ASI SHA-256: `f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f`.**
- [Portable package CI #42443720194](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42443720194) passed application build, native source/hash verification, install/collect/restore integration and actual Lua Scheduler regression checks.
- First real-world 2.5 modded capture: approximately 2.39 million callbacks assigned to named owners; **zero `<unknown>` owners**; independent callback counter checkpoints matched.
- Final real-world 2.5 capture with 0-Engine and Scheduler probe (about 10.5 minutes): five native jobs correctly registered and attributed to `GCET25SchedulerProbe`: `frame10` 3,726 calls; `timed1` 569; `once3` 1; `cancel5` 8; `paused4` 6. Independent Lua checkpoints matched the one-shot, cancellation and paused counts, with no reported fixture registration errors. Original 0-Engine callbacks remained active.
- CapFrameX synchronization was good. Scheduler timings are already inside the measured 0-Engine owner cost and must **not** be added twice.
- This is in-game evidence for the **tested configuration**, not a promise that all third-party mods or installations are compatible.

## Installation

1. Close Cyberpunk. If another G-CET profiler version is installed, **RESTORE ORIGINAL STATE** using the previous manager first. This matters particularly for earlier temporary adaptive 0-Engine bridges.
2. Extract the release ZIP once and open **`Install_Instructions.html`**.
3. Run **`G-CET-Runtime-Profiler/G-CET-Runtime-Profiler.exe`**, select the game root, then install the profiler. For advanced Scheduler attribution, use compatible 0-Engine integration; otherwise use core-only.
4. Start and stop a manual capture with F11, or run a 120-second capture with F10. Collect results with the manager after returning from the game.
5. Restore the original CET/0-Engine installation when finished.

The ZIP contains only the installation instructions and the profiler folder. It includes the application icon and excludes obsolete documentation and temporary validation mods. The matching ZIP SHA-256 is supplied as a **separate GitHub release asset**, not loose inside the archive.

**Source branch:** [`cyberpunk-2.5-cet-1.38.1`](https://github.com/giorgioherma/G-CET_Runtime_Profiler/tree/cyberpunk-2.5-cet-1.38.1). The CET 2.31 / 1.37.1 release remains separately available.
