param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "Resolver missing: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-amm-squeeze-contract'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-111111_AMM_SQUEEZE'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressed)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try { $gzip.CopyTo($output); [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray()) }
finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }

$ammDir = Join-Path $mods 'AppearanceMenuMod'
New-Item -ItemType Directory -Force $ammDir | Out-Null
@'
local AMM = {
    Props = { buildMode=false, presetLoadInProgress=false, SensePropsTriggers=function() end },
    Director = { activeCamera=nil, SenseTriggers=function() end, SenseNPCTalk=function() end },
    Scan = { SenseSavedDespawns=function() end, SenseAppTriggers=function() end, DrawMinimalUI=function() end },
    Light = { stickyMode=false, activeLight=nil, camera={Move=function() end} },
    Tools = { directMode=false, currentTarget='', useTeleportAnimation=false },
    UI = { currentTheme='default', Start=function() end, End=function() end, Load=function() end },
    archivesInfo = { missing=false },
    playerAttached=true,
    playerInMenu=false,
    playerInPhoto=false,
    player=nil,
    currentTarget=nil,
    shouldCheckSavedAppearance=true,
    userSettings={teleportAnimation=false,photoModeEnhancements=false},
    selectedTheme='default',
    displayInteractionPrompt=false
}
local drawWindow = false
local finishedUpdate = true
local buttonPressed = false
local spamTimer = 0.0
local Cron = { Update=function() end, After=function() end }
local db = { urows=function() return function() return nil end end }
local Util = {
    PlayerPositionChangedSignificantly=function() return false end,
    FreezePlayer=function() end, RemovePlayerEffects=function() end,
    SetInteractionHub=function() end
}
local Tools = AMM.Tools

function AMM:CheckCustomDefaults(target) end
function AMM:CheckSavedAppearance(target) end
function AMM:SenseSBTriggers() end
function AMM:UpdateSettings() end
function AMM:Begin() end
function AMM:NewTarget(handle, class, id, name, app, options)
    return {handle=handle,name=name or 'target',appearance=app}
end
function AMM:GetScanID() return 'id' end
function AMM:GetScanClass() return 'entEntity' end
function AMM:GetNPCName() return 'NPC' end
function AMM:GetVehicleName() return 'Vehicle' end
function AMM:GetObjectName() return 'Object' end
function AMM:GetScanAppearance() return 'app' end
function AMM:GetAppearanceOptions() return {} end
function AMM:CreateBusInteractionPrompt() end

registerInput("amm_cycle", "Cycle Appearance", function(down)
    if down then
        local target = AMM:GetTarget()
        if target ~= nil then AMM.shouldCheckSavedAppearance = false end
    end
end)

 local frameCounter = 0

 __gcetRegisterEvent_45("onUpdate", function(deltaTime)
		 AMM.deltaTime = deltaTime

		 local mod = GetMod("gtaTravel")
		 if mod ~= nil and AMM.TeleportMod == nil then
			 AMM.TeleportMod = mod
			 AMM.Tools.useTeleportAnimation = AMM.userSettings.teleportAnimation
		 end

     	Cron.Update(deltaTime)

		 if AMM.playerAttached and (not(AMM.playerInMenu) or AMM.playerInPhoto) then
				frameCounter = frameCounter + 1
				local fps = 1 / deltaTime
				local C = 4800
				local computed = math.ceil(C / fps)
				local threshold = math.max(math.min(computed, 120), 60)

				if frameCounter >= threshold then
					AMM.currentTarget = AMM:GetTarget()
					frameCounter = 0
					
					if not AMM.archivesInfo.missing and finishedUpdate and AMM.player ~= nil then
						local target = AMM.currentTarget
						AMM:CheckCustomDefaults(target)

						Cron.After(1, function()
							if not AMM.shouldCheckSavedAppearance then
								AMM.shouldCheckSavedAppearance = true
							end
						end)

						if not drawWindow and AMM.shouldCheckSavedAppearance then
							local count = 0
							for x in db:urows("SELECT COUNT(1) FROM saved_appearances UNION ALL SELECT COUNT(1) FROM blacklist_appearances") do
								count = count + x
								break
							end
							if count ~= 0 then
								AMM:CheckSavedAppearance(target)
								AMM.shouldCheckSavedAppearance = false
							end
						end
					end

					if not drawWindow and AMM.playerAttached then
						AMM.Director:SenseNPCTalk()
						AMM.Director:SenseTriggers()
						AMM.Scan:SenseSavedDespawns()
						local playerPos = Game.GetPlayer():GetWorldPosition()
						local playerPosChanged = Util:PlayerPositionChangedSignificantly(playerPos)
						AMM:SenseSBTriggers()
						if playerPosChanged then
							AMM.Props:SensePropsTriggers()
							AMM.Scan:SenseAppTriggers()
						end
					end
				end

				if AMM.Props.presetLoadInProgress then
					Util:FreezePlayer()
				end
				if AMM.Director.activeCamera then
					AMM.Director.activeCamera:Move()
				end
				if AMM.Light.stickyMode and AMM.Light.activeLight and AMM.Light.activeLight.isAMMLight then
					AMM.Light.activeLight:Move()
					AMM.Light.camera:Move()
				end
				if AMM.Tools.directMode and AMM.Tools.currentTarget and AMM.Tools.currentTarget ~= '' then
					AMM.Tools.currentTarget:Move()
				end
				if buttonPressed then
					spamTimer = spamTimer + deltaTime
				end
			end
	 end)

 __gcetRegisterEvent_46("onDraw", function()

	 	ImGui.SetNextWindowPos(500, 500, ImGuiCond.FirstUseEver)

		if drawWindow or AMM.Props.buildMode then
			if AMM.UI.currentTheme ~= AMM.selectedTheme then
				AMM.UI:Load(AMM.selectedTheme)
				AMM:UpdateSettings()
			end
			AMM.UI:Start()
		end

	 	if drawWindow then
			AMM:Begin()
	 	end

		if AMM.Props.buildMode then
			AMM.Scan:DrawMinimalUI()
		end

		if drawWindow or AMM.Props.buildMode then
			AMM.UI:End()
		end
	 end)

