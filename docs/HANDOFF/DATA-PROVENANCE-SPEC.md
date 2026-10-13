# Data Provenance Profiler — locked engineering specification (2026-10-13)

**Status:** approved development direction; implement in the existing G-CET Profiler and Resolver, not a separate optimizer. This document is authoritative across future chats. **First read** `PROJECT-CONTRACT.md`, `GC-RESEARCH.md`, `ACTIVE-STATE.md` and this file. Track work completed and the latest HEAD/CI in `ACTIVE-STATE.md`.

## Objective / why

The user wants to attack the *continuous stream of unnecessarily created data* in a heavily modded CET stack, not cosmetically lower GC counts or merely throttle callbacks. We already consolidated numerous callbacks; that did not eliminate work performed by their consumers. A truly useful profiler must identify **who generated data**, **what was produced**, **how frequently**, **how much**, **whether the meaningful result changes**, **whether multiple mods produce equivalent results independently**, and **whether objects are short-lived**. The **existing Resolver** then validates current mod source and generates safe generic/semantic passes. Preserve gameplay responsiveness and every existing time / burst / hitch / collector / allocation metric.

Distinct potential outcomes:
1. **Eliminate:** a source-proven unnecessary allocation, throwaway list, redundant transform or needless materialization.
2. **Reuse:** repeated equivalent data with a proven safe lifetime, freshness, invalidation and alias policy.
3. **Share:** independent consumers make semantically equivalent queries or reconstruct identical immutable data, without observable behavioral differences.

**Neither equal function names nor similar output hashes prove safe sharing.** They are candidate evidence only. Different parameters, mutation, return identity, error behavior, lifecycle, mutable native handles and ordering may make sharing invalid.

## Existing authoritative measurements (not hypotheses)

Capture `CET-20261013-000328_THE_QUESTS_AND_AUTOLOOT_RESOLVER` — 470.629s, raw CET callback wall **126.879ms/s**, native collector **12,568.538ms** (26.71ms/s, NON-ADDITIVE to CET wall); 8 completed cycles. Large apparent callback spikes overlap GC: radioExt ~575ms at frame 13470 with ~639ms collector in that frame, 0-Engine ~519ms at frame 20934 with ~574ms collector. The interrupted callback is not automatically the allocator.

G-GC positive allocator growth ~1,288.7MiB, ~10.70M allocation operations. Unscoped ID 0: **504.252MiB (39.1% byte share)**, **3,452,096 ops (~32.3%)** but only **56,947 observed GC-object births (~3.1%)**, showing the volume is not simply 504MiB of disposable GC objects. **All MiB are cumulative positive allocator growth over the capture**, not retained heap or bytes awaiting collection. It is generally steady but may burst. Native 2.15 has no function sample for ID 0. Attribution cannot be retroactively invented.

0-Engine routes ~261.58MiB, heavily shared/routed work (onUpdate ~197MiB; OnAction ~63.6MiB); route owner != allocating source owner. Leave GigCore and ImmersiveFirstPerson gameplay logic alone until provenance warrants revisiting; user clarified they are not permanently excluded. Prioritize source identification over cutting milliseconds. **No existing ms/s field may be deleted or replaced**.

CMB `0.04.2` is a **GC policy/heap observer**, not allocation provenance. The user saw LuaJIT default GC shrink around 750–850MiB; CMB auto thresholds/timer/pressure did not trigger with their conditions; manual CMB forced full GCs caused ~1s+ stalls in a different capture. CMB 0.04.2's update timer appears to divide CET seconds by 1000 (CET v1.37.1 source uses seconds), to be corrected/tested separately; do NOT change collection policy as part of provenance. Its export records final settings, not settings history.

## Architecture: one profiler, one Resolver

### A. First-phase work — safe, existing-capture analysis (managed; no new hooks)

- Emit `dataProvenanceCandidates` in **both** `CET_Summary.json` and `CET_Resolver_Input.json` and append an HTML section; all existing fields remain **unchanged**.
- Analyze G-GC full registration counters, bounded `GGC_Functions` samples, source file / line, function name, execution kind and caller-resolution quality. Separate `=[C]` native functions, missing caller, legacy caller-not-recorded, ambiguous or named Lua source.
- Rank *investigative* repeated-producer candidates by sampled operations and bytes; identify **source-location overlap across distinct mod registrations** without asserting shared result semantics. Retain registration and owner identities and evidence counts; identify same-source-**file+line+function**, *not* just a method name. No mod logic rewritten based on these correlations.
- Clearly distinguish observed operations, sampled calls, allocation bytes from those sampled functions, and registration-wide totals. **Never extrapolate bounded samples to the entire mod or sum sampled bytes as exact total savings.**
- If source-level callsite is missing, state `NOT_MEASURED`. No invented read frequency or value-change percentage.

### B. Real producer provenance — experimental native instrumentation (capture-only)

