-- GCETFrameListeners.lua: opt-in pausable adopted CET onUpdate/onDraw handles.
-- Does not change stock EventEmitter or registration/consumption ordering for
-- existing mods. Re-enabling a paused listener appends it to dispatch order.
local M = { version = "1.0.0" }

function M.New(engine)
    local api, activeCount, pausedCount = {}, 0, 0

    function api.MakeRegistrar(owner, fallback)
        if type(engine.MakeEventRegistrar) ~= "function" then
            error("0-Engine MakeEventRegistrar required for pausable frame listeners")
        end
        local registrar = engine.MakeEventRegistrar(owner, fallback)
        return function(event, callback)
            if (event ~= "onUpdate" and event ~= "onDraw") or type(callback) ~= "function" then
                return registrar(event, callback)
            end
            local enabled, cancelled, inside = true, false, false
            local subscription, deferred = nil, nil
            local wrapper, ensureAttached, detach

            detach = function()
                if deferred then deferred = nil end
                if subscription then
                    subscription.unsubscribe()
                    subscription = nil
                end
            end
            local function deferDetach()
                if deferred then return end
                -- Never remove from EventEmitter while it is dispatching:
                -- stock EventEmitter's unsubscribe mutates its array.
                if engine.Schedule and type(engine.Schedule.NextTick) == "function" then
                    deferred = engine.Schedule.NextTick({ pause = "never",
                        id = "gcet_listener_detach" }, function()
                        deferred = nil
                        if not enabled or cancelled then detach() end
                    end)
                elseif type(engine.OnFrame) == "function" then
                    deferred = engine.OnFrame(1, function()
                        local h = deferred
                        deferred = nil
                        if h then h.unsubscribe() end
                        if not enabled or cancelled then detach() end
                    end, "GCETFrameListeners")
                end
            end
            wrapper = function(...)
                if not enabled or cancelled then return end
                inside = true
                local ok, result = pcall(callback, ...)
                inside = false
                if not enabled or cancelled then deferDetach() end
                if not ok then error(result) end
            end
            ensureAttached = function()
                if not subscription and not cancelled then
                    subscription = registrar(event, wrapper)
                    activeCount = activeCount + 1
                end
            end
            ensureAttached()
            local handle = {}
            function handle.Pause()
                if cancelled or not enabled then return end
                enabled = false
                activeCount = math.max(0, activeCount - 1)
                pausedCount = pausedCount + 1
                if inside then deferDetach()
                else detach() end
            end
            function handle.Resume()
                if cancelled or enabled then return end
                enabled = true
                pausedCount = math.max(0, pausedCount - 1)
                if deferred and deferred.Cancel then deferred.Cancel(); deferred = nil end
                ensureAttached()
            end
            function handle.SetActive(value)
                if value then handle.Resume() else handle.Pause() end
            end
            function handle.IsActive() return enabled and not cancelled end
            function handle.Cancel()
                if cancelled then return end
                if enabled then activeCount = math.max(0, activeCount - 1)
                else pausedCount = math.max(0, pausedCount - 1) end
                cancelled, enabled = true, false
                if inside then deferDetach()
                else detach() end
            end
            handle.unsubscribe = handle.Cancel
            return handle
        end
    end
    function api.GetInfo()
        return { active = activeCount, paused = pausedCount }
    end
    return api
end

return M
