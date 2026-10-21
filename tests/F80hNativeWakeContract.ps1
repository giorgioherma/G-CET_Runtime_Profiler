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
if(!$first.result.Applied -or $first.rows.Count -ne 1){throw 'f80h first pass was not applied exactly once'}
$output=[Text.Encoding]::UTF8.GetString($first.rows[0].Bytes)
foreach($need in @('attachCompiled(10042, #ANCHORS','__gcetF80hCanSkipNew()','__gcetF80hFar = nil','and next(preActiveQueue) == nil')) {
 if(!$output.Contains($need)){throw "f80h omitted $need"}
}
$second=ApplySource $output
if($second.result.Applied){throw 'f80h regenerated a completed stage'}
Write-Host 'f80h native wake first pass and idempotence: PASS'