- **Pinned production native 2.15.0 must stay byte-identical.** Experimental native 2.16 previously crashed on world load before F11; separate branch/artifact and identical-save boot test are mandatory before any new native build is promoted.
- At allocation time distinguish VM execution state / active CET registration / no registration (ID 0) / registration overflow / Lua execution context when safely available, including mod initialization outside callback. Do not infer owners from previous or neighboring callbacks.
- Implement **bounded**, capture-gated, allocation-free-on-hot-path aggregation or stratified sampling with coverage and dropped-counts. Avoid global per-allocation stack walking, Lua debug hook hijacking, unlimited strings, keeping native pointers, and extra `collectgarbage` calls.
- Retain sampled object *creation* owner, type, approximate size/time and *reclamation* time/type only while observed. Sampling coverage and collisions explicit. A heap drop is not proof an individual sampled owner created the unreachable objects; GC trigger site != producer.
- Optional capture bins (e.g. 1s): growth bytes/ops/births by registration/context; separate GC pause/phase timeline and sampled reclamation. Bounded buffer and overhead control.

### C. Frequent reads vs rare changes — selected in-situ scalar probes

- Instrument only **existing** selected source-proven getter callsites (never issue extra getter calls). Observe bounded scalar results or safe immutable snapshots without keeping game-native entities alive. Avoid nontrivial object hashing on hot paths.
- Record provider, stable source fingerprint (mod/file/line/receiver/query semantics), input parameters as relevant, calls/s, distinct logical transitions, unchanged-result share, exception/invalid observations, changing context and coverage.
- Clear state at safe game/session/load/quest generation boundaries; do not incorrectly conflate identity with equality, asynchronous queries, player or world changes. An unchanged scalar does not prove all reads are redundant.
- This evidence remains **review only** until source-based proof establishes allowable freshness/invalidation and all behavioral contracts.

### D. Cross-mod duplication and candidate analyzer

- Fingerprint **provider + input/parameter semantics + receiver/lifecycle**; record origin and consumer separately; distinguish per-callback/same-frame/longer reuse domains.
- Group only clearly comparable observations; report mod set, source callsites, sample coverage, repeats/s, stable-result share, bytes/ops where measured, possible equivalent-source overlap, and confidence `OBSERVED_SOURCE_OVERLAP`, `PARAMETERS_CONFIRMED`, `RESULT_EQUIVALENT_SAMPLED` or `INSUFFICIENT_EVIDENCE`.
- Emit hypotheses for eliminations, same-consumer reuse, or shared read-only snapshots. *Never* automatically route live data through a global cache based on statistical equivalence.
- Resolve mutable container escape/aliasing, native handle lifetime, ordering and error differences, invalidation/reset and consumers before generating a pass; fail closed. Source changes must be version-tolerant, composable, idempotent and use original/current files.
- Measure saved source allocations and *retained memory* after the patch, plus GC stalls, not just fewer allocation ops. Caches can increase live heap and collector costs.

## Output contract

Additive HTML/JSON with:
- `provenanceVersion`, `stage`, `measurementMode`, `nativeVersion`, `coverage`, `dropped`, `timingMetricsPreserved`, `rewriteAuthorized=false`.
- Per candidate `producer` (mod/registration/Lua source/function/line/callsite resolution), `consumers`, `operation`, `inputSignatureStatus`, `resultEquivalenceStatus`, `calls`, `sampledAllocOps`, `sampledPositiveBytes`, `observedBirths`, `sampledReclaim`, `scope`, `confidence`, `evidenceLimitations`, `sourceVersion`.
- Top repeated producers / cross-owner source overlap / native-C unclear caller / owner-0 attribution gaps, with distinct measured and unknown fields. No source ownership or savings guesses.

**No automatic optimization from G-GC scores.** Candidate evidence is passed to existing Resolver source inspection/admission only.

## Acceptance and safety gates

1. Additive-only: no removal, modification or loss of existing CPU/ms/s, inclusive/exclusive, scheduler/burst, GC, allocation, markers, capture, output or gameplay behavior.
2. Fixture with two owners executing the same Lua-source identity gives a *candidate*, not a proven duplicated result. Same function name at different source locations does **not** merge; native `=[C]` with no callsite does not merge across mods. Unscoped ID 0 remains unknown.
3. Legacy native 2.15 reports absence of callsite/value telemetry truthfully, never inserts guessed numbers. Known-only and missing-data fixtures; zero denominators; repeated runs deterministic; bounded output.
4. New native capture must have capture-off baseline; same-save load; profile on/off; no hook conflict; memory and frametime overhead; pause/resume; startup crash reproduction resolved before production. Do not promote product merely because CI is green.
5. Resolver passes must test original version and variants, idempotence, existing partially applied work, dependency/ordering/quest/UI behavior and matched before-after gameplay; safe failure falls back to author code.

## Work order

**Immediate:** commit this spec in `docs/HANDOFF`, link from README/NEXT-CHAT, implement A and tests, run CI. Then implement B as separately gated native experiment, followed by targeted C and D. After each milestone update `ACTIVE-STATE.md`, change log and tests; link exact commit/CI. Do not lose this specification between sessions.
