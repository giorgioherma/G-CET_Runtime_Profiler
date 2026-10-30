param(
  [Parameter(Mandatory=$true)][string]$ResolverExe,
  [Parameter(Mandatory=$true)][string]$OriginalMods,
  [Parameter(Mandatory=$true)][string]$Capture,
  [Parameter(Mandatory=$true)][string]$GeneratedRoot
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $OriginalMods 'immersive_third_person'
$generated = Join-Path $GeneratedRoot 'bin/x64/plugins/cyber_engine_tweaks/mods/immersive_third_person'
if (!(Test-Path -LiteralPath (Join-Path $source 'init.lua'))) { throw 'ITP_SOURCE_MISSING' }
if (!(Test-Path -LiteralPath (Join-Path $generated 'GCET_Semantic/itp_frame_dormancy.lua'))) {
  throw 'ITP_GENERATED_WORKER_MISSING'
}
$root = Join-Path $env:RUNNER_TEMP 'gcet-itp-detached-staged'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Force -Recurse }
$stage = Join-Path $root 'mods'
$stageItp = Join-Path $stage 'immersive_third_person'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
  Copy-Item -LiteralPath (Join-Path $OriginalMods '0-Engine') -Destination (Join-Path $stage '0-Engine') -Recurse
  # Reconcile the actually emitted host overlay, not a stock host with no
  # shared schedule driver. Completed ITP source is only valid alongside it.
  $hostOverlay = Join-Path $GeneratedRoot 'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine'
  if (!(Test-Path -LiteralPath (Join-Path $hostOverlay 'init.lua'))) {
    throw 'ITP_COMPOSED_HOST_DEPENDENCY_MISSING'
  }
  Copy-Item -Path (Join-Path $hostOverlay '*') -Destination (Join-Path $stage '0-Engine') -Recurse -Force
  Copy-Item -LiteralPath $source -Destination $stageItp -Recurse
  Copy-Item -LiteralPath (Join-Path $generated 'init.lua') -Destination (Join-Path $stageItp 'init.lua') -Force
  $worker = Join-Path $stageItp 'GCET_Semantic/itp_frame_dormancy.lua'
  New-Item -Path (Split-Path $worker) -ItemType Directory -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $generated 'GCET_Semantic/itp_frame_dormancy.lua') -Destination $worker -Force
  $completeSourceRaw = Get-Content -LiteralPath (Join-Path $stageItp 'init.lua') -Raw
  $completeSource = $completeSourceRaw.Replace("`r`n","`n")
  $completeWorker = Get-Content -LiteralPath $worker -Raw
  if ($completeSource.Contains('pcall(controller.Register)') -or
      !$completeSource.Contains('local __gcetItppAttachOnInitV3' + [char]10 + 'registerForEvent("onInit", function()') -or
      !$completeSource.Contains('__gcetItppAttachOnInitV3 = function()') -or
      $completeSource.Contains('local function __gcetItppAttachOnInitV3()') -or
      $completeSource.IndexOf('local __gcetItppAttachOnInitV3' + [char]10) -gt
        $completeSource.IndexOf('registerForEvent("onInit", function()') -or
      $completeSource.IndexOf('  __gcetItppAttachOnInitV3()') -gt
        $completeSource.IndexOf('__gcetItppAttachOnInitV3 = function()') -or
      !$completeSource.Contains('local ready = controller.Register()') -or
      !$completeSource.Contains('if not __gcetItppWorkerFactoryV3 then') -or
      !$completeSource.Contains('mod.loadModule("GCET_Semantic/itp_frame_dormancy")') -or
      $completeSource.Contains('return dofile("GCET_Semantic/itp_frame_dormancy.lua").Install') -or
      !$completeSource.Contains('[G-CET ITP] detached frame fallback:') -or
      !$completeSource.Contains('  __gcetItppAttachOnInitV3()' + "`n" +
        '  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end' + "`n" + 'end)') -or
      $completeSource.IndexOf('local ready = controller.Register()') -gt
        $completeSource.IndexOf('local UI = {') -or
      !$completeWorker.Contains('function M.Register()') -or
      !$completeWorker.Contains('active=false') -or
      $completeWorker.Contains('handle.Cancel()')) {
    throw 'ITP_GENERATED_FILE_LOAD_ORDER_NOT_PROVEN'
  }

  function ResolveStaged([string]$case) {
    $lines = @(& $ResolverExe --capture $Capture --mods $stage --generate-pass --json 2>&1)
    $json = $null
    foreach ($line in $lines) {
      try {
        $candidate = ([string]$line) | ConvertFrom-Json -ErrorAction Stop
        if ($null -ne $candidate.ok) { $json = $candidate; break }
      } catch {}
    }
    if ($null -eq $json) { throw "ITP_STAGE_${case}: Resolver gave no machine-readable response" }
    $manifest = $null
    if ($json.pass -and $json.pass.ManifestPath -and (Test-Path -LiteralPath $json.pass.ManifestPath)) {
      $manifest = Get-Content -LiteralPath $json.pass.ManifestPath -Raw | ConvertFrom-Json
    }
    return @{ Result = $json; Manifest = $manifest }
  }

  # Real deployed V3 had a local defined after the onInit callback.
  # It resolved as a nil global. Prove staged source-only recovery of that
  # exact generator-owned defect without changing the working external worker.
  $forwardAnchor = 'local __gcetItppAttachOnInitV3' + [char]10 +
    'registerForEvent("onInit", function()'
  if ([regex]::Matches($completeSource,[regex]::Escape($forwardAnchor)).Count -ne 1) {
    throw 'ITP_FORWARD_LOCAL_NOT_UNIQUE'
  }
  $brokenV3 = $completeSource.Replace($forwardAnchor,'registerForEvent("onInit", function()')
  $brokenV3 = $brokenV3.Replace('__gcetItppAttachOnInitV3 = function()',
    'local function __gcetItppAttachOnInitV3()')
  [IO.File]::WriteAllText((Join-Path $stageItp 'init.lua'),$brokenV3,[Text.Encoding]::UTF8)
  $v3 = ResolveStaged 'BROKEN_V3_FORWARD_REFERENCE'
  if (!$v3.Result.ok -or !$v3.Manifest) { throw 'ITP_V3_FORWARD_LOCAL_NOT_REPAIRED' }
  $v3rules = @($v3.Manifest.transforms | Where-Object {
    $_.RuleId -eq 'immersive-third-person' -and $_.type -eq 'SEMANTIC_RULE'
  })
  if ($v3rules.Count -ne 1 -or @($v3rules[0].files).Count -ne 1 -or
      !([string]@($v3rules[0].files)[0]).Replace('\','/').Equals(
        'immersive_third_person/init.lua',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'ITP_V3_FORWARD_LOCAL_MIGRATION_REWROTE_WORKER'
  }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $v3zip = [IO.Compression.ZipFile]::OpenRead($v3.Result.pass.ZipPath)
  try {
    $entry = @($v3zip.Entries | Where-Object {
      $_.FullName.Replace('\','/').EndsWith('/immersive_third_person/init.lua')
    }) | Select-Object -First 1
    if (!$entry) { throw 'ITP_V3_FORWARD_LOCAL_OUTPUT_MISSING' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $v3actual = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $crlf = ([string][char]13 + [string][char]10)
    $lf = [string][char]10
    if ($v3actual.Replace($crlf,$lf) -cne $completeSource) {
      throw 'ITP_V3_MIGRATION_OUTPUT_NOT_EQUAL_TO_PRISTINE'
    }
  } finally { $v3zip.Dispose() }
  # Restore the original staged input text, including its original CRLF layout.
  [IO.File]::WriteAllText((Join-Path $stageItp 'init.lua'),$completeSourceRaw,[Text.Encoding]::UTF8)

  $full = ResolveStaged 'COMPLETE'
  if ($full.Manifest -and
      @($full.Manifest.transforms | Where-Object { $_.RuleId -eq 'immersive-third-person' }).Count -gt 0) {
    throw 'ITP_FULLY_OPTIMIZED_REPEAT: duplicate ITP semantic transformation'
  }
  if ($full.Manifest -and
      @($full.Manifest.skipped | Where-Object {
        $_.RuleId -eq 'immersive-third-person' -and $_.category -eq 'GENERATION_FAILURE'
      }).Count -gt 0) {
    throw 'ITP_COMPLETE_STAGE_REFUSED: source proof incorrectly rejected'
  }
  if ((Get-Content -LiteralPath (Join-Path $stageItp 'init.lua') -Raw) -cne $completeSourceRaw -or
      (Get-Content -LiteralPath $worker -Raw) -cne $completeWorker) {
    throw 'ITP_STAGED_TEST_MUTATED_SOURCE: Resolver should be read-only'
  }

  Remove-Item -LiteralPath $worker -Force
  $partial = ResolveStaged 'MISSING_WORKER'
  if (!$partial.Result.ok -or !$partial.Manifest) {
    throw ('ITP_PARTIAL_WORKER_NOT_RECOVERED: ' + [string]$partial.Result.error)
  }
  $owners = @($partial.Manifest.transforms | Where-Object {
    $_.RuleId -eq 'immersive-third-person' -and $_.type -eq 'SEMANTIC_RULE'
  })
  if ($owners.Count -ne 1) {
    $relevantTransforms = @($partial.Manifest.transforms | Where-Object {
      $_.RuleId -like '*immersive-third-person*'
    } | Select-Object -First 8 RuleId, type, status, reason)
    $relevantSkips = @($partial.Manifest.skipped | Where-Object {
      $_.RuleId -like '*immersive-third-person*' -or
      [string]$_.owner -like '*immersive_third_person*'
    } | Select-Object -First 12 RuleId, category, reason)
    $admitted = @($partial.Manifest.transforms | Where-Object { $_.type -eq 'SEMANTIC_RULE' }).Count
    Write-Host ("ITP_STAGE_ADMISSION_DEBUG: resultOk={0} semanticTransforms={1} itpTransforms={2} itpSkips={3}" -f
      $partial.Result.ok, $admitted, ($relevantTransforms | ConvertTo-Json -Compress -Depth 5),
      ($relevantSkips | ConvertTo-Json -Compress -Depth 5))
    throw "ITP_PARTIAL_WORKER_NOT_GENERATED: expected one owned rule, got $($owners.Count)"
  }
  # The missing-worker upgrade must not re-emit unchanged author init.lua.
  $changedFiles = @($owners[0].files)
  if ($changedFiles.Count -ne 1 -or
      !([string]$changedFiles[0]).Replace('\\','/').Equals(
        'immersive_third_person/GCET_Semantic/itp_frame_dormancy.lua',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ITP_WORKER_ONLY_RECOVERY_REWROTE_AUTHOR_SOURCE'
  }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip = [IO.Compression.ZipFile]::OpenRead($partial.Result.pass.ZipPath)
  try {
    $outWorker = @($zip.Entries | Where-Object {
      $_.FullName.Replace('\','/').EndsWith('/immersive_third_person/GCET_Semantic/itp_frame_dormancy.lua')
    })
    if ($outWorker.Count -ne 1) { throw 'ITP_PARTIAL_WORKER_PATH_OR_COUNT' }
    $reader = [IO.StreamReader]::new($outWorker[0].Open())
    try { $actual = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($actual.Replace("`r`n","`n") -cne $completeWorker.Replace("`r`n","`n")) {
      throw 'ITP_PARTIAL_WORKER_BEHAVIOR_DRIFT'
    }
  } finally { $zip.Dispose() }

  # Rebuild the previous known file-load bridge as a byte-faithful staged
  # source fixture; the new source must migrate without reintroducing a
  # native onUpdate registration from onInit.
  $newBegin = $completeSource.IndexOf('local __gcetItppWorkerFactoryV3 = nil')
  $newEnd = $completeSource.IndexOf("`n`nlocal UI = {", $newBegin)
  $ownerPattern = '(?m)^  (?<reg>registerForEvent|__gcetRegisterEvent_\d+)\((?<q>["''])(?:onUpdate)\k<q>, __gcetItppFrameStepV1\)$'
  $callback = [regex]::Matches($completeSource.Substring($newBegin,$newEnd-$newBegin),$ownerPattern)
  if ($newBegin -lt 0 -or $newEnd -lt 0 -or $callback.Count -ne 1) {
    throw 'ITP_GENERATED_SOURCE_OWNER_AMBIGUOUS'
  }
  $registrar = $callback[0].Groups['reg'].Value
  $quote = $callback[0].Groups['q'].Value
  $currentInit = '  __gcetItppAttachOnInitV3()' + "`n" +
    '  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end' + "`n" + 'end)'
  $previousInit = '  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end' + "`n" + 'end)'
  if ([regex]::Matches($completeSource,[regex]::Escape($currentInit)).Count -ne 1) {
    throw 'ITP_CURRENT_ONINIT_NOT_UNIQUE'
  }
  $oldBridge = @'
local __gcetItppRuntimeV1 = nil
local __gcetItppLoadFailureV1 = "0-Engine scoped schedule unavailable at module load"
do
  local ok, host = pcall(GetMod, "0-Engine")
  if ok and type(host) == "table" and type(host.__gcetScheduleDriver) == "function"
     and type(host.Register) == "function" then
    local okClient, client = pcall(host.Register, "immersive_third_person")
    if okClient and type(client) == "table" and type(client.Schedule) == "table"
       and type(client.Schedule.EveryFrame) == "function" then
      __gcetItppRuntimeV1 = client
    end
  end
end
if __gcetItppRuntimeV1 then
  local ok, controller = pcall(function()
    local worker, reason = mod.loadModule("GCET_Semantic/itp_frame_dormancy")
    if type(worker) ~= "table" or type(worker.Install) ~= "function" then
      error(reason or "ITP external worker interface missing")
    end
    return worker.Install(
      mod, state, config, __gcetItppFrameStepV1, __gcetItppRuntimeV1)
  end)
  if ok and controller and type(controller.Register) == "function" then
    local registered, ready = pcall(controller.Register)
    if registered and ready == true then
      mod.__gcetItppDormancyV1 = controller
    else
      __gcetItppLoadFailureV1 = "frame Register failed: " .. tostring(ready)
    end
  else
    __gcetItppLoadFailureV1 = "worker failed to load: " .. tostring(controller)
  end
end
if not mod.__gcetItppDormancyV1 then
  if type(print) == "function" then
    print("[G-CET ITP] detached frame fallback: " .. tostring(__gcetItppLoadFailureV1))
  end
  __REGISTER__("onUpdate", __gcetItppFrameStepV1)
end
'@.TrimEnd().Replace('__REGISTER__',$registrar)
  if ($quote -ne '"') { $oldBridge=$oldBridge.Replace('"onUpdate"',$quote+'onUpdate'+$quote) }
  $oldSource = $completeSource.Substring(0,$newBegin) +
    $oldBridge + $completeSource.Substring($newEnd)
  $oldSource = $oldSource.Replace($currentInit,$previousInit)
  $oldSource = $oldSource.Replace($forwardAnchor,'registerForEvent("onInit", function()')
  [IO.File]::WriteAllText((Join-Path $stageItp 'init.lua'),$oldSource,[Text.Encoding]::UTF8)
  [IO.File]::WriteAllText($worker,$completeWorker,[Text.Encoding]::UTF8)
  $upgrade = ResolveStaged 'KNOWN_FILE_LOAD'
  if (!$upgrade.Result.ok -or !$upgrade.Manifest -or
      @($upgrade.Manifest.transforms | Where-Object {
        $_.RuleId -eq 'immersive-third-person' -and $_.type -eq 'SEMANTIC_RULE'
      }).Count -ne 1) {
    $skip=@($upgrade.Manifest.skipped | Where-Object { $_.RuleId -eq 'immersive-third-person' } |
      Select-Object -First 3 reason)
    Write-Host ("ITP_HISTORICAL_MIGRATION_DEBUG: " + ($skip|ConvertTo-Json -Compress -Depth 3))
    throw 'ITP_PREVIOUS_FILE_LOAD_NOT_MIGRATED'
  }
  $changedFiles = @(@($upgrade.Manifest.transforms | Where-Object {
    $_.RuleId -eq 'immersive-third-person' })[0].files)
  if ($changedFiles.Count -ne 1 -or
      !([string]$changedFiles[0]).Replace('\','/').Equals(
        'immersive_third_person/init.lua',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'ITP_MIGRATION_REWROTE_COMPLETE_WORKER'
  }
  $zip=[IO.Compression.ZipFile]::OpenRead($upgrade.Result.pass.ZipPath)
  try {
    $entry=@($zip.Entries | Where-Object {
      $_.FullName.Replace('\','/').EndsWith('/immersive_third_person/init.lua')
    }) | Select-Object -First 1
    if (!$entry) { throw 'ITP_MIGRATION_MISSING_UPDATED_INIT' }
    $reader=[IO.StreamReader]::new($entry.Open())
    try { $actualUpgrade=$reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($actualUpgrade.Replace("`r`n","`n") -cne $completeSource) {
      throw 'ITP_MIGRATION_DID_NOT_RECONSTRUCT_CURRENT_SOURCE'
    }
  } finally { $zip.Dispose() }
  [IO.File]::WriteAllText((Join-Path $stageItp 'init.lua'),$completeSource,[Text.Encoding]::UTF8)
  Set-Content -LiteralPath $worker -Value '-- incompatible ITP worker source drift' -Encoding utf8
  $drift = ResolveStaged 'DRIFT'
  if ($drift.Manifest -and @($drift.Manifest.transforms | Where-Object {
    $_.RuleId -eq 'immersive-third-person' -and $_.type -eq 'SEMANTIC_RULE'
  }).Count -gt 0) {
    throw 'ITP_UNSAFE_WORKER_DRIFT_WAS_OVERWRITTEN'
  }
  # The Resolver records source-aware fail-closed refusals in the
  # semantic manifest as skipped SEMANTIC_RULE with an explicit reason.
  # The legacy 'category=GENERATION_FAILURE' predicate was never populated
  # on these entries and therefore rejected a correct hard refusal.
  $unsafeRefusals = @()
  if ($drift.Manifest) {
    $unsafeRefusals = @($drift.Manifest.skipped | Where-Object {
      $_.RuleId -eq 'immersive-third-person' -and
      $_.type -eq 'SEMANTIC_RULE' -and
      ([string]$_.reason).Contains('ITP dormant frame worker drifted')
    })
  }
  $explicitFailure = !$drift.Result.ok -and
    ([string]$drift.Result.error).Contains('ITP dormant frame worker drifted')
  if ($unsafeRefusals.Count -ne 1 -and !$explicitFailure) {
    $actualRefusals = @($drift.Manifest.skipped | Where-Object {
      $_.RuleId -eq 'immersive-third-person'
    } | Select-Object -First 3 RuleId, type, reason)
    Write-Host ("ITP_STAGE_DRIFT_DEBUG: resultOk={0} resultError={1} itpSkips={2}" -f
      $drift.Result.ok, $drift.Result.error,
      ($actualRefusals | ConvertTo-Json -Compress -Depth 4))
    throw 'ITP_UNSAFE_WORKER_DRIFT_NOT_REPORTED'
  }
  Write-Host 'ITP pristine/complete/missing worker/unsafe drift: one owned generator, structurally checked and read-only PASS'
} finally {
  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
