#!/usr/bin/env python3
"""Read-only, name-independent evidence scan of the CET mods folder.

This tool is a candidate-discovery stage for future Resolver integration.
It NEVER approves a transformation, emits location anchors, or modifies files.
The Resolver's source/side-effect and wake-equivalence proof is still required.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

# Deliberately mechanical patterns. Every match is evidence for review, not
# a claim that an expression executes or a coordinate is truly immutable.
RECURRING = re.compile(
    r'\b(?:Cron\s*\.\s*Every|Schedule\s*\.\s*Every|'
    r'registerForEvent\s*\(\s*["\']onUpdate["\']|onUpdate\s*=)\b?',
    re.IGNORECASE,
)
POSITION = re.compile(
    r'\b(?:GetWorldPosition|GetPlayerPosition|GetPlayer\s*\(|'
    r'GetPlayer\b|Vector4\s*\.\s*Distance|DistanceSquared|'
    r'distSq|distance)\b', re.IGNORECASE,
)
# Common literal site shapes. These are not presumed to be world-space.
XYZ_TABLE = re.compile(
    r'\b(?:x\s*=\s*[-+]?\d+(?:\.\d+)?\s*[,;]\s*'
    r'y\s*=\s*[-+]?\d+(?:\.\d+)?\s*[,;]\s*'
    r'z\s*=\s*[-+]?\d+(?:\.\d+)?|'
    r'["\']x["\']\s*:\s*[-+]?\d+(?:\.\d+)?\s*,\s*'
    r'["\']y["\']\s*:\s*[-+]?\d+(?:\.\d+)?\s*,\s*'
    r'["\']z["\']\s*:\s*[-+]?\d+(?:\.\d+)?)',
    re.IGNORECASE,
)
VECTOR_LITERAL = re.compile(
    r'\bVector4\s*\.\s*new\s*\(\s*[-+]?\d+(?:\.\d+)?\s*,\s*'
    r'[-+]?\d+(?:\.\d+)?\s*,\s*[-+]?\d+(?:\.\d+)?\s*[,)]',
    re.IGNORECASE,
)
DISTANCE = re.compile(
    r'\b(?:distance|distSq|distanceSquared|LengthSquared|'
    r'GetDistance|IsInRange)\b', re.IGNORECASE,
)
# These tokens warn that a callback may have non-location obligations.
MIXED = re.compile(
    r'\b(?:GetQuestsSystem|registerHotkey|registerInput|'
    r'Observe(?:After)?|GetMappinSystem|GetFactsDB|'
    r'Spawn|Despawn|Save|Shutdown|Quest)\b', re.IGNORECASE,
)
ROOT_TAIL = ('bin', 'x64', 'plugins', 'cyber_engine_tweaks', 'mods')


def validate_mods_root(raw: Path) -> Path:
    root = raw.expanduser().resolve(strict=True)
    if not root.is_dir():
        raise ValueError('CET mods path must be a directory')
    parts = tuple(p.casefold() for p in root.parts)
    if parts[-len(ROOT_TAIL):] != ROOT_TAIL:
        raise ValueError(
            'Choose Cyberpunk 2077/bin/x64/plugins/cyber_engine_tweaks/mods; '
            'do not use a development archive, parent folder, or old pass overlay'
        )
    return root


def inspect_mod(mod: Path, root: Path) -> dict:
    lua_files = list(sorted(mod.rglob('*.lua')))
    json_files = list(sorted(mod.rglob('*.json')))
    evidence = []
    occurrence = {'recurring': 0, 'player_or_position': 0,
                  'literal_xyz': 0, 'distance': 0, 'mixed_obligations': 0}
    for path in lua_files + json_files:
        if path.is_symlink() or path.stat().st_size > 2_000_000:
            continue
        data = path.read_bytes()
        txt = data.decode('utf-8-sig', errors='replace')
        counts = {
            'recurring': len(RECURRING.findall(txt)) if path.suffix == '.lua' else 0,
            'player_or_position': len(POSITION.findall(txt)) if path.suffix == '.lua' else 0,
            'literal_xyz': len(XYZ_TABLE.findall(txt)) + (
                len(VECTOR_LITERAL.findall(txt)) if path.suffix == '.lua' else 0),
            'distance': len(DISTANCE.findall(txt)) if path.suffix == '.lua' else 0,
            'mixed_obligations': len(MIXED.findall(txt)) if path.suffix == '.lua' else 0,
        }
        for k, v in counts.items():
            occurrence[k] += v
        if any(counts.values()):
            evidence.append({
                'path': path.relative_to(root).as_posix(),
                'sha256': hashlib.sha256(data).hexdigest(),
                'matches': counts,
            })
    # Cross-file matches are deliberately weak; only strong enough for triage.
    potential = bool(occurrence['recurring'] and occurrence['player_or_position']
                     and occurrence['literal_xyz'] and occurrence['distance'])
    return {
        'owner_folder': mod.name,
        'lua_files': len(lua_files),
        'candidate': potential,
        'classification': 'SOURCE_REVIEW_REQUIRED' if potential else 'NOT_IDENTIFIED',
        'wake_replacement_proven': False,
        'safe_to_generate': False,
        'evidence_counts': occurrence,
        'evidence_files': evidence,
    }


def discover(mods_root: Path) -> dict:
    root = validate_mods_root(mods_root)
    # The directory contents, not a baked-in list or `modsLIVEnow` archive,
    # define installation membership. No mod names are hardcoded.
    mods = sorted(
        (p for p in root.iterdir() if p.is_dir() and not p.is_symlink()),
        key=lambda p: p.name.casefold()
    )
    results = [inspect_mod(mod, root) for mod in mods]
    return {
        'schema': 'gcet-native-static-discovery-v1',
        'selected_mods_root': str(root),
        'installed_mod_folders': len(results),
        'candidates_for_source_review': sum(x['candidate'] for x in results),
        'approved_for_automatic_patch': 0,
        'requires_wake_equivalence_proof': True,
        'mods': results,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    opts = ap.add_mutually_exclusive_group(required=True)
    opts.add_argument('--mods', type=Path, help='Actual CET mods directory')
    opts.add_argument('--game', type=Path, help='Cyberpunk 2077 root directory')
    ap.add_argument('--output', type=Path, help='JSON report (no game files modified)')
    args = ap.parse_args()
    root = (args.game / Path(*ROOT_TAIL)) if args.game else args.mods
    try:
        result = discover(root)
        output = json.dumps(result, indent=2, ensure_ascii=False)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(output + '\n', encoding='utf-8')
        else:
            print(output)
        print(f"Reviewed {result['installed_mod_folders']} installed mod folders; "
              f"{result['candidates_for_source_review']} need source proof; "
              "0 automatically admitted.", file=sys.stderr)
        return 0
    except (OSError, ValueError) as exc:
        print(f'G-CET static discovery refused: {exc}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
