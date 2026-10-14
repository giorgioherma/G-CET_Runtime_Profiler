param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if (!(Test-Path $exe)) { throw 'Missing resolver binary' }
Add-Type -AssemblyName System.IO.Compression.FileSystem

function FromZip($file,$suffix) {
  $zip=[IO.Compression.ZipFile]::OpenRead([string]$file)
  try {
    $entry=@($zip.Entries|Where-Object {$_.FullName.Replace('\','/').EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1
    if(!$entry){throw "Missing generated $suffix"}
    $rd=[IO.StreamReader]::new($entry.Open())
    try{return $rd.ReadToEnd()}finally{$rd.Dispose()}
  }finally{$zip.Dispose()}
}

function RunCase([string]$variant,[bool]$drift=$false) {
  $root=Join-Path $env:RUNNER_TEMP ("calllist-one-pass-"+$variant)
  if(Test-Path $root){Remove-Item -Recurse -Force $root}
  $capture=Join-Path $root 'capture'
  $mods=Join-Path $root 'mods'
  $mod=Join-Path $mods 'CallList-VehicleDetailsOverlay-CET'
  $engine=Join-Path $mods '0-Engine'
  New-Item -ItemType Directory -Force $capture,$mod,$engine|Out-Null
  $encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
  $bytes=[Convert]::FromBase64String($encoded)
  $inp=[IO.MemoryStream]::new($bytes)
  $gz=[IO.Compression.GZipStream]::new($inp,[IO.Compression.CompressionMode]::Decompress)
  $out=[IO.MemoryStream]::new()
  try {$gz.CopyTo($out);[IO.File]::WriteAllBytes((Join-Path $engine 'init.lua'),$out.ToArray())}
  finally {$out.Dispose();$gz.Dispose();$inp.Dispose()}

  $quote=if($variant -eq 'quotes'){"'"}else{'"'}
  $snippet=@"
local animOpen = true
local popupOpen = true
local playerPosX = 1
local playerPosY = 1
local settings = { minigame = true }
ObserveAfter("VehiclesManagerListItemController", "OnSelected", function() end)
registerForEvent("onInit", function()
  Observe("PlayerPuppet", "OnAction", function(this, action, consumer)
    if not animOpen or not popupOpen or not playerPosX or not settings.minigame then return end
    local sizeX, sizeY = GetDisplayResolution()
    local name = Game.GetLocalizedText(action:GetName(action).value)
    if action:IsButtonJustPressed(action) then
      if string.find(name:lower(), $($quote)moveup$($quote)) then
        if playerPosY >= sizeY - (sizeY * 0.1) then
          playerPosY = playerPosY - sizeY * 0.05
        end
      end
    end
  end)
end)
"@
  if($variant -eq 'legacy-v1') {
    $snippet=[regex]::Replace($snippet,'(?m)^    local sizeX, sizeY = GetDisplayResolution()\r?\n','')
    $snippet=$snippet.Replace('      if string.find(name:lower(), ' + $quote + 'moveup' + $quote + ') then', '      if string.find(name:lower(), ' + $quote + 'moveup' + $quote + ') then' + "`n        -- G-CET:CallListActionLazyDisplayV1`n        local sizeX, sizeY = GetDisplayResolution()")
  }
  if($drift) { $snippet=$snippet.Replace('string.find(name:lower()', 'string.match(name:lower()') }
  $source=Join-Path $mod 'init.lua'
  $snippet | Set-Content -LiteralPath $source -Encoding utf8
  $before=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
  $input=@{
    schemaVersion='2.0'
    callbacks=@(@{
      registrationId=716; owner='CallList-VehicleDetailsOverlay-CET'; infrastructure=$false
      kind='Observe'; target='PlayerPuppet::OnAction'
      source=@{file='init.lua';lineStart=7;lineEnd=21}
      callsPerSecond=80; exclusiveMsPerSecond=7.1; globalWorkSharePct=7
      familyWorkSharePct=100;avgExclusiveUs=89;maxExclusiveMs=2;spikeCount=0
    })
    optimizerEvidence=@()
  } | ConvertTo-Json -Depth 25
  $input|Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
  $result=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
  if(!$result.ok -and !$drift){throw "Resolver failed: $variant"}
  $count=0
  if($result.pass -and $result.pass.ManifestPath){
    $manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
    $count=@($manifest.transforms|Where-Object {$_.RuleId -eq 'calllist-action-lazy-display-query'}).Count
  }
  if($drift){
    if($count -ne 0){throw 'Incompatible action behavior should fail closed'}
    return
  }
  if($count -ne 1){throw "Missing CallList one-pass transform for $variant"}
  $patched=FromZip $result.pass.ZipPath 'CallList-VehicleDetailsOverlay-CET/init.lua'
  $a=$patched.IndexOf('if string.find(name:lower()')
  $b=$patched.IndexOf('local sizeX, sizeY = GetDisplayResolution()')
  $press=$patched.IndexOf('if action:IsButtonJustPressed(action) then')
  $name=$patched.IndexOf('local name = Game.GetLocalizedText(action:GetName(action).value)')
  if($a -lt 0 -or $b -lt $a -or $name -lt $press -or $a -lt $name -or
     !$patched.Contains('G-CET:CallListActionLazyInputV2')){
    throw "The native display query was not deferred for $variant"
  }
  if($patched -notmatch 'playerPosY\s*=\s*playerPosY\s*-\s*sizeY\s*\*\s*0.05') {throw 'Original input movement behavior lost'}
  if((Get-FileHash $source -Algorithm SHA256).Hash -ne $before) { throw 'Resolver edited live source' }
  $patched | Set-Content -LiteralPath $source -Encoding utf8
  $second=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
  if(!(Test-Path -LiteralPath $source)) {throw "CallList source disappeared on rerun: $variant"}
  if($second.pass -and $second.pass.ManifestPath){
    $again=Get-Content $second.pass.ManifestPath -Raw|ConvertFrom-Json
    if(@($again.transforms|Where-Object {$_.RuleId -eq 'calllist-action-lazy-display-query'}).Count -gt 0){
      throw "Non-idempotent new stage $variant"
    }
  }
  Write-Host "CallList pristine one-pass, source variation and idempotence: $variant PASS"
}
RunCase 'original'
RunCase 'quotes'
RunCase 'legacy-v1'
RunCase 'drift' $true
