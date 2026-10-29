param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom(
  (Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$catalog=$assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$method=$inject.GetMethod('ApplySpecterRecoveryPrerequisites',$flags)
$complete=$inject.GetMethod('IsSpecterRecoveryColdGcComplete',
  ([Reflection.BindingFlags]'Static,NonPublic,Public'))
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') |
  Where-Object {$_.GetParameters().Count -eq 4})[0]
if(!$method -or !$complete -or !$ctor){throw 'Missing source-owned Specter existing rule/structural verifier'}
$root=Join-Path $env:RUNNER_TEMP 'gcet-specter-recovery-cold-gc'
if(Test-Path $root){Remove-Item $root -Force -Recurse}
$dir=Join-Path $root 'SpecterM11RecoveryDev'
New-Item -ItemType Directory -Force $dir|Out-Null
$init=Join-Path $dir 'init.lua'
$music=Join-Path $dir 'specter_mission_music.lua'
$hud=Join-Path $dir 'm11_hud.lua'
$musicSource=@'
local M = { owner=nil, event=nil, elapsed=0, retry=0, blocked=nil, pending={} }
local function flush(bus)
  for event in pairs(M.pending) do
    local ok, stopped=pcall(function() return bus:StopSpecterMusic(event) end)
    if ok and stopped==true then M.pending[event]=nil end
  end
  return next(M.pending)==nil
end
function M.stop(bus)
  M.owner=nil
  return flush(bus)
end
function M.update(delta,bus,contract,stage,state,playerPresent,arrived)
  local id=contract and contract.id
  local active=playerPresent and id and state~="idle"
  if not active then
    if M.owner then M.stop(bus) else flush(bus) end
    return
  end
  return id
end
return M
'@
$hudSource=@'
local HUD = {active=false}
function HUD.Update()
  if HUD.active and Game.GetEngineTime():ToFloat() > 12 then HUD.active=false end
end
function HUD.IsActive()
  return HUD.active == true
end
return HUD
'@
$original=@'
local Music = require("specter_mission_music")
local HUD = require("m11_hud")
local core=nil
local owner=nil
local active=false
local stage=0
local restorePending=false
local autoStartClock=0
local STORY_NS="infinitegigs"
local function refresh()
  if not core then pcall(function() core=GetMod("GigCore") end) end
  pcall(function() owner=GetMod("InfiniteGigs") end)
end
local function saveBus()
  return nil
end
local function metricGet(ns,key,default)
  refresh()
  default=math.floor(tonumber(default) or 0)
  if core and core.State and core.State.get then
    local ok,v=pcall(function() return core.State.get(ns,key,default) end)
    if ok and v~=nil then return tonumber(v) or default end
  end
  if core and core.Bus and core.Bus.getMetric then
    local ok,v=pcall(function() return core.Bus.getMetric(ns,key,default) end)
    if ok and v~=nil then return tonumber(v) or default end
  end
  local sys=saveBus()
  if sys then
    local ok,v=pcall(function() return sys["GetMetric"](sys,ns,string.lower(key),default) end)
    if ok and v~=nil then return tonumber(v) or default end
  end
  return default
end
local function phoneBus() return nil end
local function applyRestore() end
local function start() end
local function present() end
registerForEvent("onSessionStart",function()
  Music.stop(phoneBus())
end)
registerForEvent("onUpdate",function(delta)
  Music.update(delta, phoneBus(), active and {id="recovery_counter_isolation"} or nil, stage, active and "recovery" or "idle", Game.GetPlayer() ~= nil, active)
  if restorePending then applyRestore() end
  if not active then
    present("HUD update",HUD.Update)
    local seq=metricGet(STORY_NS,"specter_story_sequence",0)
    local rainbowDone=metricGet(STORY_NS,"specter_story_done_specter_rainbow_market_dev",0)
    local recoveryDone=metricGet(STORY_NS,"specter_story_done_recovery_counter_isolation",0)
    if seq==9 and rainbowDone==1 and recoveryDone==0 then
      autoStartClock=autoStartClock+(delta or 0)
      refresh()
      if autoStartClock>=0.75 and owner and owner.state=="idle" and owner.contract==nil and Game.GetPlayer() then
        autoStartClock=0
        start()
      end
    else
      autoStartClock=0
    end
    return
  end
  present("HUD update",HUD.Update)
end)
'@
[IO.File]::WriteAllText($music,$musicSource,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($hud,$hudSource,[Text.UTF8Encoding]::new($false))
function Apply([string]$source) {
  [IO.File]::WriteAllText($init,$source,[Text.UTF8Encoding]::new($false))
  $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'SpecterM11RecoveryDev',
    [string]'specter-recovery-prerequisite-reads',
    [Collections.Generic.Dictionary[string,byte[]]]::new()))
  try{$result=$method.Invoke($null,[object[]]@($ctx))}
  catch {if($_.Exception.InnerException){throw $_.Exception.InnerException};throw}
  $rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  return @{result=$result;rows=$rows}
}
function Reject([string]$source,[string]$why) {
  $refused=$false
  try{$null=Apply $source}
  catch{if($_.Exception -is [InvalidOperationException]){$refused=$true}else{throw}}
  if(!$refused){throw "Specter unsafe partial stage accepted: $why"}
}
$r=Apply $original
if(!$r.result.Applied -or $r.rows.Count -ne 1){throw 'Specter original missing unified source transformation'}
$first=[Text.Encoding]::UTF8.GetString($r.rows[0].Bytes)
foreach($token in @(
  'local __gcetRecoveryMusicContract = {id="recovery_counter_isolation"}',
  '__gcetSpecterMetricCoreOnlyV1',
  'pcall(GetMod, "GigCore")',
  'pcall(core.State.get,ns,key,default)',
  'pcall(core.Bus.getMetric,ns,key,default)',
  '__gcetSpecterIdleMusicBusV1',
  'Music.owner ~= nil or next(Music.pending) ~= nil',
  'if HUD.IsActive() then present("HUD update",HUD.Update) end',
  'autoStartClock>=0.75',
  'if seq==9 and rainbowDone==1 and recoveryDone==0 then',
  'refresh()'
)){
  if(!$first.Contains($token)){throw "Specter original behavior/cold GC evidence missing: $token"}
}
if($first.Contains('pcall(function() return core.State.get(ns,key,default) end)') -or
   $first.Contains('Music.update(delta, phoneBus(),')){
  throw 'Specter hot source still allocates protected closure or idle bus'
}
$firstPath=Join-Path $dir 'init.lua'
[IO.File]::WriteAllText($firstPath,$first,[Text.UTF8Encoding]::new($false))
if(-not $complete.Invoke($null,[object[]]@([string]$dir))){
  throw 'Specter source-completion verifier rejected complete source'
}
Remove-Item -LiteralPath $music -Force
if($complete.Invoke($null,[object[]]@([string]$dir))){
  throw 'Specter falsely accepted a complete source without its required authored music module'
}
[IO.File]::WriteAllText($music,$musicSource,[Text.UTF8Encoding]::new($false))
$again=Apply $first
if($again.result.Applied -or $again.rows.Count -ne 0){
  throw 'Specter complete source reapplied or emitted a duplicate'
}
Reject ($first.Replace('pcall(core.State.get,ns,key,default)',
                       'pcall(function() return core.State.get(ns,key,default) end)')) 'metric half-reversion'
