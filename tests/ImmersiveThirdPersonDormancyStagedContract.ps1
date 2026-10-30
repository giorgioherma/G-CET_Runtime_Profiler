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
  $completeSource = Get-Content -LiteralPath (Join-Path $stageItp 'init.lua') -Raw
  $completeWorker = Get-Content -LiteralPath $worker -Raw

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
  if ((Get-Content -LiteralPath (Join-Path $stageItp 'init.lua') -Raw) -cne $completeSource -or
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
    throw "ITP_PARTIAL_WORKER_NOT_GENERATED: expected one owned rule, got $($owners.Count)"
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

  Set-Content -LiteralPath $worker -Value '-- incompatible ITP worker source drift' -Encoding utf8
  $drift = ResolveStaged 'DRIFT'
  if ($drift.Manifest -and @($drift.Manifest.transforms | Where-Object {
    $_.RuleId -eq 'immersive-third-person' -and $_.type -eq 'SEMANTIC_RULE'
  }).Count -gt 0) {
    throw 'ITP_UNSAFE_WORKER_DRIFT_WAS_OVERWRITTEN'
  }
  if ($drift.Result.ok -and $drift.Manifest -and
      @($drift.Manifest.skipped | Where-Object {
        $_.RuleId -eq 'immersive-third-person' -and $_.category -eq 'GENERATION_FAILURE'
      }).Count -eq 0) {
    throw 'ITP_UNSAFE_WORKER_DRIFT_NOT_REPORTED'
  }
  Write-Host 'ITP pristine/complete/missing worker/unsafe drift: one owned generator, structurally checked and read-only PASS'
} finally {
  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
