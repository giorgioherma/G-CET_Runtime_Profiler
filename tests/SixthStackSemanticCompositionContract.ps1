param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "G-CET resolver executable not found: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-sixth-stack-semantic-composition'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-060606_SIXTH_STACK'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressed)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try { $gzip.CopyTo($output); [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray()) }
finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }

function Write-ModFile([string]$Mod,[string]$Relative,[string]$Source) {
    $path = Join-Path (Join-Path $mods $Mod) $Relative
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    $Source | Set-Content -LiteralPath $path -Encoding utf8
}

Write-ModFile 'GiveCraftMat' 'init.lua' @'
local cetopen = false
local windowstate = { Current = { mywindowhidden = false } }
function SaveWindowState()
    local file = io.open("windowstate.json", "w")
    if file then file:write(json.encode(windowstate.Current)); file:close() end
end
function DrawWindowHider() if not cetopen then return end end
function DrawButtons() if not cetopen or windowstate.Current.mywindowhidden == true then return end end
registerForEvent('onDraw', function()
    DrawButtons()
    local WindowHiderTool = GetMod("WindowHiderTool")
    if WindowHiderTool and cetopen then
        DrawWindowHider()
    elseif not WindowHiderTool then
        windowstate.Current.mywindowhidden = false
        SaveWindowState()
    end
end)
registerForEvent('onOverlayOpen', function() cetopen = true end)
registerForEvent('onOverlayClose', function() cetopen = false; SaveWindowState() end)
'@

Write-ModFile 'Simple Notepad - CET' 'init.lua' @'
local state = { open = false }
local windowhidden = false
local Buttons = {}
function saveWindowState()
    local file = io.open("windowstate.json", "w")
    if file then file:write(json.encode({windowhidden=windowhidden})); file:close() end
end
function Buttons.DrawWindowHider() end
function Buttons.Draw3() if not state.open then return end end
function Buttons.Draw2() if windowhidden or state.open == false then return end end
function Buttons.Draw() if windowhidden or state.open == false then return end end
registerForEvent('onDraw', function()
    local WindowHiderTool = GetMod("WindowHiderTool")
    if WindowHiderTool and state.open then
        Buttons.DrawWindowHider()
    elseif not WindowHiderTool then
        windowhidden = false
        saveWindowState()
    end
    Buttons.Draw3()
    Buttons.Draw2()
    Buttons.Draw()
end)
registerForEvent('onOverlayOpen', function() state.open = true end)
registerForEvent('onOverlayClose', function() saveWindowState(); state.open = false end)
'@

Write-ModFile 'DedraPalmjetQuickslot' 'init.lua' @'
local function safe(fn, fallback)
    local ok, value = pcall(fn)
    if ok then return value end
    return fallback
end
local function getActivePalmjetVariant() return nil end
local function activateSelectedPalmjet() end
local function isUseCombatGadgetPress(action)
    if not action then return false end
    local actionName = safe(function() return Game.NameToString(action:GetName(action)) end, "")
    if actionName == "" then actionName = safe(function() return Game.NameToString(action:GetName()) end, "") end
    local actionType = safe(function()
        local t = action:GetType(action)
        return t and t.value or ""
    end, "")
    if actionType == "" then
        actionType = safe(function()
            local t = action:GetType()
            return t and t.value or ""
        end, "")
    end
    if actionName == "UseCombatGadget" and actionType == "BUTTON_PRESSED" then return true end
    local isAction = safe(function() return action:IsAction(action, "UseCombatGadget") end, false)
    return isAction and actionType == "BUTTON_PRESSED"
end
local function installPalmjetHooks()
    Observe("PlayerPuppet", "OnAction", function(_self, action)
        if isUseCombatGadgetPress(action) then
            local variant = getActivePalmjetVariant()
            if variant then activateSelectedPalmjet() end
        end
    end)
end
registerForEvent("onInit", function() installPalmjetHooks() end)
'@

Write-ModFile 'BackStepDuo' 'init.lua' @'
local settings = { EnableDash = true }
local function getPlayer() return Game.GetPlayer() end
local function canUseAbility(player) return player ~= nil end
local function normalizeActionName(name)
    name = tostring(name or "")
    name = string.lower(name)
    return name:gsub("[^%w]", "")
end
local function getActionName(action)
    local ok, name = pcall(function() return Game.NameToString(action:GetName(action)) end)
    if ok and name ~= nil then return normalizeActionName(name) end
    return ""
end
local function getActionType(action)
    local ok, t = pcall(function() return action:GetType(action) end)
    if ok and t ~= nil then
        local ok2, v = pcall(function() return t.value end)
        if ok2 and v ~= nil then return tostring(v) end
    end
    return ""
end
local function getActionValue(action)
    local ok, value = pcall(function() return action:GetValue(action) end)
    if ok and type(value) == "number" then return value end
    return 0
