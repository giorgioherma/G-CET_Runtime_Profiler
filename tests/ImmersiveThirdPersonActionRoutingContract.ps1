param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function FromPass([string]$zipPath){
  $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
  try {
    $item=@($zip.Entries|Where-Object {$_.FullName.Replace('\','/').EndsWith('immersive_third_person/init.lua',[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1
    if(!$item){throw 'Third-person init.lua missing from pass'}
    $rd=[IO.StreamReader]::new($item.Open())
    try { return $rd.ReadToEnd() } finally {$rd.Dispose()}
  } finally {$zip.Dispose()}
}
function TestVariant([bool]$drift) {
  $suffix=if($drift){'drift'}else{'pristine'}
  $root=Join-Path $env:RUNNER_TEMP ("gcet-itpp-onaction-"+$suffix)
  if(Test-Path $root){Remove-Item $root -Recurse -Force}
  $mods=Join-Path $root 'mods'
  $capture=Join-Path $root 'capture'
  $owner=Join-Path $mods 'immersive_third_person'
  $engine=Join-Path $mods '0-Engine'
  New-Item -ItemType Directory -Force $capture,$owner,$engine|Out-Null
  $bytes=[Convert]::FromBase64String((Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim())
  $input=[IO.MemoryStream]::new($bytes)
  $gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
  $output=[IO.MemoryStream]::new()
  try {$gzip.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $engine 'init.lua'),$output.ToArray())}
  finally {$output.Dispose();$gzip.Dispose();$input.Dispose()}
  $source=@'
local state={enabled=false,observedActions=false}
local config={camera={analogHoldTime=0.1,invertX=false,invertY=false}}
local function isPlayerInAnyMenu() return false end
local function getActionName(action) return action:GetName(action).value end
local function getActionValue(action) return action:GetValue(action) end
local function setDigitalMove(actionName, actionValue)
  if actionName == "Forward" then return true end
  if actionName == "Back" then return true end
  if actionName == "Left" then return true end
  if actionName == "Right" then return true end
  return false
end
local function handlePlayerAction(action)
  if not state.enabled or isPlayerInAnyMenu() then return end
  local actionName = getActionName(action)
  local actionValue = getActionValue(action)
  if not actionName then return end
  if actionName == "mouse_x" then return end
  if actionName == "mouse_y" then return end
  if actionName == "right_stick_x" then return end
  if actionName == "right_stick_y" then return end
  if actionName == "CameraMouseX" then return end
  if actionName == "CameraMouseY" then return end
  if actionName == "CameraX" then return end
  if actionName == "CameraY" then return end
  if setDigitalMove(actionName, actionValue) then return end
  if actionName == "world_map_menu_move_horizontal" or actionName == "left_stick_x" then return end
  if actionName == "world_map_menu_move_vertical" or actionName == "left_stick_y" then return end
  if actionName == "MoveX" then return end
  if actionName == "MoveY" then return end
end
local function observePlayerActions()
  if state.observedActions or not Observe then
    return
  end
  local okObs = pcall(function()
    Observe('PlayerPuppet', 'OnAction', function(_, action)
      handlePlayerAction(action)
    end)
  end)
  if okObs then
    state.observedActions = true
  end
end
local function applyTppMode(opts)
  state.enabled = true
end
local function restoreFppMode(immediate, headBackImmediate, forceRemove)
  state.enabled = false
end
local function updateSessionGuard(delta)
  if state.enabled then
    state.enabled = false
  end
end
registerInput("tpp_camera_toggle", "TPP", function(pressed)
  if pressed then applyTppMode() else restoreFppMode() end
end)
registerForEvent("onInit", function()
  observePlayerActions()
end)
'@
  if($drift){$source=$source.Replace('actionName == "MoveY"','actionName == "OtherMove"')}
  $path=Join-Path $owner 'init.lua'
  $source|Set-Content -LiteralPath $path -Encoding utf8
  $before=(Get-FileHash $path -Algorithm SHA256).Hash
  @{
    schemaVersion='2.0';callbacks=@(@{
      registrationId=1090;owner='immersive_third_person';infrastructure=$false
      kind='Observe';target='PlayerPuppet::OnAction'
      source=@{file='init.lua';lineStart=32;lineEnd=39}
      callsPerSecond=1305;exclusiveMsPerSecond=0.316;globalWorkSharePct=0.5;familyWorkSharePct=100
      avgExclusiveUs=0.3;maxExclusiveMs=0.004;spikeCount=0
    });optimizerEvidence=@()
  }|ConvertTo-Json -Depth 20|Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
  $result=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  $transforms=@()
  if($result.pass -and $result.pass.ManifestPath){
    $manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
    $transforms=@($manifest.transforms|Where-Object {$_.RuleId -eq 'immersive-third-person-action-routing'})
  }
  if($drift){
    if($transforms.Count -gt 0){throw 'Changed action interest must fail closed'}
    $global:LASTEXITCODE=0
    Write-Host 'Changed third-person action source refused: PASS'
    return
  }
  if($transforms.Count -ne 1){throw "Third-person pristine source did not receive one-pass exact action routing: $($result|ConvertTo-Json -Depth 4)"}
  $patched=FromPass $result.pass.ZipPath
  if(!$patched.Contains('__gcetItppRoutedOnActionV1') -or !$patched.Contains('zeroEngineMod.SubscribeAction') -or
     !$patched.Contains('__gcetItppActionDormancyV2') -or
     !$patched.Contains('decodeType = false, active = state.enabled == true') -or
     !$patched.Contains('handle.SetActive(state.enabled == true)') -or
     !$patched.Contains("Observe('PlayerPuppet', 'OnAction'") -or
     !$patched.Contains('handlePlayerAction(action)') -or
     !$patched.Contains('registerInput("tpp_camera_toggle"')) {
    throw 'Routed source lost v1 fallback, v2 dormancy or mod-owned CET wake key'
  }
  if(([regex]::Matches($patched,'__gcetItppActionHandle\s*=\s*handle')).Count -ne 2 -or
     ([regex]::Matches($patched,'__gcetItppSyncActionDormancy\(\)')).Count -ne 4) {
    throw 'Native subscriber handle or exact TPP/FPP/session lifecycle gates changed'
  }
  if((Get-FileHash $path -Algorithm SHA256).Hash -ne $before){throw 'Resolver modified live source'}
  # Rehydrate an earlier v1-only routed source without adding a separate
  # semantic rule. The same rule upgrades it directly to v2.
  $v1=$patched
  $v1=[regex]::Replace($v1,'(?s)-- __gcetItppActionDormancyV2:.*?(?=local function observePlayerActions\(\))','')
  $v1=$v1.Replace(', active = state.enabled == true','')
  $v1=[regex]::Replace($v1,'(?m)^    if routed then __gcetItppActionHandle = handle end\r?\n','')
  $v1=[regex]::Replace($v1,'(?m)^[ \t]*__gcetItppSyncActionDormancy\(\)\r?\n','')
  if(!$v1.Contains('__gcetItppRoutedOnActionV1') -or $v1.Contains('__gcetItppActionDormancyV2')) {
    throw 'Historical v1-only action fixture was not formed'
  }
  $v1|Set-Content -LiteralPath $path -Encoding utf8
  $upgrade=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  if(!$upgrade.ok -or !$upgrade.pass -or !$upgrade.pass.ManifestPath) {
    $report=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
    $semantic=@($report.callbackFamilies|ForEach-Object {$_.topConsumers}|
      Where-Object {$_.semantic.RuleId -eq 'immersive-third-person-action-routing'})
    throw ("v1 upgrade refused despite expected source evidence: " +
      ($upgrade|ConvertTo-Json -Depth 8 -Compress) +
      " semantic=" + ($semantic|ConvertTo-Json -Depth 10 -Compress) +
      " v1statewriters=" + ([regex]::Matches($v1,'state\.enabled\s*=\s*(?:true|false)')).Count +
      " handleLines=" + ([regex]::Matches($v1,'routed\s*=\s*ok and type\(handle\)')).Count +
      " routePresent=" + $v1.Contains('__gcetItppRoutedOnActionV1'))
  }
  $upgradeManifest=Get-Content $upgrade.pass.ManifestPath -Raw|ConvertFrom-Json
  if(@($upgradeManifest.transforms|Where-Object {$_.RuleId -eq 'immersive-third-person-action-routing'}).Count -ne 1) {
    throw 'Existing v1 action route must upgrade its missing v2 stage'
  }
  $upgraded=FromPass $upgrade.pass.ZipPath
  # Upgraded v1 is a separately formatted input. Prove identical finite
  # routing and lifecycle postconditions rather than byte-equality of a
  # historical source file after preserving its original cosmetic layout.
  $originalActions=[regex]::Match($patched,'local actions = \{(?<names>[^\}]+)\}')
  $upgradedActions=[regex]::Match($upgraded,'local actions = \{(?<names>[^\}]+)\}')
  if(!$originalActions.Success -or !$upgradedActions.Success -or
      $originalActions.Groups['names'].Value -ne $upgradedActions.Groups['names'].Value -or
      !$upgraded.Contains('__gcetItppActionDormancyV2') -or
      !$upgraded.Contains('handle.SetActive(state.enabled == true)') -or
      ([regex]::Matches($upgraded,'__gcetItppActionHandle\s*=\s*handle')).Count -ne 2 -or
      ([regex]::Matches($upgraded,'__gcetItppSyncActionDormancy\(\)')).Count -ne 4 -or
      ([regex]::Matches($upgraded,"Observe\('PlayerPuppet', 'OnAction'")).Count -ne 1 -or
      !$upgraded.Contains('registerInput("tpp_camera_toggle"')) {
    throw 'Legacy v1 upgrade lost exact action interest, original fallback, author wake or new lifecycle stage'
  }

  $patched|Set-Content -LiteralPath $path -Encoding utf8
  $again=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  if($again.pass -and $again.pass.ManifestPath){
    $newManifest=Get-Content $again.pass.ManifestPath -Raw|ConvertFrom-Json
    if(@($newManifest.transforms|Where-Object {$_.RuleId -eq 'immersive-third-person-action-routing'}).Count -gt 0){throw 'Non-idempotent action routing'}
  }
  $global:LASTEXITCODE=0
  Write-Host 'Third-person OnAction pristine one-pass, fallback and rerun contract: PASS'
}
TestVariant $false
TestVariant $true

# A newly introduced source-side state writer outside audited TPP/FPP/
# session paths invalidates the lifecycle proof.
$root=Join-Path $env:RUNNER_TEMP 'gcet-itpp-onaction-pristine'
$path=Join-Path $root 'mods/immersive_third_person/init.lua'
$v2=Get-Content -LiteralPath $path -Raw
$bad=$v2.Replace('-- __gcetItppActionDormancyV2:', 'state.enabled = false'+[Environment]::NewLine+'-- __gcetItppActionDormancyV2:')
$bad|Set-Content -LiteralPath $path -Encoding utf8
$cap=Join-Path $root 'capture'
$mods=Join-Path $root 'mods'
$check=(& $exe --capture $cap --mods $mods --json|ConvertFrom-Json)
$report=Get-Content (Join-Path $cap 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
$matching=@($report.callbackFamilies|ForEach-Object {$_.topConsumers}|
  Where-Object {$_.semantic.RuleId -eq 'immersive-third-person-action-routing'})
if($matching.Count -gt 0 -and $matching[0].semantic.AlreadySatisfied){
  throw 'Unexpected third-person state writer was incorrectly accepted as completed source'
}
$global:LASTEXITCODE=0
Write-Host 'Third-person v2 activation, original v1 upgrade and source-lifecycle drift contracts: PASS'
