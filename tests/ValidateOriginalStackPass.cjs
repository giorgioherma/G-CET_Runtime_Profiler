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

// Reporting only: enrich the same existing error/warning decisions with
// rule/stage/path/reason, and put a short fix-direction table in Actions Summary.
const diagnoses = [];
const clean = value => String(value ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, 500);
const actionEscape = value => clean(value).replace(/%/g, '%25').replace(/:/g, '%3A');
const md = value => clean(value).replace(/\|/g, '\\|').replace(/</g, '&lt;').replace(/>/g, '&gt;');
function report(level, owner, issue, info = {}) {
  const data = {
    level,
    stage: clean(info.stage || 'Resolver generation'),
    mod: clean(info.mod || ''),
    rule: clean(info.rule || ''),
    handler: clean(info.handler || ''),
    file: clean(owner),
    issue: clean(issue),
    reason: clean(info.reason || ''),
    action: clean(info.action || '')
  };
  diagnoses.push(data);
  const ref = [data.mod && 'mod=' + data.mod, data.rule && 'rule=' + data.rule,
    data.handler && 'handler=' + data.handler, 'file=' + data.file,
    data.reason && 'reason=' + data.reason, data.action && 'next=' + data.action]
    .filter(Boolean).join(' | ');
  console.log('::' + level + ' title=' + actionEscape(data.stage + ' / ' + data.issue) +
    '::' + actionEscape(ref));
  (level === 'error' ? failures : warnings).push(data.file + ': ' + data.issue);
}
function diagnosticSource(t) {
  return {
    stage: t.type === 'SEMANTIC_RULE' ? 'Semantic' : clean(t.type || 'Generator'),
    mod: t.owner || t.Owner,
    rule: t.ruleId || t.RuleId,
    handler: t.handler || t.Handler,
    action: 'Inspect the existing authoritative generator for this rule.'
  };
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
function requiredOriginal(relative, info = {}) {
  const p = sourcePath(relative);
  if (p) report('error', p, 'MISSING_REQUIRED_ORIGINAL', {
    stage: 'Original-source input', mod: info.owner || info.Owner,
    rule: info.ruleId || info.RuleId,
    reason: 'Resolver selected a transformation but its untouched source file is absent.',
    action: 'Add this exact file to the private original CET repository.'
  });
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
      if (!overlayExists(output.path)) {
        const matching = transforms.find(t => (Array.isArray(t.files) ? t.files : [t.file])
          .some(p => typeof p === 'string' && outputLocation(p) === outputLocation(output.path)));
        report('error', outputLocation(output.path) || 'Resolver', 'manifest output not generated', {
          ...diagnosticSource(matching || {}),
          reason: 'Manifest declares an output absent from the generated ZIP.',
          action: 'Inspect the generating stage and staged ZIP output.'
        });
      }
    }

    // A source rewrite or coordinator claimed by a successful rule must be
    // present. New generated files are valid WITHOUT original counterparts.
    for (const t of transforms) {
      const paths = Array.isArray(t.files) ? t.files : (t.file ? [t.file] : []);
      for (const relative of paths) {
        if (typeof relative !== 'string') continue;
        if (!overlayExists(relative))
          report('error', outputLocation(relative) || 'Resolver', 'claimed transformation file missing', {
            ...diagnosticSource(t),
            reason: 'Applied transform lists a file that is absent from the generated ZIP.',
            action: 'Inspect the emitting rule and staged output.'
          });
      }
    }

    // Validate an admitted paired wake stage using the untouched author's
    // exact CET toggle and published API signature as the applicability gate.
    // A filename count alone missed a real startup-wake regression: Dedra
    // shipped but base ITP's read-only startup proof was omitted.
    const itpInitOriginal = path.join(modsRoot, 'immersive_third_person', 'init.lua');
    const itpApiOriginal = path.join(modsRoot, 'immersive_third_person', 'api.lua');
    if (fs.existsSync(itpInitOriginal)) {
      const original = fs.readFileSync(itpInitOriginal, 'utf8');
      if (original.includes('registerInput("tpp_camera_toggle"') &&
          original.includes('local function autoTppHandleToggle()')) {
        if (!fs.existsSync(itpApiOriginal)) {
          requiredOriginal('immersive_third_person/api.lua', {
            owner: 'immersive_third_person',
            ruleId: 'immersive-third-person-dedra-toggle-signal'
          });
        } else if (fs.readFileSync(itpApiOriginal, 'utf8').includes('core.public = readOnly(touching({')) {
          const paired = transforms.filter(t => t.RuleId === 'immersive-third-person-dedra-toggle-signal' &&
            t.type === 'SEMANTIC_RULE');
          const required = ['immersive_third_person/init.lua', 'immersive_third_person/api.lua'];
          const paths = paired.length === 1 && Array.isArray(paired[0].files) ? paired[0].files : [];
          if (paired.length !== 1 || required.some(p => !paths.includes(p)))
            report('error', 'immersive_third_person', 'ITP/Dedra paired wake not generated from original',
              {stage:'Semantic',mod:'immersive_third_person',
               rule:'immersive-third-person-dedra-toggle-signal',
               reason:'Both init.lua and api.lua must be composed in one pass.'});
          else {
            const init = fs.readFileSync(path.join(overlayRoot, ...outputLocation(required[0]).split('/')), 'utf8');
            const api = fs.readFileSync(path.join(overlayRoot, ...outputLocation(required[1]).split('/')), 'utf8');
            if (!init.includes('mod.__gcetDedraWakeBridgeV2 = true') ||
                !init.includes('gcetDedraWakeReady = function() return mod.__gcetDedraWakeBridgeV2 == true end') ||
                !init.includes('pcall(addon.__gcetWakeFromBase, "__gcetItpToggleWakeV2")') ||
                init.includes('mod.api.__gcetDedraWakeBridgeV2') ||
                !api.includes('__gcetDedraWakeCapabilityV3') ||
                !api.includes('return type(host.gcetDedraWakeReady) == "function" and host.gcetDedraWakeReady() == true'))
              report('error', 'immersive_third_person', 'ITP/Dedra runtime wake dependency incomplete',
                {stage:'Semantic',mod:'immersive_third_person',
                 rule:'immersive-third-person-dedra-toggle-signal',
                 reason:'Original CET keybind and read-only public API do not contain matching safe wake proofs.'});
          }
        }
      }
    }

    // The fixed base capture can refer to original files removed when trimming
    // the source repository. Report exactly the missing file instead of a folder.
    const missing = new Map();
    for (const skipped of manifest.skipped || []) {
      if (skipped?.reason === 'Current deployed source file was not found.' &&
          sourcePath(skipped.file) && !missing.has(skipped.file)) {
        missing.set(skipped.file, skipped);
      }
    }
    for (const relative of [...missing.keys()].sort())
      requiredOriginal(relative, missing.get(relative));

    // A production-admitted semantic stage that fails generation is a hard
    // output failure. File existence and green build jobs do not establish
    // source-equivalent behavior when a required wake dependency was skipped.
    const skippedSemantic = (manifest.skipOutcomes?.entries || []).filter(x =>
      x.category === 'GENERATION_FAILURE');
    for (const item of skippedSemantic.slice(0, 20)) {
      const raw = (manifest.skipped || []).find(s =>
        (s.RuleId || s.ruleId || '') === (item.ruleId || '') &&
        (s.Owner || s.owner || '') === (item.owner || ''));
      report('error', item.file || item.owner || item.ruleId || 'Resolver',
        'required semantic generation failed', {
          stage: item.type === 'SEMANTIC_RULE' ? 'Semantic' : (item.type || 'Resolver'),
          mod: item.owner, rule: item.ruleId, handler: raw?.Handler || raw?.handler,
          reason: item.reason,
          action: 'Inspect source proof and the existing generator; avoid corrective semantic passes.'
        });
    }
    console.log('Untouched CET generation: ' + transforms.length + ' transforms, ' +
      outputs.length + ' output files, ' + failures.length + ' errors, ' +
      warnings.length + ' warnings. Runtime performance is not evaluated here.');
  }
}
// The summary is advisory metadata. Failure thresholds above stay unchanged.
// Do not publish original files, private capture details, or complete Lua source.
if (process.env.GITHUB_STEP_SUMMARY) {
  const rows = diagnoses.slice(0, 40).map(d => '| ' +
    [d.level.toUpperCase(), d.stage, d.mod || '—', d.rule || '—',
     d.handler || '—', d.file, d.reason || d.issue, d.action || 'Review CI annotation']
      .map(md).join(' | ') + ' |');
  const header = [
    '### Original CET generation — fix directions',
    '',
    'Errors: **' + failures.length + '** | Warnings: **' + warnings.length + '**',
    '',
    '| Status | Stage | Mod | Rule | Generator handler | File | Reason | Next action |',
    '|---|---|---|---|---|---|---|---|',
    ...(rows.length ? rows : ['| PASS | — | — | — | — | — | No reported issue | — |']),
    diagnoses.length > 40 ? '\nOnly the first 40 diagnostics are shown here; see annotations for additional entries.' : '',
    '',
    'The check and its pass/fail criteria are unchanged. Runtime profiling is separate.',
    ''
  ].join('\n');
  try { fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, header); }
  catch (err) { console.log('CI summary unavailable: ' + clean(err.message)); }
}
if (failures.length) process.exit(1);
