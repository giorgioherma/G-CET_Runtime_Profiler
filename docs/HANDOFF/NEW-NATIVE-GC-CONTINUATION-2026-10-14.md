# New native G-GC continuation capture — 2026-10-14

## Source and boundaries

User upload: `CET-20261014-013105_NEW_NATIVE_GC_CONTINUATION.zip` (3:05.908, UTC 2026-10-13 23:31:05 start). Source of truth: archive `CET_Summary.json`, `CET_Resolver_Input.json`, `Data/Runtime/*.csv`, companion CapFrameX. Native DLL in v1.2.7 package: manifest `2.15.1-scalar`, SHA `89d848b775fb67bc5e56043053f07f315eb414d46940eb7d343f9eaea3ab095b`, built in native-only #38473605694; main #38476578740. Do **not** confuse these tool identities with an observed runtime binary hash inside the capture; archive itself does not cryptographically attest the loaded DLL.

## Integrity: successful capture, no evidence of post-stop issue in-window

- Exactly one `CAPTURE_START` at 0 ms and `PAUSE` at 185908.0194 ms; no `RESUME`; markers end at PAUSE. Unlike earlier 698.202 s archive with disputed stop boundary, this capture ends correctly.
- 139 active owners, 3,169,129 calls, 158.487917 exclusive ms/s, 1,553 G-GC registration rows. 2,055 deep samples, 36,610 deep callsite rows, 43,450 line rows, 1,349 G-GC sampled function rows. All main runtime/deep/GGC/GC phase/timeline/scheduler files emitted. Drop counts for timeline/spikes/deep samples, lines, callsites, GC slow steps = 0.
- Paired CapFrameX **GOOD** alignment: 185.847 s versus CET 185.908 s (difference 61 ms), 9,848 frames, avg 52.99 FPS, p95 24.795 ms, p99 36.186 ms, maximum 445.069 ms.
- `scalarChangeObservations.status=NOT_CAPTURED`: scalar API is compiled but **no actual in-situ Lua callsites were instrumented**. Absence of CSV is expected and NOT a native failure.
- The player reported **normal gameplay and menu controls for a while AFTER capture**, then an unusual transition out of **Wannabe Edgerunner Cyberpsychosis** followed by nonresponsive Esc/inventory menus and a stall entering the bed sleep-hour selector. Symptoms postdate end of capture. Nothing in this ZIP records the event; **do not attribute it to the profiler merely because it was installed**.

## GC pressure measured (do not infer causality)

- Gross positive LuaJIT allocation growth 626.689908 MiB / 185.908 s (~3.371 MiB/s), 4,910,854 positive allocation operations, 661,740 observed GC object births, 13,976 new strings. NOT resident heap.
- Unattributed scope 243.958874 MiB = 38.928%, ~1.312 MiB/s, 1,670,904 allocation operations. Scope zero is not a mod.
- Automatic internal GC 6,188.073 ms total (33.28567 ms/s), 433,038 incremental calls, 4 completed cycles, no recorded full collection. 15 GC operations over 2 ms; major atomic→sweepstring steps 187.452 ms at t144.610s, 164.568 at t41.816, 135.173 at t69.258, 113.491 at t158.492. Max **frame collector bucket** 409.591 ms at t157.862s (nested collector work), near 445.069 ms worst frame.
- Paired callback spikes during some steps can include GC time; e.g. `0-Engine::PlayerPuppet::OnAction` 187.515ms coincident with 187ms GC operation; `ImmersiveFirstPerson::onUpdate` 138.141ms coincident with ~135ms collector; `AutoLoot::ShowRoleMappinsByTask` 114.540ms coincident with 113ms collector. **Trigger site != source of allocations, and CPU timings may nest GC.**
- G-GC raw global finalizer count 3,532,064 is explicitly `UNVALIDATED_DO_NOT_USE_FOR_AUTOMATIC_DECISIONS`; do not use as GC-object proof.
- Native GGC sampled caller resolution still `NOT_RECORDED_LEGACY` and C-function samples remain mostly unassigned. New scalar API does NOT solve missing native origin attribution without opt-in probes.

## Most useful before/after clue: NPC Outfit Manager

Compare earlier `CET-20261013-031413_CONTAIN_THE_DATA` (698.202 s, different gameplay; **not controlled A/B**):

