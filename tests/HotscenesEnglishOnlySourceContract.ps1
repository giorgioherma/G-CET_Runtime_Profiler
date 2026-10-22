param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom(
  (Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$type=$asm.GetType('GCETRuntimeProfiler.Core.Services.HotscenesEnglishOnlySource',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$run=$type.GetMethod('TryStage',$flags)
if(!$run){throw 'English-only stage unavailable'}
$root=Join-Path $env:RUNNER_TEMP 'gcet-english-optin'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force (Join-Path $root 'hotscenes')|Out-Null
$src=@'
function saveTextFile(text, fileName)
  print(text,fileName)
end

exportedStringsFileHeader = {
 name="test"
}

stringsFileSet = {cetUiStrings='cetUiStrings.json'}

local uiDefaultStrings = {cetUiStrings={}, nuiUiStrings={}}
local uiStrings = cloneTable(uiDefaultStrings)
exportTable = {}
exportTable.exportOrder = {'fileHeader', 'uiStrings'}
exportTable.fileHeader = exportedStringsFileHeader
exportTable.uiStrings = uiDefaultStrings.cetUiStrings
pcall(function() jsonDump = dumpTableToJson(exportTable, true, true) end)
if type(jsonDump) == 'string' then saveTextFile(jsonDump, "language/en-us_template/cetUiStrings.json") end

function replaceTableEntries(sourceTable, replacementTable, finalDataType, verbose)
 return sourceTable
end


local currentUiLanguage = "en-us"
languages = {['en-us']=false}

function loadAllLanguageFiles()
 return loadJsonTable("language/en-us/cetUiStrings.json")
end

loadAllLanguageFiles()

function quickloadUserSettings()
 return {}
end

function getGameUILanguage(updateUserSettings, forceSettingsUpdate)
 return "en-us"
end

function loadLocalizedStrings(updateUserSettings, forceSettingsUpdate)
 currentUiLanguage=getGameUILanguage(updateUserSettings,forceSettingsUpdate)
end

loadLocalizedStrings()

local function vectorDistance2D(src, target);
 return src.x-target.x
end;

registerForEvent("onInit",function()
  if uiDefaultStrings.nuiUiStrings then
    local exportTable = {}
    exportTable.exportOrder = {'fileHeader', 'uiStrings'}
    exportTable.fileHeader = exportedStringsFileHeader
    exportTable.uiStrings = uiDefaultStrings.nuiUiStrings
    if type(jsonDump) == 'string' then saveTextFile(jsonDump, "language/en-us_template/nuiUiStrings.json") end
    if type(uiDefaultStrings.fullPerformerNames) == 'table' then
      local exportTable = {}
      if type(jsonDump) == 'string' then saveTextFile(jsonDump, "language/en-us_template/performerStrings.json") end
    end
    if type(uiDefaultStrings.destinationNames) == 'table' then
      local exportTable = {}
      if type(jsonDump) == 'string' then saveTextFile(jsonDump, "language/en-us_template/destinationStrings.json") end
    end
  end
  local nativeSettings = GetMod("nativeSettings")
  local lastlanguageSelected = userSettings.lastLanguageSelected
  loadLocalizedStrings(true, true)
  if userSettings.lastLanguageSelected ~= lastlanguageSelected then saveUserSettings() end
end)
local translation = scenePerformers[performer].performerFullNameTranslations[currentUiLanguage]
if type(settings.lastLanguageSelected) == 'string' then userSettings.lastLanguageSelected = settings.lastLanguageSelected else userSettings.lastLanguageSelected = "en-us" end
function setObservers()
  Observe('PlayerPuppet', 'OnGameAttached', function(this)
    local lastlanguageSelected = userSettings.lastLanguageSelected
    loadLocalizedStrings(true)
    if userSettings.lastLanguageSelected ~= lastlanguageSelected then saveUserSettings() end
  end)
  ObserveBefore('inkISystemRequestsHandler','RequestSaveUserSettings',function(this)
    local lastlanguageSelected = userSettings.lastLanguageSelected
    loadLocalizedStrings(true)
    if userSettings.lastLanguageSelected ~= lastlanguageSelected then saveUserSettings() end
    updatePerformers()
  end)
end
local isFemaleScene = false
'@
$path=Join-Path $root 'hotscenes/init.lua'
[IO.File]::WriteAllText($path,$src,[Text.UTF8Encoding]::new($false))
function Apply($stage,$errors) {
 $changes=[Collections.Generic.Dictionary[string,byte[]]]::new()
 $counts=[Collections.Generic.Dictionary[string,int]]::new()
 return @($run.Invoke($null,[object[]]@(
    [string]$root,$changes,$counts,$stage,$errors)),$changes)
}
$manifest=[Collections.Generic.List[object]]::new()
$errors=[Collections.Generic.List[object]]::new()
$a=Apply $manifest $errors
if($a[0] -ne 0 -or $a[1].Count -ne 0){throw 'English-only stage unexpectedly enabled by default'}
[IO.File]::WriteAllText((Join-Path $root 'G-CET-UserOptions.json'),
 '{"hotscenesEnglishOnly":true}',[Text.UTF8Encoding]::new($false))
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
$counts=[Collections.Generic.Dictionary[string,int]]::new()
$manifest=[Collections.Generic.List[object]]::new()
$errors=[Collections.Generic.List[object]]::new()
$result=$run.Invoke($null,[object[]]@([string]$root,$staged,$counts,$manifest,$errors))
if($result -ne 1 -or $errors.Count -gt 0){
 throw "English-only opt-in refused: $($errors | ConvertTo-Json -Depth 4)"
}
$out=[Text.Encoding]::UTF8.GetString($staged['hotscenes/init.lua'])
foreach($required in @('personal:hotscenes-english-only-v1','local uiDefaultStrings',
  'performerFullNameTranslations["en-us"]','nativeUI.updateUiStrings','updatePerformers()')){
 if(!$out.Contains($required)){throw "Lost retained English/scene behavior: $required"}
}
foreach($unwanted in @('loadAllLanguageFiles','loadLocalizedStrings','getGameUILanguage',
  'saveTextFile','quickloadUserSettings','local isFemaleScene','local function vectorDistance2D')){
 if($out.Contains($unwanted)){throw "Translation cleanup incomplete: $unwanted"}
}
$again=[Collections.Generic.List[object]]::new()
$repeat=$run.Invoke($null,[object[]]@([string]$root,$staged,$counts,$again,
    [Collections.Generic.List[object]]::new()))
if($repeat -ne 0 -or $again.Count -ne 0){throw 'Repeated English-only generation duplicated changes'}
if([IO.File]::ReadAllText($path) -ne $src){throw 'Installed Hotscenes was modified'}
$staged.Clear()
[IO.File]::WriteAllText($path,$src.Replace('loadAllLanguageFiles','changedLanguageLoader'),
 [Text.UTF8Encoding]::new($false))
$rejections=[Collections.Generic.List[object]]::new()
$refused=$run.Invoke($null,[object[]]@([string]$root,$staged,$counts,
 [Collections.Generic.List[object]]::new(),$rejections))
if($refused -ne 0 -or $rejections.Count -ne 1 -or $staged.Count -ne 0){
 throw 'Drifted source did not fail closed'
}
Write-Host 'Hotscenes English-only opt-in, no default feature loss, source-only output, idempotence and drift refusal: PASS'
