param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Resolver executable missing'}
$root=Join-Path $env:RUNNER_TEMP 'gcet-airbackflip-dormancy-v2'
if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
$capture=Join-Path $root 'CET-20990101-000000_AIRFLIP'
$mods=Join-Path $root 'mods'
$air=Join-Path $mods 'AirBackFlip'
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$air,$zero|Out-Null
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
$input=[IO.MemoryStream]::new([Convert]::FromBase64String($encoded))
$zipstream=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output=[IO.MemoryStream]::new()
try { $zipstream.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray()) }
finally { $zipstream.Dispose();$input.Dispose();$output.Dispose() }
$initPath=Join-Path $air 'init.lua'
@'
local cfg = { enabled = true }
local flip = nil
local GAME_ACTIONS = {
    Airbackflip_Backflip = function() end,
    Airbackflip_Frontflip = function() end,
    Airbackflip_SideflipLeft = function() end,
    Airbackflip_SideflipRight = function() end,
    Airbackflip_SwingOver = function() end,
}
local function loadConfig()
    local saved = {}
    for k,v in pairs(saved) do cfg[k] = v end
end
registerForEvent("onInit", function()
    loadConfig()
    Observe("PlayerPuppet", "OnAction", function(_, action)
        pcall(function()
            local name = Game.NameToString(ListenerAction.GetName(action))
            if name == "MoveY" then
                local moveY = ListenerAction.GetValue(action)
                return
            end
            local fn = GAME_ACTIONS[name]
            if not fn then return end
            if ListenerAction.IsButtonJustPressed(action) then fn(true)
            elseif ListenerAction.IsButtonJustReleased(action) then fn(false) end
        end)
    end)
end)
registerForEvent("onUpdate", function(dt)
    if flip then flip = nil end
end)
registerForEvent("onDraw", function()
    cfg.enabled = ImGui.Checkbox("Enabled", cfg.enabled)
end)
'@ | Set-Content -LiteralPath $initPath -Encoding utf8
$original=Get-Content -LiteralPath $initPath -Raw
$empty=@{schemaVersion='2.0';callbacks=@();optimizerEvidence=@()}|ConvertTo-Json -Depth 30
$empty|Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
$result=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
if(!$result.ok -or !$result.pass){throw 'AirBackFlip original source-only generation failed'}
$report=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
$candidates=@($report.semanticSourceOnlyCandidates|Where-Object {$_.ruleId -eq 'air-backflip'})
if($candidates.Count -ne 1 -or !$candidates[0].stageComposed -or @($report.callbackFamilies).Count -ne 0){
    throw 'AirBackFlip source-only candidate lost composed-stage evidence or fabricated callbacks'
}
$manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
$stages=@($manifest.transforms|Where-Object {$_.RuleId -eq 'air-backflip'})
if($stages.Count -ne 1 -or $stages[0].admissionEvidence -ne 'SOURCE_PROVEN_NO_CAPTURE' -or
   $null -ne $stages[0].runtimeMsPerSecond){throw 'AirBackFlip source-only provenance mismatch'}
