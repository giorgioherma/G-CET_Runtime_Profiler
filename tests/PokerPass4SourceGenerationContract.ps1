param([Parameter(Mandatory = $true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Resolver missing: $exe"}
$root=Join-Path $env:RUNNER_TEMP 'gcet-poker-pass4-original-source'
if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
$capture=Join-Path $root 'CET-20990101-040506_POKER_PASS4'
$mods=Join-Path $root 'mods'
$poker=Join-Path $mods 'StreetGamesPoker'
New-Item -ItemType Directory -Force $capture,$poker | Out-Null

# Resolver's actual pass contract requires the existing 0-Engine runtime.
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zero | Out-Null
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed=[Convert]::FromBase64String($encoded)
$inputStream=[System.IO.MemoryStream]::new($compressed)
$gzip=[System.IO.Compression.GZipStream]::new(
    $inputStream,[System.IO.Compression.CompressionMode]::Decompress)
$output=[System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray())
}
finally {
    $output.Dispose();$gzip.Dispose();$inputStream.Dispose()
}
$path=Join-Path $poker 'init.lua'
$tablesPath=Join-Path $poker 'StreetTables.lua'
@'
StreetTables={activeSpotId=nil, locations={}}
local SpotManager={activeSpotId=nil}
local CHAIR_SPAWN_RANGE=12
local CHAIR_DESPAWN_RANGE=20
local TRACE_RANGE=6
local TRACE_INTERVAL=2
local traceClock=0
local function checkStranded(pos) end
local function spawnChair(loc) end
local function despawnChair(loc) end
local function traceApproach(loc, player, pos) end
function StreetTables.update(dt)
    local player = GetPlayer()
    if not player then return end
    local pos = player:GetWorldPosition()
    pcall(checkStranded, pos)
    traceClock = traceClock + (dt or 0)
    local doTrace = false
    if traceClock >= TRACE_INTERVAL then
        traceClock = 0
        doTrace = true
    end
    local px, py, pz = pos.x, pos.y, pos.z
    for _, loc in ipairs(StreetTables.locations) do
        local dx = px - loc.center.x
        local dy = py - loc.center.y
        local dz = pz - loc.floorZ
        local d = math.sqrt(dx * dx + dy * dy + dz * dz)
        if d < CHAIR_SPAWN_RANGE and StreetTables.activeSpotId ~= loc.id then
            spawnChair(loc)
        elseif d > CHAIR_DESPAWN_RANGE then
            despawnChair(loc)
        end
        if doTrace and d < TRACE_RANGE and SpotManager.activeSpotId == nil then
            pcall(traceApproach, loc, player, pos)
        end
    end
end
return StreetTables
'@ | Set-Content -LiteralPath $tablesPath -Encoding utf8

@'
StreetGamesPoker={ready=false}
local Logger={info=function() end}
local Cron={Update=function() end}
local SeatController={update=function() end}
local StreetTables={update=function() end,registerMapPins=function() end}
local InteractionMenu={update=function() end}
local CardRenderer={update=function() end}
local TableTalk={update=function() end}
local PokerHUD={draw=function() end}
local inMenu=true
local inGame=false
registerForEvent("onInit", function()
    StreetTables.registerMapPins()
    StreetGamesPoker.ready = true
end)
registerForEvent("onUpdate", function(dt)
    if not StreetGamesPoker.ready or inMenu or not inGame then return end
    Cron.Update(dt)
    SeatController.update(dt)
    StreetTables.update(dt)
    InteractionMenu.update()
    CardRenderer.update(dt)
    TableTalk.update(dt)
end)
registerForEvent("onDraw", function()
    PokerHUD.draw()
end)
'@ | Set-Content -LiteralPath $path -Encoding utf8
$seatPath=Join-Path $poker 'SeatController.lua'
# Exercise the unchanged author seat contract as well as scheduler and world
# source. Camera getters and vector/angle work should run only for a seat
# within its authored interaction range; all prompt hide paths remain.
@'
local SeatController = {spots={},activeSpotId=nil,forcedCam=false,activeCam=nil}
local function player() return GetPlayer() end
local function setCamera() end
local function showPrompt() end
local function hidePrompt() end
function SeatController.update()
    local p = player()
    if not p then return end
    if SeatController.forcedCam and SeatController.activeCam then
        local entry = SeatController.spots[SeatController.activeCam]
        if entry then
            setCamera(true, entry.spotObject)
        end
        return
    end
    if SeatController.activeSpotId then return end
    local position = p:GetWorldPosition()
    local forward = p:GetWorldForward()
    local pitch = Game.GetCameraSystem():GetActiveCameraData().rotation:ToEulerAngles().pitch
    for _, entry in pairs(SeatController.spots) do
        local spot = entry.spotObject
        local marker = spot.mappin_worldPosition
        local distance = Vector4.Distance(position, marker)
        local difference = Vector4.new(position.x - marker.x, position.y - marker.y, position.z - marker.z, 0)
        local angle = 180 - Vector4.GetAngleBetween(forward, difference)
        local visible = distance < (spot.mappin_interactionRange or 2)
            and angle < (spot.mappin_interactionAngle or 90)
            and pitch > (spot.mappin_pitchMin or -75)
            and pitch < (spot.mappin_pitchMax or 5)
        if visible and not spot.spot_showingInteractUI then showPrompt(spot)
        elseif not visible and spot.spot_showingInteractUI then hidePrompt(spot) end
    end
end
function SeatController.changeSpotData(allSpots, changes, spotId) end
return SeatController
'@ | Set-Content -LiteralPath $seatPath -Encoding utf8
$authorSeat=Get-Content -LiteralPath $seatPath -Raw
$authorInit=Get-Content -LiteralPath $path -Raw
$authorTables=Get-Content -LiteralPath $tablesPath -Raw
$input=@{schemaVersion='1.8';callbacks=@(
    @{registrationId=999;owner='StreetGamesPoker';infrastructure=$false;kind='event';target='onUpdate'
       source=@{file='init.lua';lineStart=17;lineEnd=24}
       callsPerSecond=60;exclusiveMsPerSecond=8;globalWorkSharePct=12
       familyWorkSharePct=80;avgExclusiveUs=130;maxExclusiveMs=4;spikeCount=0;maxSpikeExclusiveMs=0}
);optimizerEvidence=@()} | ConvertTo-Json -Depth 30
$input | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
function Generate {
    $result=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
    if(!$result.ok -or !$result.pass){
  $resolver=Join-Path $capture 'G-CET_Resolver.json'
  if(Test-Path -LiteralPath $resolver){
    $json=Get-Content -LiteralPath $resolver -Raw | ConvertFrom-Json
    $rows=@($json.callbackFamilies | ForEach-Object {$_.topConsumers} | Where-Object {$_.owner -eq 'StreetGamesPoker'})
    foreach($row in $rows){Write-Host ('Poker disposition '+($row.disposition|ConvertTo-Json -Compress)+' proof '+($row.semantic|ConvertTo-Json -Depth 8 -Compress))}
  }
  throw 'PASS4 generation failed'
}
    return $result
}
# Source-only admission for reviewed PASS4: no captured onUpdate callback
# must not prevent its complete original-source two-file optimization.
$emptyInput=@{schemaVersion='1.8';callbacks=@();optimizerEvidence=@()} | ConvertTo-Json -Depth 30
$emptyInput | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
try {
    $unmeasured=Generate
    $report=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
    $candidates=@($report.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq 'streetgamespoker'})
    if($candidates.Count -ne 1 -or !$candidates[0].stageComposed -or
       @($report.callbackFamilies | Where-Object { -not $_.sourceFirst }).Count -ne 0){
        throw 'Poker source-only composition absent or fabricated captured callbacks'
    }
    $m=Get-Content -LiteralPath $unmeasured.pass.ManifestPath -Raw | ConvertFrom-Json
    $t=@($m.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'})
    if($t.Count -ne 1 -or $t[0].admissionEvidence -ne 'SOURCE_PROVEN_NO_CAPTURE' -or
       $null -ne $t[0].runtimeMsPerSecond -or @($t[0].files).Count -ne 3){
        throw ('Poker source-only admission incomplete: transforms='+$t.Count+
            ' files='+(@($t[0].files) -join ';')+
            ' admission='+$t[0].admissionEvidence+
            ' runtime='+[string]$t[0].runtimeMsPerSecond)
    }
}
finally {
    $input | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
}
$first=Generate
$manifest=Get-Content -LiteralPath $first.pass.ManifestPath -Raw | ConvertFrom-Json
$transforms=@($manifest.transforms | Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'streetgamespoker'})
if($transforms.Count -ne 1){throw "Expected exactly one Poker semantic; got $($transforms.Count)"}
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$first.pass.ZipPath)
try{
    $entry=$zip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/init.lua')
    if(!$entry){throw 'PASS4 output missing'}
    $reader=[System.IO.StreamReader]::new($entry.Open())
    try{$generated=$reader.ReadToEnd()}finally{$reader.Dispose()}
}finally{$zip.Dispose()}
$forward=$generated.IndexOf('local __gcetPokerAttachPass4')
$onInit=$generated.IndexOf('registerForEvent("onInit"')
$assigned=$generated.IndexOf('__gcetPokerAttachPass4=function()')
# Generic runs before source-aware Poker PASS4 and can source-proven route
# the author onUpdate through a single existing 0-Engine frame registrar.
# The lexical startup constraint applies to the actual onUpdate callsite,
# regardless of which *proven* source-safe registrar owns it.
$onUpdateCalls=[regex]::Matches($generated,
    '(?m)^[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*["'']onUpdate["'']')
