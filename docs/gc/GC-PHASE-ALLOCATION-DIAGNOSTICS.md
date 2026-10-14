# LuaJIT GC phase and allocator diagnostics (capture-only)

Native v2.14.0 must be hash-locked and verified for all three diagnostics
before the normal Windows package can be published. This intentionally
rejects stale profiler binaries after source-only changes.

GCET_COLLECTOR_PROBE_V2 records internal GC states, slow collector operations
and positive allocation growth. It never changes collector parameters,
scheduling, game hooks or any mod behavior.

## New runtime CSVs

- CET_Runtime_Profile_GC_Phases.csv — total measured duration and invocation
  count for pause, propagate, atomic, sweepstring, sweep, finalize.
- CET_Runtime_Profile_GC_SlowSteps.csv — bounded 2,048 slow operations of at
  least 2 ms, with capture-relative timing, state transitions, native thread,
  active CET registration, heap before/after, threshold, debt, pause, stepmul.
  Includes a dropped-event count.
- CET_Runtime_Profile_Lua_Allocations.csv — total positive LuaJIT allocator
  growth at lj_mem_realloc and lj_mem_newgco attributed to the innermost
  measured CET callback. Nested instrumented callbacks are excluded.

## Interpretation and restrictions

A registration executing during a GC pause is a trigger site; that callback
did not necessarily produce the collected garbage. Allocation growth is
not net heap growth. It excludes native allocations and unmeasured LuaJIT
allocator paths, and time outside instrumented CET callbacks is unattributed.

All timings are wall time and can include Windows thread preemption.
Phase duration is nested in existing collector totals, callback timings,
and possibly 0-Engine Scheduler time: never add these values or subtract
them from an individual callback.

Instrumentation is capture-gated. The runtime does not call GC explicitly,
does not change pause or multiplier, does not install new game event hooks
and does not authorize automatic source rewrites. The collector probe uses
a bounded static event buffer; it does not allocate during GC events.

Repeat similar gameplay scenes for comparable captures. Inspect phase
bottlenecks and allocation-heavy callbacks, then inspect the mod's actual
source before electing StateSignals, cache invalidation, WorkQueue or
PhasePlanner. GC settings remain at the pre-existing values until
measurements justify controlled experiments.
