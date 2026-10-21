param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-vehicle-worker-contract'
$owner=Join-Path $root 'VehicleMileage'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $owner | Out-Null
$init=@'
local function maybe_run_gas(dt, id, label, vehicle, spec)
end
GAS.setup({
})
registerForEvent("onInit", function()
  if Mod then
    Mod.Subscribe("VehicleMount", on_vehicle_mount)
    Mod.Subscribe("VehicleUnmount", on_vehicle_unmount)
  end
end)
'@
$gas=@'
function M.setup(opts)
end
function M.update(dt, helpers)
  M._currentStationEmpty = false
  local pointIdx, gasPoint = findGasPoint(px, py, pz)
end
'@
[IO.File]::WriteAllText((Join-Path $owner 'init.lua'),$init)
[IO.File]::WriteAllText((Join-Path $owner 'vm_gas_station.lua'),$gas)
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$type=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$ctx=$ctor.Invoke([object[]]@([string]$root,[string]'VehicleMileage',[string]'native-location-vehicle-mileage-pumps',[Collections.Generic.Dictionary[string,byte[]]]::new()))
$m=$type.GetMethod('ApplyVehicleMileageNativePumps',$flags)
try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw $_.Exception.InnerException.Message}
if(!$result.Applied){throw 'Vehicle worker stage not applied'}
$rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
if($rows.Count -ne 3){throw "Vehicle expected 3 outputs, got $($rows.Count)"}
$worker=@($rows | Where-Object{$_.RelativeFile -like '*/GCET_Semantic/vehicle_native_pumps.lua'})|Select-Object -First 1
if(!$worker){throw 'Vehicle native worker output missing'}
$workerSource=[Text.Encoding]::UTF8.GetString($worker.Bytes)
if(!$workerSource.Contains('attachCompiled(10040, #gas.points')){throw 'Vehicle worker missing native host registration'}
Write-Host 'Vehicle pump coordinator output: PASS'
