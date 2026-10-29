# Specter / InfiniteGigs / GigCore — Phase 3 source and GC audit (2026-10-29)

## Scope and source of truth

User-supplied untouched distributions:

- `Infinity Gig SPECTER V1.5 34167 1.5 2026-10-10T20-38Z psSjsvIEM.zip` (archive root identifies itself as `Infinite Gig Specter 1.15.13`; do not infer a different mod version or treat its internal dev naming as release identity).
- `GigCore 0.7.3 30988 0.7.3 2026-10-12T17-09Z IieOig6tl(1).zip`.

Comparison capture: `CET-20261029-204841_F80F_AUTOLOOT.zip`, **120.001s CET and CapFrameX synchronized**; see [COMBAT-POLICE-120](COMBAT-POLICE-120-BASELINE.md). No newly generated Specter code was measured in this capture.

## What it actually does

**Specter is not one infinite polling quest.** Its `InfiniteGigs` CET owner runs the **authored campaign** (M-series objectives, dialogue/voice/captions, actors, checkpoints, save/load restoration, phone dispatch, audio, HUD, route gates and native objective coordination). `SpecterInterceptorClean` is the distinct **repeatable procedural Interceptor**: a phone SCAN can dispatch linked jobs involving 2–4 sites, assassination targets, an actor-placement bank, distance/geometry retries, deferred/cancel safety, saved step identity and payment. Those features cannot safely sleep solely because V is far from one static point.

Separate CET owners in the supplied ZIP include `SpecterCampaignProgression` (0.5-second session-ready campaign unlocking/offer checks), `SpecterM11RecoveryDev` (its historical folder/save namespace remains `m11` but source labels it canonical **M10 recovery** after the M9 sequence), `SpecterNessaIntro`, and optional development/test sites (`SpecterB34S7Dev`). Per-owner lifecycle and fallback must remain independent.

**GigCore** is a general-purpose framework, not Specter's private helper. It supplies a redscript-backed persistent metric/event bus, per-job state, session and context lifecycle, timers, map pins/markers, NPC/actor services, economy, phone, vehicle and damage helpers, and subscriptions used by other gig mods. Its shared per-frame work must not be disabled according to Specter mission location or state. Existing G-CET GigCore source passes already remove idle unsubscribed vehicle-damage health reads and repeated phone/pause closures. Preserve those.

## Exact 120-second attribution (not retained garbage)

| Callback (specific registration window) | Positive bytes | Allocator requests | GC object births |
|---|---:|---:|---:|
| `SpecterM11RecoveryDev onUpdate` | 5,099,150 B (~4.86 MiB) | 77,130 | **38,565** |
| `InfiniteGigs onUpdate` | 2,307,809 B (~2.20 MiB) | 36,979 | **31,793** |
| `GigCore onUpdate` | 14,512,567 B (~13.84 MiB) | 101,782 | **41,566** |

These are callback-window allocator measures, not proof of retained owner heaps. They do not explain whole-stack unscoped allocations or the GC pause by themselves; native stage and job measurements overlap and cannot be summed.

Deep sampled callsites for Specter recovery include a protected `phoneBus` / `Game.GetScriptableSystemsContainer` chain and recurring persistent metric/bus reads. Its archived original `SpecterM11RecoveryDev/init.lua` has `Music.update(delta, phoneBus(), ...)` **before** the inactive check, plus `metricGet()` calling `refresh()` (fetches `GetMod("InfiniteGigs")` every call) and wrapping `core.State.get`/`core.Bus.getMetric` in freshly created protected closures. Existing G-CET stage already short-circuited second/third story metrics when earlier prerequisites fail, reused the immutable music contract, and guarded two HUD updates. The new work extends **that same generator**.

## New source-owned Specter recovery producer changes

`src/G.CETProfiler.Core/Services/ModPasses/SpecterRecoveryPrerequisites.cs`:

1. **Per-frame metricGet owner and closure reduction** (`__gcetSpecterMetricCoreOnlyV1`): resolve only missing GigCore for that metric invocation using `pcall(GetMod, "GigCore")`; call `pcall(core.State.get, ns,key,default)` or `pcall(core.Bus.getMetric,ns,key,default)` with the SAME original arguments and fallbacks. Do not call `refresh()` (which also refreshes the irrelevant `InfiniteGigs` owner) while only reading a story metric. The authored **M9-to-recovery check remains each frame**, and the explicit `refresh()` in the eligible `seq==9, rainbowDone==1, recoveryDone==0` path is retained *before* examining the owner and the authored 0.75-second start clock. Persistence writes, session loading and active stage refreshes stay unchanged.
2. **Idle music native bus reduction** (`__gcetSpecterIdleMusicBusV1`): only when the actual authored `specter_mission_music.lua` structurally owns `M.owner`, `M.pending`, `flush(bus)`, and its inactive stop path, pass `phoneBus()` to every-frame `Music.update` if `active or Music.owner ~= nil or next(Music.pending) ~= nil`. Otherwise pass nil with the exact same `Music.update` call and all arguments. An active music owner or pending stop still retries with the native bus; no audio wake, session stop, timer, or stage transition is removed. Compact unsupported music modules are untouched rather than guessing their state.
3. **Conditional structural completion**, not marker-only: `IsSpecterRecoveryColdGcComplete` proves original prerequisite gating, real HUD branches, immutable contract, and—for source versions having them—the completed metric and music states. This is wired through the existing semantic library `completionVerifier = SPECTER_RECOVERY_COLD_GC_V3`. An older completed G-CET layout is eligible for missing work; incomplete/changed active mission or music source must fail closed. No second fixup pass, extra scheduler, native DLL, LuaJIT, or GigCore change.

## Validation contracts and boundaries

- Dedicated `tests/SpecterRecoveryColdGcContract.ps1`: original-shaped 1.5 source with full metric and music, idempotence, incomplete marker refusal, unchanged M9 activation, pending music stop, CRLF, and older compact module fallback.
- Existing `tests/GigCoreSpecterSourceContract.ps1`: original 1.4.1 and 1.5 synthetic variants, one-pass regeneration and no duplicate emission.
- `.github/workflows/original-cet-source.yml`: private full untouched-stack generation validates actual emitted Specter source still checks M9 in the current frame and contains native bus/metric reductions only when the author source supports them.
- Full Windows package CI is a **source and build gate**. Do NOT claim game-scale performance improvement before a subsequent matched F10 COMBAT-POLICE-120 capture, nor claim gameplay equivalence of all authored campaign/Interceptor/phone/VO/save states until user plays those cases.

**Next separate targets after this recovery pass:** `InfiniteGigs` remaining UI/phone and story service calls (careful active contract, music/caption, campaign restore, Interceptor waits); GigCore shared core attribution and native boundary; then NPC Outfit Manager, ELM_ImmersiveWay, ziggy_last_play. The 0-Engine internal attribution pass, unscoped allocator tracing, and adaptive GC helper belong to their authoritative instrumentation/GC design, not to a Specter mod semantic.
