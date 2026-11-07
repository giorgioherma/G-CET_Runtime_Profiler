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

local function save()
    local names = {}
    for k in pairs(rows) do names[#names + 1] = k end
    for k in pairs(registered) do
        if not rows[k] then names[#names + 1] = k end
    end
    table.sort(names)

    -- Locate the real CET root from this mod's source path, independently
    -- of the game's working directory. Fallback to the CET process cwd.
    local target = "CET_Runtime_Profile_Validation_Counters.csv"
    if debug and debug.getinfo then
        local ok, info = pcall(debug.getinfo, 1, "S")
        if ok and info and type(info.source) == "string" then
            local source = info.source:gsub("^@", ""):gsub("\\", "/")
            local cet = source:match("^(.-)/mods/[^/]+/init%.lua$")
            if cet then target = cet .. "/" .. target end
        end
    end

    local fh, err = io.open(target, "w")
    if not fh then
        if not errors.output then
            errors.output = tostring(err)
            print(M .. ": could not write " .. target .. ": " .. tostring(err))
        end
        return
    end
    fh:write("Test,Calls,FirstSeconds,LastSeconds,Registered,Error\n")
    for _, k in ipairs(names) do
        local row = rows[k] or {calls = 0, first = -1, last = -1}
        fh:write(string.format("%s,%d,%.6f,%.6f,%s,%s\n",
            k, row.calls, row.first, row.last,
            registered[k] and "yes" or "no",
            errors[k] and ("\""..tostring(errors[k]):gsub('"', '""').."\"") or ""))
    end
    for k, v in pairs(errors) do
        if not rows[k] and not registered[k] then
            fh:write(string.format("%s,0,-1,-1,no,\"%s\"\n",
                k, tostring(v):gsub('"', '""')))
        end
    end
    fh:close()
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

registerForEvent("onUpdate", function(dt)
    hit("event.onUpdate")
    elapsed = elapsed + (tonumber(dt) or 0)
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
