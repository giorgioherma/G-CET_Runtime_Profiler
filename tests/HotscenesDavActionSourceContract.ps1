param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path $exe)){throw 'Experimental Resolver missing'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Join-Path $env:RUNNER_TEMP 'gcet-native-action-c-ci1336'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$capture=Join-Path $root 'capture'
$mods=Join-Path $root 'mods'
$hs=Join-Path $mods 'hotscenes'
$dav=Join-Path $mods 'DriveAerialVehicle'
New-Item -ItemType Directory -Force $capture,$hs,$dav,(Join-Path $dav 'Modules')|Out-Null
function Put([string]$path,[string]$content) {
  $parent=Split-Path $path -Parent
  New-Item -ItemType Directory -Force $parent|Out-Null
  [IO.File]::WriteAllText($path,$content,[Text.Encoding]::UTF8)
}
function ZipText([string]$zipPath,[string]$suffix){
  $z=[IO.Compression.ZipFile]::OpenRead($zipPath)
  try{
    $e=@($z.Entries|Where-Object {$_.FullName.Replace('\','/').EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1
    if(!$e){throw "Generated ZIP missing $suffix"}
    $reader=[IO.StreamReader]::new($e.Open())
    try{return $reader.ReadToEnd()} finally {$reader.Dispose()}
  } finally {$z.Dispose()}
}
# A structurally valid pinned runtime, not a newly fabricated 0-Engine file.
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zero|Out-Null
$encoded=(Get-Content (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$input=[IO.MemoryStream]::new([Convert]::FromBase64String($encoded))
$gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$out=[IO.MemoryStream]::new()
try{$gzip.CopyTo($out);[IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$out.ToArray())}
finally{$out.Dispose();$gzip.Dispose();$input.Dispose()}

# Author-shaped synthetic Hotscenes: exactly one initialization and seven
# subsequent visibility writers; arbitrary action mapping stays author-owned.
$native=@'
local nativeUI={}
nativeUI.isShowtime = false
nativeUI.isMainMenu = false
nativeUI.isSelectionList = false
nativeUI.activeInstances = {}
local lastShardNotificationController = nil
local function getNavigation(action) return action:IsAction("popup_moveUp") end
local function handleShardButtonNavigation(controller, instance, navigation) end
local function closeA()
    nativeUI.isShowtime = false
end
local function showA()
    nativeUI.isShowtime = true
end
local function closeB()
    nativeUI.isShowtime = false
end
local function closeC()
    nativeUI.isShowtime = false
end
local function closeD()
    nativeUI.isShowtime = false
end
local function closeE()
    nativeUI.isShowtime = false
end
local function closeF()
    nativeUI.isShowtime = false
end
local function attach()
    ObserveAfter('PlayerPuppet', 'OnAction', function(this, action, consumer)
        if not nativeUI.isShowtime then return end
        if not (nativeUI.isSelectionList or nativeUI.isMainMenu) then return end
        if #nativeUI.activeInstances < 1 then return end
        if not IsDefined(lastShardNotificationController) then return end
        actionType = action:GetType(action).value
        if action:GetType(action).value ~= 'REPEAT' then return end
        navigateTo = getNavigation(action)
        if not navigateTo then return end
        handleShardButtonNavigation(lastShardNotificationController, nativeUI.activeInstances[1], navigateTo)
	end)
end
'@
Put (Join-Path $hs 'hotscenes_native_ui.lua') $native

# Minimal pristine DriveAerialVehicle source with authentic author listener
# shape; only the selected situation state writers may toggle wildcard input.
$init=@'
local DAV={}
local Def={Situation={Idle=0,Normal=1}}
registerForEvent('onUpdate',function(delta)
  Cron.Update(delta)
  if Def.Situation.Idle~=nil then
    DAV.core_obj.av_obj.engine_obj:Update(delta)
  end
end)
'@
$core=@'
local Core={}
local VehicleSystem={}
local function defaultSituation() return Def.Situation.Normal end
function Core:Update() end
function Core:Init()
    Cron.Every(DAV.time_resolution, function()
        self.event_obj:CheckAllEvents()
        self:GetActions()
    end)
end
function Core:SetInputListener()
    local exception_in_entry_area_list = Utils:ReadJson("Data/exception_in_entry_area_input.json")
    local exception_in_veh_list = Utils:ReadJson("Data/exception_in_veh_input.json")
    local exception_in_popup_list = Utils:ReadJson("Data/exception_in_popup_input.json")

    Observe("PlayerPuppet", "OnAction", function(this, action, consumer)
        if self.event_obj.current_situation ~= Def.Situation.Waiting and self.event_obj.current_situation ~= Def.Situation.InVehicle then
            return
        end
        local action_name = action:GetName(action).value
        local action_type = action:GetType(action).value
        local action_value = action:GetValue(action)
        if self.event_obj:IsInVehicle() then
            for _, exception in pairs(exception_in_veh_list) do
                if action_name == exception then consumer:Consume() end
            end
            for _, exception in pairs(exception_in_popup_list) do
                if action_name == exception then consumer:Consume() end
            end
        elseif self.event_obj:IsInEntryArea() then
            for _, exception in pairs(exception_in_entry_area_list) do
                if action_name == exception then consumer:Consume() end
            end
        end
        self:StorePlayerAction(action_name, action_type, action_value)
    end)

    -- Disable Iconic Cyberware
    Override("PlayerPuppet", "ActivateIconicCyberware", function(this, wrapped_method)
        wrapped_method()
    end)
end
'@
$event=@'
local Event={}
local DAV={}
GameUI.Observe("SessionStart", function()
        DAV.core_obj:SetFastTravelPosition()
        self.current_situation = Def.Situation.Normal

    end)
GameUI.Observe("SessionEnd", function()
        self.current_situation = Def.Situation.Idle
    end)
function Event:SetSituation(situation)
    if self.current_situation == Def.Situation.Idle then
        return false
    elseif situation == Def.Situation.Landing then
        self.current_situation = Def.Situation.Landing
        return true
    elseif situation == Def.Situation.Waiting then
        self.current_situation = Def.Situation.Waiting
        return true
    elseif situation == Def.Situation.InVehicle then
        self.current_situation = Def.Situation.InVehicle
        return true
    elseif situation == Def.Situation.TalkingOff then
        self.current_situation = Def.Situation.TalkingOff
        return true
    elseif situation == Def.Situation.Waiting then
        self.current_situation = Def.Situation.Waiting
        return true
    elseif situation == Def.Situation.Normal then
        self.current_situation = Def.Situation.Normal
        return true
    elseif situation == Def.Situation.Normal then
        self.current_situation = Def.Situation.Normal
        return true
    else
        return false
    end
end
'@
Put (Join-Path $dav 'init.lua') $init
Put (Join-Path $dav 'Modules\core.lua') $core
Put (Join-Path $dav 'Modules\event.lua') $event
$known=@(
  (Join-Path $hs 'hotscenes_native_ui.lua'),
  (Join-Path $dav 'init.lua'),
  (Join-Path $dav 'Modules\core.lua'),
  (Join-Path $dav 'Modules\event.lua')
)
$hashes=@{}
foreach($p in $known){$hashes[$p]=(Get-FileHash $p -Algorithm SHA256).Hash}

$handoff=@{
 schemaVersion='2.0'
 callbacks=@(
   @{registrationId=7801;owner='hotscenes';infrastructure=$false;kind='ObserveAfter';target='PlayerPuppet::OnAction';source=@{file='hotscenes_native_ui.lua';lineStart=33;lineEnd=47};callsPerSecond=1383;exclusiveMsPerSecond=1.14;globalWorkSharePct=1;familyWorkSharePct=100;avgExclusiveUs=0.83;maxExclusiveMs=11.45;spikeCount=2},
   @{registrationId=7802;owner='DriveAerialVehicle';infrastructure=$false;kind='event';target='onUpdate';source=@{file='init.lua';lineStart=3;lineEnd=7};callsPerSecond=60;exclusiveMsPerSecond=5.1;globalWorkSharePct=4;familyWorkSharePct=100;avgExclusiveUs=85;maxExclusiveMs=2.2;spikeCount=0}
 )
 optimizerEvidence=@()
}|ConvertTo-Json -Depth 30
Put (Join-Path $capture 'CET_Resolver_Input.json') $handoff
$result=& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json
if(!$result.ok -or !$result.pass){throw 'Experimental source pass generation failed'}
$manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
foreach($id in @('hotscenes-ui-action-dormancy','driveaerialvehicle')){
 $matched=@($manifest.transforms|Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $id})
 if($matched.Count -ne 1){
   $skip=@($manifest.skipped|Where-Object {$_.RuleId -eq $id})|ConvertTo-Json -Compress -Depth 8
   throw "No source-proven pass for $id (skips=$skip)"
 }
}
$hot=ZipText $result.pass.ZipPath 'hotscenes/hotscenes_native_ui.lua'
$dCore=ZipText $result.pass.ZipPath 'DriveAerialVehicle/Modules/core.lua'
$dEvt=ZipText $result.pass.ZipPath 'DriveAerialVehicle/Modules/event.lua'
$dInit=ZipText $result.pass.ZipPath 'DriveAerialVehicle/init.lua'
if(!$hot.Contains('phase = "after"') -or !$hot.Contains('__gcetHotscenesAfterV1') -or
   !$hot.Contains("ObserveAfter('PlayerPuppet', 'OnAction', __gcetHotscenesOnAction)") -or
   ([regex]::Matches($hot,'__gcetHotscenesSync\(\)')).Count -lt 8){
 throw 'Hotscenes phase, fallback or UI wake incomplete'
}
if(!$dCore.Contains('phase = "before"') -or !$dCore.Contains('actions = "*"') -or
   !$dCore.Contains('self:StorePlayerAction(action_name, action_type, action_value)') -or
   !$dCore.Contains('Observe("PlayerPuppet", "OnAction", __gcetDAVOnAction)') -or
   !$dEvt.Contains('DAV.core_obj:SyncActionListener()') -or
   !$dInit.Contains('__gcetSituation')){
 throw 'DAV wildcard, before-phase fallback, source writer or prior B stage incomplete'
}
foreach($p in $known){
 if((Get-FileHash $p -Algorithm SHA256).Hash -ne $hashes[$p]){
   throw "Resolver changed original source: $p"
 }
}

# Regression: a live mod with 2/3 B-stage markers and CRLF owner source.
# Previously it was reported as INCOMPLETE_AMBIGUOUS and silently omitted.
$lf=[string][char]10
$crlf=([string][char]13)+([string][char]10)
$stagedRoot=Join-Path $root 'staged-mods'
$stagedDav=Join-Path $stagedRoot 'DriveAerialVehicle'
$stagedCapture=Join-Path $root 'staged-capture'
New-Item -ItemType Directory -Force $stagedDav,(Join-Path $stagedDav 'Modules'),$stagedCapture | Out-Null
$previousInit=@'
-- G-CET semantic:driveaerialvehicle
local DAV={}
local Def={Situation={Idle=0,Normal=1}}
registerForEvent('onUpdate',function(delta)
  Cron.Update(delta)
  if Def.Situation.Idle~=nil then
    local __gcetEvent = DAV.core_obj.event_obj
    local __gcetSituation = __gcetEvent and __gcetEvent.current_situation or Def.Situation.Idle
    if __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal then
      DAV.core_obj.av_obj.engine_obj:Update(delta)
    end
  end
end)
'@
$previousCore=$core.Replace('function Core:Init()',('function Core:Init()'+$lf+'    local __gcetIdleEventTick = 0'))
$previousCore='-- G-CET semantic:driveaerialvehicle'+$lf+$previousCore
Put (Join-Path $stagedDav 'init.lua') $previousInit
Put (Join-Path $stagedDav 'Modules\core.lua') ($previousCore.Replace($lf,$crlf))
Put (Join-Path $stagedDav 'Modules\event.lua') ($event.Replace($lf,$crlf))
Copy-Item -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Destination (Join-Path $stagedCapture 'CET_Resolver_Input.json')
$stagedHash=@{}
foreach($path in @('init.lua','Modules\core.lua','Modules\event.lua')){
  $full=Join-Path $stagedDav $path
  $stagedHash[$path]=(Get-FileHash $full -Algorithm SHA256).Hash
}
$stagedResult=& $exe --capture $stagedCapture --mods $stagedRoot --generate-pass --json | ConvertFrom-Json
if(!$stagedResult.ok -or !$stagedResult.pass){throw 'DAV staged source pass generation failed'}
$stagedManifest=Get-Content $stagedResult.pass.ManifestPath -Raw | ConvertFrom-Json
$stagedRows=@($stagedManifest.transforms|Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'driveaerialvehicle'})
if($stagedRows.Count -ne 1){
 $skip=@($stagedManifest.skipped|Where-Object {$_.RuleId -eq 'driveaerialvehicle'})|ConvertTo-Json -Compress -Depth 8
 throw "DAV staged B-stage did not upgrade to C-stage (skipped=$skip)"
}
$sc=ZipText $stagedResult.pass.ZipPath 'DriveAerialVehicle/Modules/core.lua'
$se=ZipText $stagedResult.pass.ZipPath 'DriveAerialVehicle/Modules/event.lua'
if(!$sc.Contains('__gcetDAVActionGateV2') -or !$sc.Contains('phase = "before"') -or
   !$sc.Contains('local __gcetIdleEventTick = 0') -or
   !$se.Contains('DAV.core_obj:SyncActionListener()')){
 throw 'DAV staged output lacks native action stage, B-stage or wake'
}
if(!$sc.Contains($crlf) -or !$se.Contains($crlf)){
 throw 'DAV stage upgrade lost original CRLF EOL convention'
}
foreach($path in $stagedHash.Keys){
 if((Get-FileHash (Join-Path $stagedDav $path) -Algorithm SHA256).Hash -ne $stagedHash[$path]){
  throw "DAV staged source changed on disk: $path"
 }
}
Write-Host 'DAV 2/3 marked live-shaped CRLF -> complete native action upgrade passed.'

Write-Host 'Experimental two-mod one-pass source contract passed; live gameplay/source bytes remain independent validation.'
