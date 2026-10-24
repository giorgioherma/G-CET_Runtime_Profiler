param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-f80h-native-wake'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force (Join-Path $root 'f80h_aroused_expressions') | Out-Null
$source=@'
local ANCHORS = dofile("AnchorsIndex.lua")
local scriptActive = false
local tracked      = {}
local pending      = {}
local pendingAppearance = {}
local preActiveQueue = {}
local function tryRegisterNPC(self)
  if not isEnabled() then return end
end
registerForEvent("onInit", function()
end)
  ObserveAfter("NPCPuppet", "OnGameAttached", function(self)
  if not isEnabled() then return end
end)
'@
$folder=Join-Path $root 'f80h_aroused_expressions'
[IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object {$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyF80hArousedNativeWake',$flags)
if(!$m){throw 'Missing f80h native injector'}
function ApplySource([string]$sourceText) {
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$sourceText,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'f80h_aroused_expressions',[string]'native-location-f80h-aroused',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try {$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "f80h source injection failed: $($_.Exception.InnerException.Message)"}
 $rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
 return @{result=$result;rows=$rows}
}
$first=ApplySource $source
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'f80h first pass did not stage bridge plus worker'}
$bridge=@($first.rows | Where-Object {$_.RelativeFile -like '*/init.lua'}) | Select-Object -First 1
$worker=@($first.rows | Where-Object {$_.RelativeFile -like '*/GCET_Semantic/f80h_native_wake.lua'}) | Select-Object -First 1
if(!$bridge -or !$worker){throw 'f80h bridge or generated coordinator missing'}
$output=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$coordinator=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($need in @('GCET_Semantic/f80h_native_wake','__gcetF80hCanSkipNew()','and next(preActiveQueue) == nil')) {
 if(!$output.Contains($need)){throw "f80h bridge omitted $need"}
}
foreach($need in @('G-CET semantic coordinator:native-location-f80h-aroused','attachCompiled(10042, count','function M.attach(count)','function M.isFar()')) {
 if(!$coordinator.Contains($need)){throw "f80h worker omitted $need"}
}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic') | Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/f80h_native_wake.lua'),$coordinator,[Text.UTF8Encoding]::new($false))
$second=ApplySource $output
if($second.result.Applied){throw 'f80h regenerated a completed stage'}
$ownedPath=Join-Path $folder 'GCET_Semantic/f80h_native_wake.lua'
$previous=$coordinator.Replace('return M', "-- older generated implementation`nreturn M")
[IO.File]::WriteAllText($ownedPath,$previous,[Text.UTF8Encoding]::new($false))
$upgrade=ApplySource $source
if(!$upgrade.result.Applied -or $upgrade.rows.Count -ne 2){throw 'Same-rule older worker was not upgraded'}
$upgraded=@($upgrade.rows | Where-Object {$_.RelativeFile -like '*/GCET_Semantic/f80h_native_wake.lua'}) | Select-Object -First 1
if(!$upgraded -or [Text.Encoding]::UTF8.GetString($upgraded.Bytes) -ne $coordinator){throw 'Worker upgrade differs from reviewed coordinator'}
[IO.File]::WriteAllText($ownedPath,($previous.Replace('native-location-f80h-aroused','unrelated-owner')),[Text.UTF8Encoding]::new($false))
$refused=$false
try { $null=ApplySource $source } catch { $refused=$true }
if(!$refused){throw 'Foreign-owned coordinator was overwritten'}
Write-Host 'f80h same-owner worker upgrade and foreign-owner protection: PASS'

# Phase1+2 PASS8 was formerly inherited from an already optimized AudioXL
# init. The SAME existing f80h active-GC source owner must now generate it
# from an unmarked, original-shaped source, without a predecessor overlay.
$pristineGc=@'
local SWEEP_INTERVAL   = 1.5
local PENDING_TTL      = 12.0
local AUDIO_CHECK_INTERVAL = 1.0
local scriptActive = false
local rngSeeded = false
local tracked = {}
local pending = {}
local pendingAppearance = {}
local preActiveQueue = {}
local anchorAudio = {}
local audioCheckTimer = 0
local sweepTimer = 0
local function resetRuntime()
  scriptActive = false
  tracked = {}
  pending = {}
  preActiveQueue = {}
  pendingAppearance = {}
  sweepTimer = 0
end

