local DerivedState = {}
local cached = {lookPitch = 0, district = "Unknown", hasWeapon = false, isInWorkspot = false, cover={direction="None"}}
local pollThrottle = 6
local pollFrameCounter = 0
local districtFrameCounter = 0
local weaponSlotID = nil
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
local function resolveHasWeapon(player)
    local ok, result = pcall(function()
        if not weaponSlotID then
            weaponSlotID = TweakDBID.new("AttachmentSlots.WeaponRight")
        end
        local ts = Game.GetTransactionSystem()
        if not ts then return false end
        return ts:GetItemInSlot(player, weaponSlotID) ~= nil
    end)
    return ok and result or false
end
local function resolveIsInWorkspot(player)
    local ok, result = pcall(function()
        local ws = Game.GetWorkspotSystem()
        if not ws then return false end
        return ws:IsActorInWorkspot(player)
    end)
    return ok and result or false
end
local function resolveCoverDirection(player) return "None" end
function DerivedState.Update(player)
    if not player then return end
    cached.lookPitch = resolveLookPitch()
    pollFrameCounter = pollFrameCounter + 1
    if pollFrameCounter >= pollThrottle then
        pollFrameCounter = 0
        cached.hasWeapon = resolveHasWeapon(player)
        cached.isInWorkspot = resolveIsInWorkspot(player)
        local coverDir = resolveCoverDirection(player)
        cached.cover.direction = coverDir
    end
    districtFrameCounter = districtFrameCounter + 1
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
    cached.hasWeapon = false
    cached.isInWorkspot = false
    pollFrameCounter = 0
    districtFrameCounter = 0
end
return DerivedState
