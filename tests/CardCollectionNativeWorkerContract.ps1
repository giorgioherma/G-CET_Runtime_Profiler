param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-cards-worker'
$owner=Join-Path $root 'Card Collection'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $owner|Out-Null
$src=@'
local function buildSpatialEntries()
  return {}
end
local function registerSpatialSet()
end
local function unregisterSpatialSet()
end
local __gcetCardNativeFar=false
local function __gcetAttachCardNative()
  local lookup=engine and engine.GCET and engine.GCET.NativeLocationHost
  if not lookup or type(lookup.attachCompiled)~="function" then return end
  -- All 100 source-defined card anchors were compiled by Resolver.
  -- Keep exact collection and picked-state logic in Lua.
  if #buildSpatialEntries()==0 then return end
  lookup:attachCompiled(10035,100,
    function()
      __gcetCardNativeFar=false
      if engine.IsPlaying() then registerSpatialSet() end
    end,
    function()
      __gcetCardNativeFar=true
      unregisterSpatialSet()
    end,
    function()
      __gcetCardNativeFar=false
      if engine.IsPlaying() then registerSpatialSet() end
      return true
    end)
end
registerForEvent("onInit",function()
  __gcetAttachCardNative()
end)
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object {$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyCardCollectionNativeWorker',$flags)
function Apply([string]$text) {
 [IO.File]::WriteAllText((Join-Path $owner 'init.lua'),$text,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Card Collection',[string]'card-collection-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$r=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Card worker generation: $($_.Exception.InnerException.Message)"}
 return @{result=$r;rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $src
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'Expected card bridge and worker'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/card_collection_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Missing card output file'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('GCET_Semantic/card_collection_native_worker','registerSpatialSet()','unregisterSpatialSet()','100 or 0')){if(!$b.Contains($token)){throw "Bridge omitted $token"}}
foreach($token in @('G-CET semantic coordinator:card-collection-native-worker','host:attachCompiled(10035, count','onTransition(true)','onTransition(false)')){if(!$w.Contains($token)){throw "Worker omitted $token"}}
New-Item -ItemType Directory -Force (Join-Path $owner 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $owner 'GCET_Semantic/card_collection_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Card coordinator injected twice'}
Write-Host 'Card Collection native worker and idempotence: PASS'