---------------------------------------------------------
-- UPDATE LOOP
---------------------------------------------------------
registerForEvent("onUpdate", function(delta)
  Cron.Update(delta)

  -- ensure we seed as soon as game time becomes available
  if scriptActive and not rngSeeded then
    seedRngOnce()
  end

  if not isEnabled() then return end
  if not scriptActive then return end

  -- Couple variance (time-based + random despawn)
  if CoupleVariance and CoupleVariance.Update then
    CoupleVariance.Update(
      delta,
      settings,
      ANCHORS,
      tracked,
      function(npc)
        if npc and npc.Dispose then
          pcall(function() npc:Dispose() end)
        end
      end,
      function(anchorName)
        setAnchorWoman(anchorName, nil)
      end
    )
  end

  local womenByAnchor = {}   -- anchorName -> NPCPuppet
  for _, entry in pairs(tracked) do
    if entry.anchorName then womenByAnchor[entry.anchorName] = entry.entity end
  end
  for _, anchor in ipairs(ANCHORS) do
    setAnchorWoman(anchor.name, womenByAnchor[anchor.name])
  end

  audioCheckTimer = audioCheckTimer + delta
  sweepTimer = sweepTimer + delta
  if sweepTimer < SWEEP_INTERVAL then return end
  sweepTimer = 0

  for hash, data in pairs(pending) do
      data.since = data.since + delta
  end
  for hash, data in pairs(pendingAppearance) do
      data.since = data.since + delta
  end
end)

registerForEvent("onInit", function()
  ObserveAfter("NPCPuppet", "OnDetach", function(self)
    local id = self and self:GetEntityID()
    if not id then return end
    local entry = tracked[id.hash]
    if entry and entry.activationTimer then
      Cron.Halt(entry.activationTimer)
    end
    if entry and entry.entity then
      deactivateLookAt(entry.entity)
    end

    tracked[id.hash] = nil
    pending[id.hash] = nil
    pendingAppearance[id.hash] = nil
  end)
end)
'@
$gcMethod=$inject.GetMethod('ApplyF80hActiveGcSweep',$flags)
if(!$gcMethod){throw 'Existing f80h active-GC generating owner is missing'}
function Test-GcPass([string]$lua) {
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$lua,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'f80h_aroused_expressions',[string]'f80h-active-gc-sweep',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$gcMethod.Invoke($null,[object[]]@($ctx))}
 catch{throw "Existing f80h GC + PASS8 generation failed: $($_.Exception.InnerException.Message)"}
 $rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
 return @{result=$result;rows=$rows}
}
$gcFirst=Test-GcPass $pristineGc
if(!$gcFirst.result.Applied -or $gcFirst.rows.Count -ne 1){throw 'Pristine f80h did not generate both active GC and PASS8 in one owner'}
$gcOutput=[Text.Encoding]::UTF8.GetString($gcFirst.rows[0].Bytes)
foreach($token in @(
'__gcetF80hActiveGcSweepV1',
'local idleCronElapsed = 0.0',
'idleCronElapsed >= 0.50',
'next(preActiveQueue) ~= nil',
'next(anchorAudio) ~= nil',
'idleCronElapsed = 0.0',
'G-CET f80h detach-safe-v1',
'__gcetVarianceDispose',
'data.since = data.since + __gcetSweepElapsed'
)){
 if(!$gcOutput.Contains($token)){throw "Pristine f80h one-pass output missing: $token"}
}
if($gcOutput -match '(?s)ObserveAfter\("NPCPuppet", "OnDetach".{0,400}deactivateLookAt\(entry.entity\)'){
 throw 'f80h still calls the native look-at deactivator while streaming out'
}
# The Phase 1+2 authority stops disabled/inactive updates BEFORE any
# Cron work; the pristine author source originally called Cron first.
$gateDisabled=$gcOutput.IndexOf('  if not isEnabled() then return end')
$gateInactive=$gcOutput.IndexOf('  if not scriptActive then return end')
$seed=$gcOutput.IndexOf('  if not rngSeeded then')
$idle=$gcOutput.IndexOf('  local __gcetRuntimeBusy =')
$drain=$gcOutput.IndexOf('  Cron.Update(delta)')
if($gateDisabled -lt 0 -or $gateInactive -le $gateDisabled -or
   $seed -le $gateInactive -or $idle -le $seed -or
   $drain -le $idle -or
   $gcOutput.Contains('if scriptActive and isEnabled() then')) {
 throw 'Pristine f80h PASS8 Cron ordering diverged from authoritative inactive-first Phase 1+2.'
}
$gcRepeat=Test-GcPass $gcOutput
if($gcRepeat.result.Applied -or $gcRepeat.rows.Count -ne 0){throw 'f80h GC + PASS8 are not idempotent'}
$gcDrift=$pristineGc.Replace('local preActiveQueue = {}','local preActiveQueue = getDynamicQueue()')
$gcRefused=$false
try{$null=Test-GcPass $gcDrift}catch{$gcRefused=$true}
if(!$gcRefused){throw 'f80h changed runtime ownership must fail closed'}
Write-Host 'f80h original-shaped GC + PASS8 one-pass, reset, detach, repeat and drift: PASS'

Write-Host 'f80h native coordinator, bridge and idempotence: PASS'
