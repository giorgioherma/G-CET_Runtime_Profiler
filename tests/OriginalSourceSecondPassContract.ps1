param(
    [Parameter(Mandatory=$true)][string]$ResolverExe,
    [Parameter(Mandatory=$true)][string]$OriginalMods,
    [Parameter(Mandatory=$true)][string]$Capture,
    [Parameter(Mandatory=$true)][string]$FirstOverlayRoot
)
$ErrorActionPreference='Stop'
# Private source is only copied to RUNNER_TEMP, never published or logged.
$root=Join-Path $env:RUNNER_TEMP ('gcet-repeat-'+[Guid]::NewGuid().ToString('N'))
$live=Join-Path $root 'mods'
try {
    New-Item -ItemType Directory -Force -Path $live | Out-Null
    Get-ChildItem -LiteralPath $OriginalMods -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $live -Recurse -Force
    }
    $overlay=Join-Path $FirstOverlayRoot 'bin/x64/plugins/cyber_engine_tweaks/mods'
    if (!(Test-Path -LiteralPath (Join-Path $overlay '0-Engine/init.lua'))) {
        throw 'REPEAT_PASS_INPUT_MISSING: no generated engine'
    }
    $copied=0
    Get-ChildItem -LiteralPath $overlay -Recurse -File | ForEach-Object {
        $relative=[IO.Path]::GetRelativePath($overlay,$_.FullName)
        $target=Join-Path $live $relative
        New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        $script:copied++
    }
    if($copied -lt 1){throw 'REPEAT_PASS_INPUT_MISSING: empty overlay'}
    $ggc=Join-Path $live 'G-GC/threshold.lua'
    $beforeGgc=if(Test-Path -LiteralPath $ggc){
        (Get-FileHash -LiteralPath $ggc -Algorithm SHA256).Hash
    }else{$null}
    $secondZip=Join-Path $root 'repeated.zip'
    $secondResult=(& $ResolverExe --capture $Capture --mods $live --generate-pass --pass-output $secondZip --json | ConvertFrom-Json)
    if($LASTEXITCODE -ne 0 -or !$secondResult.ok -or !$secondResult.pass){
        throw 'SECOND_PASS_GENERATION_FAILED'
    }
    $manifest=Join-Path $root 'repeated.json'
    if(!(Test-Path -LiteralPath $manifest)){throw 'SECOND_PASS_MANIFEST_MISSING'}
    $second=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $analysis = Get-Content -LiteralPath $secondResult.ResolverPath -Raw | ConvertFrom-Json
    $readySemantic = @($analysis.semanticSourceOnlyCandidates | Where-Object {
        $_.sourceProofSatisfied -and !$_.alreadySatisfied
    } | ForEach-Object { [string]$_.owner + '/' + [string]$_.ruleId })
    $readyGeneric = @($analysis.callbackFamilies | ForEach-Object {
        $_.topConsumers
    } | Where-Object { $_.generic.Automatable } | ForEach-Object {
        [string]$_.owner + '/' + [string]$_.registrationId + '/' +
        (@($_.generic.RecipeFamilies) -join '+')
    })
    if ([int]$secondResult.SemanticReadyRuleCount -ne 0 -or
        [int]$secondResult.GenericResolvedCount -ne 0 -or
        [int]$secondResult.OptimizationCoverage.ReadyRuleCount -ne 0) {
        throw ('SECOND_PASS_ANALYZE_STILL_ADVERTISES_COMPLETE_RULES: semantic=' +
            $secondResult.SemanticReadyRuleCount + ' generic=' +
            $secondResult.GenericResolvedCount + ' catalog=' +
            $secondResult.OptimizationCoverage.ReadyRuleCount +
            ' semanticIds=' + ($readySemantic -join ',') +
            ' genericIds=' + ($readyGeneric -join ','))
    }
    $new=@($second.transforms)
    $fail=@($second.skipOutcomes.entries | Where-Object {$_.category -eq 'GENERATION_FAILURE'})
    if($fail.Count -gt 0){
        $types=@($fail | ForEach-Object {$_.type} | Select-Object -Unique)
        throw ("SECOND_PASS_COMPLETION_FALSE_FAILURE: count=$($fail.Count) types=$($types -join ',')")
    }
    if($new.Count -gt 0){
        $types=@($new | ForEach-Object {$_.type} | Select-Object -Unique)
        throw ("SECOND_PASS_DUPLICATED_TRANSFORMS: count=$($new.Count) types=$($types -join ',')")
    }
    if(@($second.files | Where-Object {$_.path -like 'G-GC/*'}).Count -gt 0){
        throw 'SECOND_PASS_REGENERATED_UNCHANGED_GGC'
    }
    if($beforeGgc -ne $null -and $beforeGgc -ne
       (Get-FileHash -LiteralPath $ggc -Algorithm SHA256).Hash){
        throw 'SECOND_PASS_CHANGED_LIVE_GGC'
    }
    # A newly added owner that was absent from the old profiler capture
    # must still get generic source-first optimization.
    $owner='GCET_NewInstall_Regression'
    $dir=Join-Path $live $owner
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'init.lua'),@'
registerForEvent("onUpdate", function()
    local player = Game.GetPlayer()
    if player then return true end
end)
'@,[Text.UTF8Encoding]::new($false))
    $newZip=Join-Path $root 'new-owner.zip'
    $newResult=(& $ResolverExe --capture $Capture --mods $live --generate-pass --pass-output $newZip --json | ConvertFrom-Json)
    if($LASTEXITCODE -ne 0 -or !$newResult.ok -or !$newResult.pass){
        throw 'NEW_OWNER_SOURCE_FIRST_GENERATION_FAILED'
    }
    $newManifest=Get-Content -LiteralPath (Join-Path $root 'new-owner.json') -Raw | ConvertFrom-Json
    $adopted=@($newManifest.transforms | Where-Object {
        $_.file -eq "$owner/init.lua" -and $_.type -eq 'NATIVE_READ_REUSE'
    })
    if($adopted.Count -ne 1){throw 'UNMEASURED_NEW_OWNER_GENERIC_ADOPTION_FAILED'}
    $other=@($newManifest.transforms | Where-Object {$_.file -ne "$owner/init.lua"})
    if($other.Count -gt 0){throw 'NEW_OWNER_PASS_REGENERATED_COMPLETED_OTHER_MODS'}
    Write-Host 'Repeated first-pass sources: 0 new, 0 failures; unmeasured new owner generic adoption PASS'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
