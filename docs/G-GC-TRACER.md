# G-GC Tracer — integrated G-CET Runtime Profiler feature

> **Startup-safety rollback (1.2.2):** Product 1.2.2 deliberately ships the exact
> native 2.15.0 DLL from successful #925 after native 2.16.0 was associated
> with a reproducible pre-F11 world-load crash on the same stack/save. The
> 2.16 native callsite CSV extension is **not deployed**. Managed MEGAMIND
> interop accepts legacy tracer function rows with unresolved Lua caller fields.
> See [MEGAMIND plan](MEGAMIND-INTEGRATION-PLAN.md).


Product 1.2.0, native 2.15.0. This is not a standalone application.
The Resolver consumes review-only evidence and never authorizes a rewrite from GC
metrics alone. Collector policy and mod functionality remain unchanged.

## Two-stage measurement

1. Every LuaJIT positive allocation request is counted by size and frequency
   with the current CET registration as its creator. ID 0 is UNATTRIBUTED;
   IDs beyond 32766 use a separate OVERFLOW bucket, not another author's name.
2. Object births via lj_mem_newgco and interned strings via lj_str_alloc are
   counted separately from allocator growth requests.
3. Starting after five capture seconds, every five seconds the profiler picks
   the five heaviest mods by allocated bytes, then up to five more distinct
   high-frequency mods by allocation operation count. All other registrations
   retain broad counters. Selection is not by callback CPU time.
4. Sampled objects are associated with creation owner, timestamp and size
   through a bounded external pointer table, not altered LuaJIT GC headers.
   Ordinary sweeping and actual finalizer calls are observed without forcing
   GC. Unfocused sampling is one in 512 births; focused sampling is one in 32.
   Collisions are dropped and reported. A paused capture clears live pointer
   tags to avoid stale address reuse on resume.
5. Existing sampled Lua debug call/return hooks measure per-function positive
   allocation deltas, excluding child function deltas. The resulting function
   totals are samples, not complete global measurements. JIT inlining and
   uninstrumented callbacks are explicitly outside source-level attribution.

## Output

- CET_Runtime_Profile_GGC_Registrations.csv — all measured registrations:
  allocation bytes, allocation operations, GC object and string births, sampled
  reclamation and lifetime evidence.
- CET_Runtime_Profile_GGC_Rankings.csv — per-mod top-five/top-five ranking,
  dynamic focus state, object counts, sampled reclaim by type, actual global
  finalizer invocation count.
- CET_Runtime_Profile_GGC_Functions.csv — sampled source-level function
  allocations, by registration and source location when available.

Data is carried to CET_Report.html, CET_Summary.json and
CET_Resolver_Input.json with no-automatic-rewrite restrictions.

## Critical distinctions

Allocation requests are not the same as GC object births. Bytes allocated are
not bytes retained, and a GC sweep-state operation is not equivalent to freeing
one object. A callback in which GC happens is not automatically the producer of
the garbage. Sampled reclamation cannot be extrapolated to all allocations
without validated statistical assumptions. Tracking the creator does not imply
knowing the reference responsible for retaining the object.

Capture-only overhead still exists. Evaluate relative trends and keep
untraced gameplay as the frametime control before patching source.
