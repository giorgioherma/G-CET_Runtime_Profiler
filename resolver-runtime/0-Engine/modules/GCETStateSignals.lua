-- GCETStateSignals.lua: reuse 0-Engine's existing state-transition emitters.
-- No polling or additional Observe hooks. Subscribers opt into specific keys.
local M = { version = "1.0.0" }

function M.New(engine)
    local api, keys, epoch = {}, {}, 0
    local catalog = {
        inVehicle = { events = { "MountedToVehicleChanged" },
            extract = function(value) return value == true end },
        inCombat = { events = { "CombatStateChanged" },
            extract = function(value) return value == true end },
        inMenu = { events = { "MenuOpen", "MenuClose" },
            extract = function(_, event) return event == "MenuOpen" end },
        district = { events = { "DistrictChanged" },
            extract = function(value) return value end }
    }

    local function removeListener(bucket, item)
        for i = #bucket.listeners, 1, -1 do
            if bucket.listeners[i] == item then
                table.remove(bucket.listeners, i)
                break
            end
        end
    end

    local function notify(bucket, newValue)
        if bucket.known and bucket.value == newValue then return end
        local oldValue = bucket.known and bucket.value or nil
        bucket.value, bucket.known = newValue, true
        bucket.version = bucket.version + 1
        local snapshot = {}
        for i = 1, #bucket.listeners do snapshot[i] = bucket.listeners[i] end
        for i = 1, #snapshot do
            local item = snapshot[i]
            if item.active then
                pcall(item.fn, newValue, oldValue, bucket.version, epoch)
            end
        end
    end

    local function readCurrent(key)
        if type(engine.GetState) ~= "function" then return nil end
        local ok, state = pcall(engine.GetState)
        if not ok or not state then return nil end
        if key == "district" then
            return state.derived and state.derived.district
        end
        return state[key]
    end

    local function ensureAttached(key, bucket)
        if #bucket.sources > 0 then return end
        if type(engine.Subscribe) ~= "function" then
            error("0-Engine Subscribe required for StateSignals")
        end
        local spec = catalog[key]
        for _, event in ipairs(spec.events) do
            local currentEvent = event
            bucket.sources[#bucket.sources + 1] =
                engine.Subscribe(event, function(value)
                    notify(bucket, spec.extract(value, currentEvent))
                end, "GCETStateSignals")
        end
    end

    function api.Watch(key, fn, options)
        options = options or {}
        if not catalog[key] then error("Unsupported StateSignals key: " .. tostring(key)) end
        if type(fn) ~= "function" then error("StateSignals.Watch requires function") end
        local bucket = keys[key]
        if not bucket then
            bucket = { value = readCurrent(key), known = true,
                version = 0, listeners = {}, sources = {} }
            keys[key] = bucket
        end
        local item = { fn = fn, active = options.active ~= false }
        bucket.listeners[#bucket.listeners + 1] = item
        ensureAttached(key, bucket)
        if options.immediate == true and item.active then
            pcall(fn, bucket.value, nil, bucket.version, epoch)
        end
        local handle = {}
        function handle.Pause() item.active = false end
        function handle.Resume() item.active = true end
        function handle.SetActive(value) item.active = value == true end
        function handle.IsActive() return item.active end
        function handle.Cancel()
            if not item.active and item.cancelled then return end
            item.active, item.cancelled = false, true
            removeListener(bucket, item)
            -- Retain the one event-driven observer and cache. Removing it
            -- from inside EventEmitter:trigger would mutate the active array.
            -- This does not add any per-frame work.
        end
        handle.unsubscribe = handle.Cancel
        return handle
    end

    function api.Get(key)
        local bucket = keys[key]
        if bucket then return bucket.value, bucket.version, epoch end
        return readCurrent(key), 0, epoch
    end

    function api.GetInfo()
        local active, watcherCount = 0, 0
        for _, bucket in pairs(keys) do
            if #bucket.listeners > 0 then active = active + 1 end
            watcherCount = watcherCount + #bucket.listeners
        end
        return { keys = active, watchers = watcherCount, generation = epoch }
    end

    function api.Invalidate()
        epoch = epoch + 1
        for key, bucket in pairs(keys) do
            bucket.value, bucket.known = nil, false
            bucket.version = bucket.version + 1
            local snapshot = {}
            for i = 1, #bucket.listeners do snapshot[i] = bucket.listeners[i] end
            for _, item in ipairs(snapshot) do
                if item.active then pcall(item.fn, nil, nil, bucket.version, epoch) end
            end
        end
    end

    if type(engine.Subscribe) == "function" then
        engine.Subscribe("PlayerInvalidated", api.Invalidate, "GCETStateSignals")
    end
    return api
end

return M
