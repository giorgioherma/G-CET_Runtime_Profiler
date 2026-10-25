// Minimal CI smoke check for production Resolver generation from untouched stack input.
// Do not hardcode individual mod/worker names, original file counts or performance rules.
const fs = require('node:fs');
const path = require('node:path');

const [modsRoot, overlayRoot, manifestFile] = process.argv.slice(2);
if (!modsRoot || !overlayRoot || !manifestFile) {
  console.error('Usage: node ValidateOriginalStackPass.cjs MODS_ROOT OVERLAY_ROOT MANIFEST');
  process.exit(2);
}
const prefix = 'bin/x64/plugins/cyber_engine_tweaks/mods/';
const failures = [];
const warnings = [];

function report(level, owner, issue) {
  const message = String(issue).replace(/[\r\n:]/g, ' ');
  const file = String(owner).replace(/[\r\n]/g, '');
  console.log('::' + level + ' title=' + message + '::' + file);
  (level === 'error' ? failures : warnings).push(file + ': ' + message);
}
function sourcePath(relative) {
  if (typeof relative !== 'string') return null;
  const v = relative.replace(/\\/g, '/').replace(/^\/+/, '');
  if (!v || v === '.' || v.split('/').some(p => p === '.' || p === '..')) return null;
  return v.startsWith(prefix) ? v : prefix + v;
}
function outputLocation(relative) {
  if (typeof relative !== 'string') return null;
  const p = relative.replace(/\\/g, '/').replace(/^\/+/, '');
  if (!p || p.split('/').includes('..')) return null;
  return p.startsWith('bin/') || p.startsWith('red4ext/') ? p : prefix + p;
}
function overlayExists(relative) {
  const p = outputLocation(relative);
  return p !== null && fs.existsSync(path.join(overlayRoot, ...p.split('/')));
}
function requiredOriginal(relative) {
  const p = sourcePath(relative);
  if (p) report('error', p, 'MISSING_REQUIRED_ORIGINAL - supply this exact file');
  else report('error', 'Resolver', 'missing original source path unavailable');
}

if (!fs.existsSync(manifestFile)) {
  report('error', 'Resolver', 'generation manifest missing');
} else {
  let manifest;
  try { manifest = JSON.parse(fs.readFileSync(manifestFile, 'utf8')); }
  catch { report('error', 'Resolver', 'generation manifest invalid'); }

  if (manifest) {
    const transforms = Array.isArray(manifest.transforms) ? manifest.transforms : [];
    const outputs = Array.isArray(manifest.files) ? manifest.files : [];
    if (!transforms.length) report('error', 'Resolver', 'no transformations generated from untouched stack');

    // All generated files are matched against the *actual* ZIP paths.
    // File manifest paths for mod Lua are relative to the CET mods folder;
    // native game-root artifacts are already game-root relative.
    for (const output of outputs) {
      if (typeof output.path !== 'string') continue;
      if (!overlayExists(output.path))
        report('error', outputLocation(output.path) || 'Resolver', 'manifest output not generated');
    }

    // A source rewrite or coordinator claimed by a successful rule must be
    // present. New generated files are valid WITHOUT original counterparts.
    for (const t of transforms) {
      const paths = Array.isArray(t.files) ? t.files : (t.file ? [t.file] : []);
      for (const relative of paths) {
        if (typeof relative !== 'string') continue;
        if (!overlayExists(relative))
          report('error', outputLocation(relative) || 'Resolver', 'claimed transformation file missing');
      }
    }

    // The fixed base capture can refer to original files removed when trimming
    // the source repository. Report exactly the missing file instead of a folder.
    const missing = new Set();
    for (const skipped of manifest.skipped || []) {
      if (skipped?.reason === 'Current deployed source file was not found.' &&
          sourcePath(skipped.file)) missing.add(skipped.file);
    }
    for (const relative of [...missing].sort()) requiredOriginal(relative);

    // Report semantic source-refusals, but let the generator's existing
    // source-proof/outcome policy determine whether a rule is applicable.
    const skippedSemantic = (manifest.skipOutcomes?.entries || []).filter(x =>
      x.category === 'GENERATION_FAILURE');
    for (const item of skippedSemantic.slice(0, 20)) {
      report('warning', item.file || item.owner || item.ruleId || 'Resolver',
        'source/rule refusal ' + (item.ruleId || item.type || 'unknown'));
    }
    console.log('Untouched CET generation: ' + transforms.length + ' transforms, ' +
      outputs.length + ' output files, ' + failures.length + ' errors, ' +
      warnings.length + ' warnings. Runtime performance is not evaluated here.');
  }
}
if (failures.length) process.exit(1);
