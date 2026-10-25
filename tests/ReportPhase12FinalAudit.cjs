// Optional FINAL review only. Phase 1+2 is behavior evidence, not a byte/version lock.
// This tool reports paths and feature metadata, never source content.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const [referenceRoot, finalRoot] = process.argv.slice(2);
const prefix = 'bin/x64/plugins/cyber_engine_tweaks/mods/';

// Exactly these six historical reference init.lua files contain a wrong-base
// AudioXL bridge. Do not exclude the entire owner: other Lua coordinators,
// dependency files and non-audio optimized code remain comparable.
const AUDIOXL_MANUAL_FILES = new Set([
  'DedraJNCJoytoy/init.lua', 'Dedrapanamgoondate/init.lua',
  'f80h_aroused_expressions/init.lua', 'FenixRebirth/init.lua',
  'ImmersiveVDialogueExpanded/init.lua', 'rarobottruck_1/init.lua'
].map(p => prefix + p));
// CourierJobs 5.0 relocated former Lua gameplay/update code into REDscript.
// Its historical init.lua comparison is version-specific, not an AudioXL bug.
const COURIER_MANUAL_FILE = prefix + 'CourierJobs/init.lua';
const MANUAL_INIT_FILES = new Set([...AUDIOXL_MANUAL_FILES, COURIER_MANUAL_FILE]);
function owner(relative) {
  return relative.startsWith(prefix)
    ? relative.slice(prefix.length).split('/')[0] : '[native/runtime]';
}
function isAudioFile(relative) {
  const p = relative.replace(/\\/g, '/').toLowerCase();
  const leaf = p.split('/').pop();
  // Path- and component-based classification, not source-content guesses.
  // Sound assets do not enter collect() at all (it reads Lua/native only).
  return /(^|\/)(?:audioxl|audioware|audio|sounds?|soundtracks?|music|voices?|voiceover|sfx)(\/|$)/.test(p) ||
    /(^|[._-])(?:audioxl|audioware|audio|sound|sounds|soundtrack|music|voice|voices|voiceover|sfx)(?=[._-]|$)/.test(leaf);
}
function manualReason(relative) {
  if (isAudioFile(relative)) return 'audio-related';
  if (AUDIOXL_MANUAL_FILES.has(relative)) return 'Wrong-base AudioXL init.lua reference';
  if (relative === COURIER_MANUAL_FILE) return 'CourierJobs source-version and REDscript migration';
  return null;
}

