param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$ResolverRoot=(Resolve-Path $ResolverRoot).Path
$exe=Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if(!(Test-Path $exe)){throw 'Resolver executable missing'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Join-Path $env:RUNNER_TEMP 'gcet-yaiba-apartment-distant'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$cap=Join-Path $root 'capture'
$mods=Join-Path $root 'mods'
$apt=Join-Path $mods 'yaibaPenthouse'
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $cap,$apt,$zero | Out-Null
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
$compressed=[Convert]::FromBase64String($encoded)
$input=[IO.MemoryStream]::new($compressed)
$gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output=[IO.MemoryStream]::new()
try {$gzip.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray())}
finally {$gzip.Dispose();$input.Dispose();$output.Dispose()}

$original = @'
local ui = require("interactionUI")
local CONFIG = {
    position = { x = -1160.27, y = 1579.75, z = 75.74 },
    iconRange = 6.0,
}
local bathtub = { filled = false, hubVisible = false, pinID = nil, waterApplied = false }
local function buildHub() end
local function togglePin(near) end
function bathtub.update(player, pPos)
    ui.update()
    if not player or not pPos then return end
    if not CONFIG.position then return end
    local pos = CONFIG.position
    local near = true
    togglePin(near)
    if near and not bathtub.waterApplied then
        bathtub.waterApplied = true
    elseif not near then
        bathtub.waterApplied = false
    end
    if not bathtub.hubVisible then buildHub() end
end
return bathtub
'@
$file=Join-Path $apt 'bathtub.lua'
$inputJson=@{
 schemaVersion='2.0'
 callbacks=@(@{
  registrationId=1077;owner='yaibaPenthouse';infrastructure=$false
  kind='event';target='onUpdate'
  source=@{file='init.lua';lineStart=100;lineEnd=135}
  callsPerSecond=51.559;exclusiveMsPerSecond=3.660565
  globalWorkSharePct=1;familyWorkSharePct=100
  avgExclusiveUs=70.99;maxExclusiveMs=1.05;spikeCount=0
 })
 optimizerEvidence=@()
} | ConvertTo-Json -Depth 20
$inputJson | Set-Content -LiteralPath (Join-Path $cap 'CET_Resolver_Input.json') -Encoding utf8
'registerForEvent("onUpdate", function() bathtub.update(GetPlayer(), nil) end)' |
 Set-Content -LiteralPath (Join-Path $apt 'init.lua') -Encoding utf8
function Invoke-Variant([string]$label,[string]$source,[bool]$shouldApply) {
 $source | Set-Content -LiteralPath $file -Encoding utf8
 $before=(Get-FileHash $file -Algorithm SHA256).Hash
 $run=@(& $exe --capture $cap --mods $mods --generate-pass --json 2>$null)
 $result=if($run.Count){($run -join "`n") | ConvertFrom-Json}else{$null}
 if((Get-FileHash $file -Algorithm SHA256).Hash -ne $before){throw "${label}: live mod changed"}
 if(!$shouldApply) {
  if($result -and $result.ok){
   $manifest=Get-Content $result.pass.ManifestPath -Raw | ConvertFrom-Json
   if(@($manifest.transforms | Where-Object {$_.RuleId -eq 'yaiba-penthouse-distant-bathtub'}).Count){
    throw "${label}: repeated/drift rule unexpectedly applied"
   }
  }
  return $null
 }
 if(!$result -or !$result.ok){throw "${label}: no generated ZIP"}
 $manifest=Get-Content $result.pass.ManifestPath -Raw | ConvertFrom-Json
 $rules=@($manifest.transforms | Where-Object {
   $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'yaiba-penthouse-distant-bathtub'
 })
 if($rules.Count -ne 1){throw "${label}: expected one Yaiba production semantic, got $($rules.Count)"}
 if(@($rules[0].files).Count -ne 1){throw "${label}: non-bathtub file touched"}
 $zip=[IO.Compression.ZipFile]::OpenRead([string]$result.pass.ZipPath)
 try {
  $entry=@($zip.Entries | Where-Object {
   $_.FullName.Replace('\','/').EndsWith('yaibaPenthouse/bathtub.lua',[StringComparison]::OrdinalIgnoreCase)
  }) | Select-Object -First 1
  if(!$entry){throw "${label}: patch missing bathtub.lua"}
  $reader=[IO.StreamReader]::new($entry.Open())
  try{$new=$reader.ReadToEnd()}finally{$reader.Dispose()}
 } finally{$zip.Dispose()}
 foreach($token in @('G-CET semantic:yaiba-penthouse-distant-bathtub',
 'not bathtub.hubVisible and not bathtub.pinID and not bathtub.waterApplied',
 '__gcetBathPos = CONFIG.position','ui.update()','togglePin(near)')){
  if(!$new.Contains($token)){throw "${label}: generated code lost $token"}
 }
 return $new
}
$patched=Invoke-Variant 'original 1.3.0 source structure' $original $true
Invoke-Variant 'already optimized idempotence' $patched $false | Out-Null
$spaced=$original.Replace('function bathtub.update(player, pPos)','function   bathtub.update ( player, pPos )')
Invoke-Variant 'whitespace-only author drift' $spaced $true | Out-Null
$drift=$original.Replace('togglePin(near)','togglePinByUnknownMechanism(near)')
Invoke-Variant 'semantic author drift fails closed' $drift $false | Out-Null
Write-Host 'Yaiba original, version-fluid whitespace, idempotence, source-drift and pass scope contract passed.'
$global:LASTEXITCODE=0
