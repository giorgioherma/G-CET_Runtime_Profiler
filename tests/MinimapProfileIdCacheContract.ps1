param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$dll=Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'
if(!(Test-Path -LiteralPath $dll -PathType Leaf)){throw "Resolver core missing $dll"}
$asm=[Reflection.Assembly]::LoadFrom($dll)
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$method=$t.GetMethod('ReuseMinimapMappinProfileIds',
    [Reflection.BindingFlags]'Static,NonPublic')
if(!$method){throw 'Authoritative Minimap Widgets profile-ID cache source stage missing'}

function Rewrite([string]$source) {
    return [string]$method.Invoke($null,[object[]]@($source))
}
function MustRefuse([string]$source,[string]$label) {
    $refused=$false
    try { $null=Rewrite $source }
    catch {
        if($_.Exception.InnerException -is [InvalidOperationException]) {
            $refused=$true
        } else { throw }
    }
    if(!$refused){throw "Minimap fail-closed source proof did not refuse $label"}
}

$original=@'
local authorPins = 1
ObserveAfter('MinimapContainerController', 'CreateMappinUIProfile', function(this, mappin, mappinVariant, customData)
    local defaultRuntimeProfile = TweakDBID.new('MinimapMappinUIProfile.Default')
    if mappin:IsExactlyA("gamemappinsPointOfInterestMappin") then
        return MappinUIProfile.Create('poi', TweakDBID.new('MappinUISpawnProfile.Always'), defaultRuntimeProfile)
    end
    if mappinVariant == Enum.new('gamedataMappinVariant', 'Zzz19_DelamainTaxiVariant') then
        return MappinUIProfile.Create('taxi', TweakDBID.new('MappinUISpawnProfile.ShortRange'), TweakDBID.new('MinimapMappinUIProfile.Vehicle'))
    end
    return nil
end)
'@
$generated=Rewrite $original
foreach($token in @(
 'local __gcetMinimapProfileIdsV1 = {}',
 'local function __gcetMinimapProfileIdV1(key)',
 '__gcetMinimapProfileIdsV1[key] = value',
 "__gcetMinimapProfileIdV1('MappinUISpawnProfile.Always')",
 "__gcetMinimapProfileIdV1('MappinUISpawnProfile.ShortRange')",
 "__gcetMinimapProfileIdV1('MinimapMappinUIProfile.Vehicle')",
 'return MappinUIProfile.Create',
 "Enum.new('gamedataMappinVariant', 'Zzz19_DelamainTaxiVariant')",
 'return nil'
)){
 if(!$generated.Contains($token)){throw "Minimap source lost behavior/token: $token"}
}
if($generated.Contains("local defaultRuntimeProfile = TweakDBID.new(") -or
   ([regex]::Matches($generated,'TweakDBID\.new\(')).Count -ne 1 -or
   ([regex]::Matches($generated,'local function __gcetMinimapProfileIdV1')).Count -ne 1){
 throw 'Minimap repeatable constructor still allocates immutable IDs or duplicated helper'
}
if((Rewrite $generated) -cne $generated){
 throw 'Minimap reapplying profile ID stage is not idempotent'
}
$lf=[string][char]10
$crlf=([string][char]13)+$lf
$windows=$original.Replace($lf,$crlf)
$windowsResult=Rewrite $windows
if(!$windowsResult.Contains('local __gcetMinimapProfileIdsV1 = {}')){
 throw 'CRLF source did not receive profile helper'
}
if((Rewrite $windowsResult) -cne $windowsResult){throw 'CRLF minimap stage not idempotent'}
if((Rewrite 'local oldVariant = true') -cne 'local oldVariant = true'){
 throw 'Absent Minimap profile controller was rewritten'
}
MustRefuse 'local noCreateMappinUIProfileObserver = true' 'dangling callback identifier'
MustRefuse ($original.Replace('CreateMappinUIProfile', 'CreateMappinUIProfileAlt')) 'renamed binding'
MustRefuse ($original.Replace("TweakDBID.new('MappinUISpawnProfile.ShortRange')",
                              'TweakDBID.new(dynamicProfile)')) 'dynamic TweakDBID producer'
MustRefuse ($generated.Replace(
 "__gcetMinimapProfileIdV1('MappinUISpawnProfile.Always')",
 "TweakDBID.new('MappinUISpawnProfile.Always')")) 'partially reverted cached producer'
MustRefuse ($generated.Replace('__gcetMinimapProfileIdsV1[key] = value',
                               '__gcetMinimapProfileIdsV1[key] = nil')) 'cache does not retain IDs'
Write-Host 'Minimap profile-id cache: same author hook, all pins, immutable IDs, CRLF, idempotence and partial-source refusal PASS'
