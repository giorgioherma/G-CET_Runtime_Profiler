param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$dll=Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'
$asm=[Reflection.Assembly]::LoadFrom($dll)
$native=$asm.GetType('GCETRuntimeProfiler.Core.Services.CompiledNativeLocations',$true)
$counter=$native.GetMethod('CyberStuntsFixedJumpCount',[Reflection.BindingFlags]'Static,NonPublic')
$compiler=$native.GetMethod('Compile',[Reflection.BindingFlags]'Static,NonPublic')
$injectionType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$apply=$injectionType.GetMethod('ApplyCyberStuntsNativeLocation',[Reflection.BindingFlags]'Static,NonPublic')
$contextType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$constructor=$contextType.GetConstructors([Reflection.BindingFlags]'Instance,Public,NonPublic') | Select-Object -First 1
if(!$counter -or !$compiler -or !$apply -or !$constructor){throw 'CyberStunts native source contract API missing'}
$root=Join-Path $env:RUNNER_TEMP 'gcet-cyberstunts-native-contract'
$owner='CyberStunts-StuntJumps-CET'
$modPath=Join-Path $root $owner
New-Item -ItemType Directory -Force $modPath | Out-Null
$file=Join-Path $modPath 'init.lua'
$source=@'
local jumps = {
    { jumpName = "first", jumpPos = { x = 100.0, y = 200.0, z = 25.0 }, landPos = { x = 110, y = 220, z = 21 } },
    { jumpName = "second", jumpPos = { z = 70.0, x = 400.0, y = 500.0 }, landPos = { z = 68, y = 510, x = 410 } },
}
local userJumps = {}
local isJumping=false
local wasVehicleAirborne=false
local launchDetectionRadius = 5.0
function SaveUserJumps()
 local file=io.open('User/UserJumps.json','w')
