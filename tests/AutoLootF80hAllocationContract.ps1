param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inj = $assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$f80h = $inj.GetMethod('OptimizeF80hAnchorRegistration', $flags)
$loot = $inj.GetMethod('ReuseAutoLootProtectedOwnerGetter', $flags)
if (!$f80h -or !$loot) { throw 'Missing source-owned f80h or AutoLoot GC optimization' }
function Rewrite($method, [string]$source) {
    return [string]$method.Invoke($null, [object[]]@($source))
}
function MustRefuse($method, [string]$source, [string]$reason) {
    $refused = $false
    try { $null = Rewrite $method $source }
    catch {
        if ($_.Exception.InnerException -is [InvalidOperationException]) { $refused = $true }
        else { throw }
    }
    if (!$refused) { throw "Unsafe source drift accepted: $reason" }
}
$anchorOriginal = @'
local ANCHORS = {}
local OUTER_MARGIN = 0.5
local function distSq(p, a)
  local dx,dy,dz = p.x-a.pos.x,p.y-a.pos.y,p.z-a.pos.z
  return dx*dx+dy*dy+dz*dz
end
local pending = {}
local function tryRegisterNPC(self)
  local id = self:GetEntityID()
  local app = safeGetAppearance(self)
  local p = safeGetPos(self)
if not p then
  pendingAppearance[id.hash] = { npc = self, since = 0 }
  return
end
  for _, anchor in ipairs(ANCHORS) do
    local rule = resolveAllowRule(app, anchor)
    if rule then
      local at   = isAtAnchor(self, anchor)
      local near = (not at) and isNearAnchor(self, anchor)
      if at then
        startTrackingNPC(self, anchor, rule)
        return
      end
      if near then
        pending[id.hash] = { npc = self, anchor = anchor, since = 0 }
        return
      end
    end
  end
end
'@
$anchor = Rewrite $f80h $anchorOriginal
foreach ($needle in @(
    'local __gcetAnchorDistSquared = distSq(p, anchor)',
    'local __gcetOuterRadius = anchor.radius + OUTER_MARGIN',
    'local rule = __gcetAnchorDistSquared <= __gcetOuterRadius * __gcetOuterRadius',
    'and resolveAllowRule(app, anchor) or nil',
    'local at = __gcetAnchorDistSquared <= anchor.radius * anchor.radius',
    'local near = (not at) and __gcetAnchorDistSquared <= __gcetOuterRadius * __gcetOuterRadius',
    'startTrackingNPC(self, anchor, rule)',
    'pending[id.hash] = { npc = self, anchor = anchor, since = 0 }'
)) {
    if (!$anchor.Contains($needle)) { throw "f80h position/rule order lost $needle" }
}
if ($anchor.Contains('isAtAnchor(self, anchor)') -or
    $anchor.Contains('isNearAnchor(self, anchor)') -or
    (Rewrite $f80h $anchor) -cne $anchor) {
    throw 'f80h still fetches position twice per anchor or source is not idempotent'
}
MustRefuse $f80h ($anchor.Replace('local __gcetAnchorDistSquared = distSq(p, anchor)',
                                'local __gcetAnchorDistSquared = 0')) 'f80h partial position stage'
MustRefuse $f80h ($anchorOriginal.Replace('local p = safeGetPos(self)', 'local p = nil')) 'f80h missing native position'
MustRefuse $f80h ($anchorOriginal.Replace('local at   = isAtAnchor(self, anchor)',
                                          'local at = customLocation(self)')) 'f80h changed anchor matching'
if ((Rewrite $f80h 'local noNpcRegistration = true') -cne 'local noNpcRegistration = true') {
    throw 'f80h changed older limited source with no anchor loop'
}
# The author GetOwner() error is swallowed; a named private function
# executes the same colon call under pcall and never caches native handles.
$lootOriginal = @'
local logic = {}
function logic.lootInRange(range)
  local gameObjOwner = nil
  result = false
  pcall(function() gameObjOwner = gameObj:GetOwner() end)
  if gameObjOwner then result, items = transactionSystem:GetItemList(gameObjOwner) end
  local takeIt = true
end
'@
$out = Rewrite $loot $lootOriginal
foreach ($needle in @(
    'local function __gcetAutoLootProtectedOwnerV1(obj)',
    'return obj:GetOwner()',
    'local __gcetOwnerOk, __gcetOwnerValue = pcall(__gcetAutoLootProtectedOwnerV1, gameObj)',
    'if __gcetOwnerOk then gameObjOwner = __gcetOwnerValue end',
    'if gameObjOwner then result, items = transactionSystem:GetItemList(gameObjOwner) end'
)) {
    if (!$out.Contains($needle)) { throw "AutoLoot protected native owner semantics lost $needle" }
}
if ($out.Contains('pcall(function() gameObjOwner = gameObj:GetOwner() end)') -or
    (Rewrite $loot $out) -cne $out) {
    throw 'AutoLoot still allocates protected closure per candidate or is not idempotent'
}
MustRefuse $loot ($out.Replace('pcall(__gcetAutoLootProtectedOwnerV1, gameObj)',
                               'pcall(function() return gameObj:GetOwner() end)')) 'AutoLoot partial helper'
MustRefuse $loot ($lootOriginal.Replace('function logic.lootInRange(range)',
                                        'function logic.scanSomethingElse(range)')) 'AutoLoot unknown owner'
if ((Rewrite $loot 'local olderLoot = true') -cne 'local olderLoot = true') {
    throw 'AutoLoot altered unrelated source'
}
$eol=([string][char]13)+([string][char]10)
foreach ($pair in @(@($f80h,$anchorOriginal), @($loot,$lootOriginal))) {
    $windows = ([string]$pair[1]).Replace($eol, [string][char]10).Replace([string][char]10, $eol)
    $generated = Rewrite $pair[0] $windows
    if ((Rewrite $pair[0] $generated) -cne $generated) { throw 'CRLF input is not idempotent' }
}
Write-Host 'f80h NPC-local anchor matching and AutoLoot protected owner GC source contract PASS'
