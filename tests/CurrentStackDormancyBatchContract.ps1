param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path $exe)){throw "Missing Resolver: $exe"}
$root=Join-Path $env:RUNNER_TEMP 'gcet-current-stack-five'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$capture=Join-Path $root 'CET-20990101-101010_FIVE_STAGE'
$mods=Join-Path $root 'mods'
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$mods,$zero | Out-Null
$enc=(Get-Content -Raw (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64')).Trim()
$ms=[IO.MemoryStream]::new([Convert]::FromBase64String($enc))
$gz=[IO.Compression.GZipStream]::new($ms,[IO.Compression.CompressionMode]::Decompress)
$out=[IO.MemoryStream]::new()
try{$gz.CopyTo($out);[IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$out.ToArray())}
finally{$out.Dispose();$gz.Dispose();$ms.Dispose()}
$fixtures=[ordered]@{
 'Minimap Widgets/init.lua'=@'
local TimeWidget={widget=true,label={SetText=function() end}}
local WeatherWidget={widget=true,label={SetText=function() end}}
local MinimapWidgetsConfig={Time=true,Weather=true,TimeSwitch=1}
function updateTimeWidget()
    if not TimeWidget.widget then return end
    if not MinimapWidgetsConfig.Time then return end
    local hours = Game.GetTimeSystem():GetGameTime():Hours()
    local minutes = (Game.GetTimeSystem():GetGameTime():Minutes())
    local mod = GetMod("TimeIsRunningOut")
    if MinimapWidgetsConfig.TimeSwitch == 5 and mod ~= nil then
        TimeWidget.label:SetText("mod")
    elseif MinimapWidgetsConfig.TimeSwitch == 5 and not mod then
        TimeWidget.label:SetText("normal")
    end
end
function updateWeatherWidget()
    if not WeatherWidget.widget then return end
    if not MinimapWidgetsConfig.Weather then return end
    local hours = Game.GetTimeSystem():GetGameTime():Hours()
    local minutes = Game.GetTimeSystem():GetGameTime():Minutes()
    WeatherWidget.label:SetText(hours..minutes)
end
registerForEvent("onInit", function() end)
registerForEvent("onUpdate",function(dt) updateTimeWidget(); updateWeatherWidget() end)
'@
 'CyberTrials/init.lua'=@'
local cetopen = false
local windowSettings={Current={mywindowhidden=false}}
function DrawEditor() end
function BestTimes() end
function DrawWindowHider() end
registerRuntimeEvent('onDraw', function()
    DrawEditor()
    BestTimes()
    local WindowHiderTool = GetMod("WindowHiderTool")
    if WindowHiderTool and cetopen then
        DrawWindowHider()
    elseif not WindowHiderTool then
        windowSettings.Current.mywindowhidden = false
    end
end)
'@
 'NPC Outfit Manager/init.lua'=@'
local autoOutfitsData = {outfits={}}
local function GetNPCID(target)
    if not target then return "Unknown" end
    local rawID = tostring(target:GetRecordID())
    if string.find(rawID, "ToTweakDBID") then
        local match = rawID:match("%[%[(.-)%]%]")
        if match then
            match = match:gsub("%-%-$", "")
            match = match:gsub("^%s+", ""):gsub("%s+$", "")
            if match ~= "" then return match end
        end
    end
    return rawID
end

local function CleanKey(k)
    return k
end
local function ProcessRadarOutfits()
    for id,npc in pairs(autoOutfitsData.outfits) do
       local record = GetNPCID(npc)
    end
end
registerForEvent("onInit", function() end)
registerForEvent("onUpdate", function(delta) ProcessRadarOutfits() end)
'@
 'illegal_mechanic_phase1a/init.lua'=@'
local PhysicalNpc=require("physical_npc")
registerForEvent("onInit", function() end)
registerForEvent("onUpdate", function(delta) PhysicalNpc.Update(delta) end)
'@
 'illegal_mechanic_phase1a/physical_npc.lua'=@'
local state={mechanics={},initialized=true,nextUpdateAt=0.0}
local cfg={updateInterval=0.2}
local function now() return os.clock() end
local function anchorPosition(entry) return Vector4.new(entry.x,entry.y,entry.z,1.0) end
local function getPlayer() return Game.GetPlayer() end
local function loadMatrix() return true end
local function resolvePendingSpawn(entry) end
local function distanceFromPlayer(entry, player)
  if player == nil then return nil end
  local ok, distance = pcall(function()
    return Vector4.Distance(player:GetWorldPosition(), anchorPosition(entry))
  end)
  return ok and distance or nil
end
local PhysicalNpc = {}
function PhysicalNpc.Update(delta)
  if not state.initialized then return end
  local currentTime = now()
  if currentTime < state.nextUpdateAt then
    for _, entry in ipairs(state.mechanics) do
      if entry.spawnPending then resolvePendingSpawn(entry) end
    end
    return
  end
  state.nextUpdateAt = currentTime + cfg.updateInterval
  if #state.mechanics == 0 and not loadMatrix() then return end
  local player = getPlayer()
  if player == nil then return end

  for _, entry in ipairs(state.mechanics) do
    if entry.spawnPending then
      resolvePendingSpawn(entry)
    else
      local distance = distanceFromPlayer(entry, player)
      if distance ~= nil then
        entry.lastDistance = distance
      end
    end
  end
end
return PhysicalNpc
'@
}
foreach($pair in $fixtures.GetEnumerator()){
 $file=Join-Path $mods ($pair.Key.Replace('/',[IO.Path]::DirectorySeparatorChar))
 New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
 [IO.File]::WriteAllText($file,[string]$pair.Value)
}
@{schemaVersion='1.8';callbacks=@();optimizerEvidence=@()} |
 ConvertTo-Json -Depth 20 |
 Set-Content -Encoding utf8 (Join-Path $capture 'CET_Resolver_Input.json')
function Generate {
 $stderrPath = Join-Path $capture 'gcet-test-resolver-stderr.json'
 $stdout = & $exe --capture $capture --mods $mods --generate-pass --json 2> $stderrPath
 $exitCode = $LASTEXITCODE
 $global:LASTEXITCODE = 0
 $stdoutText = (@($stdout) -join [Environment]::NewLine).Trim()
 $stderrText = if (Test-Path -LiteralPath $stderrPath) {
     (Get-Content -LiteralPath $stderrPath -Raw).Trim()
 } else { '' }
 $jsonText = if ($stdoutText.Length -gt 0) { $stdoutText } else { $stderrText }
 try { $parsed = $jsonText | ConvertFrom-Json -ErrorAction Stop }
 catch { throw "Resolver did not return parsable JSON, exit ${exitCode}: $jsonText" }
 $noPass = $exitCode -eq 1 -and !$parsed.ok -and
     [string]$parsed.error -like '*no applicable generic or source-proven semantic transforms*'
 if ($exitCode -ne 0 -and !$noPass) {
     throw "Unexpected Resolver exit code ${exitCode}: $jsonText"
 }
 $o = $parsed
 $report=Get-Content -Raw (Join-Path $capture 'G-CET_Resolver.json') | ConvertFrom-Json
 return @{Output=$o;Report=$report;ExitCode=$exitCode;NoPass=$noPass}
}
$ids=@(
 'minimap-widgets-time-weather-snapshot',
 'cybertrials-closed-overlay-provider',
 'npc-outfit-manager-id-normalization',
 'illegal-mechanic-npc-position-reuse'
)
$expected=@{
 'minimap-widgets-time-weather-snapshot'='Minimap Widgets/init.lua'
 'cybertrials-closed-overlay-provider'='CyberTrials/init.lua'
 'npc-outfit-manager-id-normalization'='NPC Outfit Manager/init.lua'
 'illegal-mechanic-npc-position-reuse'='illegal_mechanic_phase1a/physical_npc.lua'
}
$first=Generate
if(!$first.Output.ok -or !$first.Output.pass){throw 'Four new source-only stages did not generate'}
if(@($first.Report.callbackFamilies).Count -ne 0){throw 'Source-only stages fabricated callback measurements'}
$manifest=Get-Content -Raw $first.Output.pass.ManifestPath | ConvertFrom-Json
$zip=[IO.Compression.ZipFile]::OpenRead([string]$first.Output.pass.ZipPath)
try {
 foreach($id in $ids){
  $c=@($first.Report.semanticSourceOnlyCandidates|Where-Object {$_.ruleId -eq $id})
  $t=@($manifest.transforms|Where-Object {$_.RuleId -eq $id})
  if($c.Count -ne 1 -or $t.Count -ne 1 -or
     $t[0].admissionEvidence -ne 'SOURCE_PROVEN_NO_CAPTURE' -or
     $null -ne $t[0].runtimeMsPerSecond -or
     @($t[0].files).Count -ne 1 -or
     $t[0].files[0] -ne $expected[$id]){
     throw "Source-only stage missing/incorrect: $id"
  }
  $entry=$zip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/'+$expected[$id])
  if(!$entry){throw "Generated overlay missing: $id"}
  $dst=Join-Path $mods ($expected[$id].Replace('/',[IO.Path]::DirectorySeparatorChar))
  $stream=$entry.Open()
  try{$file=[IO.File]::Open($dst,[IO.FileMode]::Create,[IO.FileAccess]::Write)
      try{$stream.CopyTo($file)}finally{$file.Dispose()}}
  finally{$stream.Dispose()}
 }
}finally{$zip.Dispose()}
$after=Generate
foreach($id in $ids){
 if(@($after.Report.semanticSourceOnlyCandidates|Where-Object {$_.ruleId -eq $id}).Count -ne 0){
  throw "Already complete source re-admitted: $id"
 }
}
$minimap=Get-Content -Raw (Join-Path $mods 'Minimap Widgets\init.lua')
if(([regex]::Matches($minimap,'local __gcetMinimapGameTimePerInvocation =')).Count -ne 2 -or
   !$minimap.Contains('if MinimapWidgetsConfig.TimeSwitch == 5 then mod = GetMod')){
  throw 'Minimap time/provider stage incomplete'
}
$npc=Get-Content -Raw (Join-Path $mods 'NPC Outfit Manager\init.lua')
if(!$npc.Contains('__gcetNpcIdCacheCount >= 512') -or
   !$npc.Contains('target:GetRecordID()') -or
   !$npc.Contains('return __gcetRememberNpcId(rawID, match)')){
 throw 'NPC normalized string cache lost fresh record getter or bounded reuse'
}
$illegal=Get-Content -Raw (Join-Path $mods 'illegal_mechanic_phase1a\physical_npc.lua')
if(!$illegal.Contains('local __gcetMechanicPlayerPosition = nil') -or
   !$illegal.Contains('if entry.spawnPending then') -or
   !$illegal.Contains('local pos = playerPos or player:GetWorldPosition()') -or
   !$illegal.Contains('if distance ~= nil and __gcetPositionObserved then')){
 throw 'Illegal Mechanic source update lost position/pending fallback'
}
$cyber=Get-Content -Raw (Join-Path $mods 'CyberTrials\init.lua')
if(!$cyber.Contains('if cetopen or (windowSettings.Current and windowSettings.Current.mywindowhidden) then') -or
   !$cyber.Contains('DrawWindowHider()')){
 throw 'CyberTrials closed/open overlay source incorrect'
}
# Restore original, introduce a substantive drift, and make sure that owner
# does not receive an unproven source-only stage.
foreach($pair in $fixtures.GetEnumerator()){
 $file=Join-Path $mods ($pair.Key.Replace('/',[IO.Path]::DirectorySeparatorChar))
 [IO.File]::WriteAllText($file,[string]$pair.Value)
}
$cyberPath=Join-Path $mods 'CyberTrials\init.lua'
[IO.File]::WriteAllText($cyberPath,([IO.File]::ReadAllText($cyberPath)).Replace(
 'local WindowHiderTool = GetMod("WindowHiderTool")',
 'local WindowHiderTool = GetMod("UnknownProvider")'))
$drift=Generate
if(@($drift.Report.semanticSourceOnlyCandidates |
 Where-Object {$_.ruleId -eq 'cybertrials-closed-overlay-provider'}).Count -gt 0){
 throw 'CyberTrials substantive provider drift admitted'
}

# Recheck actual author-style tabs and harmless provider-quote/CRLF variants.
foreach($pair in $fixtures.GetEnumerator()){
 $file=Join-Path $mods ($pair.Key.Replace('/',[IO.Path]::DirectorySeparatorChar))
 [IO.File]::WriteAllText($file,[string]$pair.Value)
}
$cyberPath=Join-Path $mods 'CyberTrials\init.lua'
$cyberVariant=[IO.File]::ReadAllText($cyberPath).Replace(
 '        DrawWindowHider()', (([string][char]9)+'DrawWindowHider()')).Replace(
 '        windowSettings.Current.mywindowhidden = false', (([string][char]9)+'windowSettings.Current.mywindowhidden = false'))
[IO.File]::WriteAllText($cyberPath,$cyberVariant)
$minimapPath=Join-Path $mods 'Minimap Widgets\init.lua'
$miniVariant=[IO.File]::ReadAllText($minimapPath).Replace(
 'GetMod("TimeIsRunningOut")', "GetMod('TimeIsRunningOut')")
[IO.File]::WriteAllText($minimapPath,$miniVariant.Replace(
 ([string][char]10), (([string][char]13)+([string][char]10))))
$variant=Generate
foreach($id in @('cybertrials-closed-overlay-provider','minimap-widgets-time-weather-snapshot')){
 if(@($variant.Report.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq $id}).Count -ne 1){
  throw "Quote/CRLF/tab source variant not admitted: $id"
 }
}
# Deliberate source drift must reject only the affected owner.
foreach($pair in $fixtures.GetEnumerator()){
 $file=Join-Path $mods ($pair.Key.Replace('/',[IO.Path]::DirectorySeparatorChar))
 [IO.File]::WriteAllText($file,[string]$pair.Value)
}
$npcPath=Join-Path $mods 'NPC Outfit Manager\init.lua'
[IO.File]::WriteAllText($npcPath,([IO.File]::ReadAllText($npcPath)).Replace(
 'target:GetRecordID()', 'target:GetOtherRecordID()'))
$illegalPath=Join-Path $mods 'illegal_mechanic_phase1a\physical_npc.lua'
[IO.File]::WriteAllText($illegalPath,([IO.File]::ReadAllText($illegalPath)).Replace(
 'Vector4.Distance(player:GetWorldPosition(), anchorPosition(entry))',
 'Vector4.Distance(getPositionByOtherRules(player), anchorPosition(entry))'))
$minimapPath=Join-Path $mods 'Minimap Widgets\init.lua'
[IO.File]::WriteAllText($minimapPath,([IO.File]::ReadAllText($minimapPath)).Replace(
 'Game.GetTimeSystem():GetGameTime():Hours()',
 'Game.GetTimeSystem():GetDifferentTime():Hours()'))
$driftBatch=Generate
foreach($id in @('npc-outfit-manager-id-normalization','illegal-mechanic-npc-position-reuse','minimap-widgets-time-weather-snapshot')){
 if(@($driftBatch.Report.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq $id}).Count -gt 0){
  throw "Unsafe semantic source drift admitted: $id"
 }
}

Write-Host 'Current-stack dormancy batch: four original, missing-stage, source-only, composition, idempotence and drift checks passed.'
