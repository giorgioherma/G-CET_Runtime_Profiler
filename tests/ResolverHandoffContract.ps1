param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot
)

$ErrorActionPreference = 'Stop'

$PublishedRoot = (Resolve-Path $PublishedRoot).Path
$exe = Join-Path $PublishedRoot 'G-CET-Runtime-Profiler.exe'
$profilerAsi = Join-Path $PublishedRoot 'payload\cyber_engine_tweaks.PROFILER.asi'
$awareScheduler = Join-Path $PublishedRoot 'payload\0-Engine\modules\Scheduler.lua'

if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Profiler executable not found: $exe" }
if (!(Test-Path -LiteralPath $profilerAsi -PathType Leaf)) { throw "Profiler ASI not found: $profilerAsi" }
if (!(Test-Path -LiteralPath $awareScheduler -PathType Leaf)) { throw "Profiler-aware Scheduler payload not found: $awareScheduler" }

function New-ResolverFixture([string]$Name, [bool]$WithZeroEngine = $true) {
    $root = Join-Path $env:RUNNER_TEMP $Name
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }

    $plugins = Join-Path $root 'bin\x64\plugins'
    $cet = Join-Path $plugins 'cyber_engine_tweaks'
    $mods = Join-Path $cet 'mods'
    New-Item -ItemType Directory -Force $mods | Out-Null
    Copy-Item -LiteralPath $profilerAsi -Destination (Join-Path $plugins 'cyber_engine_tweaks.asi')

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
Assert-Equal ((Get-FileHash (Join-Path $adaptive.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $adaptiveSchedulerOriginal 'Adaptive install changed the user Scheduler.lua.'
if ((Get-FileHash (Join-Path $adaptive.Zero 'init.lua') -Algorithm SHA256).Hash -eq $adaptiveInitOriginal) {
    throw 'Adaptive install did not patch the recognized init.lua.'
}

$adaptiveRestore = Restore-Normal $adaptive
Assert-False ([bool]$adaptiveRestore.status.managed) 'Adaptive restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $adaptive.Zero 'init.lua') -Algorithm SHA256).Hash) $adaptiveInitOriginal 'Adaptive restore did not restore init.lua exactly.'
Assert-Equal ((Get-FileHash (Join-Path $adaptive.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $adaptiveSchedulerOriginal 'Adaptive restore changed the user Scheduler.lua.'
Assert-False (Test-Path -LiteralPath (Join-Path $adaptive.Zero 'modules\CETProfilerScheduler.lua') -PathType Leaf) 'Adaptive restore left the profiler-only Scheduler bridge behind.'

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

$awareInitOriginal = (Get-FileHash (Join-Path $aware.Zero 'init.lua') -Algorithm SHA256).Hash
$awareBefore = Read-Status $aware
Assert-True ([bool]$awareBefore.schedulerProfilerAware) 'Known profiler-aware Scheduler was not recognized.'

$awareInstall = Install-Normal $aware
Assert-Equal $awareInstall.managedMode 'integrated' 'Profiler-aware integrated fixture changed resolver mode.'
Assert-Equal $awareInstall.state.zeroEngine.scheduler.mode 'preexisting-profiler' 'Pre-existing profiler-aware Scheduler should not be claimed as replaced.'
Assert-Equal ((Get-FileHash (Join-Path $aware.Zero 'init.lua') -Algorithm SHA256).Hash) $awareInitOriginal 'Profiler-aware integrated install changed init.lua.'
Assert-Equal ((Get-FileHash (Join-Path $aware.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $awareSchedulerHash 'Profiler-aware Scheduler changed unexpectedly.'
Assert-False (Test-Path -LiteralPath (Join-Path $aware.StateRoot '0-Engine.Scheduler.ORIGINAL.lua') -PathType Leaf) 'Profiler-aware Scheduler unexpectedly received a replacement backup.'

$awareRestore = Restore-Normal $aware
Assert-False ([bool]$awareRestore.status.managed) 'Profiler-aware restore left managed state active.'
Assert-Equal ((Get-FileHash (Join-Path $aware.Zero 'modules\Scheduler.lua') -Algorithm SHA256).Hash) $awareSchedulerHash 'Profiler-aware Scheduler changed during restore.'

Write-Host 'Resolver contract: pre-existing adaptive bridge marker is recognized without rewriting init.lua'
$bridge = New-ResolverFixture 'gcet-resolver-bridge'
@'
local Engine = {}
-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2
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
