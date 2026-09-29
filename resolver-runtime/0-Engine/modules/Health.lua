-- Health.lua - low-overhead attribution, error containment and bounded spike history.

local Health = { version = "0.1.0" }

local owners = {}
local spikes = {}
local maxSpikes = 64
local spikeThresholdMs = 5.0
local sampleEvery = 64

local function clockMs()
    return os.clock() * 1000.0
end

local function key(owner, kind, label)
    return tostring(owner or "unknown") .. "\31" .. tostring(kind or "callback") .. "\31" .. tostring(label or "unnamed")
end

local function metric(owner, kind, label)
    local k = key(owner, kind, label)
    local m = owners[k]
    if not m then
        m = {
            owner = owner or "unknown", kind = kind or "callback", label = label or "unnamed",
            calls = 0, samples = 0, totalMs = 0, maxMs = 0,
            errors = 0, consecutiveErrors = 0, quarantined = false,
            lastError = nil, lastErrorFrame = 0, skipped = 0
        }
        owners[k] = m
    end
    return m
end

function Health.Configure(config)
    config = config or {}
    if type(config.sampleEvery) == "number" and config.sampleEvery >= 1 then
        sampleEvery = math.floor(config.sampleEvery)
    end
    if type(config.spikeThresholdMs) == "number" and config.spikeThresholdMs >= 0 then
        spikeThresholdMs = config.spikeThresholdMs
    end
    if type(config.maxSpikes) == "number" and config.maxSpikes >= 1 then
        maxSpikes = math.floor(config.maxSpikes)
    end
end

function Health.RecordSkip(owner, kind, label)
    local m = metric(owner, kind, label)
    m.skipped = m.skipped + 1
end

function Health.Invoke(owner, kind, label, fn, frame, ...)
    local m = metric(owner, kind, label)
    if m.quarantined then
        m.skipped = m.skipped + 1
        return false, "quarantined"
    end

    m.calls = m.calls + 1
    local sampled = m.calls == 1 or (m.calls % sampleEvery) == 0
    local started = sampled and clockMs() or 0
    local ok, result = pcall(fn, ...)

    if sampled then
        local elapsed = clockMs() - started
        m.samples = m.samples + 1
        m.totalMs = m.totalMs + elapsed
        if elapsed > m.maxMs then m.maxMs = elapsed end
        if elapsed >= spikeThresholdMs then
            spikes[#spikes + 1] = {
                owner = m.owner, kind = m.kind, label = m.label,
                elapsedMs = elapsed, frame = frame or 0
            }
            if #spikes > maxSpikes then table.remove(spikes, 1) end
        end
    end

    if ok then
        m.consecutiveErrors = 0
        return true, result
    end

    m.errors = m.errors + 1
    m.consecutiveErrors = m.consecutiveErrors + 1
    m.lastError = tostring(result)
    m.lastErrorFrame = frame or 0
    if m.consecutiveErrors >= 3 then m.quarantined = true end
    return false, result
end

function Health.ResetQuarantine(owner, kind, label)
    local m = owners[key(owner, kind, label)]
    if not m then return false end
    m.quarantined = false
    m.consecutiveErrors = 0
    return true
end

function Health.GetSummary()
    local result = {}
    for _, m in pairs(owners) do
        result[#result + 1] = {
            owner = m.owner, kind = m.kind, label = m.label,
            calls = m.calls, samples = m.samples,
            averageMs = m.samples > 0 and (m.totalMs / m.samples) or 0,
            maxMs = m.maxMs, errors = m.errors,
            quarantined = m.quarantined, skipped = m.skipped,
            lastError = m.lastError, lastErrorFrame = m.lastErrorFrame
        }
    end
    table.sort(result, function(a, b)
        if a.owner == b.owner then
            if a.kind == b.kind then return a.label < b.label end
            return a.kind < b.kind
        end
        return a.owner < b.owner
    end)
    return result
end

function Health.GetSpikes()
    local result = {}
    for i = 1, #spikes do result[i] = spikes[i] end
    return result
end

return Health
