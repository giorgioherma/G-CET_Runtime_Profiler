param([Parameter(Mandatory = $true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path -LiteralPath $exe)){throw "Resolver missing: $exe"}
$root=Join-Path $env:RUNNER_TEMP 'gcet-poker-pass4-original-source'
if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
$capture=Join-Path $root 'CET-20990101-040506_POKER_PASS4'
$mods=Join-Path $root 'mods'
$poker=Join-Path $mods 'StreetGamesPoker'
New-Item -ItemType Directory -Force $capture,$poker | Out-Null

# Resolver's actual pass contract requires the existing 0-Engine runtime.
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zero | Out-Null
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed=[Convert]::FromBase64String($encoded)
$inputStream=[System.IO.MemoryStream]::new($compressed)
$gzip=[System.IO.Compression.GZipStream]::new(
    $inputStream,[System.IO.Compression.CompressionMode]::Decompress)
$output=[System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray())
}
finally {
    $output.Dispose();$gzip.Dispose();$inputStream.Dispose()
}
$path=Join-Path $poker 'init.lua'
@'
StreetGamesPoker={ready=false}
local Logger={info=function() end}
local Cron={Update=function() end}
local SeatController={update=function() end}
local StreetTables={update=function() end,registerMapPins=function() end}
local InteractionMenu={update=function() end}
local CardRenderer={update=function() end}
local TableTalk={update=function() end}
local PokerHUD={draw=function() end}
local inMenu=true
local inGame=false
registerForEvent("onInit", function()
    StreetTables.registerMapPins()
    StreetGamesPoker.ready = true
end)
registerForEvent("onUpdate", function(dt)
    if not StreetGamesPoker.ready or inMenu or not inGame then return end
    Cron.Update(dt)
    SeatController.update(dt)
    StreetTables.update(dt)
    InteractionMenu.update()
    CardRenderer.update(dt)
    TableTalk.update(dt)
end)
registerForEvent("onDraw", function()
    PokerHUD.draw()
end)
'@ | Set-Content -LiteralPath $path -Encoding utf8
$input=@{schemaVersion='1.8';callbacks=@(
    @{registrationId=999;owner='StreetGamesPoker';infrastructure=$false;kind='event';target='onUpdate'
       source=@{file='init.lua';lineStart=17;lineEnd=24}
       callsPerSecond=60;exclusiveMsPerSecond=8;globalWorkSharePct=12
       familyWorkSharePct=80;avgExclusiveUs=130;maxExclusiveMs=4;spikeCount=0;maxSpikeExclusiveMs=0}
);optimizerEvidence=@()} | ConvertTo-Json -Depth 30
$input | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
function Generate {
    $result=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
    if(!$result.ok -or !$result.pass){throw 'PASS4 generation failed'}
    return $result
}
$first=Generate
$manifest=Get-Content -LiteralPath $first.pass.ManifestPath -Raw | ConvertFrom-Json
$transforms=@($manifest.transforms | Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'streetgamespoker'})
if($transforms.Count -ne 1){throw "Expected exactly one Poker semantic; got $($transforms.Count)"}
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$first.pass.ZipPath)
try{
    $entry=$zip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/init.lua')
    if(!$entry){throw 'PASS4 output missing'}
    $reader=[System.IO.StreamReader]::new($entry.Open())
    try{$generated=$reader.ReadToEnd()}finally{$reader.Dispose()}
}finally{$zip.Dispose()}
$forward=$generated.IndexOf('local __gcetPokerAttachPass4')
$onInit=$generated.IndexOf('registerForEvent("onInit"')
$assigned=$generated.IndexOf('__gcetPokerAttachPass4=function()')
$onUpdate=$generated.IndexOf('registerForEvent("onUpdate"')
if($forward -lt 0 -or $onInit -lt 0 -or $assigned -lt 0 -or $onUpdate -lt 0 -or
  $forward -ge $onInit -or $assigned -le $onInit -or $assigned -ge $onUpdate){
  throw 'PASS4 startup lexical scope/order incorrect'
}
foreach($token in @('poker.world_far','poker.world_near','poker.seat_near','poker.realtime',
  'Logger.info("pass4_scheduler_attached"','StreetTables.registerMapPins()','Cron.Update(dt)',
  'StreetTables.update(dt)','TableTalk.update(dt)','if pass4Attached then return end')){
    if(!$generated.Contains($token)){throw "Generated PASS4 missing $token"}
}
if($generated.Contains('log.info(') -or $generated.Contains('TryAttachPokerPass4()')){
    throw 'Invalid PASS4 logger or stale function call'
}
$generated | Set-Content -LiteralPath $path -Encoding utf8
$second=Generate
$manifest2=Get-Content -LiteralPath $second.pass.ManifestPath -Raw | ConvertFrom-Json
if(@($manifest2.transforms | Where-Object {$_.RuleId -eq 'streetgamespoker'}).Count -ne 0){
    throw 'Existing PASS4 optimization re-applied on subsequent run'
}
Write-Host 'Poker PASS4 generated from author source, with four lanes, lexical startup, logger, map pins, fallback and idempotence.'
