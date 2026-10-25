// Lightweight production-overlay integrity check. No fixed mod lists, worker
// counts, previous G-CET source, historic reference or gameplay assumptions.
const fs = require('node:fs');
const path = require('node:path');

const root = process.argv[2];
if (!root) {
  console.error('Usage: node ValidateGeneratedLuaInvariants.cjs EXTRACTED_OVERLAY_ROOT');
  process.exit(2);
}
const mods = path.join(root, 'bin', 'x64', 'plugins', 'cyber_engine_tweaks', 'mods');
if (!fs.existsSync(mods) || !fs.statSync(mods).isDirectory()) {
  console.error('Generated CET mods root is missing from overlay');
  process.exit(1);
}

const errors = [];
const ownerCalls = new Map();
let checkedLua = 0;
let checkedImports = 0;
function err(relative, issue) {
  errors.push(relative + ': ' + issue);
  if (errors.length <= 30) {
    const message = (relative + ' — ' + issue).replace(/[\r\n%:]/g, ' ');
    console.log('::error title=Generated Lua structural invariant::' + message);
  }
}
function visit(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) visit(file);
    else if (entry.isFile() && entry.name.endsWith('.lua')) check(file);
  }
}
function check(file) {
  checkedLua++;
  const rel = path.relative(mods, file).replace(/\\/g, '/');
  const parts = rel.split('/');
  const owner = parts[0];
  const code = fs.readFileSync(file, 'utf8');
  const lines = code.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (/^\s*--/.test(line)) continue;
    // Only statically known, mod-local G-CET-owned require/dofile imports.
    const imports = /\b(?:require|dofile)\s*\(?\s*(['"])(GCET_Semantic\/[^'"\r\n]+)\1/g;
    for (const match of line.matchAll(imports)) {
      checkedImports++;
      const module = match[2];
      const filename = module.endsWith('.lua') ? module : module + '.lua';
      const segments = filename.split('/');
      if (segments.some(x => x === '..' || x === '.' || !x)) {
        err(rel, 'unsafe static G-CET worker module path at line ' + (i + 1));
        continue;
      }
      const worker = path.join(mods, owner, ...segments);
      if (!fs.existsSync(worker) || !fs.statSync(worker).isFile()) {
        err(rel, 'statically referenced G-CET worker missing from overlay: ' + filename);
      }
    }
    // A negative nil guard followed by dereferencing the same expression is
    // provably unsafe if the left side is false/nil. Handles getter chains.
    const unsafe = /\bnot\s+([A-Za-z_]\w*(?:\([^()\r\n]*\))?(?:[.:][A-Za-z_]\w*\([^()\r\n]*\))*)\s+and\s+not\s+\1\s*:/g;
    if (/\b(?:if|elseif|while)\b/.test(line) && unsafe.test(line))
      err(rel, 'nil-guard dereferences the same absent value at line ' + (i + 1));
    // Reject duplicate compiled native owner attachment in a single mod,
    // regardless of whether it occurs inline or in external worker Lua.
    const attaches = /\b[A-Za-z_]\w*\s*:\s*attachCompiled\s*\(\s*(\d+)\s*,/g;
    for (const match of line.matchAll(attaches)) {
      const key = owner + '/' + match[1];
      if (ownerCalls.has(key)) err(rel, 'duplicate compiled native owner ' + match[1] + ' (first at ' + ownerCalls.get(key) + ')');
      else ownerCalls.set(key, rel + ':' + (i + 1));
    }
  }
}
visit(mods);
console.log('Generated Lua invariants: ' + checkedLua + ' Lua files, ' +
  checkedImports + ' statically referenced coordinators, ' +
  ownerCalls.size + ' compiled attachments, ' + errors.length + ' errors.');
if (errors.length) process.exit(1);