Reject ($first.Replace('next(Music.pending) ~= nil',
                       'next(Music.pending) == nil')) 'lost pending-stop music guarantee'
Reject ($original.Replace('      refresh()','      owner=nil')) 'same-frame owner wake removed'
Reject ($original.Replace('  refresh()', '  otherRefresh()', [StringComparison]::Ordinal)) 'unknown metric owner resolution'
$lineEnd=([string][char]13)+([string][char]10)
$windows=$original.Replace($lineEnd,[string][char]10).Replace([string][char]10,$lineEnd)
$cr=Apply $windows
if(!$cr.result.Applied){throw 'CRLF Specter was not generated'}
$crText=[Text.Encoding]::UTF8.GetString($cr.rows[0].Bytes)
if((Apply $crText).result.Applied){throw 'CRLF Specter is not idempotent'}
# The original module may be old/compact and lacks bus-state ownership:
# metric work should still compose, not infer an unsafe music wake.
[IO.File]::WriteAllText($music,'local M={}; function M.update(delta,bus,contract,stage,state,playerPresent,arrived) return nil end; return M',[Text.UTF8Encoding]::new($false))
$oldMusic=Apply $original
if(!$oldMusic.result.Applied){throw 'Specter minimal older music variant not optimized'}
$older=[Text.Encoding]::UTF8.GetString($oldMusic.rows[0].Bytes)
if($older.Contains('__gcetSpecterIdleMusicBusV1')){
  throw 'Specter guessed idle music ownership without source evidence'
}
Write-Host 'Specter 1.5 source-owned cold GC: native bus, metric callbacks, same-frame mission wake, partial source and CRLF PASS'
