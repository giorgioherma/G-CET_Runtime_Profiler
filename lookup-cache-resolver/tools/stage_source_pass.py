#!/usr/bin/env python3
"""Experimental reviewed-source overlay staging; never writes the game root.

Each insertion must be reviewed against the exact original mod source,
complete site roster and downstream wake/cleanup obligations. This is
NOT automatic G-HoneyTrap approval, semantic parsing or a production pass.
"""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import math
from pathlib import Path, PurePosixPath
import sys
import zipfile

SCHEMA="g-lookupcache-reviewed-source-adoption-v1"
MAX_ANCHOR_ID=2_099_999_999
REQUIRED_PROOFS=("all_possible_sites_proven","original_wake_side_effects_reviewed",
                 "independent_listeners_preserved","author_fallback_proven",
                 "cancelable_original_discovery_job")
class Refused(ValueError):
    pass

def digest(data:bytes)->str:
    return hashlib.sha256(data).hexdigest()

def _path(root:Path, relative:str)->Path:
    if not isinstance(relative,str) or "\\" in relative:
        raise Refused("invalid overlay path")
    parts=PurePosixPath(relative)
    if not relative or parts.is_absolute() or any(s in (".","..") for s in relative.split("/")):
        raise Refused("path outside selected game installation")
    if not str(parts).startswith("bin/x64/plugins/cyber_engine_tweaks/mods/"):
        raise Refused("only installed CET Lua sources in current prototype")
    path=root.joinpath(*parts.parts)
    if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
        raise Refused("source path escapes selected game root")
    return path

def _validate_contract(contract:dict)->None:
    if not isinstance(contract,dict) or contract.get("schema")!=SCHEMA:
        raise Refused("unsupported reviewed contract")
    if not isinstance(contract.get("proof"),dict) or any(
        contract["proof"].get(k) is not True for k in REQUIRED_PROOFS):
        raise Refused("missing explicit source/wake proof")
    sites=contract.get("sites")
    if not isinstance(sites,list) or not sites:
        raise Refused("complete nonempty location roster required")
    seen=set()
    for site in sites:
        if not isinstance(site,dict):
            raise Refused("invalid site")
        anchor=site.get("id")
        if type(anchor)!=int or not 0<anchor<=MAX_ANCHOR_ID or anchor in seen:
            raise Refused("duplicate/invalid native anchor ID")
        seen.add(anchor)
        for coord in ("x","y","z","radius"):
            value=site.get(coord)
            if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value):
                raise Refused("nonfinite location data")
        if site["radius"]<=0 or site.get("metric") not in ("xyz","xy"):
            raise Refused("invalid native wake geometry")
    if not isinstance(contract.get("dependency_sha256"),dict):
        raise Refused("missing source dependency fingerprints")
    edits=contract.get("insertions")
    if not isinstance(edits,list) or not edits or len(edits)>16:
        raise Refused("missing/bulk source insertion contract")
    seen_names=set()
    for edit in edits:
        if not isinstance(edit,dict) or set(edit)!={"name","needle","before","after"}:
            raise Refused("insertion must have name, needle, before, after")
        name=edit["name"]
        if not isinstance(name,str) or not name.isidentifier() or name in seen_names:
            raise Refused("duplicate/non-identifier injection name")
        seen_names.add(name)
        if not isinstance(edit["needle"],str) or len(edit["needle"])<15:
            raise Refused("weak insertion anchor")
        if any(not isinstance(edit[k],str) for k in ("before","after")):
            raise Refused("nontext insertion")
        if not edit["before"] and not edit["after"]:
            raise Refused("empty insertion")

def _insert_block(name:str,content:str)->str:
    return "-- G-LOOKUPCACHE:BEGIN:"+name+"\n"+content+"\n-- G-LOOKUPCACHE:END:"+name+"\n"

def _source_state(source:str,contract:dict)->tuple[str,str]:
    """Normalize original/live/partial to pristine; refuse drift or tampering."""
    pristine=source
    names=[edit["name"] for edit in contract["insertions"]]
    for edit in contract["insertions"]:
        marker=_insert_block(edit["name"],edit["before"]+edit["after"])
        # Patches are *insertions*: original author code is never deleted.
        if marker in pristine:
            if pristine.count(marker)!=1:
                raise Refused("duplicate managed insertion")
            pristine=pristine.replace(marker,"")
    if "G-LOOKUPCACHE:BEGIN:" in pristine or "G-LOOKUPCACHE:END:" in pristine:
        raise Refused("unknown or tampered native injection")
    base=pristine.encode("utf-8")
    if digest(base)!=contract["original_sha256"]:
        raise Refused("source drift or unsupported original/partial variant")
    updated=pristine
    for edit in contract["insertions"]:
        needle=edit["needle"]
        if updated.count(needle)!=1:
            raise Refused("insertion anchor not unique")
        block=_insert_block(edit["name"],edit["before"]+edit["after"])
        updated=updated.replace(needle,edit["before"] and (block+needle) or (needle+block),1)
    return pristine,updated

def stage(game:Path,contract:dict,restore:bool=False)->tuple[str,bytes]:
    _validate_contract(contract)
    root=game.expanduser().resolve(strict=True)
    src=_path(root,contract["source"])
    if not src.is_file() or src.suffix.lower()!=".lua" or src.stat().st_size>2_000_000:
        raise Refused("source file unavailable or oversized")
    for dep,expected in contract["dependency_sha256"].items():
        file=_path(root,dep)
        if not file.is_file() or digest(file.read_bytes())!=expected:
            raise Refused("dependent source/data changed: "+dep)
    raw=src.read_bytes()
    try:
        text=raw.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise Refused("source must be UTF-8") from exc
    original,output=_source_state(text,contract)
    new=original if restore else output
    status=("ALREADY_ORIGINAL" if restore else "ALREADY_OPTIMIZED") if text==new else (
        "RESTORE_STAGED" if restore else "OPTIMIZATION_STAGED")
    metadata={
        "schema":"g-lookupcache-stage-result-v1","status":status,
        "source":contract["source"],"original_sha256":contract["original_sha256"],
        "input_sha256":digest(raw),"output_sha256":digest(new.encode("utf-8")),
        "native_anchor_count":len(contract["sites"]),"reviewed_source_only":True,
        "auto_admission":False,"gameplay_verified":False,
    }
    # Stable ordering and timestamps make the output reproducible.
    buffer=io.BytesIO()
    with zipfile.ZipFile(buffer,"w",compression=zipfile.ZIP_DEFLATED) as zipout:
        for path,data in [(contract["source"],new.encode("utf-8")),
                          ("G-LookupCache/REVIEWED-STAGE.json",
                           (json.dumps(metadata,sort_keys=True,indent=2)+"\n").encode("utf-8"))]:
            info=zipfile.ZipInfo(path,date_time=(1980,1,1,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=0o644<<16
            zipout.writestr(info,data)
    return status,buffer.getvalue()

def main(argv=None)->int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game",type=Path,required=True)
    parser.add_argument("--contract",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    parser.add_argument("--restore",action="store_true")
    args=parser.parse_args(argv)
    try:
        contract=json.loads(args.contract.read_text(encoding="utf-8"))
        status,data=stage(args.game,contract,args.restore)
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_bytes(data)
        print(status+" / no live source files modified")
        return 0
    except (Refused,OSError,ValueError,KeyError,TypeError) as exc:
        print("G-LookupCache refused: "+str(exc),file=sys.stderr)
        return 2

if __name__=="__main__":
    raise SystemExit(main())
