# MEGAMIND — one Resolver, unified CET evidence

MEGAMIND names the existing G-CET Resolver decision layer, not a third application or a GC-only optimizer.

The Profiler supplies evidence, including its integrated G-GC Tracer. The Resolver keeps ownership of generic transforms, source-proven semantic rules, source checks, idempotence and generated passes. 0-Engine framework code is NOT eligible for automatic updates.

## What is complete in this pass

The Profiler summary and Resolver handoff now use one identical G-GC Tracer evidence object, contractVersion 1.0, vocabulary MEGAMIND_CET_MULTISIGNAL_V1. It contains full mod rankings, exact per-registration positive allocation counts, separate GC-object births, bounded function samples, sampled reclamation and coverage. Original capture counters are left intact.

Native 2.16 extends function records with FunctionKey, ExecutionKind, LuaCallerFile, LuaCallerLine and CallsiteResolution. The records distinguish registered callback owner, executing function and observed Lua callsite. A single sampled callsite is not a proof of exclusive ownership; ambiguous callers stay ambiguous. These are resolved from existing sampled deep-callsites at report dump time, without more hot-path Lua debug hooks.

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

## Existing mod treatment

0-Engine: manual framework-only updates. ziggy_last_play and JoytoysOfNightCity: honor gameplay transitions and existing optimizations. GigCore and Specter: analyze dependency pair, not isolated owners. AutoLoot: investigate repeated scans/query construction and potentially better cadence, using source proof first. Wrongen: recognize existing optimizations before additional changes. No blanket GC pass should overwrite these cases.

## Strict interpretation rules

Allocation operations, positive bytes, object births, sampled reclamation and GC collector wall time are different quantities. C function attribution is not a Lua callsite; a Lua callsite is not a proven memory owner. Collector frame buckets are not caller-owned GC CPU. Sampled data is not extrapolated. Suspicious finalizer counters are explicitly UNVALIDATED. Source proof and idempotence stay mandatory for behavior changes.

## Validation of this implementation pass

Product 1.2.1 / native 2.16.0 is compiled from the pinned CET 1.37.1 source. The native rebuild is SHA-256 locked in MANIFEST.json. The independent managed CI must compile the shared Profiler + Resolver, test the exact common report schema, distinguish the C source from its Lua callsite, and preserve old capture reporting. The main Windows CI must additionally execute the full Resolver regressions and package both binaries. No in-game validation is claimed until the user performs a new representative capture.