function AMM:GetTarget()
	local player = Game.GetPlayer()
	
	if player then
		local target = Game.GetTargetingSystem():GetLookAtObject(player, true, false) or Game.GetTargetingSystem():GetLookAtObject(player, false, false)
		local t = nil

		if target ~= nil then
			local id = AMM:GetScanID(target)
			if id == "0x62701058, 29" then return nil end
			if target:IsNPC() or target:IsReplacer() then
				t = AMM:NewTarget(target, AMM:GetScanClass(target), AMM:GetScanID(target), AMM:GetNPCName(target), AMM:GetScanAppearance(target), AMM:GetAppearanceOptions(target))
			elseif target:IsVehicle() then
				t = AMM:NewTarget(target, 'vehicle', AMM:GetScanID(target), AMM:GetVehicleName(target), AMM:GetScanAppearance(target), AMM:GetAppearanceOptions(target))
			else
				t = AMM:NewTarget(target, AMM:GetScanClass(target), "None", AMM:GetObjectName(target), AMM:GetScanAppearance(target), nil)
			end
			if t ~= nil and t.name ~= "gameuiWorldMapGameObject" and t.name ~= "ScriptedWeakspotObject" then
				AMM:CreateBusInteractionPrompt(t)
				return t
			end
		end
	end

	if AMM.displayInteractionPrompt and GetVersion() ~= "v1.15.0" then
		Util:SetInteractionHub("Enter Bus", "Choice1", false)
		AMM.displayInteractionPrompt = false
	end
	return nil
end

return AMM
'@ | Set-Content -LiteralPath (Join-Path $ammDir 'init.lua') -Encoding utf8

function Find-Line([string]$Path,[string]$Pattern) {
    $lines=@(Get-Content -LiteralPath $Path)
    for($i=0;$i -lt $lines.Count;$i++) { if($lines[$i] -match $Pattern) { return $i+1 } }
    throw "Pattern not found: $Pattern"
}
function CallbackRow([int]$Id,[string]$Target,[int]$Line,[double]$Ms,[double]$Calls) {
    @{
        registrationId=$Id; owner='AppearanceMenuMod'; infrastructure=$false
        kind='event'; target=$Target
        source=@{file='init.lua';lineStart=$Line;lineEnd=$Line+105}
        callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms
        globalWorkSharePct=0.6; familyWorkSharePct=100
        avgExclusiveUs=25; maxExclusiveMs=1; spikeCount=0; maxSpikeExclusiveMs=0
    }
}
$init=Join-Path $ammDir 'init.lua'
$updateLine=Find-Line $init '__gcetRegisterEvent_45\("onUpdate"'
$drawLine=Find-Line $init '__gcetRegisterEvent_46\("onDraw"'

