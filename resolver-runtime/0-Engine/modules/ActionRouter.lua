-- ActionRouter.lua - one indexed PlayerPuppet::OnAction fan-out.

local ActionRouter = { version = "0.3.0" }

local exact = {}
local wildcard = {}
local nextId = 0
local dispatchDepth = 0
local dirtyBuckets = {}
local dirtyPending = false
local Logger = nil
local Health = nil
local getFrame = function() return 0 end

local function noop() end

local function normalizeList(value)
    if value == nil then return nil end
    if type(value) == "table" then return value end
    return { value }
end

-- PASS9: PlayerPuppet::OnAction is the hottest 0-Engine path (~1.4k calls/s
-- in the PASS8 combat/chase capture). CET CName.value is the native fast path;
-- Game.NameToString() and pcall() are kept only as abnormal fallbacks.
local function actionName(action)
    if not action then return nil end

    local raw = action:GetName(action)
    if raw == nil then return nil end

    local value = raw.value
    if value ~= nil then return value end

    if Game and Game.NameToString then
        local okName, converted = pcall(Game.NameToString, raw)
        if okName and converted ~= nil then return tostring(converted) end
    end

    return tostring(raw)
end

local function actionType(action)
    if not action then return nil end

    local raw = action:GetType(action)
    if raw == nil then return nil end

    local value = raw.value
    if value ~= nil then return value end

    local result = tostring(raw)
    return result:match("([%w_]+)$") or result
end

local function typeSet(value)
    local list = normalizeList(value)
    if not list then return nil end
    local result = {}
    for _, v in ipairs(list) do result[tostring(v)] = true end
    return result
end

local function markDirty(bucket)
    if bucket then
        dirtyBuckets[bucket] = true
        dirtyPending = true
    end
end

local function compact(bucket)
    local write = 0
    for read = 1, #bucket do
        local entry = bucket[read]
        if entry and not entry.cancelled then
            write = write + 1
            bucket[write] = entry
        end
    end
    for i = write + 1, #bucket do bucket[i] = nil end
end

local function flushDirty()
    if dispatchDepth > 0 or not dirtyPending then return end
    for bucket in pairs(dirtyBuckets) do
        compact(bucket)
        dirtyBuckets[bucket] = nil
    end
    dirtyPending = false
end

local function bucketNeedsType(bucket)
    if not bucket then return false end
    local n = #bucket
    for i = 1, n do
        local entry = bucket[i]
        if entry and not entry.cancelled and entry.active and entry.needsType then
            return true
        end
    end
    return false
end

local function invokeBucket(bucket, this, action, consumer, name, kind)
    if not bucket then return end
    local n = #bucket
    for i = 1, n do
        local entry = bucket[i]
        if entry and not entry.cancelled and entry.active and (not entry.types or entry.types[kind]) then
            local ok, err
            if Health then
                ok, err = Health.Invoke(entry.owner, "action", entry.label, entry.fn, getFrame(), this, action, consumer, name, kind)
            else
                ok, err = pcall(entry.fn, this, action, consumer, name, kind)
            end
            if not ok and err ~= "quarantined" and Logger then
                Logger.Log("0-Engine", "Action callback error [" .. entry.owner .. "/" .. entry.label .. "]: " .. tostring(err), "error")
            end
        end
    end
end

function ActionRouter.Init(logger, health, frameGetter)
    Logger = logger
    Health = health
    if type(frameGetter) == "function" then getFrame = frameGetter end
end

function ActionRouter.Subscribe(config, fn, owner)
    config = config or {}
    if type(fn) ~= "function" then
        if Logger then Logger.Log("0-Engine", "SubscribeAction expected a function", "warn") end
        return { unsubscribe = noop, Cancel = noop, SetActive = noop, IsActive = function() return false end }
    end

    local actions = normalizeList(config.actions or "*")
    local buckets = {}
    local types = typeSet(config.types)
    nextId = nextId + 1
    local entry = {
        id = nextId,
        owner = owner or "unknown",
        label = config.id or config.label or ("action_" .. nextId),
        fn = fn,
        types = types,
        -- Existing subscribers keep the old contract by default: routed kind is
        -- decoded and passed even without a type filter. A subscriber that does
        -- not consume routed kind can explicitly opt out of that native call.
        needsType = types ~= nil or config.decodeType ~= false,
        active = config.active ~= false,
        cancelled = false
    }

    local seen = {}
    for _, value in ipairs(actions) do
        local name = tostring(value)
        if not seen[name] then
            seen[name] = true
            local bucket
            if name == "*" then
                bucket = wildcard
            else
                bucket = exact[name]
                if not bucket then bucket = {}; exact[name] = bucket end
            end
            bucket[#bucket + 1] = entry
            buckets[#buckets + 1] = bucket
        end
    end

    local handle = {}
    function handle.unsubscribe()
        if entry.cancelled then return end
        entry.cancelled = true
        entry.active = false
        for _, bucket in ipairs(buckets) do markDirty(bucket) end
        flushDirty()
    end
    handle.Cancel = handle.unsubscribe
    function handle.SetActive(value)
        if not entry.cancelled then entry.active = value == true end
    end
    function handle.Pause() handle.SetActive(false) end
    function handle.Resume() handle.SetActive(true) end
    function handle.IsActive() return entry.active and not entry.cancelled end
    return handle
end

function ActionRouter.Dispatch(this, action, consumer)
    local name = actionName(action)
    if not name then return end

    -- PASS8/PASS9: most PlayerPuppet actions have no 0-Engine subscribers.
    -- Resolve the CName through .value, then return before action-type decoding
    -- and Health/fan-out bookkeeping for unrelated events.
    local exactBucket = exact[name]
    if exactBucket == nil and #wildcard == 0 then return end

    local kind = nil
    if bucketNeedsType(exactBucket) or bucketNeedsType(wildcard) then
        kind = actionType(action)
    end

    dispatchDepth = dispatchDepth + 1
    invokeBucket(exactBucket, this, action, consumer, name, kind)
    invokeBucket(wildcard, this, action, consumer, name, kind)
    dispatchDepth = dispatchDepth - 1
    flushDirty()
end

function ActionRouter.GetInfo()
    local exactSubscribers = 0
    local exactActions = 0
    for _, bucket in pairs(exact) do
        exactActions = exactActions + 1
        for _, entry in ipairs(bucket) do
            if entry and not entry.cancelled then exactSubscribers = exactSubscribers + 1 end
        end
    end
    local wildcardSubscribers = 0
    for _, entry in ipairs(wildcard) do
        if entry and not entry.cancelled then wildcardSubscribers = wildcardSubscribers + 1 end
    end
    return { exactActions = exactActions, exactSubscribers = exactSubscribers, wildcardSubscribers = wildcardSubscribers }
end

return ActionRouter
