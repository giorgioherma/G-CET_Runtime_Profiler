param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$dll=Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'
$type=[Reflection.Assembly]::LoadFrom($dll).GetType('GCETRuntimeProfiler.Core.Services.NativeLocationFourOwnersSource',$true)
function Upgrade([string]$name,[string]$src) {
 $m=$type.GetMethod($name,[Reflection.BindingFlags]'Static,NonPublic')
 if(!$m){throw "Missing $name"}
 try{return [string]$m.Invoke($null,[object[]]@($src))}
 catch{throw "$name rejected: $($_.Exception.InnerException.Message)"}
}
$poker=@'
-- G-CET native location:poker-v1
if lookup and type(lookup.attach) == 'function' and type(lookup.attach) == "function" then
 if farWorldJob then farWorldJob.Resume() end
        return true
end
'@
$p=Upgrade 'UpgradePokerV2' $poker
if(!$p.Contains('poker-v2') -or !$p.Contains('if nearMode then farWorldJob.Pause() else farWorldJob.Resume() end')) {
 throw 'Poker native v2 fallback failed'
}
$tunnel=@'
local __gcetTunnelNativeFar = false
if __gcetTunnelNativeFar then return true end
-- G-CET native location:tunnel-v1
function() __gcetTunnelNativeFar=false end,
function() __gcetTunnelNativeFar=true end,
function() __gcetTunnelNativeFar=false;return true end)
local player=Game.GetPlayer()
'@
$t=Upgrade 'UpgradeTunnelV2' $tunnel
foreach($part in @('tunnel-v2','__gcetTunnelNativeOutside = nil',
  'if __gcetTunnelNativeOutside ~= nil then return __gcetTunnelNativeOutside end',
  '__gcetTunnelNativeOutside=false','__gcetTunnelNativeOutside=true',
  '__gcetTunnelNativeOutside=nil;return true','local player=Game.GetPlayer()')) {
 if(!$t.Contains($part)){throw "Tunnel near/far/unknown contract missing $part"}
}
$illegal=@'
-- G-CET native location:illegal-v1
  if state.__gcetNativeFar then
    local hasLiveOrPending=false
    for _,entry in ipairs(state.mechanics) do
      if entry.spawnPending or entry.entityID ~= nil or entry.entity ~= nil then
        hasLiveOrPending=true;break
      end
    end
    if not hasLiveOrPending then return end
  end  local player = getPlayer()
  if entry.spawnPending then resolvePendingSpawn(entry) end
'@
$i=Upgrade 'UpgradeIllegalV2' $illegal
foreach($part in @('illegal-v2','resolvePendingSpawn(entry)',
  'deleteEntry(entry, "player left spawn radius (native FAR)")',
  'if not needsOriginal then return end','local player = getPlayer()')) {
 if(!$i.Contains($part)){throw "Illegal mechanic far-native contract missing $part"}
}
$spacing=$illegal.Replace('  end  local player', '  end' + "`r`n" + '  local player')
if(!(Upgrade 'UpgradeIllegalV2' $spacing).Contains('if not needsOriginal then return end')) {
 throw 'Illegal native source equivalent failed'
}
$drift=$illegal.Replace('entry.spawnPending or entry.entityID','entry.spawnPending and entry.entityID')
$refused=$false
try{[void](Upgrade 'UpgradeIllegalV2' $drift)}catch{$refused=$true}
if(!$refused){throw 'Illegal native v2 accepted substantive pending-state drift'}
$dedka=@'
-- G-CET native location:dedka-v1
local __gcetZone1=__gcetDedkaZone1
lookup:attach(10034,sites,near,far,fallback)
'@
$d=Upgrade 'UpgradeDedkaV2' $dedka
if(!$d.Contains('dedka-v2') -or !$d.Contains('local __gcetZone1=__gcetDedkaZone1')) {
 throw 'Dedka native zone fallback stage failed'
}
Write-Host 'Four native-location v1->v2 source upgrades + fallback/drift: PASS'
