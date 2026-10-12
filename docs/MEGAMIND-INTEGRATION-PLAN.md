# MEGAMIND — one Resolver, unified CET evidence

MEGAMIND names the existing G-CET Resolver decision layer, not a third application or a GC-only optimizer.

The Profiler supplies evidence, including its integrated G-GC Tracer. The Resolver keeps ownership of generic transforms, source-proven semantic rules, source checks, idempotence and generated passes. 0-Engine framework code is NOT eligible for automatic updates.

## What is complete in this pass

The Profiler summary and Resolver handoff now use one identical G-GC Tracer evidence object, contractVersion 1.0, vocabulary MEGAMIND_CET_MULTISIGNAL_V1. It contains full mod rankings, exact per-registration positive allocation counts, separate GC-object births, bounded function samples, sampled reclamation and coverage. Original capture counters are left intact.

Native 2.16 experimentally extended function records with FunctionKey, ExecutionKind, LuaCallerFile, LuaCallerLine and CallsiteResolution, but it was rolled back from distribution because world-load crashed before F11. Product 1.2.2 ships exact known-good native 2.15.0. Managed evidence parsing supports the extended fields only if present. The records distinguish registered callback owner, executing function and observed Lua callsite. A single sampled callsite is not a proof of exclusive ownership; ambiguous callers stay ambiguous. These are resolved from existing sampled deep-callsites at report dump time, without more hot-path Lua debug hooks.

Resolver ingestion accepts v1.0 only with the explicit metric semantics and no-rewrite policy. It requires exact registration ID and matching owner, rejects duplicate IDs, owner mismatches and incompatible data, and never changes AUTO or semantic admission from G-GC evidence. Legacy captures remain supported.

The first real capture exposed 35.8% of allocated bytes unscoped and 92.2% of sampled function allocation operations in native/C frames. These remain measurement limitations, not solved by assigning unowned work to a nearby mod. The observed 10.89 million finalizer callbacks versus zero matched sampled finalizers is unvalidated; finalizer counters are withheld from optimization decisions until an independent instrumented check.

## Next gates toward full MEGAMIND integration

### M1. Measurement validation and overhead

Validate finalizer invocations against controlled userdata/cdata LuaJIT fixtures. Split unscoped allocations by demonstrably identifiable contexts if this can be done without per-allocation stack walks: initialization, collector/finalizer, native bridge, and unknown. Keep uncertain activity unowned. Verify birth counts, collision/sampling drop rates, captures with/without pause, and distinguish new object births from positive allocation requests. Benchmark the profiler impact on CPU and frametime against an untraced baseline.

Exit: representative native micro-tests and runtime baselines pass, no invented ownership.

### M2. Cross-signal correlation without new AUTO rules

Join callback CPU, burst/hitch, cadence/dormancy, shared Scheduler and allocator evidence on exact registration ID, then on stable source/callsite identity where supported. Cross-mod function execution (e.g., GigCore functions invoked by Specter) must never be silently renamed as ownership. Preserve observation windows, sampling coverage and confidence. Produce one review candidate with multiple measured dimensions, not a GC-only parallel pipeline.

Exit: recorded review opportunities become easier to explain, while all existing generative dispositions and source-proof requirements remain unchanged.

### M3. Source-proven optimization families

Reuse existing generic and semantic transformation architecture. Begin with redundant read-only queries and temporary object construction only when escape, identity, invalidation and side-effect preconditions can be proven. Where accurate evidence suggests a better cadence/dormancy fix, still rely on existing source-proven rules. Never eliminate gameplay features to improve a metric.

Exit: deterministic, idempotent source transform, source-version checks, functional regression tests and rollback; measured improvement from matched before/after captures.

### M4. MEGAMIND unified planner

Present one decision combining CPU work, burst risk, unnecessary polling, allocation pressure, evidence quality, already-applied optimizations and safe transformations. Only rules with established semantic proof can be AUTO; unresolved opportunities remain review. Validate post-install performance and behavior. There is no new third application.

Exit: pass legacy and new-capture regression matrix across diverse real stacks, including a manual 0-Engine configuration.

## October 12: measured GC follow-through (package 1.2.2)

