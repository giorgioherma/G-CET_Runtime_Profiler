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
# Current-source generic discovery is not a fabricated profiler capture.
$invalidSourceFamilies=@($report.callbackFamilies|Where-Object {
    $_.sourceFirst -ne $true -or
    @($_.topConsumers|Where-Object {
        $_.sourceFirst -ne $true -or $null -ne $_.runtime
    }).Count -gt 0
})
if($candidates.Count -ne 1 -or !$candidates[0].stageComposed -or $invalidSourceFamilies.Count -ne 0){
    throw 'AirBackFlip source-only candidate lost composed-stage evidence or fabricated callbacks'
}
$manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
$stages=@($manifest.transforms|Where-Object {$_.RuleId -eq 'air-backflip'})
if($stages.Count -ne 1 -or $stages[0].sourceProof -ne $true -or
   $null -ne $stages[0].runtimeMsPerSecond){
    $skips=@($manifest.skipped | Where-Object {$_.RuleId -eq 'air-backflip'})
    throw ('AirBackFlip source-only semantic stage did not compose: ' +
        (@{transforms=$stages;skipped=$skips}|ConvertTo-Json -Depth 12 -Compress))
}
$archive=[IO.Compression.ZipFile]::OpenRead($result.pass.ZipPath)
try{
    $entry=$archive.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/AirBackFlip/init.lua')
    if(!$entry){throw 'AirBackFlip output source missing'}
    $reader=[IO.StreamReader]::new($entry.Open())
    try{$patched=$reader.ReadToEnd()}finally{$reader.Dispose()}
}
finally{$archive.Dispose()}
$usesGeneric = $patched.Contains('__gcetAirBackFlipDormancyV3')
$usesLegacy = $patched.Contains('__gcetAirBackFlipDormancyV2')
if(!$usesGeneric -and !$usesLegacy){throw 'AirBackFlip lacks semantic dormancy stage'}
$tokens=@(
    'pcall(h.SetActive, desired)',
    'desired = cfg.enabled == true or flip ~= nil',
    '__gcetAirBackFlipLastActive',
    'cfg.enabled = ImGui.Checkbox("Enabled", cfg.enabled)'
)
if($usesGeneric) {
    $tokens+=@('__gcetAirBackFlipHandles = __gcetHandles_',
                'local function __gcetOnAction_',
                'if not __gcetRouted_')
} else {
    $tokens+='Observe("PlayerPuppet", "OnAction", gcetAirBackFlipOnAction)'
}
foreach($token in $tokens){
    if(!$patched.Contains($token)){throw "AirBackFlip missing source stage: $token"}
}
$patched|Set-Content -LiteralPath $initPath -Encoding utf8
$again=(& $exe --capture $capture --mods $mods --json|ConvertFrom-Json)
if(!$again.ok){throw 'AirBackFlip completed-source analysis failed'}
$completed=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
if(@($completed.semanticSourceOnlyCandidates|Where-Object {$_.ruleId -eq 'air-backflip'}).Count -gt 0){
    $checks=@{
        marker=$patched.Contains('__gcetAirBackFlipDormancyV2')
        sync=$patched.Contains('local function __gcetAirBackFlipSync()')
        setActive=$patched.Contains('pcall(h.SetActive, desired)')
        fallback=$patched.Contains('Observe("PlayerPuppet", "OnAction", gcetAirBackFlipOnAction)')
        handles=[regex]::Matches($patched,'__gcetAirBackFlipHandle\s*=\s*handle;\s*__gcetAirBackFlipLastActive\s*=\s*nil;\s*__gcetAirBackFlipSync\(\)').Count
        checkbox=$patched.Contains("cfg.enabled = ImGui.Checkbox(`"Enabled`", cfg.enabled)`n        __gcetAirBackFlipSync()")
        writers=[regex]::Matches($patched,'\bcfg\.enabled\s*=(?![=])').Count
    }
    throw ('Completed AirBackFlip v2 still eligible: '+($checks|ConvertTo-Json -Compress))
}
# New author-side enable writer must fail closed, never be accepted as completed.
# The generic 0-Engine frame stage may rename the onDraw registrar.
# Insert the extra author state writer against either current-source form,
# and explicitly assert the fixture mutation happened before testing drift.
$drawMatches=[regex]::Matches($patched,'(?m)^[ \t]*(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*["'']onDraw["'']')
if($drawMatches.Count -ne 1){throw 'AirBackFlip drift fixture lost unique onDraw registration'}
$drifted=$patched.Insert($drawMatches[0].Index,'cfg.enabled = false' + [Environment]::NewLine)
if($drifted -eq $patched -or !$drifted.Contains('cfg.enabled = false')){
    throw 'AirBackFlip drift fixture did not insert author config writer'
}
$drifted|Set-Content -LiteralPath $initPath -Encoding utf8
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
