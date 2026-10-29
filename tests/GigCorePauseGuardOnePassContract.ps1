param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function ZipText([string]$zipPath,[string]$suffix){
  $z=[IO.Compression.ZipFile]::OpenRead($zipPath)
  try {
    $entry=@($z.Entries|Where-Object {$_.FullName.Replace('\','/').EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1
    if(!$entry){throw 'Missing GigCore pass source'}
    $reader=[IO.StreamReader]::new($entry.Open())
    try {return $reader.ReadToEnd()}finally{$reader.Dispose()}
  }finally{$z.Dispose()}
}
function Check([string]$version,[bool]$drift=$false){
  $root=Join-Path $env:RUNNER_TEMP ("gcet-gig-guard-"+$version)
  if(Test-Path $root){Remove-Item $root -Recurse -Force}
  $capture=Join-Path $root 'capture'
  $mods=Join-Path $root 'mods'
  $gig=Join-Path $mods 'GigCore'
  $engine=Join-Path $mods '0-Engine'
  New-Item -ItemType Directory -Force $capture,$gig,$engine|Out-Null
  $encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
  $bytes=[Convert]::FromBase64String($encoded)
  $inp=[IO.MemoryStream]::new($bytes)
  $zip=[IO.Compression.GZipStream]::new($inp,[IO.Compression.CompressionMode]::Decompress)
  $out=[IO.MemoryStream]::new()
  try{$zip.CopyTo($out);[IO.File]::WriteAllBytes((Join-Path $engine 'init.lua'),$out.ToArray())}finally{$out.Dispose();$zip.Dispose();$inp.Dispose()}
  $withDamage=($version -eq 'damage')
  $damageSource=Join-Path $gig 'core/vehicle_damage.lua'
  if($withDamage) {
    New-Item -ItemType Directory -Force (Split-Path $damageSource -Parent)|Out-Null
    @'
local VD={}
local subs        = {}
local healthVid, healthPrev
local now=0
local accum={}
local Bus={takeDamageEvent=function() return -1 end}
local function backstopPoll() end
local function flushDue() end
function VD.tick(dt)
  now = now + (dt or 0)
  local guard = 0
  while guard < 64 do
    guard = guard + 1
    local ki = Bus.takeDamageEvent()
    if not ki or ki < 0 then break end
    local amt = Bus.lastDamageAmount() or 0.0
  end
  backstopPoll()
  flushDue()
end
return VD
'@ | Set-Content -LiteralPath $damageSource -Encoding utf8
  }
  $registration=if($version -eq 'live'){'__gcetRegisterEvent_413'}else{'registerForEvent'}
  $snippet=@"
local pausedThisFrame=false
local function _rawMenuOrPhotoPaused()
  local defs = Game.GetAllBlackboardDefs()
  local ui   = Game.GetBlackboardSystem():Get(defs.UI_System)
  if ui and ui:GetBool(defs.UI_System.IsInMenu) then return true end
  local pm = Game.GetBlackboardSystem():Get(defs.PhotoMode)
  if pm and pm:GetBool(defs.PhotoMode.IsActive) then return true end
  return false
end
local function computePauseState() return _rawMenuOrPhotoPaused() end
local Marker={tickAll=function() end}
local VehicleDamage={tick=function() end}
local function crumb(v) end
$registration("onUpdate", function(delta)
  do local okPs, errPs = pcall(function() pausedThisFrame = computePauseState() end)
     if not okPs then crumb("onUpdate GUARD: computePauseState threw: " .. tostring(errPs)) end end
  local okMk, errMk = pcall(Marker.tickAll, delta, activeGigId)
  local okVd, errVd = pcall(VehicleDamage.tick, delta)
end)
"@
  if($drift){$snippet=$snippet.Replace('pausedThisFrame = computePauseState()', 'pausedThisFrame = not computePauseState()')}
  if($version -eq 'live'){
    $snippet=$snippet.Replace('  local ui   = Game.GetBlackboardSystem():Get(defs.UI_System)',"  local __gcetGigBoards = Game.GetBlackboardSystem()`n  local ui   = __gcetGigBoards:Get(defs.UI_System)")
    $snippet=$snippet.Replace('  local pm = Game.GetBlackboardSystem():Get(defs.PhotoMode)','  local pm = __gcetGigBoards:Get(defs.PhotoMode)')
  }
  $src=Join-Path $gig 'init.lua'
  $snippet | Set-Content -LiteralPath $src -Encoding utf8
  $old=(Get-FileHash $src -Algorithm SHA256).Hash
  $data=@{
    schemaVersion='2.0'
    callbacks=@(@{
      registrationId=812;owner='GigCore';infrastructure=$false;kind='event';target='onUpdate'
      source=@{file='init.lua';lineStart=6;lineEnd=11}
      callsPerSecond=60;exclusiveMsPerSecond=9.21;globalWorkSharePct=8;familyWorkSharePct=100
      avgExclusiveUs=153.5;maxExclusiveMs=1.84;spikeCount=0
    })
    optimizerEvidence=@()
  }|ConvertTo-Json -Depth 25
  $data|Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
  $result=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  if(!$result.ok){throw "Resolver failure $version"}
  $count=0
  if($result.pass -and $result.pass.ManifestPath){
    $manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
    $count=@($manifest.transforms|Where-Object {$_.RuleId -eq 'gigcore-blackboard-once'}).Count
  }
  if($drift){
    if($count -ne 0){throw 'Changed pause semantics must fail closed'}
    return
  }
  if($count -ne 1){
    Write-Host ($result | ConvertTo-Json -Depth 20 -Compress)
    if($manifest){Write-Host ($manifest | ConvertTo-Json -Depth 20 -Compress)}
    throw "Missing GigCore source transformation: $version"
  }
  $text=ZipText $result.pass.ZipPath 'GigCore/init.lua'
  if(!$text.Contains('local function __gcetGigCoreRefreshPausedFrame()') -or
     !$text.Contains('pcall(__gcetGigCoreRefreshPausedFrame)') -or
     $text.Contains('pcall(function() pausedThisFrame = computePauseState() end)') -or
     !$text.Contains('pcall(Marker.tickAll, delta, activeGigId)') -or
     !$text.Contains('__gcetGigBoards:Get(defs.PhotoMode)')){
    throw "Unexpected GigCore frame guard output: $version"
  }
  if((Get-FileHash $src -Algorithm SHA256).Hash -ne $old){throw 'Live GigCore mutated by Resolver'}
  if($withDamage) {
    $damageOut=ZipText $result.pass.ZipPath 'GigCore/core/vehicle_damage.lua'
    if(!$damageOut.Contains('__gcetGigDamageNoSubscribersV1') -or
       !$damageOut.Contains('if #subs == 0 then') -or
       !$damageOut.Contains('healthVid, healthPrev = nil, nil') -or
       !$damageOut.Contains('Bus.takeDamageEvent()') -or
       !$damageOut.Contains('backstopPoll()') -or
       !$damageOut.Contains('flushDue()')){
      throw 'GigCore idle damage stage lost empty-subscription drain or original active detector'
    }
    $damageOut|Set-Content -LiteralPath $damageSource -Encoding utf8
  }
  $text|Set-Content -LiteralPath $src -Encoding utf8
  $again=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  if(!(Test-Path -LiteralPath $src)) {throw "GigCore source disappeared on rerun: $version"}
  if($again.pass -and $again.pass.ManifestPath){
    $m2=Get-Content $again.pass.ManifestPath -Raw|ConvertFrom-Json
    if(@($m2.transforms|Where-Object {$_.RuleId -eq 'gigcore-blackboard-once'}).Count -gt 0){
      throw 'GigCore frame guard pass is not idempotent'
    }
  }
  Write-Host "GigCore one-pass protected frame guard: $version PASS"
}
Check 'original'
Check 'live'
Check 'damage'
Check 'drift' $true
