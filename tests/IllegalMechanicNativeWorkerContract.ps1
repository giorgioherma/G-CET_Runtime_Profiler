param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-illegal-worker'
$owner=Join-Path $root 'Illegal Mechanic'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $owner|Out-Null
$source=@'
local state={}
local function loadMatrix()
  local result={}
  state.mechanics = result
  -- G-CET native location:illegal-v3
  -- One compiled REDscript-backed site per mechanic: 1003301+registry index.
  -- Do not register positions from Lua. If absent, leave each site's author
  -- supervision active; only an authoritative FAR parks a dormant site.
  local engineOk,engine=pcall(GetMod,"0-Engine")
  local host=engineOk and engine and engine.GCET and engine.GCET.NativeLocationHost
  if host and type(host.attachCompiled)=="function" then
    for _,entry in ipairs(result) do
      local index=tonumber(entry.index)
      if index and index>=0 and index<256 then
        host:attachCompiled(1003301+index,1,
          function() entry.__gcetNativeFar=false;state.nextUpdateAt=0 end,
          function() entry.__gcetNativeFar=true;state.nextUpdateAt=0 end,
          function() entry.__gcetNativeFar=false;state.nextUpdateAt=0;return true end)
      end
    end
  end  log("matrix loaded mechanics=" .. tostring(#result))
  return true
end
-- This is the real source-boundary shape that the old worker regex erased.
local function distanceFromPlayer(entry, player)
  if player == nil then return nil end
  return Vector4.Distance(player:GetWorldPosition(), anchorPosition(entry))
end
local function deleteEntry(entry, reason)
  entry.entityID = nil
  entry.entity = nil
  entry.spawnPending = false
end
function PhysicalNpc.Update(delta)
  if #state.mechanics == 0 and not loadMatrix() then return end
  for _,entry in ipairs(state.mechanics) do
    if entry.__gcetNativeFar == true and not entry.spawnPending and entry.entityID == nil and entry.entity == nil then
    elseif entry.spawnPending then
      resolvePendingSpawn(entry)
    else
      local distance=0
    end
  end
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplyIllegalMechanicNativeWorker',$flags)
if(!$m){throw 'Illegal Mechanic native worker injector absent'}
function Apply([string]$text) {
 [IO.File]::WriteAllText((Join-Path $owner 'physical_npc.lua'),$text,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Illegal Mechanic',[string]'illegal-mechanic-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$r=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Illegal Mechanic worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$r;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $source
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'Expected mechanic bridge and worker outputs'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/physical_npc.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/illegal_mechanic_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Illegal Mechanic generated output missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('require("GCET_Semantic/illegal_mechanic_native_worker").attach(result, state)','function PhysicalNpc.Update(delta)','resolvePendingSpawn(entry)','log("matrix loaded mechanics="',
  '  return true', 'local function distanceFromPlayer(entry, player)', 'local function deleteEntry(entry, reason)', 'entry.spawnPending = false')){if(!$b.Contains($x)){throw "Mechanic bridge omitted $x"}}
if ($b.Contains('  end  log(')) {throw 'Native extraction left merged end/log boundary'}
if ($b.IndexOf('local function distanceFromPlayer(') -lt $b.IndexOf('return true')) {throw 'Mechanic loadMatrix closure is out of order'}
if ($b.IndexOf('local function deleteEntry(') -lt $b.IndexOf('local function distanceFromPlayer(')) {throw 'Mechanic original function ordering damaged'}
foreach($x in @('host:attachCompiled(1003301 + index, 1','index >= 0 and index < 256','entry.__gcetNativeFar = true','state.nextUpdateAt = 0')){if(!$w.Contains($x)){throw "Mechanic worker omitted $x"}}
New-Item -ItemType Directory -Force (Join-Path $owner 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $owner 'GCET_Semantic/illegal_mechanic_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Mechanic worker duplicated on second generation'}
Write-Host 'Illegal Mechanic native per-site worker and idempotence: PASS'