$handoff=@{
    schemaVersion='1.8'
    callbacks=@(
        (CallbackRow 45 'onUpdate' $updateLine 1.30 58),
        (CallbackRow 46 'onDraw' $drawLine 0.52 58)
    )
    optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'AMM squeeze pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$ammUpdate=@($resolver.callbackFamilies |
    Where-Object {$_.target -eq 'onUpdate'} |
    ForEach-Object {$_.topConsumers} |
    Where-Object {$_.owner -eq 'AppearanceMenuMod'})[0]
if($null -eq $ammUpdate) { throw 'AMM onUpdate missing from resolver.' }
if($ammUpdate.semantic.RuleId -ne 'appearance-menu-mod-squeeze' -or
   -not $ammUpdate.semantic.SourceProofSatisfied -or
   -not $ammUpdate.semantic.generationReady) {
    throw "AMM rule was not generation-ready below the default 3 ms/s threshold."
}
if([double]$ammUpdate.semantic.RuntimeThresholdMsPerSecond -ne 1.0) {
    throw "AMM rule-local runtime threshold was not propagated."
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
$ammTransform=@($manifest.transforms | Where-Object {
    $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'appearance-menu-mod-squeeze'
})
if($ammTransform.Count -ne 1) { throw "AMM semantic transform missing from manifest." }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    $entry=$zip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/AppearanceMenuMod/init.lua')
    if($null -eq $entry){throw 'Generated AMM init.lua missing from ZIP.'}
    $reader=[System.IO.StreamReader]::new($entry.Open())
    try{$txt=$reader.ReadToEnd()} finally{$reader.Dispose()}

    $required=@(
        'G-CET semantic:appearance-menu-mod-squeeze',
        'if not drawWindow and not AMM.Props.buildMode then return end',
        'function AMM:GetTarget()',
        'local target = targetingSystem and (targetingSystem:GetLookAtObject(player, true, false) or targetingSystem:GetLookAtObject(player, false, false)) or nil',
        'AMM.currentTarget = AMM:GetTarget()',
        'Cron.After(1, function()',
        'if not AMM.shouldCheckSavedAppearance then',
        'AMM.Director:SenseTriggers()',
        'AMM.Scan:SenseSavedDespawns()',
        'AMM.Tools.currentTarget:Move()',
        'Cron.Update(deltaTime)'
    )
    foreach($token in $required) {
        if(-not $txt.Contains($token)) { throw "Generated AMM source missing preserved/optimized token: $token" }
    }

    if($txt -notmatch 'local targetingSystem = (?:Game\.GetTargetingSystem|__gcetGetTargetingSystem)\(\)') {
        throw 'AMM GetTarget did not collapse targeting-system acquisition to one local provider handle.'
    }

    $forbidden=@(
        'prefetchedTarget',
        'targetWasPrefetched',
        '__gcetAmmLastPeriodicHandle',
        '__gcetAmmLastPeriodicKnown',
        '__gcetAmmTargetRefreshElapsed',
        '__gcetAmmRefreshPeriodicTarget',
        '__gcetAmmSavedAppearanceWakePending'
    )
    foreach($token in $forbidden) {
        if($txt.Contains($token)) { throw "Generated AMM source retains unsafe cross-frame cache token: $token" }
    }

    if(([regex]::Matches($txt,'Game\.GetTargetingSystem\(\):GetLookAtObject\(player').Count) -gt 0) {
        throw 'AMM GetTarget still performs the original duplicated targeting-system lookup.'
    }

    $draw=$txt.IndexOf('__gcetRegisterEvent_46("onDraw"')
    $guard=$txt.IndexOf('if not drawWindow and not AMM.Props.buildMode then return end',$draw)
    $setPos=$txt.IndexOf('ImGui.SetNextWindowPos',$draw)
    if($draw -lt 0 -or $guard -lt $draw -or $setPos -lt 0 -or $guard -gt $setPos) {
        throw 'AMM hidden-UI guard is not at the top of the draw path.'
    }

    $cron=$txt.IndexOf('Cron.Update(deltaTime)')
    $periodic=$txt.IndexOf('AMM.currentTarget = AMM:GetTarget()')
    if($cron -lt 0 -or $periodic -lt 0 -or $cron -gt $periodic) {
        throw 'AMM Cron frame lane was moved behind periodic target sensing.'
    }
}
finally { $zip.Dispose() }

Write-Host 'AppearanceMenuMod squeeze + rule-local admission contract passed.'
