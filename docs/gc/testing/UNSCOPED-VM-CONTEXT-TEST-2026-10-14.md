# Test procedure — unscoped VM-context G-GC instrumentation (2026-10-14)

**Build under test:** Profiler **v1.2.7**, [main CI #38497725639](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38497725639); separate Resolver artifact same version. Native CI [#38496930001](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38496930001); embedded DLL SHA-256 `72cb0f09827fcd86a07e6e510145de6907c7eb9f2e0d68f5381333857b381976`, manifest native label `2.15.1-scalar-vm-context`.

## What changed

- `native-profiler-build/v2.11.0/Patch-LuaJIT-GC-Source.ps1` groups **unattributed owner-0 positive allocation growth** by already-available LuaJIT `g->vmstate` at existing allocator instrumentation sites. These are VM execution contexts (INTERP/C/GC/EXIT/RECORD/OPT/ASM/UNKNOWN), not guaranteed allocating source, mod or producer.
- A bounded 1-second capture history (0–1199 sec; overflow grouped as 1200) accompanies the summary, using the capture offset on resume. Fixed-size buffer, no runtime heap allocations per event and no extra Lua calls. Only unscoped capture-active allocation growth records get the added clock/context work.
- The native `CETRuntimeProfiler.h` writes additive CSVs:
  - `CET_Runtime_Profile_GGC_UnscopedContexts.csv`: state, positive bytes, alloc ops;
  - `CET_Runtime_Profile_GGC_UnscopedTimeline.csv`: capture second/state, positive bytes, alloc ops.
- Both are explicitly in the manifest and match the existing archive wildcard. All previous GGC/GC/deep/timing CSV schemas preserved. No automatic Resolver pass may be generated from these values.

## User test — keep installed mods and save constant

1. Download **`G-CET-Runtime-Profiler-v1.2.7` from main CI #38497725639** (not the native-only artifact). Use the normal Profiler manager's install/restore workflow. Do not manually replace `cyber_engine_tweaks.asi`.
2. Launch the game on the **same save and mod stack**. Before starting a capture, move/play briefly and confirm ordinary menus, inputs and world loading. If the game crashes **before** capture, retain logs/dump and roll back via manager.
3. Run a normal ~3–5 minute capture: include some idle time, some movement, and optionally looting; stop with the configured toggle. Collect normally through the Profiler app.
4. The archive should contain both new CSVs and prior full files. Verify **sum of `PositiveBytes` and `AllocationOperations` in `GGC_UnscopedContexts` equals the `<unattributed>` row of `GGC_Registrations`** (registration ID 0); sum the timeline likewise. This is a crucial correctness requirement.
5. Compare second-by-second owner-zero peaks with collector slow steps. Timestamp overlap alone **does not assign producer or cause**. Compare whether capture remains stable and whether CapFrameX FPS and memory overhead look reasonable against the prior control run.
6. Test controls and menus for a while **after capture stop** (given the earlier unrelated, unproven psychosis UI incident). Report any new game crash or bad transitions, with CET/RED4ext logs.
7. Send the single collected ZIP. We will inspect the new classification, timeline totals, cross-CSV reconciliation and possible priority source targets before any further native instrumentation or Resolver rule.

## Limitations

- Native and combined CI passed; **no in-game runtime proof** yet.
- The bins use a low-overhead millisecond wall clock and nominal capture offset; boundary precision is on the order of the timer and pause/resume timing, not nanosecond accuracy. Overflow means all time at/after 1200 sec.
- LuaJIT VM-state is not stack attribution and VM_C is **not necessarily mod native code**. Owner-0 remains unattributed in the authoritative GGC registration/ranking tables.
- Dynamic gameplay, GC phase timing and other mod callback frequencies may differ between captures; no unsupported before/after savings claims.
