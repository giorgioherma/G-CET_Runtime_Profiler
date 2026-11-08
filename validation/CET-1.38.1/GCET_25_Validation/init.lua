-- Temporary comparison fixture for the public G-CET profiler / CET 1.38.1.
-- No persistent hooks outside this mod, no changes to game state.
-- The mod counts Lua callback bodies independently of the native profiler.
local M = "GCET_25_Validation"
local rows = {}
local elapsed = 0
local lastFlush = 0
local lateHook = false
local registered = {}
local errors = {}
local started = false

local function hit(key)
    local row = rows[key]
    if not row then
        row = { calls = 0, first = -1, last = -1 }
        rows[key] = row
    end
    row.calls = row.calls + 1
    if row.first == -1 then row.first = elapsed end
    row.last = elapsed
end

-- CET's mod sandbox has no io.open or debug.getinfo. Use the profiler's
-- existing native markers for independent, capture-scoped checkpoints.
-- This is a test mod only; it is not installed by the standalone manager.
local snapshotCount = 0
local MAX_SNAPSHOTS = 90

local function count(name)
    return rows[name] and rows[name].calls or 0
end

local function save()
    if snapshotCount >= MAX_SNAPSHOTS or
        type(CETProfilerMark) ~= "function" or
        type(CETProfilerIsRunning) ~= "function" then return end
    local runningOK, running = pcall(CETProfilerIsRunning)
    if not runningOK or not running then return end

    local errorCount = 0
    for _ in pairs(errors) do errorCount = errorCount + 1 end
    -- The first marker after F11 is the baseline. Subtract successive labels
    -- to verify native owner/method counters independently of registration age.
    local label = string.format(
        "GCET25_V1_BEFORE_%d_LATE_%d_AFTER_%d_OVERRIDE_%d_UPDATE_%d_DRAW_%d_ERRORS_%d",
        count("Observe.PlayerPuppet::OnAction"),
        count("ObserveDelayed.PlayerPuppet::OnAction"),
        count("ObserveAfter.PlayerPuppet::OnAction"),
        count("Override.PlayerPuppet::IsInCombat"),
        count("event.onUpdate"),
        count("event.onDraw"),
        errorCount)
    local ok, result = pcall(CETProfilerMark, label)
    if ok and not (type(result) == "string" and
        result:find("buffer full", 1, true)) then
        snapshotCount = snapshotCount + 1
    end
end

local function safeHook(name, api, className, method, cb)
    local ok, err = pcall(api, className, method, cb)
    if ok then
        registered[name] = true
    else
        errors[name] = tostring(err)
        print(M .. " registration failure: " .. name .. ": " .. tostring(err))
    end
end

registerForEvent("onInit", function()
    hit("event.onInit")
    if started then return end
    started = true

    safeHook("Observe.PlayerPuppet::OnAction", Observe,
        "PlayerPuppet", "OnAction", function()
            hit("Observe.PlayerPuppet::OnAction")
        end)

    safeHook("ObserveAfter.PlayerPuppet::OnAction", ObserveAfter,
        "PlayerPuppet", "OnAction", function()
            hit("ObserveAfter.PlayerPuppet::OnAction")
        end)

    -- Pass-through only: do not alter the original boolean or game state.
    safeHook("Override.PlayerPuppet::IsInCombat", Override,
        "PlayerPuppet", "IsInCombat", function(self, wrappedMethod, ...)
            hit("Override.PlayerPuppet::IsInCombat")
            return wrappedMethod(...)
        end)
    save()
end)

local wasCapturing = false
registerForEvent("onUpdate", function(dt)
    hit("event.onUpdate")
    elapsed = elapsed + (tonumber(dt) or 0)
    local capturing = false
    if type(CETProfilerIsRunning) == "function" then
        local ok, value = pcall(CETProfilerIsRunning)
        capturing = ok and value == true
    end
    if capturing and not wasCapturing then
        snapshotCount = 0
        save()
    end
    wasCapturing = capturing
    if elapsed >= 3 and not lateHook then
        lateHook = true
        -- Source-level proof for CET 1.38.1's try_lock + pending queue.
        safeHook("ObserveDelayed.PlayerPuppet::OnAction", Observe,
            "PlayerPuppet", "OnAction", function()
                hit("ObserveDelayed.PlayerPuppet::OnAction")
            end)
    end
    if elapsed - lastFlush >= 5 then
        lastFlush = elapsed
        save()
    end
end)

registerForEvent("onDraw", function()
    hit("event.onDraw")
end)

registerForEvent("onShutdown", function()
    hit("event.onShutdown")
    save()
end)

print(M .. ": independent callback counter installed")