| Measure | Old capture | New capture | Indication |
| --- | ---: | ---: | --- |
| NPC Outfit Manager `onUpdate` exclusive ms/s | 3.037171 | 0.188525 | ~93.8% lower |
| NPC Outfit Manager entire mod exclusive ms/s | 3.241968 | 0.352804 | ~89.1% lower |
| NPC Outfit Manager G-GC allocation ops/s | 803.323 | 29.875 | ~96.3% lower |
| NPC Outfit Manager G-GC gross allocated MiB/s | 0.061041 | 0.002043 | ~96.7% lower |

`onUpdate` still called every frame (50.08 vs 52.99/s). Very strong **consistency evidence** for successful no-auto-outfit-rules idle work removal, not proof that all gameplay cases/saved looks or deferred work remain correct. Do not claim measured global GC win in uncontrolled scene.

## Why total CPU cannot be compared causally

Old 698 s CET 138.964 ms/s vs new 186 s CET 158.488 ms/s; old 50.10 avg FPS vs new 52.99; old GC collector 28.407 ms/s vs new 33.286. No matched scene/state/mod behavior.

Main changed work cadence: `Minimap Widgets` `MinimapStealthMappinController::UpdateAboveBelowVerticalRelation` 46.60 -> 170.12 calls/s, which alone changes its mod cost 3.577 -> 8.686 ms/s; `CyberTrials` `BaseMappinBaseController::UpdateRootState` 61.20 -> 188.75 calls/s, mod 3.179 -> 7.465 ms/s; `0-Engine` `PlayerPuppet::OnAction` 1228.07 -> 1440.07 calls/s, mod 34.763 -> 41.063 ms/s. These changed workloads dominate apparent total CET difference. `AutoLoot` spikes and GC trigger overlap are present but not evidence of regression.

Current-archive and older baseline **mod-owner name sets identical (139 owners)**; this does NOT prove no orphan pass files, and does NOT show their contents. `Wannabe Edgerunner` not listed as a live CET profiling owner; it is primarily redscript/RED4ext and cannot be exonerated or implicated from CET callback CSV alone. Other similarly named CET mods `Enhanced Edgerunner`, `More Cyberpsychosis`, `repeatable_cyberpsychos` appear; names are not proof of identity.

## User's merged Pass ZIP / missing-mod audit

User had combined all previous passes into one overlay and is re-generating passes to remove stale modifications targeting mods that may no longer be installed. **Neither the merged pass ZIP nor an authoritative current installed-mod tree is included in this capture.** The earlier uploaded `mods(3).zip` is an earlier CET source snapshot, not guaranteed to be the tree actually installed for the post-capture symptom. To audit, compare game-relative files in the combined overlay against a current user-owned live mod inventory; filter unknown/removed mod files, inspect 0-Engine callbacks/registration state, preserve manual current edits and re-run Resolver from current original/live source. Do not bulk delete based only on names in the CSV or earlier archive.

## Psychosis context (external evidence, separate from capture)

Wannabe Edgerunner's Nexus documentation describes a **Post-Psychosis state** continuing until bed rest; historical v2.0.4 changelog says callback use of time dilation flag was fixed specifically to improve behavior with game pausing/opening menus, and historical changes mention fixes around sleep/psychosis transition. 2026 Nexus comments report stuck psychosis state in some installations. These make the user's explanation plausible but do **not** prove this instance. The appropriate first test is same save, normal gameplay+menus, then psychosis transition and sleep both **without starting any capture**, with unchanged mod files and existing native installed; separately compare the previous known-good native if reproducible. Do not remove a stateful REDscript mod mid-episode without reviewing its uninstall/reset instructions. `https://www.nexusmods.com/cyberpunk2077/mods/5646`.

## Recommended next steps

1. Preserve this capture as a valid new-native baseline; **do not bump v1.2.7** without user request/milestone approval.
2. Validate gameplay recovery and reproduce the cyberpsychosis exit and bed UI independently of capture. Record exact mod/version/state, CET/RED4ext/redscript logs and any dump.
3. Rebuild merged passes from currently present mods; compare source paths and eliminate any stale overlay files only after verifying ownership/author versions.
4. Controlled NPC Outfit Manager idle/edit/auto-outfit test and before/after same-scene capture; if stable, this is a meaningful optimization win.
5. Prioritize GC origin proof for heavy collector steps and missing unattributed producer coverage; optional scalar probe needs actual source callsites.
