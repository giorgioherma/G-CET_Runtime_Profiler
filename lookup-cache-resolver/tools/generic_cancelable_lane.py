#!/usr/bin/env python3
"""Conservative generic CET source->native job adoption (one pure XYZ FAR lane).

This is a deliberately narrow *structural* auto-admission family. It does not
infer that arbitrary helper calls are pure, split mixed callbacks, or admit a
source merely because a review manifest sets boolean proof flags. Rejected
sources remain entirely unchanged. No game installation is modified.
"""
from __future__ import annotations

import hashlib
import io
import math
from pathlib import Path, PurePosixPath
import re
import sys
import zipfile

from lua_callback_scopes import masked, scope_end
from stage_source_pass import SCHEMA, REQUIRED_PROOFS, Refused, stage

IDENT = r"[A-Za-z_][A-Za-z0-9_]*"
NUMBER = r"-?(?:\d+(?:\.\d+)?|\.\d+)"
# Only this exact Lua arithmetic has proved XYZ Euclidean semantics. In
# particular, calls into an unreviewed helper are NOT treated as equivalent.
PURE = re.compile(r"""
\A\s*local\s+(?P<pos>ID)\s*=\s*Game\.GetPlayer\(\):GetWorldPosition\(\)\s*
local\s+(?P<dx>ID)\s*=\s*(?P=pos)\.x\s*-\s*(?P<x>N)\s*
local\s+(?P<dy>ID)\s*=\s*(?P=pos)\.y\s*-\s*(?P<y>N)\s*
local\s+(?P<dz>ID)\s*=\s*(?P=pos)\.z\s*-\s*(?P<z>N)\s*
if\s+(?P=dx)\s*\*\s*(?P=dx)\s*\+\s*(?P=dy)\s*\*\s*(?P=dy)
\s*\+\s*(?P=dz)\s*\*\s*(?P=dz)\s*<=\s*(?P<r2>\d+(?:\.\d+)?)\s*then\s*
(?P<near>ID)\.Resume\(\)\s*else\s*(?P=near)\.Pause\(\)\s*end\s*\Z
""".replace("ID", IDENT).replace("N", NUMBER), re.X | re.S)
ASSIGN = re.compile(r"(?<![\w.])(?P<job>"+IDENT+
                    r")\s*=\s*client\.Schedule\.Every\s*\(", re.S)
HEADER = re.compile(r"\s*(?P<period>\d+(?:\.\d+)?)\s*,\s*"
                    r"\{(?P<options>[^{}]*)\}\s*,\s*function\s*\(\s*\)", re.S)
OPTION_ACTIVE_FALSE = re.compile(r"\bactive\s*=\s*false\b")
OPTION_ACTIVE_TRUE = re.compile(r"\bactive\s*=\s*true\b")
REMOTE = re.compile(r'\b(?:registerForEvent|Observe|registerHotkey)\s*\(')

