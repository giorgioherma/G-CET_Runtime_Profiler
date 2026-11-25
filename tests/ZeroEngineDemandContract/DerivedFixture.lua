local DerivedState = {}
local cached = {lookPitch = 0, district = "Unknown"}
local function resolveLookPitchInner()
    local camSys = Game.GetCameraSystem()
    if not camSys then return 0 end
    local fwd = camSys:GetActiveCameraForward()
    if not fwd then return 0 end
    local clamped = math.max(-1.0, math.min(1.0, fwd.z))
    return math.deg(math.asin(clamped))
end
local function resolveLookPitch()
    local ok, result = pcall(resolveLookPitchInner)
    return ok and result or 0
end
function DerivedState.Update(player)
    if not player then return end
    cached.lookPitch = resolveLookPitch()
    cached.district = "Watson"
end
function DerivedState.SetLoading(loading)
    cached.isLoading = loading == true
end
function DerivedState.Get()
    return cached
end
function DerivedState.Reset()
    cached.lookPitch = 0
    cached.district = "Unknown"
end
return DerivedState
