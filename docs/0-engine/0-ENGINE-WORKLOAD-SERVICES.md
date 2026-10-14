# 0-Engine: additive workload services

These four modules are **0-Engine infrastructure**, distributed through generated G-CET Resolver passes. Each module is placed directly into 0-Engine/modules/ using a collision-resistant GCET-prefixed filename. No stock Scheduler, EventEmitter, ActionRouter, Health, or CET binary is replaced by this addition.

| Module file | Public API | Purpose |
| --- | --- | --- |
| GCETWorkQueue.lua | Engine.WorkQueue | Cooperative individually safe work steps across frames |
| GCETPhasePlanner.lua | Engine.PhasePlanner | Cost-aware optional maintenance deferral |
| GCETFrameListeners.lua | Engine.FrameListeners | Pausable and resumable adopted frame-event subscriptions |
| GCETStateSignals.lua | Engine.StateSignals | Existing state-transition events with cached generation and version |

These services have no dependency on CMB. 0-Engine is the required runtime. They do not create any new native onUpdate, onDraw, or Observe hooks. Services are opt-in; existing author callbacks and stock scheduling remain unchanged until a reviewed mod elects to use a service.

## 1. Cooperative work queue

    local engine = GetMod("0-Engine")
    local state = { index = 1, entries = pendingEntities }
    local handle = engine.WorkQueue.Submit({
        owner = "MyMod", id = "scan", generationScoped = true,
        maxStepsPerFrame = 4, context = state
    }, function(ctx)
        local entity = ctx.entries[ctx.index]
        if entity == nil then return true end
        inspectEntity(entity)
        ctx.index = ctx.index + 1
        return false
    end)

WorkQueue.Configure accepts budgetMs and maxOperationsPerFrame (defaults 1.5 ms, 48). Each invocation is an indivisible author-defined work unit; budgets are soft and **cannot preempt a long-running Lua function**. Submit returns a handle with Cancel, Pause, Resume, SetActive, IsActive and GetInfo. Generation-scoped tasks are cancelled on PlayerInvalidated. Erroring tasks are stopped and counted, not retried indefinitely.

## 2. Cost-aware phase planner

    local handle = engine.PhasePlanner.Every(1.0, {
        owner = "MyMod", id = "idle_refresh",
        estimatedMs = 0.5, maxDeferFrames = 6,
        slackSeconds = 0.12, generationScoped = true
    }, function(ctx)
        refreshIdleCache()
    end)

The existing 0-Engine Scheduler remains the cadence source. A dedicated opt-in lane estimates the duration of maintenance calls and may postpone a call within the explicitly allowed slack. Duplicated due ticks while pending are coalesced. Timely input consumption, quest operations, synchronous presentation and ordering-dependent callbacks **must not** use this API. An indivisible callback that takes 59 ms is still indivisible.

## 3. Pausable adopted frame listeners

    local register = engine.FrameListeners.MakeRegistrar("MyMod", registerForEvent)
    local handle = register("onUpdate", function(dt)
        -- existing callback body
    end)
    handle.Pause()
    handle.Resume()
    handle.Cancel()

Only opt-in callbacks use this mechanism. Pause detaches from the adopted event emitter at a safe Scheduler boundary. Resume appends the listener, so callback ordering can change. The original Engine.MakeEventRegistrar remains unchanged.

## 4. State-transition signals

    local handle = engine.StateSignals.Watch("inVehicle",
        function(value, oldValue, version, generation)
            -- update when mounted-state changes
        end, { immediate = true })
    handle.Cancel()

Currently supported keys: inVehicle, inCombat, inMenu, district. Events are provided by 0-Engine's existing BlackboardCache/DerivedState/visual lifecycle bus. There is **no parallel frame polling loop**. Get(key) yields the cached value, version and generation. Invalidation notifies active listeners with an unknown state and bumps generation.

## Production and adoption policy

These modules are automatically included by Resolver when it prepares the 0-Engine portion of a generated optimization pass. Updating the profiler executable alone does not mutate the live installation's stock 0-Engine module directory.

The new modules **do not automatically optimize any existing mod**. For measured 29–59 ms spikes, inspect the exact author source first. Use WorkQueue only for separable work, PhasePlanner only for delay-tolerant work, FrameListeners only when dormant state is proven, and StateSignals only when the required state transitions are already emitted. Validate before/after captures and user-visible behavior. Do not merge measurements of native callbacks and nested maintenance operations as additive time.

Generated overlays refuse to overwrite a foreign file at any new GCET-prefixed path. That collision safeguard protects stock updates and preexisting user modifications.


## Profiling and interpretation (v1.1.9)

The native Scheduler bridge now also measures opted-in workload clients:
`gcet-work`, `gcet-phase`, `gcet-listener`, and `gcet-signal`.
These timings are **nested inside their containing stock 0-Engine/CET callbacks**.
The profiler excludes `gcet-*` client counters from stock Scheduler
frame-burst accumulation and Scheduler totals, preventing artificial spikes.
Client execution durations and maxima remain visible in the existing
`CET_Runtime_Profile_Scheduler_ByJob.csv` / spikes tables.

The existing CET profiler-controls update callback additionally captures
bounded `WORKLOAD_V1` checkpoints every 15 seconds (no extra callback,
native Observer, or active-game polling service). They capture WorkQueue
backlog, activity and deferred frames; PhasePlanner backlog and deferrals;
active/paused adopted frame listeners; and StateSignals watcher count.
Reports expose `workloadServices`, and the resolver handoff carries the
same measurement-only evidence. A checkpoint is **sampled state**, not an
instantaneous peak or permission to rewrite mod code.

The LuaJIT collector instrument and workload telemetry remain completely
independent from Choom Memory Booster.

## GC-confounded callbacks and candidate selection

Real-game validation after CI #908 showed large collector buckets near multiple
slow CET callbacks. The collector is measured exactly *per native frame bucket*;
individual collector step timestamps/owners are **not** captured. A callback
which temporally overlaps a 100 ms GC frame does not automatically own that
100 ms. The report and resolver handoff therefore provide bounded
`spikeCoincidences` and conservative `potentiallyConfounded` review flags.
They are not GC-free callback durations, subtraction budgets, or authorization
to modify the offending callback.

The cooperative WorkQueue cannot preempt any single Lua call or native collector
step. It only helps when an author-proven independent operation can safely be
split into repeatable steps *without* changing deadlines, quest ordering,
synchronous UI updates or gameplay state transitions. The PhasePlanner similarly
requires explicitly delay-tolerant maintenance. Until an actual mod opts in,
zero instrumented queue activity is **expected** rather than a failed install.

When selecting new queue clients, require: (1) exact deployed source and a
separable unit-of-work boundary, (2) no observable intermediate-state hazard,
(3) stable cancellation/session semantics, and (4) before/after gameplay and
frame-time confirmation. The resolver's existing pass-only packaging remains
unchanged; no standalone framework updater is introduced.
