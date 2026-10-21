param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP 'gcet-remaining-cet-source-contract'
if(Test-Path $root){Remove-Item $root -Force -Recurse}
New-Item -ItemType Directory -Force $root | Out-Null
$asm = [Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$injector = $asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors', $true)
$context = $asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$ctor = @($context.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') | Where-Object {$_.GetParameters().Count -eq 4})[0]
if (!$ctor) { throw 'SemanticPatchContext four-argument constructor missing' }
function Probe([string]$owner, [string]$rule, [string]$method, [hashtable]$files, [int]$count, [string[]]$need) {
  foreach($path in $files.Keys) {
    $full=Join-Path (Join-Path $root $owner) $path
    New-Item -ItemType Directory -Force (Split-Path $full -Parent) | Out-Null
    [IO.File]::WriteAllText($full,[string]$files[$path],[Text.UTF8Encoding]::new($false))
  }
  $staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
  $ctx=$ctor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,$staged))
  $m=$injector.GetMethod($method,$flags)
  if(!$m){throw "Missing injector $method"}
  try { $result=$m.Invoke($null,[object[]]@($ctx)) }
  catch { throw "$method rejected valid source: $($_.Exception.InnerException.Message)" }
  if(!$result.Applied){throw "$method did not apply to source-shaped fixture"}
  $rows=@($context.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  if($rows.Count -ne $count){throw "$method staged $($rows.Count), expected $count"}
  $joined=(@($rows | ForEach-Object {[Text.Encoding]::UTF8.GetString($_.Bytes)})) -join "`n"
  foreach($part in $need) {
    if(!$joined.Contains($part)){throw "$method output missing: $part"}
  }
  Write-Host "$method generated $count source files"
}
$psyInit=@'
local Sites = require("./modules/sites.lua")
local TryAttachRepeatablePass4
registerForEvent("onInit", function()
    BossWatchdog.init(Bridge, Diagnostics)
end)
'@
$psyDog=@'
local function syncSiteWithPosition(system, site, settings, elapsed, playerPosition)
    local status = system:GetStatus(site.id)
    local triggerDistance = settings.diagnostics.fallbackDistance or 180.0
    local state = Watchdog.states[site.id]
    if status == 1 then
        -- PASS4-R4:
        local trackedID, trackedEntity = tracked(system, site.id)
    end
end
function Watchdog.clear()
end
'@
Probe 'repeatable_cyberpsychos' 'native-location-cyberpsychos' 'ApplyCyberpsychosNativeLocations' @{
 'init.lua'=$psyInit
 'modules/boss_watchdog.lua'=$psyDog
} 3 @('attachCompiled(owner, 1','watchdog.nativeFar','triggerDistance <= 180.0')
$rica=@'
local function processCompletionRewards(system, limit)
    local processed = 0
    while processed < 5 do
        local serial, siteID, cycle = 0, "", 0
        pcall(function()
            serial = system:GetPendingRewardSerial()
            siteID = system:GetPendingRewardSiteID()
            cycle = system:GetPendingRewardCycle()
        end)
        if serial <= 0 then break end
    end
end
'@
Probe 'repeatable_increased_criminal_activity' 'rica-lazy-completion-details' 'ApplyRicaLazyRewardDetails' @{
 'init.lua'=$rica
} 1 @('if serial > 0 then','G-CET semantic:rica-lazy-completion-details-v1')
$infinite=@'
local function processBlueLines(delta)
  local q = Slice.blueLines or {}
  if #q == 0 then return end
end
local function processDelayedMu3N(delta)
  local q = Slice.delayedMu3N or {}
  if #q == 0 then return end
end
local function processDeferredAppearances(delta)
  local q = Slice.pendingAppearances or {}
  if #q == 0 then return end
end
local function processDeferredHostileActions(delta)
  local q = Slice.pendingHostileActions or {}
  if #q == 0 then return end
end
local function processPendingStorySpawns(delta)
  local q = Slice.pendingStorySpawns or {}
  if #q == 0 then return end
end
local function processStoryActorProximityBlips()
  local rules = Slice.storyActorRules or {}
  if not next(rules) then return end
end
'@
Probe 'InfiniteGigs' 'infinitegigs-empty-queue-fastpath' 'ApplyInfiniteGigsEmptyQueueFastPath' @{
 'init.lua'=$infinite
} 1 @('if not q or #q == 0 then return end','if not rules or not next(rules) then return end')
Write-Host 'Remaining CET source stages: PASS'
