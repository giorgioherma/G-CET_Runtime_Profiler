param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.ZiggyProximitySource',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$m=$t.GetMethod('ReduceProtectedUpdateAllocations',$flags)
if(!$m){throw 'Missing Ziggy authoritative safe-update source stage'}
$old='local ok,err=xpcall(function()update(dt)end,function(e)return tostring(e)end)'
$a=$m.Invoke($null,[object[]]@([string]$old))
if(!$a.Contains('local ok,err=pcall(update,dt)') -or !$a.Contains('if not ok then err=tostring(err) end') -or !$a.Contains('ziggy-no-frame-closure-v7') -or $a.Contains('xpcall(function()')){throw 'Ziggy protected event recovery not preserved'}
if($m.Invoke($null,[object[]]@([string]$a)) -ne $a){throw 'Ziggy stage not idempotent'}
$drift=$old.Replace('function(e)return tostring(e)end','function(e)return "lost" end')
$failed=$false
try{$m.Invoke($null,[object[]]@([string]$drift))|Out-Null}catch{$failed=$true}
if(!$failed){throw 'Ziggy error-handler drift allowed'}
Write-Host 'Ziggy protected update source, dt/error preservation and idempotence PASS'
