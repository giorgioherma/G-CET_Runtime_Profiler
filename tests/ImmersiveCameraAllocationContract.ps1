param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ifp=$t.GetMethod('ReuseImmersiveFirstPersonCameraMath',$flags)
$itp=$t.GetMethod('ReuseImmersiveThirdPersonLiteralIds',$flags)
if(!$ifp -or !$itp){throw 'Camera owned source generators are missing'}
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
 $source=([string]$row[1]).Replace([string][char]10,$crlf)
 $out=Rewrite $row[0] $source
 if((Rewrite $row[0] $out) -cne $out){throw 'CRLF camera variant not idempotent'}
}
Write-Host 'IFP camera scratch and ITP literal cache structural source contracts PASS'
