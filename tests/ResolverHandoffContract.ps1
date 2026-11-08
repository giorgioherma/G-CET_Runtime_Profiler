param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot
)

$ErrorActionPreference = 'Stop'

$PublishedRoot = (Resolve-Path $PublishedRoot).Path
$exe = Join-Path $PublishedRoot 'G-CET-Runtime-Profiler.exe'
$profilerPayload = Join-Path $PublishedRoot 'payload\cyber_engine_tweaks.PROFILER.dll'
$awareScheduler = Join-Path $PublishedRoot 'payload\0-Engine\modules\G-CET\Scheduler.lua'

if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Profiler executable not found: $exe" }
if (!(Test-Path -LiteralPath $profilerPayload -PathType Leaf)) { throw "Profiler payload not found: $profilerPayload" }
if (!(Test-Path -LiteralPath $awareScheduler -PathType Leaf)) { throw "Profiler-aware Scheduler payload not found: $awareScheduler" }
foreach ($duplicate in @('Scheduler.lua', 'CETProfilerScheduler.lua')) {
    $obsolete = Join-Path $PublishedRoot "payload\0-Engine\modules\$duplicate"
    if (Test-Path -LiteralPath $obsolete -PathType Leaf) { throw "Duplicated $duplicate payload is forbidden." }
}
if (@(Get-ChildItem (Join-Path $PublishedRoot 'payload\0-Engine\modules') -Recurse -File -Filter '*.lua').Count -ne 1) {
    throw "Expected exactly one bundled Lua Scheduler implementation."
}

function New-ResolverFixture([string]$Name, [bool]$WithZeroEngine = $true) {
    $root = Join-Path $env:RUNNER_TEMP $Name
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }

    $plugins = Join-Path $root 'bin\x64\plugins'
    $cet = Join-Path $plugins 'cyber_engine_tweaks'
    $mods = Join-Path $cet 'mods'
    New-Item -ItemType Directory -Force $mods | Out-Null
    Copy-Item -LiteralPath $profilerPayload -Destination (Join-Path $plugins 'cyber_engine_tweaks.asi')

    $zero = Join-Path $mods '0-Engine'
    if ($WithZeroEngine) {
        New-Item -ItemType Directory -Force (Join-Path $zero 'modules') | Out-Null
    }

    [pscustomobject]@{
        Root = $root
        Plugins = $plugins
        Cet = $cet
        Mods = $mods
        Zero = $zero
        StateRoot = Join-Path $plugins '.cet_runtime_profiler'
    }
}

function Read-Status($Fixture) {
    (& $exe --status --game $Fixture.Root --json | ConvertFrom-Json)
}

function Install-Normal($Fixture) {
    (& $exe --install --game $Fixture.Root --json | ConvertFrom-Json)
}

function Install-CoreOnly($Fixture) {
    (& $exe --install --game $Fixture.Root --core-only --json | ConvertFrom-Json)
}

function Restore-Normal($Fixture) {
    (& $exe --restore --game $Fixture.Root --json | ConvertFrom-Json)
}

function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-True([bool]$Value, [string]$Message) {
    if (!$Value) { throw $Message }
}

function Assert-False([bool]$Value, [string]$Message) {
    if ($Value) { throw $Message }
}

Write-Host 'Resolver contract: untouched adaptive host receives only the CETProfilerScheduler.lua live file'
$originalHost = New-ResolverFixture 'gcet-resolver-original-host'
@'
local Engine = {}
function Engine.Register(name)
    return {}
