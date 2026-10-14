param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$exe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Missing Resolver $exe" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Join-Path $env:RUNNER_TEMP 'minimap-stealth-widget-locality-contract'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$cap = Join-Path $root 'capture'
$mods = Join-Path $root 'mods'
$minimap = Join-Path $mods 'Minimap Widgets'
$zero = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $cap,$minimap,$zero | Out-Null

# Preserve ordinary Resolver dependency and use its own published 0-Engine.
$encoded = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
$bytes = [Convert]::FromBase64String($encoded)
$inputStream = [IO.MemoryStream]::new($bytes)
$gzip = [IO.Compression.GZipStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress)
$outStream = [IO.MemoryStream]::new()
try {
    $gzip.CopyTo($outStream)
    [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$outStream.ToArray())
} finally {
    $outStream.Dispose();$gzip.Dispose();$inputStream.Dispose()
}

$original = @'
local MinimapWidgetsConfig = { ShowElevationArrow = true }
ObserveAfter('MinimapStealthMappinController', 'UpdateAboveBelowVerticalRelation', function(this)
    if not IsDefined(this) then return end
    if MinimapWidgetsConfig.ShowElevationArrow == true then
        -- Existing live PASS5 implementation avoids the clamped vertical query.
        local isClamped = this:IsClamped()
        if isClamped then
            if this.aboveWidget then this.aboveWidget:SetVisible(false) end
            if this.belowWidget then this.belowWidget:SetVisible(false) end
        else
            local vertRelation = this:GetVerticalRelationToPlayer()
            local isAbove = vertRelation == gamemappinsVerticalPositioning.Above
            local isBelow = vertRelation == gamemappinsVerticalPositioning.Below
            if this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end
            if this.belowWidget then this.belowWidget:SetVisible(isBelow) end
        end
    else
        if not this.aboveWidget and not this.belowWidget then
            return
        end
        if this.aboveWidget then this.aboveWidget:SetVisible(false) end
        if this.belowWidget then this.belowWidget:SetVisible(false) end
    end
end)
'@
$file = Join-Path $minimap 'init.lua'
$original | Set-Content -LiteralPath $file -Encoding utf8

$evidence = @{
  schemaVersion='2.0'
  callbacks=@(@{
    registrationId=1163
    owner='Minimap Widgets'
    infrastructure=$false
    kind='ObserveAfter'
    target='MinimapStealthMappinController::UpdateAboveBelowVerticalRelation'
    source=@{file='init.lua';lineStart=2;lineEnd=27}
    callsPerSecond=142.332180
    exclusiveMsPerSecond=5.679764
    globalWorkSharePct=1
    familyWorkSharePct=100
    avgExclusiveUs=39.904986
    maxExclusiveMs=0.200
    spikeCount=0
  })
  optimizerEvidence=@()
} | ConvertTo-Json -Depth 20
$evidence | Set-Content -LiteralPath (Join-Path $cap 'CET_Resolver_Input.json') -Encoding utf8

