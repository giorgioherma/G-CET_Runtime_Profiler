param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom(
 (Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$type=$assembly.GetType('GCETRuntimeProfiler.Core.Services.PassSkipSummaryService',$true)
$method=$type.GetMethod('FromManifestFile')
if(!$method){throw 'Pass skip outcome classifier is missing'}
$folder=Join-Path $env:RUNNER_TEMP 'gcet-generation-diagnostics'
New-Item -ItemType Directory -Force $folder|Out-Null
$path=Join-Path $folder 'sample.json'
$items=[Collections.Generic.List[object]]::new()
foreach($file in @('CourierJobs/init.lua','Dedrapanamgoondate/init.lua',
    'hotscenes/hotscenes_nc_delights.lua','hotscenes/init.lua','InfiniteGigs/init.lua')) {
  $items.Add(@{type='NATIVE_READ_REUSE';file=$file;reason='LUA_LOCAL_LIMIT'})
}
foreach($owner in @('ImmersiveFirstPerson','GigCore','Card Collection',
    'tunnel_rescue','yaibaPenthouse','ziggy_last_play')) {
  $items.Add(@{type='SEMANTIC_RULE';Owner=$owner;
    RuleId='sample';reason='Source stage already complete.'})
}
[IO.File]::WriteAllText($path,(@{skipped=$items}|ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
$summary=$method.Invoke($null,[object[]]@([string]$path))
if($summary.AlreadyImplementedCount -ne 6 -or
   $summary.LuaLocalLimitCount -ne 5 -or
   $summary.GenerationFailureCount -ne 0 -or
   $summary.TotalNotApplied -ne 11) {
    throw 'Current pass taxonomy should report 6 already implemented, 5 Lua deferrals, 0 failures'
}
$display=$summary.FormatForUi()
foreach($needle in @('ALREADY IMPLEMENTED:','LUA LOCAL LIMIT','Generation failures','CourierJobs/init.lua')) {
 if(!$display.Contains($needle)){throw "GUI diagnostic missing $needle"}
}
$items.Add(@{type='NATIVE_READ_REUSE';file='sample/already_partly_optimized.lua';
  reason='LUA_LOCAL_LIMIT_PARTIAL'})
$items.Add(@{type='SEMANTIC_RULE';Owner='Marmur Bank';
  RuleId='marmurbank-modern-input';
  reason='Semantic injection failed closed: source callback graph changed.'})
$items.Add(@{type='SEMANTIC_RULE';Owner='Dedka';
  RuleId='native-location-dedka';
  reason='Partial source conflict: already installed marker but missing worker.'})
[IO.File]::WriteAllText($path,(@{skipped=$items}|ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
$bad=$method.Invoke($null,[object[]]@([string]$path))
if($bad.AlreadyImplementedCount -ne 6 -or
   $bad.LuaLocalLimitCount -ne 6 -or
   $bad.GenerationFailureCount -ne 2 -or
   $bad.TotalNotApplied -ne 14) {
    throw 'Source incompatibility and partial state must be generation failures'
}
if(!$bad.FormatForUi().Contains('Marmur Bank/marmurbank-modern-input')) {
    throw 'Failure details lost owner/rule identity'
}
Write-Host 'Pass diagnostics: 6 completed + 5 Lua deferred + 0 failures; source drift and partial stages are failures: PASS'