$archive=[IO.Compression.ZipFile]::OpenRead($result.pass.ZipPath)
try{
    $entry=$archive.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/AirBackFlip/init.lua')
    if(!$entry){throw 'AirBackFlip output source missing'}
    $reader=[IO.StreamReader]::new($entry.Open())
    try{$patched=$reader.ReadToEnd()}finally{$reader.Dispose()}
    $moduleEntry=$archive.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/AirBackFlip/G-CET/AirBackFlipCoordinator.lua')
    if(!$moduleEntry){throw 'AirBackFlip external coordinator missing from ZIP'}
    $reader=[IO.StreamReader]::new($moduleEntry.Open())
    try{$coordinator=$reader.ReadToEnd()}finally{$reader.Dispose()}
}
finally{$archive.Dispose()}
foreach($token in @(
    '__gcetAirBackFlipDormancyV2',
    '__gcetAirBackFlipCoordinatorV1',
    'G-CET/AirBackFlipCoordinator',
    'pcall(mod.sync, cfg.enabled == true, flip ~= nil)',
    'Observe("PlayerPuppet", "OnAction", gcetAirBackFlipOnAction)',
    'cfg.enabled = ImGui.Checkbox("Enabled", cfg.enabled)')){
    if(!$patched.Contains($token)){throw "AirBackFlip missing source stage: $token"}
}
foreach($token in @('-- G-CET semantic coordinator:air-backflip',
    'function M.sync(enabled, flipping)', 'function M.adopt(handle, enabled, flipping)',
    'pcall(h.SetActive, desired)', 'state.lastActive = desired')){
    if(!$coordinator.Contains($token)){throw "AirBackFlip coordinator missing: $token"}
}
$created=@($stages[0].files)
if($created.Count -ne 2 -or $created -notcontains 'AirBackFlip/G-CET/AirBackFlipCoordinator.lua'){
    throw 'Semantic manifest omitted a required source/coordinator pair'
}
$patched|Set-Content -LiteralPath $initPath -Encoding utf8
$moduleDir=Join-Path $air 'G-CET'
New-Item -ItemType Directory -Force $moduleDir|Out-Null
$coordinator|Set-Content -LiteralPath (Join-Path $moduleDir 'AirBackFlipCoordinator.lua') -Encoding utf8
$again=(& $exe --capture $capture --mods $mods --json|ConvertFrom-Json)
if(!$again.ok){throw 'AirBackFlip completed-source analysis failed'}
$completed=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
if(@($completed.semanticSourceOnlyCandidates|Where-Object {$_.ruleId -eq 'air-backflip'}).Count -gt 0){
    $checks=@{
        marker=$patched.Contains('__gcetAirBackFlipDormancyV2')
        sync=$patched.Contains('local function __gcetAirBackFlipSync()')
        setActive=$coordinator.Contains('pcall(h.SetActive, desired)')
        fallback=$patched.Contains('Observe("PlayerPuppet", "OnAction", gcetAirBackFlipOnAction)')
        handles=[regex]::Matches($patched,'__gcetAirBackFlipCoordinator\.adopt').Count
        checkbox=$patched.Contains("cfg.enabled = ImGui.Checkbox(`"Enabled`", cfg.enabled)`n        __gcetAirBackFlipSync()")
        writers=[regex]::Matches($patched,'\bcfg\.enabled\s*=(?![=])').Count
    }
    throw ('Completed AirBackFlip v2 still eligible: '+($checks|ConvertTo-Json -Compress))
}
# New author-side enable writer must fail closed, never be accepted as completed.
$patched.Replace('registerForEvent("onDraw",','cfg.enabled = false' + [Environment]::NewLine + 'registerForEvent("onDraw",')|
    Set-Content -LiteralPath $initPath -Encoding utf8
$drift=(& $exe --capture $capture --mods $mods --json|ConvertFrom-Json)
$driftReport=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
$driftGenerate=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
$driftManifest=Join-Path $capture 'G-CET_Pass_Manifest.json'
if($driftGenerate.ok -and $driftGenerate.pass){
    $driftZip=[string]$driftGenerate.pass.ZipPath
    $m=Get-Content -LiteralPath $driftGenerate.pass.ManifestPath -Raw|ConvertFrom-Json
    if(@($m.transforms|Where-Object {$_.RuleId -eq 'air-backflip'}).Count -gt 0){
        throw 'AirBackFlip additional author state writer incorrectly received a pass'
    }
}
if(!(Get-Content -LiteralPath $initPath -Raw).Contains('cfg.enabled = false')){
    throw 'AirBackFlip source drift test modified the live source in-place'
}
$global:LASTEXITCODE=0
Write-Host 'AirBackFlip pristine source-only, native fallback, idempotence and state-writer drift: PASS'
