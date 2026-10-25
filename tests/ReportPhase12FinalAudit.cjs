// Optional FINAL review only. Phase 1+2 is behavior evidence, not a byte/version lock.
// This tool reports paths and feature metadata, never source content.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const [referenceRoot, finalRoot] = process.argv.slice(2);
const prefix = 'bin/x64/plugins/cyber_engine_tweaks/mods/';
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
function owner(relative) {
  return relative.startsWith(prefix)
    ? relative.slice(prefix.length).split('/')[0] : '[native/runtime]';
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
const ref = inventory(referenceFiles), fin = inventory(finalFiles);
const common = [...referenceFiles.keys()].filter(p => finalFiles.has(p));
const identical = common.filter(p => referenceFiles.get(p).hash === finalFiles.get(p).hash);
const refOnly = [...referenceFiles.keys()].filter(p => !finalFiles.has(p));
const finalOnly = [...finalFiles.keys()].filter(p => !referenceFiles.has(p));
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
  '| Reference files | Final files | Identical shared | Changed shared | Reference-only | Final-only |',
  '|---:|---:|---:|---:|---:|---:|',
  '| '+[referenceFiles.size,finalFiles.size,identical.length,common.length-identical.length,
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
  'Next: review suspicious optimization behavior against untouched author sources, worker ownership, 0-Engine/native dependencies and Phase 1+2 intent. Legacy AudioXL gameplay source differences are not optimization losses. Gameplay and performance require separate verification.',
  ''
].join('\n');
console.log('Final audit: reference='+referenceFiles.size+', final='+finalFiles.size+
  ', changed='+ (common.length-identical.length)+', reference-only='+refOnly.length+
  ', final-only='+finalOnly.length+', missing-markers='+missingMarkers.length+
  ', missing-worker-targets='+missingImports.length);
if(process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY,summary+'\n');
else console.log(summary);
// Differences are review leads; do not claim comprehensive semantic equivalence.
