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
  if (!$completeSource.Contains('pcall(controller.Register)') -or
      !$completeSource.Contains('  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end' + "`n" + 'end)') -or
      $completeSource.IndexOf('pcall(controller.Register)') -lt
        $completeSource.IndexOf('  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end' + "`n" + 'end)') -or
      $completeSource.IndexOf('pcall(controller.Register)') -gt
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

  # A previous G-CET-generated v1 installation registered the job from
  # onInit. The SAME authoritative source generator must upgrade both the
  # preexisting bridge and worker to file-load-only registration.
  $legacyBridge = '  if ok and controller then mod.__gcetItppDormancyV1 = controller end'
  $newBridge = @'
  if ok and controller and type(controller.Register) == "function" then
    local registered, ready = pcall(controller.Register)
    if registered and ready == true then mod.__gcetItppDormancyV1 = controller end
  end
'@.TrimEnd()
  if (!$completeSource.Contains($newBridge) -or $completeSource.Contains($legacyBridge)) {
    throw 'ITP_LEGACY_BRIDGE_SETUP_FAILED'
  }
  $priorText = Get-Content 'src/G.CETProfiler.Core/Services/ModPasses/ImmersiveThirdPerson.cs' -Raw
  $legacyBlock = [regex]::Match($priorText,
    '(?s)private const string ItppLateRegisterWorkerV1 = """\r?\n(?<body>.*?)\r?\n    """;')
  if (!$legacyBlock.Success) { throw 'ITP_LEGACY_GENERATOR_WORKER_MISSING' }
  $priorWorker = (($legacyBlock.Groups['body'].Value -split "\r?\n" |
     ForEach-Object { if ($_.StartsWith('    ')) { $_.Substring(4) } else { $_ } }) -join "`n") + "`n"
  [IO.File]::WriteAllText((Join-Path $stageItp 'init.lua'),
    $completeSource.Replace($newBridge,$legacyBridge), [Text.Encoding]::UTF8)
  [IO.File]::WriteAllText($worker,$priorWorker,[Text.Encoding]::UTF8)
  $migrate = ResolveStaged 'KNOWN_LATE_REGISTER'
  if (!$migrate.Result.ok -or !$migrate.Manifest -or
      @($migrate.Manifest.transforms | Where-Object { $_.RuleId -eq 'immersive-third-person' }).Count -ne 1) {
    throw 'ITP_LEGACY_FILE_LOAD_MIGRATION_FAILED'
  }
  $migZip = [IO.Compression.ZipFile]::OpenRead($migrate.Result.pass.ZipPath)
  try {
    $migInit = @($migZip.Entries | Where-Object {
      $_.FullName.Replace('\\','/').EndsWith('/immersive_third_person/init.lua')}) | Select-Object -First 1
    $migWorker = @($migZip.Entries | Where-Object {
      $_.FullName.Replace('\\','/').EndsWith('/immersive_third_person/GCET_Semantic/itp_frame_dormancy.lua')}) | Select-Object -First 1
    if (!$migInit -or !$migWorker) { throw 'ITP_LEGACY_UPGRADE_DID_NOT_EMIT_BOTH' }
    foreach ($pair in @(@($migInit,$completeSource),@($migWorker,$completeWorker))) {
      $reader=[IO.StreamReader]::new($pair[0].Open())
      try { $emitted=$reader.ReadToEnd() } finally { $reader.Dispose() }
      if ($emitted.Replace("`r`n","`n") -cne ([string]$pair[1]).Replace("`r`n","`n")) {
        throw 'ITP_LEGACY_MIGRATION_OUTPUT_MISMATCH'
      }
    }
  } finally { $migZip.Dispose() }
  [IO.File]::WriteAllText((Join-Path $stageItp 'init.lua'),
    $completeSource,[Text.Encoding]::UTF8)
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