end
return Engine
'@ | Set-Content -LiteralPath (Join-Path $originalHost.Zero 'init.lua') -Encoding utf8
$originalHostInitHash = (Get-FileHash (Join-Path $originalHost.Zero 'init.lua') -Algorithm SHA256).Hash
$originalHostInstall = Install-Normal $originalHost
Assert-Equal $originalHostInstall.managedMode 'adaptive' 'Untouched host did not select adaptive mode.'
Assert-True ([bool]$originalHostInstall.adaptiveProfilerSchedulerPresent) 'Untouched host CET Scheduler was not deployed.'
$originalHostScheduler = Join-Path $originalHost.Zero 'modules\CETProfilerScheduler.lua'
Assert-Equal (Get-FileHash $originalHostScheduler -Algorithm SHA256).Hash (Get-FileHash $awareScheduler -Algorithm SHA256).Hash 'Untouched host deployed wrong Scheduler bytes.'
Assert-False (Test-Path (Join-Path $originalHost.Zero 'modules\Scheduler.lua') -PathType Leaf) 'Untouched host received duplicate root Scheduler.'
$originalHostRestore = Restore-Normal $originalHost
Assert-False (Test-Path $originalHostScheduler -PathType Leaf) 'Untouched host restore left CET Scheduler installed.'
Assert-Equal (Get-FileHash (Join-Path $originalHost.Zero 'init.lua') -Algorithm SHA256).Hash $originalHostInitHash 'Untouched host init was not restored.'

Write-Host 'Resolver contract: composed onUpdate preserves author execution and rejects obsolete v2 bridge'
$authorHost = New-ResolverFixture 'gcet-resolver-author-onupdate'
@'
local Engine = {}
local authorTicks = 0
registerForEvent("onUpdate", function(dt)
    authorTicks = authorTicks + 1
    Engine.LastAuthorDt = dt
end)
return Engine
'@ | Set-Content -LiteralPath (Join-Path $authorHost.Zero 'init.lua') -Encoding utf8
$authorOriginal = (Get-FileHash (Join-Path $authorHost.Zero 'init.lua') -Algorithm SHA256).Hash
$authorInstalled = Install-Normal $authorHost
Assert-Equal $authorInstalled.managedMode 'adaptive' 'Original onUpdate fixture was not installed adaptively.'
$composed = Get-Content -LiteralPath (Join-Path $authorHost.Zero 'init.lua') -Raw
Assert-True ($composed.Contains('CET_RUNTIME_PROFILER_COMPOSED_DISPATCHER_BEGIN v3')) 'Composed callback capture was not injected.'
Assert-True ($composed.Contains('__CETRP_AuthorOnUpdate(...)')) 'Original author callback was not preserved.'
Assert-True ($composed.Contains('__CETRP_SchedulerOnUpdate(...)')) 'Scheduler was not composed into the same event dispatch.'
Assert-False ($composed.Contains('registerForEvent("onUpdate", function(delta)')) 'Second overriding CET update handler was left in the bridge.'
Assert-Equal $authorInstalled.zeroEngineInitKind 'profiler-bridge' 'Live v3 bridge classification failed.'
$authorRestored = Restore-Normal $authorHost
Assert-Equal (Get-FileHash (Join-Path $authorHost.Zero 'init.lua') -Algorithm SHA256).Hash $authorOriginal 'Author onUpdate source was not restored byte-exact.'

$v2 = New-ResolverFixture 'gcet-resolver-obsolete-bridge'
@'
local Engine = {}
-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2
registerForEvent("onUpdate", function() end)
return Engine
'@ | Set-Content -LiteralPath (Join-Path $v2.Zero 'init.lua') -Encoding utf8
$v2Hash = (Get-FileHash (Join-Path $v2.Zero 'init.lua') -Algorithm SHA256).Hash
Assert-Equal (Read-Status $v2).zeroEngineInitKind 'unsafe' 'Legacy v2 must not be accepted as composed.'
$v2Result = (& $exe --install --game $v2.Root --json 2>&1 | Out-String)
if ($v2Result -notmatch 'not recognized as safe') { throw "Obsolete v2 bridge was not rejected: $v2Result" }
Assert-False (Test-Path -LiteralPath $v2.StateRoot -PathType Container) 'Obsolete bridge rejection left a managed transaction.'
Assert-Equal (Get-FileHash (Join-Path $v2.Zero 'init.lua') -Algorithm SHA256).Hash $v2Hash 'Obsolete bridge rejection changed source.'

Write-Host 'Resolver contract: adaptive handoff must preserve the user Scheduler.lua'
$adaptive = New-ResolverFixture 'gcet-resolver-adaptive'
@'
local Engine = {}
function Engine.Register(name)
    local Mod = {}
    return Mod
end
return Engine
'@ | Set-Content -LiteralPath (Join-Path $adaptive.Zero 'init.lua') -Encoding utf8
'-- user scheduler: must remain byte-identical in adaptive mode' |
    Set-Content -LiteralPath (Join-Path $adaptive.Zero 'modules\Scheduler.lua') -Encoding utf8

