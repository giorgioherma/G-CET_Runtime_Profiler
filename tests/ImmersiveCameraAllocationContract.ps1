param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ifp=$t.GetMethod('ReuseImmersiveFirstPersonCameraMath',$flags)
$itp=$t.GetMethod('ReuseImmersiveThirdPersonLiteralIds',$flags)
$latches=$t.GetMethod('ReuseImmersiveThirdPersonLatches',$flags)
if(!$ifp -or !$itp -or !$latches){throw 'Camera owned source generators are missing'}
function Rewrite($method,[string]$s){return [string]$method.Invoke($null,[object[]]@($s))}
function Refuse($method,[string]$s,[string]$reason){
  try{$null=Rewrite $method $s}
  catch {if($_.Exception.InnerException -is [InvalidOperationException]){return};throw}
  throw ('Unknown camera source drift accepted: '+$reason)
}
$originalFp=@'
function CameraCore.EvaluateBody(nativePitch, crouching)
    local progress, forward, vertical, pitchCorrection = 0, 0, 0, 0
    return {
        progress = progress,
        lateral = 0.0,
        forward = forward,
        vertical = vertical,
        pitch = pitchCorrection,
    }
end

local function getFreeLookLimits(hasWeapon)
    return 0
end
local function composeAndWrite(fpp,context)
    local body = CameraCore.EvaluateBody(
        bodyDriverPitch,
        context.crouching
    )
    local freeOffset = {
        yaw = 0.0,
        pitch = 0.0,
        sideProgress = 0.0,
        backProgress = 0.0,
        lateral = 0.0,
        forward = 0.0,
        vertical = 0.0,
        roll = 0.0,
    }
    if runtime.mode == MODE.FREELOOK then
        freeOffset = CameraCore.EvaluateFreeLook(runtime.freeYaw, 0, false)
    end
    return body, freeOffset
end
'@
$fp=Rewrite $ifp $originalFp
foreach($v in @('function CameraCore.EvaluateBody(nativePitch, crouching, out)',
  'out = out or {}','return out','local __gcetIfpBodyScratchV1 = {}',
  'local __gcetIfpNeutralFreeV1 = {','local freeOffset = __gcetIfpNeutralFreeV1',
  'context.crouching,','freeOffset = CameraCore.EvaluateFreeLook(')){
  if(!$fp.Contains($v)){throw ('IFP lost original camera behavior or scratch '+$v)}
}
if((Rewrite $ifp $fp) -cne $fp){throw 'IFP source is not idempotent'}
Refuse $ifp ($originalFp.Replace('        context.crouching','        context.newCrouch')) 'IFP changed input'
Refuse $ifp ($fp.Replace('out = out or {}','out = {}')) 'IFP private scratch reallocated'
Refuse $ifp ($fp.Replace('local freeOffset = __gcetIfpNeutralFreeV1',
                         'local freeOffset = {}')) 'IFP neutral allocation restored'

$originalTp=@'
local function makeTweakDBID(value)
  if TweakDBID and TweakDBID.new then
    return TweakDBID.new(value)
  end

  return value
end
local function check(player)
  local head = makeTweakDBID('AttachmentSlots.TppHead')
  local right = makeTweakDBID("AttachmentSlots.WeaponRight")
  local status = makeTweakDBID('BaseStatusEffect.JohnnySicknessLow')
  local dynamic = makeTweakDBID(player.dynamicSlot)
  return head, right, status, dynamic
