#!/usr/bin/env python3
"""Compile source-proven, installed-only anchor declarations into little-endian GCAS.

No YAML or JSON parsing takes place inside the game. THIS PROGRAM DOES NOT INFER
source safety: a Resolver source recognizer must supply proved anchor declarations.
"""
import argparse
import hashlib
import json
import math
import pathlib
import struct
import sys

HEADER = struct.Struct('<4sIII')
RECORD = struct.Struct('<iffffII')


def compile_data(payload):
    if not isinstance(payload, dict) or payload.get('schema') != 1:
        raise ValueError('expected manifest schema=1')
    entries = payload.get('anchors')
    if not isinstance(entries, list) or len(entries) > 100000:
        raise ValueError('invalid anchors list')
    output = bytearray(HEADER.pack(b'GCAS', 1, len(entries), 0))
    seen = set()
    for item in entries:
        if not isinstance(item, dict):
            raise ValueError('anchor is not an object')
        id_ = item.get('id')
        owner = item.get('owner')
        radius = item.get('radius')
        metric = item.get('metric', 'xyz')
        if type(id_) is not int or not 0 < id_ <= 2147483647 or id_ in seen:
            raise ValueError('id must be unique positive int32')
        if type(owner) is not int or not 0 < owner <= 2147483647:
            raise ValueError('owner must be positive int32-compatible ID')
        if metric not in ('xy', 'xyz'):
            raise ValueError('metric must be xy/xyz')
        vals = [item.get(k) for k in ('x', 'y', 'z', 'radius')]
        if any(type(v) not in (float, int) or not math.isfinite(v) for v in vals):
            raise ValueError('coordinates/radius must be finite numbers')
        x, y, z, r = (float(v) for v in vals)
        if max(abs(x), abs(y), abs(z)) > 1_000_000 or not 0 < r <= 1024:
            raise ValueError('coordinate or radius outside native bounds')
        seen.add(id_)
        output += RECORD.pack(id_, x, y, z, r, int(metric == 'xy'), owner)
    return bytes(output)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('manifest', type=pathlib.Path)
    ap.add_argument('output', type=pathlib.Path)
    args = ap.parse_args()
    data = compile_data(json.loads(args.manifest.read_text(encoding='utf-8')))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(data)
    digest = hashlib.sha256(data).hexdigest()
    args.output.with_name(args.output.name + '.sha256').write_text(digest + '  ' + args.output.name + '\n', encoding='ascii')
    print(f'Compiled {len(data)} bytes; SHA256={digest}')


if __name__ == '__main__':
    main()
