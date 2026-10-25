// Tiny source-independent fixtures for the production-overlay validator.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawnSync } = require('node:child_process');
const validator = path.join(__dirname, 'ValidateGeneratedLuaInvariants.cjs');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'gcet-overlay-validation-'));
const mods = path.join(root, 'bin', 'x64', 'plugins', 'cyber_engine_tweaks', 'mods');
const owner = path.join(mods, 'FixtureOwner');
const worker = path.join(owner, 'GCET_Semantic', 'worker.lua');
const source = path.join(owner, 'init.lua');
function run(expectedPass, label) {
  const r = spawnSync(process.execPath, [validator, root], { encoding: 'utf8' });
  if ((r.status === 0) !== expectedPass) {
    throw new Error(label + ' expected ' + (expectedPass ? 'PASS' : 'FAIL') +
      '; got exit ' + r.status + '\n' + r.stdout + '\n' + r.stderr);
  }
}
try {
  fs.mkdirSync(path.dirname(worker), { recursive: true });
  fs.writeFileSync(source, 'local worker = require("GCET_Semantic/worker")\nreturn worker.attach()\n');
  fs.writeFileSync(worker, 'local M = {}\nfunction M.attach(host) return host:attachCompiled(10039, 1) end\nreturn M\n');
  run(true, 'complete external coordinator');
  fs.unlinkSync(worker);
  run(false, 'missing external coordinator');
  fs.writeFileSync(worker, 'return {attach = function() end}\n');
  fs.writeFileSync(source, 'local player = Game.GetPlayer()\nif not player and not player:GetWorldPosition() then return end\n');
  run(false, 'unsafe nil guard');
  fs.writeFileSync(source, 'return require("GCET_Semantic/worker").attach()\n');
  fs.writeFileSync(worker, 'return {attach = function(host) return host:attachCompiled(10039, 1) + host:attachCompiled(10039, 1) end}\n');
  run(false, 'duplicate native owner');
  console.log('Generated Lua structural invariants contract PASS');
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