end
'@
$tp=Rewrite $itp $originalTp
foreach($v in @('local __gcetItppLiteralKeySetV1 = {',
 'local __gcetItppLiteralTweakIdsV1 = {}',
 'local id = __gcetItppLiteralTweakIdsV1[value]',
 '__gcetItppLiteralTweakIdsV1[value] = id',
 'if __gcetItppLiteralKeySetV1[value] then',
 'return TweakDBID.new(value)','return value',
 'makeTweakDBID(player.dynamicSlot)')){
 if(!$tp.Contains($v)){throw ('ITP source cache incomplete: '+$v)}
}
if((Rewrite $itp $tp) -cne $tp){throw 'ITP source is not idempotent'}
if((Rewrite $itp 'local olderCamera = true') -cne 'local olderCamera = true'){
 throw 'Unsupported legacy ITP camera modified'
}
Refuse $itp ($originalTp.Replace('return TweakDBID.new(value)','return TweakDBID.Create(value)')) 'ITP changed native constructor'
Refuse $itp ($tp.Replace('local id = __gcetItppLiteralTweakIdsV1[value]','local id = nil')) 'ITP partial cache'
Refuse $itp ($tp.Replace('__gcetItppLiteralTweakIdsV1[value] = id','__gcetItppLiteralTweakIdsV1[value] = nil')) 'ITP lost cache ownership'
$crlf=([string][char]13)+([string][char]10)
foreach($row in @(@($ifp,$originalFp),@($itp,$originalTp))){
 $source=([string]$row[1]).Replace(([string][char]13)+([string][char]10),[string][char]10).Replace([string][char]10,$crlf)
 $out=Rewrite $row[0] $source
 if((Rewrite $row[0] $out) -cne $out){throw 'CRLF camera variant not idempotent'}
}
$latchOriginal=@'
local function autoUpdateLatches(raw)
  local autoPerspective = state.autoPerspective
  local cfg = config.autoPerspective
  local now = autoPerspective.clock
  local enterDelay = { vehicle = 0, scene = 0, combat = 0, interaction = cfg.interactionEnterDelay or 0.10 }
  local exitDelay = {
    vehicle = cfg.vehicleExitDelay or 0.40,
    scene = cfg.sceneExitDelay or 0.50,
    combat = cfg.combatExitDelay or 1.25,
    interaction = cfg.interactionExitDelay or 1.00
  }
  if autoPerspective.combatWeaponOnly then
    exitDelay.combat = cfg.combatHolsterExitDelay or 0.30
  end
  if autoPerspective.sceneQuickExit then
    exitDelay.scene = cfg.sceneQuickExitDelay or 0.30
  end
  for _, name in ipairs(mod.autoPriority) do
    local active = raw[name]
    if active then
      if not autoPerspective.latched[name] and (now - autoPerspective.rawSince[name]) >= enterDelay[name] then
        autoPerspective.latched[name] = true
      end
    else
      if autoPerspective.latched[name] and (now - autoPerspective.rawClearSince[name]) >= exitDelay[name] then
        autoPerspective.latched[name] = false
      end
    end
  end
end
'@
$latchOutput=Rewrite $latches $latchOriginal
foreach($v in @('local __gcetItppLatchTablesV1 = {',
  'local enterDelay = __gcetItppLatchTablesV1.enter',
  'local exitDelay = __gcetItppLatchTablesV1.exit',
  'enterDelay.interaction = cfg.interactionEnterDelay or 0.10',
  'exitDelay.vehicle = cfg.vehicleExitDelay or 0.40',
  'exitDelay.combat = cfg.combatExitDelay or 1.25',
  'exitDelay.interaction = cfg.interactionExitDelay or 1.00',
  'exitDelay.combat = cfg.combatHolsterExitDelay or 0.30',
  'exitDelay.scene = cfg.sceneQuickExitDelay or 0.30')){
  if(!$latchOutput.Contains($v)){throw ('TPP dynamic latch delay lost: '+$v)}
}
if($latchOutput.Contains('local enterDelay = { vehicle = 0') -or
   $latchOutput.Contains('local exitDelay = {')){
  throw 'ITP latch still creates temporary tables per sample'
}
if((Rewrite $latches $latchOutput) -cne $latchOutput){throw 'ITP latch source is not idempotent'}
Refuse $latches ($latchOriginal.Replace('cfg.sceneExitDelay or 0.50','cfg.sceneExitDelay or 0.70')) 'ITP changed scene delay'
Refuse $latches ($latchOutput.Replace('exitDelay.interaction = cfg.interactionExitDelay or 1.00',
                                     'exitDelay.interaction = 1.00')) 'ITP no longer tracks settings'
$crlfLatch=$latchOriginal.Replace(([string][char]13)+([string][char]10),[string][char]10).Replace([string][char]10,([string][char]13)+([string][char]10))
$cachedLatch=Rewrite $latches $crlfLatch
if((Rewrite $latches $cachedLatch) -cne $cachedLatch){throw 'ITP CRLF latch not idempotent'}
$combined=Rewrite $latches ($originalTp + $latchOriginal)
$combined=Rewrite $itp $combined
if(!$combined.Contains('local __gcetItppLatchTablesV1 = {') -or
   !$combined.Contains('local __gcetItppLiteralTweakIdsV1 = {}')){
  throw 'ITP combined existing generator does not compose independent allocation stages'
}

Write-Host 'IFP scratch and ITP literal-ID/latch-table garbage contracts PASS'