if (!referenceRoot || !finalRoot) {
  console.error('Usage: node ReportPhase12FinalAudit.cjs PHASE12_DIR FINAL_DIR');
  process.exit(2);
}
function collect(root) {
  const files = new Map();
  function visit(dir) {
    if (!fs.existsSync(dir)) return;
    for (const item of fs.readdirSync(dir, {withFileTypes: true})) {
      if (item.name === '.git') continue;
      const absolute = path.join(dir, item.name);
      if (item.isDirectory()) visit(absolute);
      else if (item.isFile()) {
        const relative = path.relative(root, absolute).replace(/\\/g, '/');
        if (!/\.(lua|dll|locations)$/i.test(relative)) continue;
        const bytes = fs.readFileSync(absolute);
        files.set(relative, {
          hash: crypto.createHash('sha256').update(bytes).digest('hex'),
          source: /\.lua$/i.test(relative) ? bytes.toString('utf8') : null
        });
      }
    }
  }
  visit(root);
  return files;
}
function inventory(files) {
  const mods = new Map();
  for (const [relative, file] of files) {
    const mod = owner(relative);
    if (!mods.has(mod)) mods.set(mod, {markers:new Set(), imports:new Set(),
      signals:{native:0,scheduler:0,action:0,provider:0}});
    const entry = mods.get(mod);
    if (file.source === null) continue;
    const src = file.source;
    for (const match of src.matchAll(/--\s*G-CET\s+(?:semantic(?:\s+coordinator)?|native\s+location)\s*:\s*([^\r\n]+)/gi)) {
      entry.markers.add(match[1].trim().replace(/-v\d+(?:\.\d+)?$/i, '').toLowerCase());
    }
    entry.signals.native += (src.match(/\battachCompiled\s*\(/g) || []).length;
    entry.signals.scheduler += (src.match(/\b(?:RegisterJob|registerJob|Scheduler\.(?:Register|AddJob)|scheduler:Register)\s*\(/g) || []).length;
    entry.signals.action += (src.match(/\bActionRouter\b/g) || []).length;
    entry.signals.provider += (src.match(/\b__gcetGet[A-Za-z][A-Za-z0-9_]*\s*\(/g) || []).length;
    for (const match of src.matchAll(/\b(?:require|dofile)\s*\(\s*["']([^"']*GCET_Semantic\/[^"']+)["']\s*\)/g)) {
      const target = match[1].replace(/\\/g, '/');
      const i = target.indexOf('GCET_Semantic/');
      const modulePath = target.slice(i).replace(/\.lua$/i, '') + '.lua';
      if (!modulePath.split('/').includes('..')) {
        entry.imports.add(prefix + mod + '/' + modulePath);
      }
    }
  }
  return mods;
}
const referenceFiles = collect(referenceRoot), finalFiles = collect(finalRoot);
if (!referenceFiles.size || !finalFiles.size ||
    ![...referenceFiles.keys()].some(p => p.startsWith(prefix)) ||
    ![...finalFiles.keys()].some(p => p.startsWith(prefix))) {
  console.error('Both inputs need a complete game-relative CET mods tree.');
  process.exit(2);
}
// Scope only this historical optional comparison. Routine CI and the real
// original-source generation test never use this exception list.
const excluded = [...new Set([...referenceFiles.keys(), ...finalFiles.keys()])]
  .filter(p => manualReason(p));
const comparableReference = new Map([...referenceFiles].filter(([p]) => !manualReason(p)));
const comparableFinal = new Map([...finalFiles].filter(([p]) => !manualReason(p)));
const ref = inventory(comparableReference), fin = inventory(comparableFinal);
const common = [...comparableReference.keys()].filter(p => comparableFinal.has(p));
const identical = common.filter(p => comparableReference.get(p).hash === comparableFinal.get(p).hash);
const refOnly = [...comparableReference.keys()].filter(p => !comparableFinal.has(p));
const finalOnly = [...comparableFinal.keys()].filter(p => !comparableReference.has(p));
const affected = [...MANUAL_INIT_FILES].sort().map(file => ({
  file,
  reason: file === COURIER_MANUAL_FILE ? 'CourierJobs author v5 / REDscript relocation' : 'Wrong-base AudioXL bridge',
  reference: referenceFiles.has(file),
  final: finalFiles.has(file)
}));
const rows=[], missingMarkers=[], missingImports=[];
for(const mod of new Set([...ref.keys(),...fin.keys()])){
  const before=ref.get(mod), after=fin.get(mod);
  const missing=[...(before?.markers || [])].filter(m => !after?.markers.has(m));
  const imports=[...(after?.imports || [])].filter(p => !finalFiles.has(p));
  for(const marker of missing) missingMarkers.push({mod,marker});
  for(const file of imports) missingImports.push({mod,file});
  const lost=refOnly.filter(p => owner(p)===mod).length;
  const added=finalOnly.filter(p => owner(p)===mod).length;
  const drops=before&&after?Object.keys(before.signals)
    .filter(k => before.signals[k]>after.signals[k])
    .map(k => k+' '+before.signals[k]+' to '+after.signals[k]):[];
  if(missing.length||imports.length||lost||added||drops.length||!before||!after)
    rows.push({mod,lost,added,markers:missing.length,imports:imports.length,
      drops:drops.join(', ')||'none'});
}
rows.sort((a,b)=>(b.markers+b.imports)*100-(a.markers+a.imports)*100||
  b.lost-a.lost||a.mod.localeCompare(b.mod));
const md=x=>String(x).replace(/\|/g,'\\|').replace(/</g,'&lt;').replace(/>/g,'&gt;');
const summary=[
  '## Optional Phase 1+2 final-stack review',
  '',
  'Advisory manual audit only. Different files or implementations are NOT automatically regressions.',
  '',
  '**Manual exceptions:** Exclude exactly six wrong-base AudioXL init.lua files, CourierJobs/init.lua (author-version/REDscript migration), and explicitly audio-related paths from automatic byte/marker parity. Other non-audio Lua, GCET_Semantic coordinators and dependencies under these seven mod owners remain compared normally. All G-CET optimization behavior in excluded init.lua files still requires manual source-aware review.',
  '',
  '### Scope after narrow manual exclusions',
  '',
  '| Reference files (all) | Final files (all) | Comparable reference | Comparable final | Excluded unique paths |',
  '|---:|---:|---:|---:|---:|',
  '| '+[referenceFiles.size,finalFiles.size,comparableReference.size,comparableFinal.size,excluded.length].join(' | ')+' |',
  '',
  '| Comparable reference | Comparable final | Identical shared | Changed shared | Reference-only | Final-only |',
  '|---:|---:|---:|---:|---:|---:|',
  '| '+[comparableReference.size,comparableFinal.size,identical.length,common.length-identical.length,
    refOnly.length,finalOnly.length].join(' | ')+' |',
  '',
  '### Inventory, optimization evidence and worker dependency leads',
  'Missing reference marker leads: '+missingMarkers.length+
    '; unresolved generated-worker import targets: '+missingImports.length+'.',
  '',
  '| Mod | Reference-only files | Final-only files | Markers absent | Worker imports unresolved | Signal decreases |',
  '|---|---:|---:|---:|---:|---|',
  ...rows.slice(0,90).map(r=>'| '+[r.mod,r.lost,r.added,r.markers,r.imports,r.drops].map(md).join(' | ')+' |'),
  ...(rows.length>90?['Additional mod rows omitted.']:[]),
  '',
  '### Reference optimization markers to investigate',
  ...(missingMarkers.length?missingMarkers.slice(0,60).map(v=>'- '+md(v.mod)+': '+md(v.marker)):['None found missing by this heuristic.']),
  ...(missingMarkers.length>60?['Additional marker leads omitted.']:[]),
  '',
  '### Missing final G-CET worker imports',
  ...(missingImports.length?missingImports.slice(0,60).map(v=>'- '+md(v.file)):['None detected in supplied final tree.']),
  ...(missingImports.length>60?['Additional imports omitted.']:[]),
  '',
  '### Reference-only files (not proven lost features)',
  ...(refOnly.length?refOnly.slice(0,70).map(p=>'- '+md(p)):['None.']),
  ...(refOnly.length>70?['Additional file paths omitted.']:[]),
  '',
  '### Manual-review queue — seven exact init.lua exceptions',
  '',
  '| Exact reference exception | Reason | Present in reference | Present in final | Required manual review |',
  '|---|---|---|---|---|',
  ...affected.map(v => '| '+[v.file,v.reason,v.reference ? 'yes' : 'no',v.final ? 'yes' : 'no',
    v.file === COURIER_MANUAL_FILE
      ? 'Compare source-proven v5 Lua hooks and REDscript ownership; do not recreate deprecated v3 callbacks'
      : 'Compare G-CET optimizations against untouched author source plus corrected AudioXL implementation'].map(md).join(' | ')+' |'),
  '',
  'Other audio-related paths excluded: '+excluded.filter(p => !MANUAL_INIT_FILES.has(p)).length+' (unique paths).',
  '',
  'No PASS/FAIL parity result is inferred for the six wrong-base AudioXL init.lua files, CourierJobs/init.lua, or excluded audio paths. Other non-audio files in those mod directories remain compared normally. Manual source-aware optimization review is required.',
  '',
  'Next: investigate comparable optimization leads against untouched author source and Phase 1+2 intent. Manually review the six AudioXL init.lua files and CourierJobs/init.lua against their corrected/current author sources. Gameplay and performance require separate verification.',
  ''
].join('\n');
console.log('Final audit (non-audio): comparable-reference='+comparableReference.size+
  ', comparable-final='+comparableFinal.size+', excluded-paths='+excluded.length+
  ', changed='+ (common.length-identical.length)+', reference-only='+refOnly.length+
  ', final-only='+finalOnly.length+', missing-markers='+missingMarkers.length+
  ', missing-worker-targets='+missingImports.length);
if(process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY,summary+'\n');
else console.log(summary);
// Differences are review leads; do not claim comprehensive semantic equivalence.