$onUpdate=if($onUpdateCalls.Count -eq 1){$onUpdateCalls[0].Index}else{-1}
if($generated -match '(?m)^[ \t]*__gcetRegisterEvent_\d+\s*\(\s*["'']onUpdate["'']' -and
   ($generated -notmatch 'G-CET generated frame registrar: source-selected' -or
    $generated -notmatch '__gcetApi.MakeEventRegistrar\("StreetGamesPoker", registerForEvent\)')){
    throw 'Generic frame registrar does not prove its original owner and CET fallback'
}
if($forward -lt 0 -or $onInit -lt 0 -or $assigned -lt 0 -or $onUpdate -lt 0 -or
  $forward -ge $onInit -or $assigned -le $onInit -or $assigned -ge $onUpdate){
  throw 'PASS4 startup lexical scope/order incorrect'
}
foreach($token in @('poker.world_far','poker.world_near','poker.seat_near','poker.realtime',
  'Logger.info("pass4_scheduler_attached"','StreetTables.registerMapPins()','Cron.Update(dt)',
  'StreetTables.update(dt)','TableTalk.update(dt)','if __gcetPokerAttached then return end')){
    if(!$generated.Contains($token)){throw "Generated PASS4 missing $token"}
}
if($generated.Contains('log.info(') -or $generated.Contains('TryAttachPokerPass4()')){
    throw 'Invalid PASS4 logger or stale function call'
}