end
function LoadUserJumps() end
local function DetectJumpLaunch(position) return nil end
registerForEvent("onInit", function()
    LoadUserJumps()
    for j, k in ipairs(userJumps) do
        table.insert(jumps,{ jumpName=k.jumpName,jumpPos=k.jumpPos,landPos=k.landPos })
    end
    ObserveAfter("inkMenusState", "OpenMenu", function(this, menuName, userData) end)
    Observe("DriveEvents", "OnUpdate", function(this)
        local player=Game.GetPlayer()
        local vehicle=player.mountedVehicle
        local isAirborne = vehicle:IsInAir()
        if not isJumping and isAirborne and not wasVehicleAirborne then
         local detectedJump=DetectJumpLaunch(playerPos)
        end
        wasVehicleAirborne=isAirborne
    end)
end)
registerForEvent("onUpdate", function(dt)
 if not isJumping then return end
end)
registerForEvent("onOverlayOpen", function() end)
registerForEvent("onDraw", function() DrawMenu() end)
'@
[IO.File]::WriteAllText($file,$source,[Text.UTF8Encoding]::new($false))
if([int]$counter.Invoke($null,[object[]]@($source)) -ne 2){throw 'Fixed jump extraction failed'}
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
$gcas=$compiler.Invoke($null,[object[]]@([string]$root,$staged))
$counts=$native.GetProperty('OwnerCounts',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($gcas)
if([int]$counts[10039] -ne 2){throw 'GCAS owner 10039 missing exact fixed count'}
$bytes=$native.GetProperty('Bytes',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($gcas)
if([Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'GCAS' -or
   [BitConverter]::ToUInt32($bytes,8) -ne 2){throw 'Compiled native GCAS header/site count incorrect'}
$bad=$source.Replace('jumpPos = { z = 70.0, x = 400.0, y = 500.0 }','jumpPos = playerPosition')
if([int]$counter.Invoke($null,[object[]]@($bad)) -ne 0){throw 'Unproven dynamic literal was compiled'}
function TrySource([string]$sourceText) {
  [IO.File]::WriteAllText($file,$sourceText,[Text.UTF8Encoding]::new($false))
  $staging=[Collections.Generic.Dictionary[string,byte[]]]::new()
  $ctx=$constructor.Invoke([object[]]@([string]$root,[string]$owner,
    'native-location-cyberstunts',$staging))
  try {$result=$apply.Invoke($null,[object[]]@($ctx))}
  catch {return @{Applied=$false;Error=$_.Exception.InnerException.Message;Output=''}}
  $changes=@($contextType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  $sourceRow=@($changes|Where-Object{$_.RelativeFile -eq "$owner/init.lua"})[0]
  $workerRow=@($changes|Where-Object{$_.RelativeFile -eq "$owner/GCET_Semantic/cyberstunts_native_worker.lua"})[0]
  if($result.Applied -and (!$sourceRow -or !$workerRow)){
    throw "Pristine CyberStunts must emit the source and external worker together"
  }
  $output=if($sourceRow){[Text.Encoding]::UTF8.GetString($sourceRow.Bytes)}else{''}
  $worker=if($workerRow){[Text.Encoding]::UTF8.GetString($workerRow.Bytes)}else{''}
  return @{Applied=$result.Applied;Output=$output;Worker=$worker}
}
$result=TrySource $source
if(!$result.Applied){throw "Original CyberStunts source not staged: $($result.Error)"}
if(!$result.Worker.StartsWith('-- G-CET semantic coordinator:native-location-cyberstunts'+[string][char]10) -or
   !$result.Worker.Contains('host:attachCompiled(10039, count,')){
  throw 'CyberStunts external coordinator lost reviewed native owner or source ownership'
}
foreach($required in @('G-CET native location:cyberstunts-v1',
 'GCET_Semantic/cyberstunts_native_worker','__gcetStuntsNativeFar==true',
 '__gcetStuntsUserEdited=true','if wasVehicleAirborne==nil then wasVehicleAirborne=isAirborne end',
 'registerForEvent("onDraw"','registerForEvent("onOverlayOpen"')) {
 if(!$result.Output.Contains($required)){throw "Missing CyberStunts postcondition: $required"}
}
if(([regex]::Matches($result.Output,'if __gcetStuntsLocationSleeping\(\) then return end')).Count -ne 2){
 throw 'Distant idling must cover DriveEvents and the frame scorer independently'
}
# Real onUpdate source has early SystemRequests/TimeSystem reads. Verify
# that the single composed native-location rule moves those reads behind
# the established disabled/player/mounted guards, both before and after
# generic shared-provider replacement.
$frameBlock=@'
    local requests = Game.GetSystemRequestsHandler()
    local timeSystem = Game.GetTimeSystem()
    if not oldTdValue and timeSystem and timeSystem:GetActiveTimeDilation() then
        oldTdValue = timeSystem:GetActiveTimeDilation()
    end
    if requests:IsPreGame() or requests:IsGamePaused() then
        return
    end

    local player = Game.GetPlayer()
    if not settings.enableMod or not playerPos or not player then return end

    local vehicle = player.mountedVehicle
    if not vehicle then
        wasVehicleAirborne = false
        return
    end
    if not isJumping then return end
'@
$pristineFrame=$source.Replace(' if not isJumping then return end',$frameBlock)
$stagedFrame=$pristineFrame.Replace('Game.GetSystemRequestsHandler()','__gcetGetSystemRequestsHandler()').Replace('Game.GetTimeSystem()','__gcetGetTimeSystem()').Replace('Game.GetPlayer()','__gcetGetPlayer()')
foreach($frameInput in @($pristineFrame,$stagedFrame)){
  $framePass=TrySource $frameInput
  if(!$framePass.Applied){throw "CyberStunts source-aware early guard order not generated: $($framePass.Error)"}
  $frameStart=$framePass.Output.IndexOf('registerForEvent("onUpdate", function(dt)')
  if($frameStart -lt 0){throw 'CyberStunts onUpdate owner missing'}
  $callback=$framePass.Output.Substring($frameStart)
  $checks=@(
    'if not settings.enableMod then return end',
    'local player = ',
    'if not player then return end',
    'local vehicle = player.mountedVehicle',
    'if not vehicle then',
    'local requests = ',
    'if requests:IsPreGame() or requests:IsGamePaused() then return end',
    'local timeSystem = ',
    'if not playerPos then return end'
  )
  $last=-1
  foreach($check in $checks){
    $found=$callback.IndexOf($check,[StringComparison]::Ordinal)
    if($found -lt 0 -or $found -le $last){
      throw "CyberStunts native/system getter or guard order regressed: $check"
    }
    $last=$found
  }
  if(!$callback.Contains('oldTdValue = timeSystem:GetActiveTimeDilation()')){
    throw 'CyberStunts prior dilation state recording lost'
  }
}
$repeat=TrySource $result.Output
if($repeat.Applied -or $repeat.Output){throw 'Repeated native dormancy was not idempotent'}
$lf=[string][char]10
$cr=[string][char]13
$crlf=$cr+$lf
$dos=TrySource ($source.Replace($lf,$crlf))
if(!$dos.Applied -or !$dos.Output.Contains($crlf)){throw 'CRLF original source unsupported'}
$unknown=TrySource $bad
if($unknown.Applied){throw 'Unknown source geometry should fail closed'}
Write-Host 'CyberStunts fixed-only GCAS inventory + native FAR gating + user edits + overlay fallback + idempotence + CRLF/drift: PASS'