end
local function processDirectionAction(actionName, actionType, value)
    if actionType ~= "BUTTON_PRESSED" then return end
    if actionName == "forward" or actionName == "back" or actionName == "left" or actionName == "right" then return end
    if actionName == "movey" then return end
    if actionName == "movex" then return end
end
registerForEvent("onInit", function()
    Observe("PlayerPuppet", "OnAction", function(self, action)
        local player = getPlayer()
        if not player then return end
        if not canUseAbility(player) then return end
        if not action then return end
        if not settings.EnableDash then return end
        local actionName = getActionName(action)
        local actionType = getActionType(action)
        local value = getActionValue(action)
        processDirectionAction(actionName, actionType, value)
    end)
end)
'@

Write-ModFile 'NightCityAlliesMissions' 'init.lua' @'
NCA_Missions = {}
local CargoHeist = { Update = function(dt, db) return false end }
local BountySystem = { UpdateRadar = function(db) return false end }
function NCA_Missions:SaveMissions(db)
    local file = io.open("nca_missions.json", "w")
    if file then file:write(json.encode(db)); file:close() end
end
function NCA_Missions:LoadMissions()
    local db = { activeMissions = {}, bmi = 0, heat = 0 }
    local file = io.open("nca_missions.json", "r")
    if file then
        local content = file:read("*all")
        file:close()
        if content and content ~= "" then
            local status, decoded = pcall(json.decode, content)
            if status and decoded then db = decoded end
        end
    end
    db.activeMissions = db.activeMissions or {}
    db.heat = db.heat or 0
    db.bmi = db.bmi or 0

    return db
end
local missionTick, heatEventTimer, heatPassiveTimer = 0, 0, 0
registerForEvent("onUpdate", function(dt)
    heatEventTimer = heatEventTimer + dt
    heatPassiveTimer = heatPassiveTimer + dt
    missionTick = missionTick + dt

    -- 2. CARREGAMENTO ÚNICO DOS DADOS (Otimização de Performance)
    -- Carregamos uma vez no início da frame para evitar acessos repetidos ao disco
    local db = NCA_Missions:LoadMissions()
    if not db then return end

    local saveNeeded = false
    if CargoHeist and CargoHeist.Update then
        if CargoHeist.Update(dt, db) then saveNeeded = true end
    end
    if BountySystem and BountySystem.UpdateRadar then
        if BountySystem.UpdateRadar(db) then saveNeeded = true end
    end
    if saveNeeded then
        NCA_Missions:SaveMissions(db)
        NCA_MissionDB = db
    end
end)
'@

function Find-CallbackRange([string]$Path,[string]$OpeningPattern) {
    $lines = @((Get-Content -LiteralPath $Path))
    $start = -1
    for ($i=0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $OpeningPattern) { $start=$i; break } }
    if ($start -lt 0) { throw "Opening not found: $OpeningPattern in $Path" }
    for ($i=$start+1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*end\s*\)\s*$') { return @{ Start=$start+1; End=$i+1 } }
    }
    throw "Callback closing not found: $OpeningPattern in $Path"
}
function CallbackRow([int]$Id,[string]$Owner,[string]$Kind,[string]$Target,[int]$Start,[int]$End,[double]$Ms,[double]$Calls=60) {
    @{ registrationId=$Id; owner=$Owner; infrastructure=$false; kind=$Kind; target=$Target
       source=@{file='init.lua';lineStart=$Start;lineEnd=$End}
       callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms; globalWorkSharePct=5
       familyWorkSharePct=40; avgExclusiveUs=100; maxExclusiveMs=2; spikeCount=0; maxSpikeExclusiveMs=0 }
}

$giveRange = Find-CallbackRange (Join-Path $mods 'GiveCraftMat\init.lua') "registerForEvent\('onDraw'"
$noteRange = Find-CallbackRange (Join-Path $mods 'Simple Notepad - CET\init.lua') "registerForEvent\('onDraw'"
$dedraRange = Find-CallbackRange (Join-Path $mods 'DedraPalmjetQuickslot\init.lua') 'Observe\("PlayerPuppet",\s*"OnAction"'
$backRange = Find-CallbackRange (Join-Path $mods 'BackStepDuo\init.lua') 'Observe\("PlayerPuppet",\s*"OnAction"'
$ncaRange = Find-CallbackRange (Join-Path $mods 'NightCityAlliesMissions\init.lua') 'registerForEvent\("onUpdate"'