# Archive was closed after reading init; reopen to verify the second source.
$zip2=[System.IO.Compression.ZipFile]::OpenRead([string]$first.pass.ZipPath)
try {
    $entry2=$zip2.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/StreetTables.lua')
    if(!$entry2){throw 'PASS4C world scan file not generated'}
    $reader2=[System.IO.StreamReader]::new($entry2.Open())
    try{$tablesOut=$reader2.ReadToEnd()}finally{$reader2.Dispose()}
}finally{$zip2.Dispose()}
# Verify the third authoritative source is emitted from untouched author seat
# behavior, including exact final distance and prompt cleanup.
$zipSeat=[System.IO.Compression.ZipFile]::OpenRead([string]$first.pass.ZipPath)
try{
    $entry=$zipSeat.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/SeatController.lua')
    if(!$entry){throw 'Poker seat source not generated'}
    $reader=[IO.StreamReader]::new($entry.Open())
    try{$seatOut=$reader.ReadToEnd()}finally{$reader.Dispose()}
}finally{$zipSeat.Dispose()}
foreach($token in @('poker-seat-squared-distance-prefilter-v1',
  'if dx * dx + dy * dy + dz * dz < range * range then',
  'if distance < range then','if not forward then forward = p:GetWorldForward() end',
  'if visible and not spot.spot_showingInteractUI then showPrompt(spot)',
  'elseif not visible and spot.spot_showingInteractUI then hidePrompt(spot)')){
    if(!$seatOut.Contains($token)){throw "Poker seat scan missing $token"}
}
if($seatOut.Contains('local forward = p:GetWorldForward()')){
    throw 'Poker seat source still performs unconditional camera/angle work'
}
foreach($token in @('__gcetPokerWakeSq','__gcetPokerNearAny','__gcetPokerDistanceSq',
 'StreetTables.update(dt, wakeRadius)','spawnChair(loc)','despawnChair(loc)',
 'pcall(traceApproach, loc, player, pos)')){
  if(!$tablesOut.Contains($token)){throw "PASS4C world source missing $token"}
}
if($tablesOut.Contains('math.sqrt(dx * dx + dy * dy + dz * dz)')){
  throw 'Expensive distance sqrt still present'
}
if(@($manifest.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'} | ForEach-Object {$_.files}).Count -ne 3){
  throw 'Poker semantic must patch init and world scan together'
}
$generated | Set-Content -LiteralPath $path -Encoding utf8
$tablesOut | Set-Content -LiteralPath $tablesPath -Encoding utf8
$seatOut | Set-Content -LiteralPath $seatPath -Encoding utf8
# A fully satisfied pass intentionally produces no ZIP. Analyze the optimized
# live source directly rather than requiring an unrelated transform to exist.
$second=(& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
if(!$second.ok){throw 'Resolver failed to analyze already-optimized Poker source'}
$resolver=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$pokerRows=@($resolver.callbackFamilies | ForEach-Object {$_.topConsumers} |
    Where-Object {$_.owner -eq 'StreetGamesPoker'})
if(!$pokerRows.Count){throw 'Optimized Poker owner missing from Resolver report'}
$semantic=@($pokerRows | Where-Object {
    $_.semantic.RuleId -eq 'streetgamespoker' -and
    $_.semantic.AlreadySatisfied -eq $true -and
    $_.semantic.MarkerFileCount -eq 3
})
if(!$semantic.Count){throw 'Resolver did not recognize all three optimized files as already satisfied'}

# A completed controller must be retained while an original world sweep is
# independently upgraded; likewise a completed sweep survives controller reset.
$generated | Set-Content -LiteralPath $path -Encoding utf8
$authorTables | Set-Content -LiteralPath $tablesPath -Encoding utf8
$seatOut | Set-Content -LiteralPath $seatPath -Encoding utf8
$partialWorld = Generate
$mWorld = Get-Content $partialWorld.pass.ManifestPath -Raw | ConvertFrom-Json
$tWorld = @($mWorld.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'})
if ($tWorld.Count -ne 1 -or @($tWorld[0].files).Count -ne 1 -or
    @($tWorld[0].files)[0] -ne 'StreetGamesPoker/StreetTables.lua') {
    throw 'Poker original world sweep was not independently upgraded'
}

$authorInit | Set-Content -LiteralPath $path -Encoding utf8
$tablesOut | Set-Content -LiteralPath $tablesPath -Encoding utf8
$seatOut | Set-Content -LiteralPath $seatPath -Encoding utf8
$partialController = Generate
$mController = Get-Content $partialController.pass.ManifestPath -Raw | ConvertFrom-Json
$tController = @($mController.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'})
if ($tController.Count -ne 1 -or @($tController[0].files).Count -ne 1 -or
    @($tController[0].files)[0] -ne 'StreetGamesPoker/init.lua') {
    throw 'Poker original controller was not independently upgraded'
}

# Historical PASS4 controller/world uses different local names and no canonical
# marker. Its two independent proven stages can coexist with the new seat-stage
# marker. All THREE stages must still be complete for already-satisfied status.
$legacyInit = $generated.Replace('__gcetPokerAttachPass4','TryAttachPokerPass4').
    Replace('__gcetPokerAttached','pass4Attached') -replace '(?m)^-- G-CET semantic:streetgamespoker\r?\n',''
$legacyTables = $tablesOut.Replace('__gcetPokerWakeRadius','wake').
    Replace('__gcetPokerWakeSq','wakeSq').
    Replace('__gcetPokerSpawnSq','spawnSq').
    Replace('__gcetPokerDespawnSq','despawnSq').
    Replace('__gcetPokerTraceSq','traceSq').
    Replace('__gcetPokerNearAny','nearAny').
    Replace('__gcetPokerDistanceSq','dSq') -replace '(?m)^-- G-CET semantic:streetgamespoker\r?\n',''
$legacyInit | Set-Content -LiteralPath $path -Encoding utf8
$legacyTables | Set-Content -LiteralPath $tablesPath -Encoding utf8
$seatOut | Set-Content -LiteralPath $seatPath -Encoding utf8
$legacy = (& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
if (!$legacy.ok) { throw 'Historical Poker source could not be analyzed' }
$legacyReport = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$legacyMatches = @($legacyReport.callbackFamilies | ForEach-Object { $_.topConsumers } |
    Where-Object { $_.semantic.RuleId -eq 'streetgamespoker' })
if (@($legacyMatches | Where-Object { $_.semantic.AlreadySatisfied -and
        $_.semantic.MarkerFileCount -eq 1 }).Count -lt 1) {
    throw 'Unmarked historical Poker controller/world with completed seat stage not recognized as complete'
}

# A missing wake return makes the same historical source INCOMPLETE. Merely
# finding the four job IDs in a mod must never be treated as a full pass.
($legacyTables -replace '\breturn\s+nearAny\b','return false') |
    Set-Content -LiteralPath $tablesPath -Encoding utf8
$drift = (& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
$driftReport = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$driftMatches = @($driftReport.callbackFamilies | ForEach-Object { $_.topConsumers } |
    Where-Object { $_.semantic.RuleId -eq 'streetgamespoker' })
if (@($driftMatches | Where-Object { $_.semantic.AlreadySatisfied }).Count -gt 0) {
    throw 'Poker mutated wake stage incorrectly classified complete'
}
$authorInit | Set-Content -LiteralPath $path -Encoding utf8
$authorTables | Set-Content -LiteralPath $tablesPath -Encoding utf8
$authorSeat | Set-Content -LiteralPath $seatPath -Encoding utf8

# Version-variation regression: cosmetic source changes and a different dt
# parameter name must remain eligible. The Resolver must still alter both files
# from pristine author source, with the author fallback preserved.
$variantInit=$authorInit
$variantInit=$variantInit.Replace('function(dt)', 'function(deltaTime)')
$variantInit=$variantInit.Replace('Cron.Update(dt)', 'Cron.Update(deltaTime)')
$variantInit=$variantInit.Replace('SeatController.update(dt)', 'SeatController.update(deltaTime)')
$variantInit=$variantInit.Replace('StreetTables.update(dt)', 'StreetTables.update(deltaTime)')
$variantInit=$variantInit.Replace('CardRenderer.update(dt)', 'CardRenderer.update(deltaTime)')
$variantInit=$variantInit.Replace('TableTalk.update(dt)', 'TableTalk.update(deltaTime)')
$variantTables=$authorTables.Replace(
    'function StreetTables.update(dt)',
    'function StreetTables.update(deltaTime)').Replace(
    'traceClock = traceClock + (dt or 0)',
    'traceClock = traceClock + (deltaTime or 0)')
$variantInit | Set-Content -LiteralPath $path -Encoding utf8
$variantTables | Set-Content -LiteralPath $tablesPath -Encoding utf8
$authorSeat | Set-Content -LiteralPath $seatPath -Encoding utf8
$variation=Generate
$vManifest=Get-Content -LiteralPath $variation.pass.ManifestPath -Raw | ConvertFrom-Json
if(@($vManifest.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'}).Count -ne 1){
    throw 'Version-varied Poker source did not generate PASS4'
}
$vZip=[System.IO.Compression.ZipFile]::OpenRead([string]$variation.pass.ZipPath)
try {
    foreach($pair in @(
        @('init.lua','Cron.Update(deltaTime)'),
        @('StreetTables.lua','function StreetTables.update(deltaTime, wakeRadius)')
    )){
        $entry=$vZip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/'+$pair[0])
        if(!$entry){throw "Version-variation output missing $($pair[0])"}
        $reader=[System.IO.StreamReader]::new($entry.Open())
        try {$out=$reader.ReadToEnd()} finally {$reader.Dispose()}
        if(!$out.Contains($pair[1])) {
            throw "Version-variation output lost author delta-time identity: $($pair[1])"
        }
    }
} finally {$vZip.Dispose()}
# Current untouched Nexus source uses Vector4.Distance rather than a scalar
# math.sqrt expression. It must also produce BOTH complete PASS4 files in one
# generation, without requiring a prior G-CET stage.
$vectorWorld = [regex]::Replace($authorTables,
    '(?m)^[ \t]*local px, py, pz = pos\.x, pos\.y, pos\.z\r?\n','')
$vectorWorld = [regex]::Replace($vectorWorld,
    '(?m)^[ \t]*local dx = px - loc\.center\.x\r?\n[ \t]*local dy = py - loc\.center\.y\r?\n[ \t]*local dz = pz - loc\.floorZ\r?\n[ \t]*local d = math\.sqrt\(dx \* dx \+ dy \* dy \+ dz \* dz\)',
    '        local d = Vector4.Distance(pos, Vector4.new(loc.center.x, loc.center.y, loc.floorZ, 1))')
if (!$vectorWorld.Contains('Vector4.Distance') -or $vectorWorld.Contains('math.sqrt(')) {
    throw 'Could not construct current Nexus Vector4 author-source variant'
}
$authorInit | Set-Content -LiteralPath $path -Encoding utf8
$vectorWorld | Set-Content -LiteralPath $tablesPath -Encoding utf8
$authorSeat | Set-Content -LiteralPath $seatPath -Encoding utf8
$vector = Generate
$vectorManifest = Get-Content $vector.pass.ManifestPath -Raw | ConvertFrom-Json
$vectorRule = @($vectorManifest.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'})
if ($vectorRule.Count -ne 1 -or @($vectorRule[0].files).Count -ne 3) {
    throw 'Pristine Vector4.Distance world source did not receive complete Poker PASS4'
}
$vectorZip = [System.IO.Compression.ZipFile]::OpenRead([string]$vector.pass.ZipPath)
try {
    $entry = $vectorZip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/StreetTables.lua')
    if (!$entry) { throw 'Vector4 world output missing' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $out = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if (!$out.Contains('__gcetPokerDistanceSq') -or $out.Contains('Vector4.Distance(') -or
        !$out.Contains('return __gcetPokerNearAny')) {
        throw 'Poker Vector4 source retained untransformed distance or lost the wake signal'
    }
} finally { $vectorZip.Dispose() }

Write-Host 'Poker PASS4 generated from author source, with four lanes, lexical startup, logger, map pins, fallback and idempotence.'
