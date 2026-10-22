param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$policy=$asm.GetType('GCETRuntimeProfiler.Core.Services.ModOptimizationBlacklist',$true)
$flags=[Reflection.BindingFlags]'Public,Static'
$owner=$policy.GetMethod('IsBlacklistedOwner',$flags)
$path=$policy.GetMethod('IsBlacklistedPath',$flags)
foreach($name in @('marmurbank','Marmur Bank','Marmur Bank Overhaul 21b','marmurbank-v22.5','MARMUR_BANK_20')) {
  if(!$owner.Invoke($null,[object[]]@([string]$name))){throw "Blacklisted owner escaped: $name"}
}
foreach($name in @('OtherBank','marmurbankTools','AnotherMod')) {
  if($owner.Invoke($null,[object[]]@([string]$name))){throw "Unrelated owner was blacklisted: $name"}
}
foreach($file in @('marmurbank/init.lua','Marmur Bank Overhaul 21b/module/Bank.lua','MARMUR_BANK_22.5/external/GameUI.lua')) {
  if(!$path.Invoke($null,[object[]]@([string]$file))){throw "Blacklisted path escaped: $file"}
}
if($path.Invoke($null,[object[]]@([string]'Other/mods/marmurbank/init.lua'))){throw 'Unrelated owner blocked'}
$root=Join-Path $env:RUNNER_TEMP 'gcet-bank-blacklist'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$bank=Join-Path $root 'marmurbank'
$other=Join-Path $root 'UnrelatedMod'
New-Item -ItemType Directory -Force $bank,$other|Out-Null
[IO.File]::WriteAllText((Join-Path $bank 'init.lua'),'local player = Game.GetPlayer()')
[IO.File]::WriteAllText((Join-Path $other 'init.lua'),'local player = Game.GetPlayer()')
$detected=@($policy.GetMethod('FindInstalledOwners',$flags).Invoke($null,[object[]]@([string]$root)))
if($detected.Count -ne 1 -or $detected[0] -ne 'marmurbank'){throw 'Installed owner detection failed'}
$read=$asm.GetType('GCETRuntimeProfiler.Core.Services.NativeReadReuseSource',$true)
$analyze=$read.GetMethod('Analyze',[Reflection.BindingFlags]'Static,NonPublic')
$plans=@($analyze.Invoke($null,[object[]]@([string]$root)))
if(@($plans|Where-Object{$_.RelativeFile -like 'marmurbank/*'}).Count -ne 0) {
  throw 'Blacklisted source entered generic read analysis'
}
$doc=[System.Text.Json.Nodes.JsonNode]::Parse(@'
{
 "callbackFamilies":[{"topConsumers":[
  {"owner":"marmurbank","generic":{"Automatable":true,"Status":"READY"},"semantic":{"GenerationEnabled":true}},
  {"owner":"UnrelatedMod","generic":{"Automatable":true,"Status":"READY"}}
 ]}],
 "semanticSourceOnlyCandidates":[
  {"owner":"marmurbank","generationEnabled":true},{"owner":"UnrelatedMod","generationEnabled":true}
 ],
 "sharedProviderOpportunities":[{"callbacks":[
  {"owner":"marmurbank","substitutionEligible":true},
  {"owner":"UnrelatedMod","substitutionEligible":true}
 ]}]
}
'@)
$apply=$policy.GetMethod('ApplyToResolver',[Reflection.BindingFlags]'Static,NonPublic')
$apply.Invoke($null,[object[]]@($doc,[string[]]@('marmurbank'))) | Out-Null
function Flag($node) { return [bool]::Parse($node.ToString()) }
if(Flag $doc['callbackFamilies'][0]['topConsumers'][0]['generic']['Automatable']){throw 'Blacklisted generic candidate stayed ready'}
if(Flag $doc['callbackFamilies'][0]['topConsumers'][0]['semantic']['GenerationEnabled']){throw 'Blacklisted semantic candidate stayed ready'}
if(Flag $doc['semanticSourceOnlyCandidates'][0]['generationEnabled']){throw 'Blacklisted source-only stage stayed ready'}
if(Flag $doc['sharedProviderOpportunities'][0]['callbacks'][0]['substitutionEligible']){throw 'Blacklisted provider stayed ready'}
if(!(Flag $doc['callbackFamilies'][0]['topConsumers'][1]['generic']['Automatable'])){throw 'Unrelated generic was blocked'}
if(!(Flag $doc['semanticSourceOnlyCandidates'][1]['generationEnabled'])){throw 'Unrelated semantic was blocked'}
if(!(Flag $doc['sharedProviderOpportunities'][0]['callbacks'][1]['substitutionEligible'])){throw 'Unrelated provider was blocked'}
Write-Host 'Marmur Bank all-release blacklist: owner detection, generic scan, source/semantic/provider admission, unrelated owners PASS'
