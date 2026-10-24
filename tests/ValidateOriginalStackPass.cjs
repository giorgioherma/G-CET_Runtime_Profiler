// Private-stack CI acceptance: only report file paths/contract names, never author source.
// Input: real generated Resolver overlay + manifest and exact CET source used for generation.
const fs = require('node:fs');
const path = require('node:path');
const luaparse = require('luaparse');

const [mode, inputMods, overlayRoot, manifestFile] = process.argv.slice(2);
if (!['initial', 'repeated'].includes(mode) || !inputMods || !overlayRoot || !manifestFile) {
  console.error('Usage: node ValidateOriginalStackPass.cjs initial|repeated MODS OVERLAY MANIFEST');
  process.exit(2);
}
const prefix = path.join(overlayRoot, 'bin', 'x64', 'plugins', 'cyber_engine_tweaks', 'mods');
const failures = [];
const cetSourceRoot = 'bin/x64/plugins/cyber_engine_tweaks/mods/';
let parsed = 0;
// Print stable game-relative locations, never private runner paths.
function sourceLocation(modRelativeFile) {
  const relative = String(modRelativeFile || '').replace(/\\/g, '/').replace(/^\/+/, '');
  if (!relative || relative === '.' || relative.split('/').includes('..')) return null;
  return cetSourceRoot + relative;
}
function missingOriginal(modRelativeFile) {
  const file = sourceLocation(modRelativeFile);
  if (!file) {
    fail('Resolver', 'missing-original-file-path-unavailable');
    return;
  }
  fail(file, 'MISSING_REQUIRED_ORIGINAL: supply exactly this file');
}
function fail(file, check) {
  const title = String(check).replace(/[:\r\n]/g, ' ');
  console.error('::error title=' + title + '::' + file + ' - ' + title);
  failures.push(file + ': ' + title);
}
function allFiles(root) {
  if (!fs.existsSync(root)) return [];
  const result = [];
  function visit(dir) {
    for (const item of fs.readdirSync(dir, {withFileTypes: true})) {
      const full = path.join(dir, item.name);
      if (item.isDirectory()) visit(full);
      else if (item.isFile()) result.push(full);
    }
  }
  visit(root);
  return result;
}
function relative(p) { return path.relative(prefix, p).replace(/\\/g, '/'); }
// An untouched init.lua is the authoritative presence marker for an installed CET mod.
// Generated workers and framework files are outputs, not required original inputs.
function hasOriginalInit(owner) {
  return fs.existsSync(path.join(inputMods, owner, 'init.lua'));
}
function text(p) { return fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, ''); }
function requireContains(owner, file, needles) {
  if (!fs.existsSync(file)) return fail(owner, 'required-generated-source-missing');
  const s = text(file);
  for (const item of needles) if (!s.includes(item)) fail(owner, 'missing-required-function-or-optimization-' + item);
}
if (!fs.existsSync(manifestFile)) fail('Resolver', 'generation-manifest-missing');
let manifest = {};
try { manifest = JSON.parse(text(manifestFile)); } catch { fail('Resolver', 'generation-manifest-invalid'); }
const stats = manifest.summary || {};
if ((stats.transforms || 0) < 1) fail('Resolver', 'no-composed-transformations-produced');
// Manifest generic candidates identify the exact absent original Lua files.
// Do not claim coverage when source-dependent candidates were silently skipped.
if (mode === 'initial') {
  const needed = new Set();
  for (const skipped of manifest.skipped || []) {
    if (skipped && skipped.reason === 'Current deployed source file was not found.' &&
        typeof skipped.file === 'string') {
      if (sourceLocation(skipped.file)) needed.add(skipped.file);
      else fail('Resolver', 'missing-original-file-path-unavailable');
    }
  }
  for (const file of [...needed].sort()) missingOriginal(file);
}
if ((stats.generationFailures || 0) > 0) {
  for (const e of (manifest.skipOutcomes?.entries || []).filter(x => /FAIL|ERROR|PARTIAL|DRIFT/i.test(x.category || ''))) {
    fail(e.file || e.owner || 'Resolver', 'source-generation-failed-' + (e.ruleId || e.type || 'unknown'));
  }
  fail('Resolver', 'generation-failures-' + stats.generationFailures);
}
const emitted = allFiles(prefix);
if (!emitted.length) fail('Resolver', 'empty-mod-overlay');
const generated = emitted.filter(p => p.endsWith('.lua'));
for (const p of generated) {
  const rel = relative(p);
  try {
    luaparse.parse(text(p), {luaVersion: 'LuaJIT', comments: false, scope: false});
    parsed++;
  } catch (e) {
    fail(rel, 'invalid-generated-lua-syntax-line-' + (e.line || 'unknown'));
  }
  if (mode === 'repeated') {
    const original = path.join(inputMods, ...rel.split('/'));
    if (!fs.existsSync(original)) fail(rel, 'second-pass-introduced-new-source-file');
    else if (!fs.readFileSync(p).equals(fs.readFileSync(original))) {
      fail(rel, 'second-pass-changed-already-optimized-lua');
    }
  }
}
if (mode === 'initial') {
  const owners = [
    ['Card Collection', 'card_collection_native_worker.lua'],
    ['CyberStunts-StuntJumps-CET', 'cyberstunts_native_worker.lua'],
    ['Dedka Auto Shop', 'dedka_native_worker.lua'],
    ['StreetGamesPoker', 'poker_native_worker.lua'],
    ['illegal_mechanic_phase1a', 'illegal_mechanic_native_worker.lua'],
    ['tunnel_rescue', 'tunnel_native_worker.lua'],
    ['yaibaPenthouse', 'yaiba_native_worker.lua'],
    ['ziggy_last_play', 'ziggy_native_worker.lua'],
  ];
  for (const [owner, worker] of owners) {
    if (!hasOriginalInit(owner)) continue;
    const generatedWorker = path.join(prefix, owner, 'GCET_Semantic', worker);
    if (!fs.existsSync(generatedWorker)) fail(owner, 'missing-one-pass-native-worker-' + worker);
    const fileSources = allFiles(path.join(prefix, owner))
      .concat(allFiles(path.join(inputMods, owner)))
      .filter(f => f.endsWith('.lua') && !f.includes('GCET_Semantic' + path.sep));
    const reference = 'GCET_Semantic/' + worker.replace(/\.lua$/, '');
    if (!fileSources.some(f => text(f).includes(reference))) fail(owner, 'native-worker-source-bridge-missing-' + worker);
  }
  if (hasOriginalInit('illegal_mechanic_phase1a')) {
    const authorNpc = path.join(inputMods, 'illegal_mechanic_phase1a', 'physical_npc.lua');
    if (!fs.existsSync(authorNpc)) missingOriginal('illegal_mechanic_phase1a/physical_npc.lua');
    else {
      const mechanic = path.join(prefix, 'illegal_mechanic_phase1a', 'physical_npc.lua');
      requireContains('illegal_mechanic_phase1a/physical_npc.lua', mechanic,
        ['local function loadMatrix(', 'local function distanceFromPlayer(', 'local function deleteEntry(',
         'return true', 'GCET_Semantic/illegal_mechanic_native_worker']);
    }
  }
  if (hasOriginalInit('CyberStunts-StuntJumps-CET')) {
    const stunts = path.join(prefix, 'CyberStunts-StuntJumps-CET', 'init.lua');
    if (!fs.existsSync(stunts)) fail('CyberStunts-StuntJumps-CET/init.lua', 'native-stunt-source-missing');
    else {
    const source = text(stunts);
    const start = source.indexOf('registerForEvent("onUpdate"');
    const body = start >= 0 ? source.slice(start) : '';
    const nativeRead = /\b(?:Game\.Get(?:SystemRequestsHandler|TimeSystem)|__gcetGet(?:SystemRequestsHandler|TimeSystem))\s*\(/.exec(body);
    if (!nativeRead) fail('CyberStunts-StuntJumps-CET/init.lua', 'native-time-system-read-not-located');
    else {
      const prefixText = body.slice(0, nativeRead.index);
      if (!prefixText.includes('not settings.enableMod') ||
          !/if\s+not\s+player\b/.test(prefixText) ||
          !/if\s+not\s+vehicle\b/.test(prefixText)) {
        fail('CyberStunts-StuntJumps-CET/init.lua', 'expensive-native-reads-before-disabled-player-or-vehicle-guards');
      }
    }
  }
  }
}
console.log('Original-source Resolver ' + mode + ' validation: ' + generated.length +
  ' generated Lua files, ' + parsed + ' syntax-parsed, ' + (stats.transforms || 0) +
  ' composed transforms, ' + failures.length + ' errors.');
if (failures.length) process.exit(1);