The first full experimental native 2.16 capture ran for about 443 seconds. It measured
roughly 1,457.8 MiB of positive allocator growth, 12.18 million positive allocation
requests and 2.21 million GC object births. About 559.65 MiB (38.39%) was
unattributed. The collector's 7.99-million raw global finalizer calls remain
unvalidated and are not eligible as an optimization input. Native 2.16's isolated
successful runtime test does not replace the pinned stable 2.15 product DLL.

Managed report improvements were implemented in CI #939–#942: typed compact timeline
storage, isolation of post-capture report generation from the GUI's long-lived
working set, live-process working-set sampling, and a 200,000-row stress fixture.
These CI contracts are not a measurement of post-collection idle memory on the
user's 443-second capture. A fresh actual capture is still needed.

For expanded callsite evidence the common G-GC handoff now distinguishes
SINGLE_OBSERVED_LUA_CALLSITE, MULTIPLE_CALLSITES, NOT_OBSERVED,
NOT_RECORDED_LEGACY and other malformed/unsupported states. The native/C sampled
operation buckets are distinct from full attribution. A single caller is linked
only when its observed Lua file is present and its line is positive. Even valid
links do not prove the exact allocation instruction.

The first additional GC-motivated **real Resolver optimization pass** is
`autoloot-world-pin-allocation-scan`. Exact current-source proof on
`AutoLoot/modules/logic.lua` permits replacing the per-call
`closeMappins` candidate table with scalar first/nearest tracking.
The author mappin query, filter order, single-candidate behavior, tie policy,
range check, distance outputs and existing update cadence are retained. This
is a separate onUpdate optimization; it does not replace the existing
AutoLoot onAction action-routing semantic pass. The callback had about
1.054 ms/s measured exclusive CPU workload and an isolated approximately
184 ms spike. A **rule-local 0.5 ms/s floor** is justified only by the
mechanical allocation removal and current-source proof; GC metrics never
grant admission by themselves. The generated pass is idempotent and tested
against a synthetic source-shaped fixture. A new, matched in-game capture
must confirm the net performance benefit and gameplay behavior.

## Existing mod treatment

0-Engine: manual framework-only updates. ziggy_last_play and JoytoysOfNightCity: honor gameplay transitions and existing optimizations. GigCore and Specter: analyze dependency pair, not isolated owners. AutoLoot: investigate repeated scans/query construction and potentially better cadence, using source proof first. Wrongen: recognize existing optimizations before additional changes. No blanket GC pass should overwrite these cases.

## Strict interpretation rules

Allocation operations, positive bytes, object births, sampled reclamation and GC collector wall time are different quantities. C function attribution is not a Lua callsite; a Lua callsite is not a proven memory owner. Collector frame buckets are not caller-owned GC CPU. Sampled data is not extrapolated. Suspicious finalizer counters are explicitly UNVALIDATED. Source proof and idempotence stay mandatory for behavior changes.

## Validation of this implementation pass

Product 1.2.2 intentionally restores the byte-for-byte tested native 2.15.0 DLL from #925 while retaining managed MEGAMIND v1.0. The native rebuild is SHA-256 locked in MANIFEST.json. The independent managed CI must compile the shared Profiler + Resolver, test the exact common report schema, distinguish the C source from its Lua callsite, and preserve old capture reporting. The main Windows CI must additionally execute the full Resolver regressions and package both binaries. No in-game validation is claimed until the user performs a new representative capture.

## Regression isolation: native 2.16 world-load crash

The user confirmed that the identical save and mod stack load under native 2.15 (#925) and without a profiler, but crash during world load with native 2.16 (#934), before the F11 capture trigger. The minidump shows access violation 0xC0000005 in Cyberpunk2077.exe+0x53F9D4 and does not independently attribute the fault to a specific profiler instruction. The 2.16 source delta was limited to report-dump-time C/Lua callsite attribution; this does NOT prove that block executed at load. Treat this as a native binary regression until isolated with symbols, startup tracing, or a controlled rebuild.

Product 1.2.2 retains managed evidence and Resolver joins but reverts native source and the distribution payload to 2.15. The native DLL SHA-256 is pinned to c62d57f50b1fd792ae989cb78fb54cee8d82b0370abdb51ea01617c2ddcfa23e. Callsite fields in old-format CSVs remain NOT_RECORDED_LEGACY, not inferred.

Release gate: identical save must load before F11. No native extensions are allowed back into the stable package until that gate passes. Green compilation alone is insufficient.
