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
