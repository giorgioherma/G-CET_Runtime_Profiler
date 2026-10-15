param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if (!(Test-Path $exe)) { throw 'Missing published G-CET Resolver' }
$root=Join-Path $env:RUNNER_TEMP 'gcet-native-marshalling-resolver-evidence'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$mods=Join-Path $root 'mods'
$capture=Join-Path $root 'capture'
$owner=Join-Path $mods 'NativeProbe'
New-Item -ItemType Directory -Force $capture,$owner | Out-Null
@'
Observe("PlayerPuppet", "OnAction", function(self, action, consumer)
    local name = Game.NameToString(action:GetName())
    if name == "Jump" then DummyAction() end
end)
'@ | Set-Content (Join-Path $owner 'init.lua') -Encoding utf8

$fixture=@{
 schemaVersion='2.0'
 callbacks=@(@{
  registrationId=9251;owner='NativeProbe';infrastructure=$false
  kind='Observe';target='PlayerPuppet::OnAction'
  source=@{file='init.lua';lineStart=1;lineEnd=4}
  calls=1269;callsPerSecond=1269;exclusiveMsPerSecond=0.01
  avgExclusiveUs=0.01;maxExclusiveMs=0.01;globalWorkSharePct=0.1
  familyWorkSharePct=100;spikeCount=0;maxSpikeExclusiveMs=0
 })
 optimizerEvidence=@()
 nativeMarshallingAllocationPressure=@{
  metric='NATIVE_TO_LUA_ARGUMENT_MARSHALLING_ALLOCATION_PRESSURE'
  nativeMethodHashRecorded=$true
  nativeClassHashRecorded=$true
  sampleCount=100
  onAction=@{
   observedMethodSampleCount=70
   observedClassQualifiedSampleCount=60
   observedPlayerPuppetSampleCount=55
  }
  methodGroups=@(@{
   nativeMethodHash='9658066991423262154'
   nativeClassHash='14897567717734570886'
   observedSamplePositiveBytes=120
   sampledEvents=55
   modOwner=$null
   subscriberRegistrationId=$null
  })
  automaticRewriteAuthorized=$false
 }
}
function Analyze {
    $fixture | ConvertTo-Json -Depth 35 |
        Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
    $result=& $exe --capture $capture --mods $mods --json | ConvertFrom-Json
    if (!$result.ok) { throw "Resolver analysis failed: $($result.error)" }
    return Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
}
$report=Analyze
if (!$report.nativeMarshallingAllocationPressure -or
    $report.nativeMarshallingAllocationPressure.metric -ne
        'NATIVE_TO_LUA_ARGUMENT_MARSHALLING_ALLOCATION_PRESSURE' -or
    !$report.nativeAllocationPressurePolicy.luaExclusiveCpuIsNotNativeMarshallingCost -or
    $report.summary.nativePlayerPuppetOnActionSamples -ne 55 -or
    $report.summary.nativeOnActionMethodSamples -ne 70) {
    throw 'Resolver did not preserve class-qualified native pressure handoff'
}
$rows=@($report.callbackFamilies | ForEach-Object {$_.topConsumers} |
    Where-Object {$_.owner -eq 'NativeProbe'})
if ($rows.Count -ne 1) { throw 'NativeProbe callback not present in Resolver analysis' }
$dispatch=$rows[0].runtime.nativeDispatch
if ($dispatch.allocationPressureStatus -ne 'CLASS_METHOD_SAMPLES_OBSERVED' -or
    !$dispatch.sameClassMethodSampled -or
    $dispatch.sharedPlayerPuppetOnActionNativeSamples -ne 55 -or
    $null -ne $dispatch.observedNativeBytesAttributedToThisSubscriber -or
    $dispatch.influencesSafetyAdmission -or
    $dispatch.nativeAllocationSavingsMeasured) {
    throw 'Native dispatch metric was mistaken for Lua CPU, owned bytes or admitted rewrite'
}
$fixture.nativeMarshallingAllocationPressure.nativeClassHashRecorded=$false
$fixture.nativeMarshallingAllocationPressure.onAction.observedClassQualifiedSampleCount=0
$fixture.nativeMarshallingAllocationPressure.onAction.observedPlayerPuppetSampleCount=0
$old=Analyze
$oldDispatch=@($old.callbackFamilies | ForEach-Object {$_.topConsumers} |
    Where-Object {$_.owner -eq 'NativeProbe'})[0].runtime.nativeDispatch
if ($oldDispatch.allocationPressureStatus -ne 'CLASS_NOT_RECORDED' -or
    $oldDispatch.sameClassMethodSampled -or
    $old.summary.nativePlayerPuppetOnActionSamples -ne 0) {
    throw 'Legacy method-only samples were falsely qualified as PlayerPuppet'
}
$fixture.Remove('nativeMarshallingAllocationPressure')
$absent=Analyze
$absentDispatch=@($absent.callbackFamilies | ForEach-Object {$_.topConsumers} |
    Where-Object {$_.owner -eq 'NativeProbe'})[0].runtime.nativeDispatch
if ($null -ne $absent.nativeMarshallingAllocationPressure -or
    $absentDispatch.allocationPressureStatus -ne 'NOT_CAPTURED') {
    throw 'Legacy missing native metric was falsely synthesized'
}
Write-Host 'Resolver class-qualified native marshalling metric: real/legacy/absent, no per-mod attribution, CPU separation PASS'
