param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root 'src/G.CETProfiler.Core/Services/ModPasses/NativeInteractions.cs'
$source = [IO.File]::ReadAllText($path)
# Static ownership/structure regression only. Actual pristine-source output
# and gameplay require the user's real mod archive and runtime validation.
$required = @(
    'WriteCoordinator("nativeinteractions_engine_worker.lua"',
    'local __gcetNifCadence = require(',
    '__gcetNifCadence.update(dt, self, manager, world, sceneLaunchPending, ',
    'function M.reset()',
    'function M.update(dt, mod, manager, world, sceneLaunchPending, getQuests)',
    'manager.updateDormant(position)',
    'manager.dormantPhase = (manager.dormantPhase % 4) + 1',
    'for i = manager.dormantPhase, #list, 4 do list[i]:onUpdate(pos) end',
    'function world.update(player, posPlayer, playerForward, quests)',
    'posPlayer = posPlayer or player:GetWorldPosition()',
    'playerForward = playerForward or player:GetWorldForward()',
    'local position = player:GetWorldPosition()',
    'local forward = player:GetWorldForward()',
    'local sceneLaunchPending = #resourceHelper.sceneQueue > 0',
    'resourceHelper.onUpdate()',
    'Cron.Update(dt)',
    'self.quests = nil',
    'manager.dormantPhase = 0',
    'world.pinInteractions = {}'
)
foreach ($token in $required) {
    if (!$source.Contains($token)) { throw "NIF authoritative worker structure missing: $token" }
}
if ([regex]::Matches($source,'WriteCoordinator\("nativeinteractions_engine_worker\.lua"').Count -ne 1) {
    throw 'NIF coordinator must have exactly one producing source location'
}
if ($source.Contains('local __gcetNifIdleWorldElapsed = 0.0')) {
    throw 'NIF obsolete inline cadence state reintroduced'
}
Write-Host 'NIF static worker ownership/phase/native-read regression: PASS (not end-to-end source validation)'