$handoff=@{
 schemaVersion='1.8'
 callbacks=@(
    (CallbackRow 478 'GiveCraftMat' 'event' 'onDraw' $giveRange.Start $giveRange.End 20.998509 38),
    (CallbackRow 982 'Simple Notepad - CET' 'event' 'onDraw' $noteRange.Start $noteRange.End 17.0653 38),
    (CallbackRow 1183 'DedraPalmjetQuickslot' 'Observe' 'PlayerPuppet::OnAction' $dedraRange.Start $dedraRange.End 17.523339 878),
    (CallbackRow 1182 'BackStepDuo' 'Observe' 'PlayerPuppet::OnAction' $backRange.Start $backRange.End 12.899766 878),
    (CallbackRow 733 'NightCityAlliesMissions' 'event' 'onUpdate' $ncaRange.Start $ncaRange.End 19.318309 38)
 )
 optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'Sixth-stack semantic pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$rules=@('give-craft-mat','simple-notepad-cet','dedra-palmjet-quickslot','backstep-duo','nightcity-allies-missions')
foreach($rule in $rules) {
    $matches=@()
    foreach($family in @($resolver.callbackFamilies)) {
        $matches += @($family.topConsumers | Where-Object { $_.semantic.RuleId -eq $rule })
    }
    if($matches.Count -lt 1 -or @($matches | Where-Object { $_.semantic.SourceProofSatisfied }).Count -lt 1) {
        throw "Sixth-stack semantic source proof failed: $rule"
    }
}
$manifest=Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
foreach($rule in $rules) {
    if(@($manifest.transforms | Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule }).Count -ne 1) {
        $semanticSkips = @($manifest.skipped | Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule })
        $skipText = if($semanticSkips.Count -gt 0) { ($semanticSkips | ConvertTo-Json -Depth 10 -Compress) } else { '<no semantic skip recorded>' }
        throw "Sixth-stack semantic transform missing/duplicated: $rule; $skipText"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$Name) {
        $e=$zip.GetEntry($Name); if($null -eq $e){throw "ZIP entry missing: $Name"}
        $r=[System.IO.StreamReader]::new($e.Open()); try{return $r.ReadToEnd()} finally{$r.Dispose()}
    }
    $base='bin/x64/plugins/cyber_engine_tweaks/mods/'

    $give=Read-ZipText ($base+'GiveCraftMat/init.lua')
    if($give -notmatch 'G-CET semantic:give-craft-mat' -or
       $give -notmatch 'if not cetopen then return end' -or
       $give -notmatch 'elseif not WindowHiderTool and windowstate\.Current\.mywindowhidden then' -or
       $give -match 'if WindowHiderTool and cetopen then') { throw 'GiveCraftMat semantic composition is incomplete.' }

    $note=Read-ZipText ($base+'Simple Notepad - CET/init.lua')
    if($note -notmatch 'G-CET semantic:simple-notepad-cet' -or
       $note -notmatch 'if not state\.open then return end' -or
       $note -notmatch 'elseif not WindowHiderTool and windowhidden then' -or
       $note -match 'if WindowHiderTool and state\.open then') { throw 'Simple Notepad semantic composition is incomplete.' }

    $dedra=Read-ZipText ($base+'DedraPalmjetQuickslot/init.lua')
    if($dedra -notmatch 'G-CET semantic:dedra-palmjet-quickslot' -or
       $dedra -notmatch 'gcetDedraPalmjetOnAction' -or $dedra -notmatch 'SubscribeAction' -or
       $dedra -notmatch 'UseCombatGadget' -or $dedra -notmatch 'BUTTON_PRESSED') { throw 'Dedra Palmjet routing is incomplete.' }

    $back=Read-ZipText ($base+'BackStepDuo/init.lua')
    if($back -notmatch 'G-CET semantic:backstep-duo' -or
       $back -notmatch 'gcetBackStepDuoOnAction' -or $back -notmatch 'SubscribeAction' -or
       $back -notmatch 'Forward' -or $back -notmatch 'MoveY' -or $back -notmatch 'MoveX' -or
       $back -notmatch 'processDirectionAction') { throw 'BackStepDuo routing is incomplete.' }

    $nca=Read-ZipText ($base+'NightCityAlliesMissions/init.lua')
    if($nca -notmatch 'G-CET semantic:nightcity-allies-missions' -or
       $nca -notmatch [regex]::Escape('local db = NCA_MissionDB or NCA_Missions:LoadMissions()') -or
       $nca -notmatch 'function NCA_Missions:SaveMissions\(db\)\s+NCA_MissionDB = db' -or
       $nca -notmatch 'db\.bmi = db\.bmi or 0\s+NCA_MissionDB = db\s+return db' -or
       $nca -notmatch [regex]::Escape('CargoHeist.Update(dt, db)') -or
       $nca -notmatch [regex]::Escape('BountySystem.UpdateRadar(db)')) { throw 'NCA Missions state-cache composition is incomplete.' }
}
finally { $zip.Dispose() }

Write-Host 'Sixth-stack semantic composition contract passed: GiveCraftMat + Simple Notepad + Dedra Palmjet + BackStepDuo + NightCityAlliesMissions.'