$adaptiveInitOriginal = (Get-FileHash (Join-Path $adaptive.Zero 'init.lua') -Algorithm SHA256).Hash
$adaptiveSchedulerOriginal = (Get-FileHash (Join-Path $adaptive.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash

$adaptiveBefore = Read-Status $adaptive
Assert-Equal $adaptiveBefore.zeroEngineInitKind 'adaptive' 'Recognizable unintegrated init.lua did not resolve to adaptive.'
Assert-True ([bool]$adaptiveBefore.schedulerPresent) 'Adaptive fixture Scheduler.lua should be present.'
Assert-False ([bool]$adaptiveBefore.schedulerIntegrated) 'Adaptive fixture was incorrectly classified as integrated.'

$adaptiveInstall = Install-Normal $adaptive
Assert-Equal $adaptiveInstall.managedMode 'adaptive' 'Adaptive fixture did not install through adaptive mode.'
Assert-Equal $adaptiveInstall.state.zeroEngine.init.mode 'patched-adaptive' 'Adaptive init.lua transaction mode is wrong.'
Assert-Equal $adaptiveInstall.state.zeroEngine.scheduler.mode 'not-applicable' 'Adaptive mode incorrectly claimed the user Scheduler.lua.'
Assert-True ([bool]$adaptiveInstall.adaptiveProfilerSchedulerPresent) 'Adaptive profiler scheduler was not installed.'

# Regression: a live file can disappear while transaction state still records
# "added". The app must NOT treat that record as a currently healthy install.
$adaptiveLiveProfilerScheduler = Join-Path $adaptive.Zero 'modules\CETProfilerScheduler.lua'
Remove-Item -LiteralPath $adaptiveLiveProfilerScheduler -Force
$adaptiveMissing = Read-Status $adaptive
Assert-True ([bool]$adaptiveMissing.managed) 'Missing live Scheduler unexpectedly cleared the transaction.'
Assert-False ([bool]$adaptiveMissing.adaptiveProfilerSchedulerPresent) 'Missing live Scheduler was reported as present.'
Assert-Equal $adaptiveMissing.scheduler 'LIVE CETProfilerScheduler.lua MISSING' 'Missing live Scheduler was not diagnosed.'
$verifiedAdaptivePayload = $awareScheduler
Copy-Item -LiteralPath $verifiedAdaptivePayload -Destination $adaptiveLiveProfilerScheduler -Force
$adaptiveReverified = Read-Status $adaptive
Assert-True ([bool]$adaptiveReverified.adaptiveProfilerSchedulerPresent) 'Restored Scheduler was not recognized from live SHA256.'

Assert-Equal ((Get-FileHash (Join-Path $adaptive.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $adaptiveSchedulerOriginal 'Adaptive install changed the user Scheduler.lua.'
if ((Get-FileHash (Join-Path $adaptive.Zero 'init.lua') -Algorithm SHA256).Hash -eq $adaptiveInitOriginal) {
    throw 'Adaptive install did not patch the recognized init.lua.'
}

$adaptiveRestore = Restore-Normal $adaptive
Assert-False ([bool]$adaptiveRestore.status.managed) 'Adaptive restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $adaptive.Zero 'init.lua') -Algorithm SHA256).Hash) $adaptiveInitOriginal 'Adaptive restore did not restore init.lua exactly.'
Assert-Equal ((Get-FileHash (Join-Path $adaptive.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $adaptiveSchedulerOriginal 'Adaptive restore changed the user Scheduler.lua.'
Assert-False (Test-Path -LiteralPath (Join-Path $adaptive.Zero 'modules\CETProfilerScheduler.lua') -PathType Leaf) 'Adaptive restore left the profiler-only Scheduler bridge behind.'


Write-Host 'Resolver contract: existing canonical namespaced Scheduler is reused without a second dispatcher'
$namespaced = New-ResolverFixture 'gcet-resolver-namespaced'
$namedInit = @'
local Engine = {}
-- G-CET namespaced scheduler compatibility v1
local __scheduler = require('modules/G-CET/Scheduler')
local __gcetScheduleDriver = true
registerForEvent("onUpdate", function(dt) __scheduler.Update(dt) end)
return Engine
'@
$namedInit | Set-Content -LiteralPath (Join-Path $namespaced.Zero 'init.lua') -Encoding utf8
New-Item -ItemType Directory -Force (Join-Path $namespaced.Zero 'modules\G-CET') | Out-Null
$namespacedCanonical = Join-Path $namespaced.Zero 'modules\G-CET\Scheduler.lua'
$namespacedAlias = Join-Path $namespaced.Zero 'modules\Scheduler.lua'
Copy-Item -LiteralPath $awareScheduler -Destination $namespacedCanonical
Add-Content -LiteralPath $namespacedCanonical -Value '-- user-modified but profiler-aware: preserve this exact source'
"return require('modules/G-CET/Scheduler')" | Set-Content -LiteralPath $namespacedAlias -Encoding utf8
$namedInitHash = (Get-FileHash (Join-Path $namespaced.Zero 'init.lua') -Algorithm SHA256).Hash
$namedCanonicalHash = (Get-FileHash $namespacedCanonical -Algorithm SHA256).Hash
$namedAliasHash = (Get-FileHash $namespacedAlias -Algorithm SHA256).Hash
Assert-False ($namedCanonicalHash -eq (Get-FileHash $awareScheduler -Algorithm SHA256).Hash) 'Namespaced fixture must verify non-hash-locked custom source.'
$namedBefore = Read-Status $namespaced
Assert-Equal $namedBefore.zeroEngineInitKind 'namespaced' 'Namespaced host was not detected by its actual dispatch.'
Assert-True ([bool]$namedBefore.schedulerProfilerAware) 'Custom source with real profiler hooks was not recognized.'
$namedInstall = Install-Normal $namespaced
Assert-Equal $namedInstall.managedMode 'namespaced' 'Namespaced host was misclassified after INSTALL.'
Assert-Equal $namedInstall.state.zeroEngine.namespacedScheduler.mode 'preexisting-profiler' 'Existing namespaced Scheduler was claimed as profiler-owned.'
Assert-Equal $namedInstall.state.zeroEngine.init.mode 'left-untouched' 'Namespaced host init was not preserved.'
Assert-Equal (Get-FileHash (Join-Path $namespaced.Zero 'init.lua') -Algorithm SHA256).Hash $namedInitHash 'Namespaced init.lua was modified.'
Assert-Equal (Get-FileHash $namespacedCanonical -Algorithm SHA256).Hash $namedCanonicalHash 'Namespaced canonical Scheduler was overwritten.'
Assert-Equal (Get-FileHash $namespacedAlias -Algorithm SHA256).Hash $namedAliasHash 'Namespaced root alias was overwritten.'
Assert-False (Test-Path (Join-Path $namespaced.Zero 'modules\CETProfilerScheduler.lua') -PathType Leaf) 'Namespaced host got an extra adaptive Scheduler.'
Assert-False (Test-Path (Join-Path $namespaced.StateRoot '0-Engine.NamespacedScheduler.ORIGINAL.lua') -PathType Leaf) 'Existing namespaced Scheduler was unnecessarily backed up for replacement.'
$namedRestore = Restore-Normal $namespaced
Assert-False ([bool]$namedRestore.status.managed) 'Namespaced restore left managed state behind.'
Assert-Equal (Get-FileHash (Join-Path $namespaced.Zero 'init.lua') -Algorithm SHA256).Hash $namedInitHash 'Namespaced restore changed init.'
Assert-Equal (Get-FileHash $namespacedCanonical -Algorithm SHA256).Hash $namedCanonicalHash 'Namespaced restore changed the canonical Scheduler.'
Assert-Equal (Get-FileHash $namespacedAlias -Algorithm SHA256).Hash $namedAliasHash 'Namespaced restore changed the root alias.'

Write-Host 'Resolver contract: partial namespaced host receives only its missing canonical Scheduler'
$namedPartial = New-ResolverFixture 'gcet-resolver-namespaced-partial'
$namedInit | Set-Content -LiteralPath (Join-Path $namedPartial.Zero 'init.lua') -Encoding utf8
$partialInitHash = (Get-FileHash (Join-Path $namedPartial.Zero 'init.lua') -Algorithm SHA256).Hash
$partialAlias = Join-Path $namedPartial.Zero 'modules\Scheduler.lua'
"return require('modules/G-CET/Scheduler')" | Set-Content -LiteralPath $partialAlias -Encoding utf8
$partialAliasHash = (Get-FileHash $partialAlias -Algorithm SHA256).Hash
$partialCanonical = Join-Path $namedPartial.Zero 'modules\G-CET\Scheduler.lua'
$partialBefore = Read-Status $namedPartial
Assert-Equal $partialBefore.zeroEngineInitKind 'namespaced' 'Missing canonical worker did not retain host source recognition.'
Assert-False ([bool]$partialBefore.schedulerPresent) 'Missing canonical worker was incorrectly reported present due to root alias.'
$partialInstall = Install-Normal $namedPartial
Assert-Equal $partialInstall.managedMode 'namespaced' 'Partial namespaced host selected wrong mode.'
Assert-Equal $partialInstall.state.zeroEngine.namespacedScheduler.mode 'added' 'Missing canonical source was not recorded as added.'
Assert-True ([bool]$partialInstall.schedulerProfilerAware) 'Newly deployed namespaced Scheduler not recognized.'
Assert-Equal (Get-FileHash $partialCanonical -Algorithm SHA256).Hash (Get-FileHash $awareScheduler -Algorithm SHA256).Hash 'Wrong Scheduler source deployed into the namespaced import.'
Assert-Equal (Get-FileHash $partialAlias -Algorithm SHA256).Hash $partialAliasHash 'Partial host compatibility alias was changed.'
Assert-Equal (Get-FileHash (Join-Path $namedPartial.Zero 'init.lua') -Algorithm SHA256).Hash $partialInitHash 'Partial host init.lua was changed.'
Assert-False (Test-Path (Join-Path $namedPartial.Zero 'modules\CETProfilerScheduler.lua') -PathType Leaf) 'Partial host was given a duplicate CET callback owner.'
Remove-Item -LiteralPath $partialCanonical -Force
$partialMissing = Read-Status $namedPartial
Assert-Equal $partialMissing.scheduler 'LIVE G-CET/Scheduler.lua MISSING' 'Live removal of canonical Scheduler was not detected.'
Assert-False ([bool]$partialMissing.schedulerProfilerAware) 'Live removal still reported profiler-aware.'
$partialRestore = Restore-Normal $namedPartial
Assert-False (Test-Path $partialCanonical -PathType Leaf) 'Restore did not remove profiler-added canonical Scheduler.'
Assert-Equal (Get-FileHash $partialAlias -Algorithm SHA256).Hash $partialAliasHash 'Restore changed the partial host alias.'
Assert-Equal (Get-FileHash (Join-Path $namedPartial.Zero 'init.lua') -Algorithm SHA256).Hash $partialInitHash 'Restore changed the partial host init.'

Write-Host 'Resolver contract: unsafe namespaced Scheduler source drift fails closed'
$namedDrift = New-ResolverFixture 'gcet-resolver-namespaced-drift'
$namedInit | Set-Content -LiteralPath (Join-Path $namedDrift.Zero 'init.lua') -Encoding utf8
New-Item -ItemType Directory -Force (Join-Path $namedDrift.Zero 'modules\G-CET') | Out-Null
$driftCanonical = Join-Path $namedDrift.Zero 'modules\G-CET\Scheduler.lua'
'return { Update = function() end } -- missing profile measurement hooks' |
    Set-Content -LiteralPath $driftCanonical -Encoding utf8
$driftHash = (Get-FileHash $driftCanonical -Algorithm SHA256).Hash
$driftOut = (& $exe --install --game $namedDrift.Root --json 2>&1 | Out-String)
if ($driftOut -notmatch 'lacks required profiler hooks') {
    throw "Unsafe namespaced Scheduler was not rejected: $driftOut"
}
Assert-Equal (Get-FileHash $driftCanonical -Algorithm SHA256).Hash $driftHash 'Unsafe namespaced Scheduler was overwritten.'
Assert-False (Test-Path -LiteralPath $namedDrift.StateRoot -PathType Container) 'Unsafe namespaced install left a managed transaction behind.'

Write-Host 'Resolver contract: failed install rolls back newly added namespaced Scheduler'
$namedAbort = New-ResolverFixture 'gcet-resolver-namespaced-abort'
$namedInit | Set-Content -LiteralPath (Join-Path $namedAbort.Zero 'init.lua') -Encoding utf8
$abortCanonical = Join-Path $namedAbort.Zero 'modules\G-CET\Scheduler.lua'
$abortInitHash = (Get-FileHash (Join-Path $namedAbort.Zero 'init.lua') -Algorithm SHA256).Hash
# Block the controls directory only after the canonical Scheduler deployment
# so that transaction rollback must clean up the newly added worker.
'prevent controls directory creation' |
    Set-Content -LiteralPath (Join-Path $namedAbort.Mods 'CETProfilerControls') -Encoding utf8
$abortOut = (& $exe --install --game $namedAbort.Root --json 2>&1 | Out-String)
if ($abortOut -notmatch 'error|fail|exists|directory') {
    throw "Expected an intentional controls-directory failure: $abortOut"
}
Assert-False (Test-Path $abortCanonical -PathType Leaf) 'Failed install leaked profiler-added namespaced Scheduler.'
Assert-Equal (Get-FileHash (Join-Path $namedAbort.Zero 'init.lua') -Algorithm SHA256).Hash $abortInitHash 'Failed install changed original namespaced init.'
Assert-False (Test-Path -LiteralPath $namedAbort.StateRoot -PathType Container) 'Failed install left managed state behind.'

Write-Host 'Resolver contract: integrated handoff must preserve init.lua and transactionally swap Scheduler.lua'
$integrated = New-ResolverFixture 'gcet-resolver-integrated'
@'
local Engine = {}
local Mod = {}
local Scheduler = require('modules/Scheduler')
Scheduler.Init(nil, nil)
Scheduler.Update(nil)
Engine.Schedule = {}
Mod.Schedule = {}
return Engine
'@ | Set-Content -LiteralPath (Join-Path $integrated.Zero 'init.lua') -Encoding utf8
'-- ordinary user scheduler that should be backed up and temporarily replaced' |
    Set-Content -LiteralPath (Join-Path $integrated.Zero 'modules\Scheduler.lua') -Encoding utf8

$integratedInitOriginal = (Get-FileHash (Join-Path $integrated.Zero 'init.lua') -Algorithm SHA256).Hash
$integratedSchedulerOriginal = (Get-FileHash (Join-Path $integrated.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash
$awareSchedulerHash = (Get-FileHash $awareScheduler -Algorithm SHA256).Hash

$integratedBefore = Read-Status $integrated
Assert-Equal $integratedBefore.zeroEngineInitKind 'integrated' 'Recognized Scheduler API init.lua did not resolve to integrated.'
Assert-True ([bool]$integratedBefore.schedulerIntegrated) 'Integrated fixture did not report integrated Scheduler API.'
Assert-False ([bool]$integratedBefore.schedulerProfilerAware) 'Ordinary Scheduler.lua incorrectly reported profiler-aware.'

$integratedInstall = Install-Normal $integrated
Assert-Equal $integratedInstall.managedMode 'integrated' 'Integrated fixture did not use integrated mode.'
Assert-Equal $integratedInstall.state.zeroEngine.init.mode 'preexisting-compatible' 'Integrated init.lua should be recorded as pre-existing compatible.'
Assert-Equal $integratedInstall.state.zeroEngine.scheduler.mode 'replaced' 'Integrated ordinary Scheduler.lua should be transactionally replaced.'
Assert-Equal ((Get-FileHash (Join-Path $integrated.Zero 'init.lua') -Algorithm SHA256).Hash) $integratedInitOriginal 'Integrated install changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $integrated.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $awareSchedulerHash 'Integrated install did not deploy the profiler-aware Scheduler.lua.'

$integratedBackup = Join-Path $integrated.StateRoot '0-Engine.Scheduler.ORIGINAL.lua'
Assert-True (Test-Path -LiteralPath $integratedBackup -PathType Leaf) 'Integrated Scheduler backup was not created.'
Assert-Equal ((Get-FileHash $integratedBackup -Algorithm SHA256).Hash) $integratedSchedulerOriginal 'Integrated Scheduler backup is not byte-identical.'

$integratedRestore = Restore-Normal $integrated
Assert-False ([bool]$integratedRestore.status.managed) 'Integrated restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $integrated.Zero 'init.lua') -Algorithm SHA256).Hash) $integratedInitOriginal 'Integrated restore changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $integrated.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $integratedSchedulerOriginal 'Integrated restore did not restore the original Scheduler.lua.'

Write-Host 'Resolver contract: already profiler-aware integrated Scheduler must not be claimed/replaced'
$aware = New-ResolverFixture 'gcet-resolver-aware'
@'
local Engine = {}
local Mod = {}
local Scheduler = require('modules/Scheduler')
Scheduler.Init(nil, nil)
Scheduler.Update(nil)
Engine.Schedule = {}
Mod.Schedule = {}
return Engine
'@ | Set-Content -LiteralPath (Join-Path $aware.Zero 'init.lua') -Encoding utf8
Copy-Item -LiteralPath $awareScheduler -Destination (Join-Path $aware.Zero 'modules\Scheduler.lua')
Add-Content -LiteralPath (Join-Path $aware.Zero 'modules\Scheduler.lua') -Value '-- a safe source-level variation preserving all hooks'
$customAwareHash = (Get-FileHash (Join-Path $aware.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash
Assert-False ($customAwareHash -eq $awareSchedulerHash) 'Source-level recognition requires a non-identical Scheduler fixture.'

$awareInitOriginal = (Get-FileHash (Join-Path $aware.Zero 'init.lua') -Algorithm SHA256).Hash
$awareBefore = Read-Status $aware
Assert-True ([bool]$awareBefore.schedulerProfilerAware) 'Known profiler-aware Scheduler was not recognized.'

$awareInstall = Install-Normal $aware
Assert-Equal $awareInstall.managedMode 'integrated' 'Profiler-aware integrated fixture changed resolver mode.'
Assert-Equal $awareInstall.state.zeroEngine.scheduler.mode 'preexisting-profiler' 'Pre-existing profiler-aware Scheduler should not be claimed as replaced.'
Assert-Equal ((Get-FileHash (Join-Path $aware.Zero 'init.lua') -Algorithm SHA256).Hash) $awareInitOriginal 'Profiler-aware integrated install changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $aware.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $customAwareHash 'Profiler-aware Scheduler changed unexpectedly.'
Assert-False (Test-Path -LiteralPath (Join-Path $aware.StateRoot '0-Engine.Scheduler.ORIGINAL.lua') -PathType Leaf) 'Profiler-aware Scheduler unexpectedly received a replacement backup.'

$awareRestore = Restore-Normal $aware
Assert-False ([bool]$awareRestore.status.managed) 'Profiler-aware restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $aware.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $customAwareHash 'Profiler-aware Scheduler changed during restore.'

Write-Host 'Resolver contract: pre-existing adaptive bridge marker is recognized without rewriting init.lua'
$bridge = New-ResolverFixture 'gcet-resolver-bridge'
@'
local Engine = {}
-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v3
-- pre-existing recognized profiler bridge marker
return Engine
'@ | Set-Content -LiteralPath (Join-Path $bridge.Zero 'init.lua') -Encoding utf8

$bridgeInitOriginal = (Get-FileHash (Join-Path $bridge.Zero 'init.lua') -Algorithm SHA256).Hash
$bridgeBefore = Read-Status $bridge
Assert-Equal $bridgeBefore.zeroEngineInitKind 'profiler-bridge' 'Existing profiler bridge marker was not recognized.'

$bridgeInstall = Install-Normal $bridge
Assert-Equal $bridgeInstall.managedMode 'adaptive' 'Pre-existing bridge should use adaptive handoff.'
Assert-Equal $bridgeInstall.state.zeroEngine.init.mode 'preexisting-profiler-bridge' 'Pre-existing bridge init.lua should not be claimed as patched.'
Assert-Equal ((Get-FileHash (Join-Path $bridge.Zero 'init.lua') -Algorithm SHA256).Hash) $bridgeInitOriginal 'Pre-existing bridge init.lua was rewritten.'

$bridgeRestore = Restore-Normal $bridge
Assert-False ([bool]$bridgeRestore.status.managed) 'Pre-existing bridge restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $bridge.Zero 'init.lua') -Algorithm SHA256).Hash) $bridgeInitOriginal 'Pre-existing bridge init.lua changed during restore.'
Assert-False (Test-Path -LiteralPath (Join-Path $bridge.Zero 'modules\CETProfilerScheduler.lua') -PathType Leaf) 'Profiler-added adaptive Scheduler was not removed from pre-existing bridge fixture.'

Write-Host 'Resolver contract: unsafe structure must reject full integration before any mutation'
$unsafe = New-ResolverFixture 'gcet-resolver-unsafe'
'-- unsupported custom loader with no proven Scheduler API or safe Engine return anchor' |
    Set-Content -LiteralPath (Join-Path $unsafe.Zero 'init.lua') -Encoding utf8
'-- existing user scheduler' |
    Set-Content -LiteralPath (Join-Path $unsafe.Zero 'modules\Scheduler.lua') -Encoding utf8

$unsafeAsiOriginal = (Get-FileHash (Join-Path $unsafe.Plugins 'cyber_engine_tweaks.asi') -Algorithm SHA256).Hash
$unsafeInitOriginal = (Get-FileHash (Join-Path $unsafe.Zero 'init.lua') -Algorithm SHA256).Hash
$unsafeSchedulerOriginal = (Get-FileHash (Join-Path $unsafe.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash
$unsafeBefore = Read-Status $unsafe
Assert-Equal $unsafeBefore.zeroEngineInitKind 'unsafe' 'Unsupported init.lua was not classified unsafe.'

$rejectOutput = (& $exe --install --game $unsafe.Root --json 2>&1 | Out-String)
if ($rejectOutput -notmatch 'not recognized as safe') {
    throw "Unsafe full install did not report the expected structural rejection. Output: $rejectOutput"
}
Assert-False (Test-Path -LiteralPath $unsafe.StateRoot -PathType Container) 'Unsafe full install created managed state before rejecting.'
Assert-False (Test-Path -LiteralPath (Join-Path $unsafe.Mods 'CETProfilerControls') -PathType Container) 'Unsafe full install deployed controls before rejecting.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Plugins 'cyber_engine_tweaks.asi') -Algorithm SHA256).Hash) $unsafeAsiOriginal 'Unsafe full install changed the CET ASI.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Zero 'init.lua') -Algorithm SHA256).Hash) $unsafeInitOriginal 'Unsafe full install changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $unsafeSchedulerOriginal 'Unsafe full install changed Scheduler.lua.'

Write-Host 'Resolver contract: core-only fallback must leave unsafe 0-Engine byte-untouched'
$coreInstall = Install-CoreOnly $unsafe
Assert-Equal $coreInstall.managedMode 'core-only' 'Unsafe fixture did not enter core-only fallback.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Zero 'init.lua') -Algorithm SHA256).Hash) $unsafeInitOriginal 'Core-only install changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $unsafeSchedulerOriginal 'Core-only install changed Scheduler.lua.'

$coreRestore = Restore-Normal $unsafe
Assert-False ([bool]$coreRestore.status.managed) 'Core-only restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Zero 'init.lua') -Algorithm SHA256).Hash) $unsafeInitOriginal 'Core-only restore changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $unsafe.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $unsafeSchedulerOriginal 'Core-only restore changed Scheduler.lua.'

Write-Host 'Resolver contract: absent 0-Engine must remain absent'
$absent = New-ResolverFixture 'gcet-resolver-absent' $false
$absentInstall = Install-Normal $absent
Assert-Equal $absentInstall.managedMode 'absent' 'No-0-Engine fixture did not resolve to absent mode.'
Assert-False (Test-Path -LiteralPath $absent.Zero -PathType Container) 'Install created an 0-Engine directory when none existed.'
$absentRestore = Restore-Normal $absent
Assert-False ([bool]$absentRestore.status.managed) 'No-0-Engine restore left managed state active.'
Assert-False (Test-Path -LiteralPath $absent.Zero -PathType Container) 'Restore created an 0-Engine directory when none existed.'

Write-Host 'Alpha6b resolver/handoff contract regression tests passed.'
