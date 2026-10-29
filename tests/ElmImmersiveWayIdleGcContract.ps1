param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$fn=$t.GetMethod('ApplyElmIdleGc',$flags)
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object {$_.GetParameters().Count -eq 4})[0]
if(!$fn -or !$ctor){throw 'Missing ELM original-source generator'}
$root=Join-Path $env:RUNNER_TEMP 'gcet-elm-world-interaction-source'
$owner=Join-Path $root 'ELM_ImmersiveWay'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $owner|Out-Null
$init=@'
local active={cloneID=nil,clone=nil,workspotID=nil,workspotSpawn=nil,darkFutureActive=false,dfInfoPanelSpawnRaw=nil}
local editorLiveMoveQueued=false
local flushingEtiquetteShowerActive=false
local jobs={}
local function updateDarkFutureInfoPanelFollow(delta) end
local EarnMoney={}
local EarnXP={}
local Jobs={}
local ToiletEffect={}
registerForEvent("onUpdate", function(delta)
  for i = #jobs, 1, -1 do
    local job = jobs[i]
    job.t = job.t - delta
  end
    SceneInteraction.Update()
    safeCall("DarkFuture TickScene", function() tickDarkFutureScene(delta) end)
    if EarnMoney and EarnMoney.Tick then EarnMoney.Tick(delta) end
    if EarnXP and EarnXP.Tick then EarnXP.Tick(delta) end
    if Jobs and Jobs.Tick then Jobs.Tick(delta) end
    if ToiletEffect and ToiletEffect.Tick then ToiletEffect.Tick(delta) end
end)
'@
$scene=@'
local ui={hubShown=false,refreshPending=false,input=false}
local shown=false
local SceneInteraction={}
function SceneInteraction.Show(choiceUniqueID)
  shown=true
end
function SceneInteraction.Hide()
    ui.hideHub()
    shown = false
end
function SceneInteraction.Update()
 ui.update()
end
return SceneInteraction
'@
$xp=@'
local EarnXP={}
local active=false
function EarnXP.Start(rawConfig)
  active = true
end
function EarnXP.Stop()
  active = false
end
function EarnXP.Tick(delta)
 if not active then return end
end
return EarnXP
'@
$files=@{'init.lua'=$init;'elm_scene_interaction.lua'=$scene;'elm_earnxp.lua'=$xp}
foreach($name in $files.Keys){[IO.File]::WriteAllText((Join-Path $owner $name),$files[$name],[Text.UTF8Encoding]::new($false))}
function Apply {
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'ELM_ImmersiveWay',[string]'elm-immersiveway-idle-gc',[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)))
 try{$result=$fn.Invoke($null,[object[]]@($ctx))}catch{throw $_.Exception.InnerException.Message}
 return @{result=$result;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$a=Apply
if(!$a.result.Applied -or $a.rows.Count -ne 3){throw "ELM original source must produce 3 changed files, got $($a.rows.Count)"}
foreach($row in $a.rows){
 $name=[IO.Path]::GetFileName($row.RelativeFile)
 $s=[Text.Encoding]::UTF8.GetString($row.Bytes)
 if($name -eq 'init.lua' -and (!$s.Contains('elm-immersiveway-idle-gc-v1') -or !$s.Contains('then return end') -or !$s.Contains('SceneInteraction.Update()'))){throw 'ELM main/source callbacks lost'}
 if($name -eq 'elm_scene_interaction.lua' -and !$s.Contains('return shown or ui.hubShown or ui.refreshPending or ui.input')){throw 'ELM interaction wake lost'}
 if($name -eq 'elm_earnxp.lua' -and !$s.Contains('return active == true')){throw 'ELM XP timing wake lost'}
 if([IO.File]::ReadAllText((Join-Path $owner $name)) -ne $files[$name]){throw 'Author source mutated in-place'}
 [IO.File]::WriteAllBytes((Join-Path $owner $name),$row.Bytes)
}
$b=Apply
if($b.result.Applied -or $b.rows.Count -ne 0){throw 'ELM complete stage was duplicated'}
[IO.File]::WriteAllText((Join-Path $owner 'elm_earnxp.lua'),$xp,[Text.UTF8Encoding]::new($false))
$c=Apply
if(!$c.result.Applied -or $c.rows.Count -ne 1 -or !$c.rows[0].RelativeFile.EndsWith('elm_earnxp.lua')){throw 'ELM partial XP stage not resumed'}
[IO.File]::WriteAllText((Join-Path $owner 'elm_scene_interaction.lua'),$scene.Replace('shown = false','DRIFTED_SHOWN = false'),[Text.UTF8Encoding]::new($false))
$failed=$false
try{Apply|Out-Null}catch{$failed=$true}
if(!$failed){throw 'ELM unknown Show/Hide lifecycle accepted'}
Write-Host 'ELM original, partial, idempotent and source drift tests PASS'
