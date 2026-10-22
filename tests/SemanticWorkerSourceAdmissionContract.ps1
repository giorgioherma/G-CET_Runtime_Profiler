param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-worker-admission'
if(Test-Path $root){Remove-Item -LiteralPath $root -Recurse -Force}
New-Item -ItemType Directory -Force (Join-Path $root 'ziggy_last_play') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root 'CyberStunts-StuntJumps-CET') | Out-Null
[IO.File]::WriteAllText((Join-Path $root 'ziggy_last_play/init.lua'),@'
local function __gcetZiggyOfferedFar() return false end
local function __gcetZiggyAttachNative() return true end
'@,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $root 'CyberStunts-StuntJumps-CET/init.lua'),@'
local __gcetStuntsFixedCount=#jumps
local function __gcetStuntsAttachNative() end
local function __gcetStuntsLocationSleeping() return false end
'@,[Text.UTF8Encoding]::new($false))
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticLibraryService',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$load=$t.GetMethod('Load',$flags)
$lib=$load.Invoke($null,[object[]]@([string](Join-Path (Get-Location) 'resolver-knowledge/semantic-library.json'),[string]$root))
$m=$t.GetMethod('CollectSourceIndependentCandidates',[Reflection.BindingFlags]'Instance,NonPublic')
$rows=@($m.Invoke($lib,@()))
$ids=@($rows|ForEach-Object{$_.RuleId})
foreach($id in @('ziggy-native-worker','cyberstunts-native-worker')){
 if($ids -notcontains $id){throw "Source-only shared callback candidate suppressed: $id; seen $($ids -join ',')"}
}
if($ids -contains 'native-location-ziggy-offered'){throw 'Unproven Ziggy native parent admitted'}
Write-Host 'Source-independent worker discovery admits exact shared-callback rules: PASS'