def source_hash(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()

def _jobs(text: str):
    """Recognize exact assigned scheduler expressions, never nearby functions."""
    safe = masked(text)
    rows = []
    for match in ASSIGN.finditer(safe):
        # Scheduler strings are masked, but assignment/call delimiters survive.
        start = match.end()
        header = HEADER.match(safe, start)
        if not header:
            continue
        fn = safe.find("function", start, header.end())
        end_fn = scope_end(safe, fn) if fn >= 0 else None
        if end_fn is None or not re.match(r"\s*\)", safe[end_fn:]):
            continue
        end_expr = end_fn + re.match(r"\s*\)", safe[end_fn:]).end()
        rows.append({
            "name": match.group("job"), "start": match.start(),
            "header_end": header.end(), "function_start": fn,
            "fn_end": end_fn, "end": end_expr,
            "body": text[header.end():end_fn-3] if False else
                    text[header.end():end_fn],  # parsed below
            "options": text[header.start("options"):header.end("options")],
        })
    return rows

def _pure_body(text, job):
    """Remove precisely the final function 'end', not any nested block."""
    safe = masked(text)
    begin = job["header_end"]
    end = job["fn_end"]
    suffix = re.search(r"\bend\s*\Z", safe[begin:end])
    if not suffix:
        return None
    return text[begin:begin+suffix.start()]

def admit(text: str, relative: str, owner: int, anchor: int):
    """Produce a source-derived contract for the strict single-XYZ family.

    No source-specific names, insertion recipes or self-asserted proof flags
    enter this API. Only an external collision-checked Resolver ID allocation
    and the effective installed source path are supplied.
    """
    if not isinstance(text,str) or not text or "\r" in text:
        raise Refused("noncanonical Lua source encoding")
    if not (type(owner) is int and type(anchor) is int and
            0 < owner < 2_100_000_000 and
            0 < anchor < 2_100_000_000 and owner != anchor):
        raise Refused("invalid/colliding native IDs")
    mod_root="bin/x64/plugins/cyber_engine_tweaks/mods/"
    parts=PurePosixPath(relative).parts
    if (not relative.startswith(mod_root) or len(parts)!=7 or
        parts[-1]!="init.lua" or any(p in ("..",".") for p in parts)):
        raise Refused("strict family requires an installed CET mod init.lua")
    jobs = _jobs(text)
    eligible=[]
    for far in jobs:
        body=_pure_body(text,far)
        if body is None:
            continue
        proof=PURE.fullmatch(body)
        if not proof:
            continue
        near_name=proof.group("near")
        if near_name==far["name"]:
            continue
        near_jobs=[j for j in jobs if j["name"]==near_name and j["start"]>far["end"]]
        if len(near_jobs)!=1 or OPTION_ACTIVE_FALSE.search(near_jobs[0]["options"]) is None:
            continue
        if OPTION_ACTIVE_FALSE.search(far["options"]) or len([j for j in jobs if j["name"]==far["name"]])!=1:
            continue
        near=near_jobs[0]
        # This family requires a source-local, single-purpose pair with no
        # external rearm or mutation of job handles. Other listeners remain
        # untouched and may do independent work, but cannot control the pair.
        outside=text[:far["start"]]+text[far["end"]:near["start"]]+text[near["end"]:]
        # Require one explicit local upvalue declaration before both jobs.
        # Globals would admit unrelated writers from another module/event.
        declaration=re.compile(r"(?m)^[ \t]*local[ \t]+"+
              re.escape(far["name"])+r"[ \t]*,[ \t]*"+
              re.escape(near_name)+r"[ \t]*\n")
        matches=list(declaration.finditer(outside))
        if len(matches)!=1 or matches[0].start()>=far["start"]:
            continue
        outside=declaration.sub("",outside,count=1)
        if re.search(r"\b"+re.escape(near_name)+r"\b",outside) or re.search(
            r"\b"+re.escape(far["name"])+r"\b",outside):
            continue
        # Do not transform the near lane's work at all. It is only resumed
        # when the exact author radius becomes NEAR, and the source proves
        # that no unrelated code manages either scheduler handle. Reject
        # self-mutation, new registrations and dynamic source geometry.
        near_body=_pure_body(text,near)
        if near_body is None or re.search(
            r"\b(?:Schedule|Cron|registerForEvent|Observe)\b",near_body):
            continue
        if re.search(r"\b(?:"+re.escape(near_name)+"|"+
                     re.escape(far["name"])+r")\b",near_body):
            continue
        try:
            xyz=[float(proof.group(k)) for k in ("x","y","z")]
            r2=float(proof.group("r2"))
        except ValueError:
            continue
        if not all(math.isfinite(v) for v in xyz) or not math.isfinite(r2) or r2<=0:
            continue
        radius=math.sqrt(r2)
        if radius*radius != r2:  # reject rounding boundary ambiguity
            continue
        eligible.append((far,near,xyz,radius))
    if len(eligible)!=1:
        raise Refused("expected one fully proved isolated cancelable XYZ lane")
    far,near,xyz,radius=eligible[0]
    digest=source_hash(text)
    if (text[far["header_end"]:far["header_end"]+1] != "\n" or
        text[near["end"]:near["end"]+1] != "\n"):
        raise Refused("scheduler job insertion requires newline boundary")
    far_header=text[far["start"]:far["header_end"]+1]
    near_expr=text[near["start"]:near["end"]+1]
    if text.count(far_header)!=1 or text.count(near_expr)!=1:
        raise Refused("source insertion anchors not unique")
    # Inject only at original scheduler boundaries. The original distance
    # callback remains byte-for-byte available as fail-open fallback.
    retry='        if gcetLocationBinding then gcetLocationBinding:attempt() end'
    binding=f'''
local okNativeBinding, NativeBinding = pcall(require, "GCETLookupWakeBinding")
if okNativeBinding and type(NativeBinding) == "table" then
    gcetLocationBinding = NativeBinding.new(function()
        local okMod, mod = pcall(GetMod, "G-LookupCache")
        if okMod and type(mod) == "table" and type(mod.TryAttach) == "function" then
            pcall(mod.TryAttach)
        end
        local okEngine, engine = pcall(GetMod, "0-Engine")
        return okEngine and type(engine) == "table"
            and type(engine.GCET) == "table" and engine.GCET.LookupCacheHost or nil
    end, {owner}, {far["name"]}, {{{near["name"]}}}, nil, function() end,
    {{epoch={digest!r}, fingerprint={digest!r}, relevant=true,
      sites=function() return {{{{id={anchor}, x={xyz[0]!r}, y={xyz[1]!r},
                                 z={xyz[2]!r}, radius={radius!r}, metric="xyz"}}}} end}})
    gcetLocationBinding:attempt()
end
registerForEvent("onShutdown", function()
    if gcetLocationBinding then gcetLocationBinding:shutdown(); gcetLocationBinding=nil end
end)
'''
    # Put source module state at the file beginning. In a real callback
    # structure, scoping is preserved because the binding is constructed in
    # the same lexical block after both scheduled handles exist.
    first_anchor=text[:max(1,min(48,len(text)))]
    if not first_anchor.strip() or text.count(first_anchor)!=1:
        raise Refused("ambiguous module prologue")
    edits=[
        {"name":"state","needle":first_anchor,"before":"local gcetLocationBinding = nil\n","after":""},
        {"name":"retry","needle":far_header,"before":"","after":retry},
        {"name":"adopt","needle":near_expr,"before":"","after":binding.strip()},
    ]
    return {
        "schema":SCHEMA,"source":relative,"original_sha256":digest,
        "dependency_sha256":{},
        "proof":{key:True for key in REQUIRED_PROOFS},
        "sites":[{"id":anchor,"x":xyz[0],"y":xyz[1],"z":xyz[2],
                  "radius":radius,"metric":"xyz"}],
        "insertions":edits,
        "admission":"STRICT_PURE_FAR_JOB_XYZ",
        "auto_admission":True, "reviewed_source_only":False,
    }

def bundle_runtime(game:Path, relative:str, overlay:bytes)->bytes:
    """Bundle shared CET host source; no DLL or game-root mutation."""
    adapters=Path(__file__).resolve().parents[2]/"native-static-locations"/"adapters"
    mod_root="bin/x64/plugins/cyber_engine_tweaks/mods/"
    mod=PurePosixPath(relative).parts[-2]
    shared=mod_root+"G-LookupCache/"
    files={
        mod_root+mod+"/GCETLookupWakeBinding.lua":
            adapters/"CETSourceWakeBinding.lua",
        shared+"init.lua":adapters/"CETLookupCacheModEntry.lua",
    }
    for name in ("CETNativeWakeHost.lua","CETStaticLocationConsumer.lua",
                 "CETAnchorSetGate.lua","CETNativeJobGate.lua",
                 "CETSourceAnchorLifecycle.lua","CETNativeWakeBootstrap.lua"):
        files[shared+"modules/"+name]=adapters/name
    with zipfile.ZipFile(io.BytesIO(overlay)) as inp:
        result={name:inp.read(name) for name in inp.namelist()}
    for name,source in files.items():
        data=source.read_bytes()
        live=game/name
        if live.exists():
            if not live.is_file() or live.is_symlink() or live.read_bytes()!=data:
                raise Refused("support source conflict: "+name)
            continue
        if name in result and result[name]!=data:
            raise Refused("overlay support collision: "+name)
        result[name]=data
    result[shared+"EXPERIMENTAL-NATIVE-DEPENDENCY.txt"]=(
        b"Generic structurally-proven pilot; not gameplay verified.\n"
        b"Requires a matching GCETStaticLocations RED4ext DLL.\n")
    out=io.BytesIO()
    with zipfile.ZipFile(out,"w",compression=zipfile.ZIP_DEFLATED) as z:
        for name in sorted(result):
            if not name.startswith(mod_root):
                raise Refused("non-game-root payload member: "+name)
            info=zipfile.ZipInfo(name,date_time=(1980,1,1,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=0o644<<16
            z.writestr(info,result[name])
    return out.getvalue()

def _normalize_for_admission(source:str)->str:
    """Recover original from known marker envelopes for shape rediscovery.

    Staging independently verifies the *exact* generated content, so merely
    deleting a tampered marker here never authorizes a corrupted live source.
    """
    pattern=re.compile(r"-- G-LOOKUPCACHE:BEGIN:(state|retry|adopt)\n"
                       r".*?-- G-LOOKUPCACHE:END:\1\n",re.S)
    candidate=pattern.sub("",source)
    if "G-LOOKUPCACHE:BEGIN:" in candidate or "G-LOOKUPCACHE:END:" in candidate:
        raise Refused("unknown or incomplete optimizer marker")
    return candidate

def stage_generic(game:Path,relative:str,owner:int,anchor:int):
    """Stage reversible game-root ZIP including generic host support."""
    src=game/relative
    live=src.read_text(encoding="utf-8")
    contract=admit(_normalize_for_admission(live),relative,owner,anchor)
    status,overlay=stage(game,contract)
    return (status,bundle_runtime(game,relative,overlay)),contract