function Invoke-Variant([string]$label, [string]$source, [bool]$expected) {
    $source | Set-Content -LiteralPath $file -Encoding utf8
    $oldHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $stdout = @(& $exe --capture $cap --mods $mods --generate-pass --json 2>$null)
    $result = if($stdout.Count -gt 0) {
        ($stdout -join "`n") | ConvertFrom-Json
    } else { $null }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $oldHash) {
        throw "Resolver modified live Minimap Widgets source ($label)"
    }
    if (!$expected) {
        if ($result -and $result.ok) {
            $manifest = Get-Content -LiteralPath $result.pass.ManifestPath -Raw | ConvertFrom-Json
            if (@($manifest.transforms | Where-Object {
                    $_.RuleId -eq 'minimap-stealth-widget-locality'
                }).Count -ne 0) {
                throw "Unexpected repeated/drift pass for $label"
            }
        }
        return $null
    }
    if (!$result -or !$result.ok) {
        throw "Resolver did not produce required minimap pass for $label"
    }
    $manifest = Get-Content -LiteralPath $result.pass.ManifestPath -Raw | ConvertFrom-Json
    $rules = @($manifest.transforms | Where-Object {
        $_.type -eq 'SEMANTIC_RULE' -and
        $_.RuleId -eq 'minimap-stealth-widget-locality'
    })
    if ($rules.Count -ne 1) {
        throw "${label}: expected exactly one production minimap semantic transform, got $($rules.Count)"
    }
    if (@($rules[0].files).Count -ne 1) {
        throw "${label}: minimap pass touched multiple files"
    }
    $zip = [IO.Compression.ZipFile]::OpenRead([string]$result.pass.ZipPath)
    try {
        $item = @($zip.Entries | Where-Object {
            $_.FullName.Replace('\','/').EndsWith(
                'Minimap Widgets/init.lua',
                [StringComparison]::OrdinalIgnoreCase)
        }) | Select-Object -First 1
        if (!$item) { throw "${label}: generated minimap init.lua absent from ZIP" }
        $reader = [IO.StreamReader]::new($item.Open())
        try { $optimized = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    foreach ($token in @(
        'G-CET semantic:minimap-stealth-widget-locality',
        'local __gcetAboveWidget = this.aboveWidget',
        'local __gcetBelowWidget = this.belowWidget',
        'if not __gcetAboveWidget and not __gcetBelowWidget then return end',
        'this:IsClamped()',
        'this:GetVerticalRelationToPlayer()',
        '__gcetAboveWidget:SetVisible(isAbove)',
        '__gcetBelowWidget:SetVisible(isBelow)'
    )) {
        if (!$optimized.Contains($token)) { throw "$label lost expected token: $token" }
    }
    if (([regex]::Matches($optimized,'this\.aboveWidget')).Count -ne 1 -or
        ([regex]::Matches($optimized,'this\.belowWidget')).Count -ne 1) {
        throw "${label}: native widget properties still reread inside callback"
    }
    return $optimized
}

$author317 = @'
local MinimapWidgetsConfig = { ShowElevationArrow = true }
ObserveAfter('MinimapStealthMappinController', 'UpdateAboveBelowVerticalRelation', function(this)
    if not IsDefined(this) then return end
    if MinimapWidgetsConfig.ShowElevationArrow == true then
        local vertRelation = this:GetVerticalRelationToPlayer()
        local shouldShow = this:GetRootWidget():IsVisible() and not this:IsClamped()
        local isAbove = vertRelation == gamemappinsVerticalPositioning.Above
        local isBelow = vertRelation == gamemappinsVerticalPositioning.Below
        if this:IsClamped() then
            if this.aboveWidget then this.aboveWidget:SetVisible(false) end
            if this.belowWidget then this.belowWidget:SetVisible(false) end
        else
            if this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end
            if this.belowWidget then this.belowWidget:SetVisible(isBelow) end
        end
    else
        if not this.aboveWidget and not this.belowWidget then
            return
        end
        if this.aboveWidget then this.aboveWidget:SetVisible(false) end
        if this.belowWidget then this.belowWidget:SetVisible(false) end
    end
end)
'@
$legacyOptimized=Invoke-Variant 'unmodified original Minimap Widgets 3.1.7' $author317 $true
Invoke-Variant 'original 3.1.7 idempotent' $legacyOptimized $false | Out-Null
$quoteVariant = $author317.Replace("ObserveAfter('MinimapStealthMappinController', 'UpdateAboveBelowVerticalRelation'",
                                  'ObserveAfter("MinimapStealthMappinController", "UpdateAboveBelowVerticalRelation"')
Invoke-Variant 'original equivalent quote style' $quoteVariant $true | Out-Null
$optimized = Invoke-Variant 'measured live PASS5 structure' $original $true
Invoke-Variant 'idempotent already patched' $optimized $false | Out-Null
$reformatted = $original.Replace('local isClamped = this:IsClamped()',
                                 'local  isClamped =  this:IsClamped()')
Invoke-Variant 'equivalent author spacing' $reformatted $true | Out-Null
$drift = $original.Replace('this:GetVerticalRelationToPlayer()',
                           'this:GetNewVerticalRelationState()')
Invoke-Variant 'unproven semantics' $drift $false | Out-Null
Write-Host 'Minimap native-widget source reuse: production generation, exact scope, idempotence, whitespace tolerance, author drift and read-only live source passed.'
# The last deliberate fail-closed Resolver no-op can set LASTEXITCODE to 1.
# The contract itself passed all assertions above; do not propagate that CLI status.
$global:LASTEXITCODE = 0
