#!/usr/bin/env python3
"""Read-only CET+REDscript location-wake evidence for a selected Cyberpunk root.

Mechanical indicators only. This never authorizes a callback rewrite.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys

CET=Path("bin/x64/plugins/cyber_engine_tweaks/mods")
REDS=Path("r6/scripts")
CAP=2_000_000
PATTERNS={
    "recurring":re.compile(r'\b(?:Cron\s*\.\s*Every|Schedule\s*\.\s*Every|registerForEvent\s*\(\s*["\x27]on(?:Update|Draw)["\x27]|onUpdate\s*=|OnUpdate\s*\(|OnDraw\s*\(|@(?:wrapMethod|replaceMethod)\s*\([^)]*(?:Update|Tick))',re.I),
    "player":re.compile(r'\b(?:GetPlayer|GetWorldPosition|GetPlayerPosition|PlayerPuppet)\b',re.I),
    "distance":re.compile(r'\b(?:DistanceSquared|DistanceSq|Distance|DistanceBetween|distSq|LengthSquared|GetDistance|DistanceTo|IsInRange|withinRadius)\b|\^\s*2\b',re.I),
    "literal_xyz":re.compile(r'\b[xX]\s*[:=]\s*[-+]?\d+(?:\.\d+)?\s*[,;]\s*[yY]\s*[:=]\s*[-+]?\d+(?:\.\d+)?\s*[,;]\s*[zZ]\s*[:=]\s*[-+]?\d+(?:\.\d+)?|["\x27]x["\x27]\s*:\s*[-+]?\d+(?:\.\d+)?\s*,\s*["\x27]y["\x27]\s*:\s*[-+]?\d+(?:\.\d+)?\s*,\s*["\x27]z["\x27]\s*:\s*[-+]?\d+(?:\.\d+)?|\b(?:new\s+)?Vector4\s*(?:\.\s*new)?\s*\(\s*[-+]?\d+(?:\.\d+)?\s*,\s*[-+]?\d+(?:\.\d+)?\s*,\s*[-+]?\d+(?:\.\d+)?',re.I),
    "independent_wake":re.compile(r'\b(?:OnAction|ObserveAfter|Observe|registerHotkey|registerInput|GetQuestsSystem|GetFactsDB|GetJournalManager|Phone|SMS|Message|OnSave|OnLoad|OnShutdown|OnQuest|Quest)\b',re.I),
    "mutable_source":re.compile(r'\b(?:SetWorldPosition|SetPosition|Teleport|Editor|GetTarget|GetLookAt|AddTrack|RemoveTrack|WriteFile|SaveJson|json\.encode|io\.open)\b',re.I)
}
REQUIRED=("recurring","player","distance","literal_xyz")
NUMERIC=r'[-+]?(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][-+]?\d+)?'
# Multiline numeric source coordinates are evidence, not proof of world-space.
MULTILINE_XYZ=re.compile(
    r'\b["\x27]?x["\x27]?\s*[:=]\s*'+NUMERIC+
    r'\s*,\s*["\x27]?y["\x27]?\s*[:=]\s*'+NUMERIC+
    r'\s*,\s*["\x27]?z["\x27]?\s*[:=]\s*'+NUMERIC,re.I)
MAP_ROW=re.compile(
    r'^\s*'+NUMERIC+r'\s+'+NUMERIC+r'\s+'+NUMERIC+
    r'(?:\s+'+NUMERIC+r')?\s*(?:#.*)?$')
VEHICLE_POSITION=re.compile(
    r'\b(?:GetMountedVehicle|GetVehiclePosition|GetActiveGarageForVehicle|GetWorldTransform)\b',re.I)
PLAYER_POSITION=re.compile(
    r'\b(?:GetPlayer|GetWorldPosition|GetPlayerPosition)\b',re.I)

def files_under(owner:Path,extensions:set[str])->list[Path]:
    result=[]
    for where,dirs,files in os.walk(owner,followlinks=False):
        dirs[:]=sorted(d for d in dirs if not (Path(where)/d).is_symlink())
        for name in sorted(files):
            f=Path(where)/name
            if f.is_symlink() or f.suffix.casefold() not in extensions:
                continue
            if f.is_file() and f.stat().st_size<=CAP:
                result.append(f)
            if len(result)>2000:
                raise ValueError("too many source files in "+str(owner))
    return result

def file_evidence(f:Path,root:Path,language:str)->dict:
    raw=f.read_bytes()
    counts={k:0 for k in PATTERNS}
    spans={}
    content=raw.decode("utf-8-sig",errors="replace")
    # Source location producer may be multiline rather than inside one line.
    # .map is data-only (not script); it can define a reloadable epoch.
    extra_geometry=[]
    for match in MULTILINE_XYZ.finditer(content):
        line_no=content.count("\n",0,match.start())+1
        if "\n" in match.group(0):
            extra_geometry.append((line_no,match.group(0).strip().replace("\n"," ")[:180]))
    for line_no,line in enumerate(content.splitlines(),1):
        if f.suffix.casefold()==".map" and MAP_ROW.fullmatch(line):
            counts["literal_xyz"]+=1
            if len(spans.setdefault("literal_xyz",[]))<18:
                spans["literal_xyz"].append({"line":line_no,"excerpt":line.strip()[:180]})
        for kind,pattern in PATTERNS.items():
            n=len(pattern.findall(line))
            if n:
                counts[kind]+=n
                if len(spans.setdefault(kind,[]))<18:
                    spans[kind].append({"line":line_no,"excerpt":line.strip()[:180]})
    for line_no,excerpt in extra_geometry:
        counts["literal_xyz"]+=1
        if len(spans.setdefault("literal_xyz",[]))<18:
            spans["literal_xyz"].append({"line":line_no,"excerpt":excerpt})
    vehicle=bool(VEHICLE_POSITION.search(content))
    player=bool(PLAYER_POSITION.search(content))
    return {
        "path":f.relative_to(root).as_posix(),"language":language,
        "source_position_hints":{
            "player":player,
            "vehicle_or_world_transform":vehicle,
        },
        "sha256":hashlib.sha256(raw).hexdigest(),"bytes":len(raw),
        "counts":counts,"spans":spans,
    }

def examine(name:str,language:str,paths:list[Path],root:Path)->dict:
    evidence=[file_evidence(f,root,language) for f in sorted(paths)]
    counts={k:sum(x["counts"][k] for x in evidence) for k in PATTERNS}
    same=[e["path"] for e in evidence if all(e["counts"][k] for k in REQUIRED)]
    cross=all(counts[k] for k in REQUIRED)
    classification=("SOURCE_REVIEW_SAME_FILE" if same else
                    "SOURCE_REVIEW_CROSS_FILE" if cross else
                    "PARTIAL_SIGNAL" if counts["literal_xyz"] and counts["distance"]
                    else "NOT_IDENTIFIED")
    source_hash=hashlib.sha256(json.dumps(
        [(e["path"],e["sha256"]) for e in evidence],
        separators=(",",":")).encode()).hexdigest()
    return {
        "owner":name,"runtime":language,"classification":classification,
        "source_review_candidate":bool(cross),
        "independent_wake_present":counts["independent_wake"]>0,
        "position_sources_detected":{
            "player":any(e["source_position_hints"]["player"] for e in evidence),
            "vehicle_or_world_transform":any(
                e["source_position_hints"]["vehicle_or_world_transform"] for e in evidence),
        },
        "author_data_assets":[e["path"] for e in evidence
                              if Path(e["path"]).suffix.casefold() in (".json",".map")
                              and e["counts"]["literal_xyz"]>0],
        "mutable_source_hint":counts["mutable_source"]>0,
        "source_fingerprint":source_hash,"evidence_counts":counts,
        "evidence_files":[e for e in evidence if any(e["counts"].values())],
        "source_files_inspected":len(evidence),
        "lane_policy":{
            "location_discovery":"SOURCE_AND_WAKE_PROOF_REQUIRED",
            "remote_phone_quest_ui":"PRESERVE_INDEPENDENT_LISTENERS",
            "far_waiting_for_proximity_wake":"KEEP_NATIVE_ANCHOR_ARMED",
            "location_stage_inactive":"DISARM_NATIVE_ANCHOR",
            "unknown":"ORIGINAL_AUTHOR_FAIL_OPEN",
        },
        "safe_to_patch":False,
    }

def scan(game:Path)->dict:
    root=game.expanduser().resolve(strict=True)
    if not root.is_dir():
        raise ValueError("game root must be directory")
    cet=root/CET
    reds=root/REDS
    if not cet.is_dir() and not reds.is_dir():
        raise ValueError("selected game root has neither CET mods nor r6/scripts")
    owners=[]
    if cet.is_dir():
        for p in sorted(cet.iterdir(),key=lambda v:v.name.casefold()):
            if p.is_dir() and not p.is_symlink():
                paths=files_under(p,{".lua",".json",".map"})
                if paths:
                    owners.append(examine("CET:"+p.name,"CET_LUA",paths,root))
    if reds.is_dir():
        for p in sorted(reds.iterdir(),key=lambda v:v.name.casefold()):
            if p.is_symlink():
                continue
            if p.is_dir():
                paths=files_under(p,{".reds",".json"})
                if paths:
                    owners.append(examine("REDS:"+p.name,"REDSCRIPT",paths,root))
            elif p.is_file() and p.suffix.casefold()==".reds" and p.stat().st_size<=CAP:
                owners.append(examine("REDS:"+p.name,"REDSCRIPT",[p],root))
    owners.sort(key=lambda x:(x["runtime"],x["owner"].casefold()))
    return {
        "schema":"g-lookupcache-resolver-evidence-v1",
        "selected_game_root":str(root),
        "coverage":{"cet":cet.is_dir(),"redscript":reds.is_dir()},
        "owners_inspected":len(owners),
        "source_review_candidates":sum(x["source_review_candidate"] for x in owners),
        "approved_transformations":0,"generated_anchors":0,
        "native_lifecycle_contract":{
            "zero_armed_anchors":"PLAYER_RTTI_DORMANT",
            "far_mod_woken_by_proximity":"ANCHOR_REMAINS_ARMED",
            "inactive_location_stage":"ANCHOR_DISARMED",
            "phone_quest_remote_wakes":"ALWAYS_SEPARATE_FROM_LOCATION_GATE",
            "native_unavailable":"ORIGINAL_AUTHOR_FAIL_OPEN",
        },
        "owners":owners,
    }

def main(argv=None)->int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game",type=Path,required=True)
    parser.add_argument("--output",type=Path)
    args=parser.parse_args(argv)
    try:
        report=scan(args.game)
        output=json.dumps(report,indent=2,ensure_ascii=False)+"\n"
        if args.output:
            args.output.parent.mkdir(parents=True,exist_ok=True)
            args.output.write_text(output,encoding="utf-8")
        else:
            print(output,end="")
        print("G-LookupCache: %d owners, %d review candidates, zero automatic patches" %
              (report["owners_inspected"],report["source_review_candidates"]),file=sys.stderr)
        return 0
    except (OSError,ValueError) as error:
        print("G-LookupCache scan refused: "+str(error),file=sys.stderr)
        return 2

if __name__=="__main__":
    raise SystemExit(main())
)
VEHICLE_POSITION=re.compile(
    r'\b(?:GetMountedVehicle|GetVehiclePosition|GetActiveGarageForVehicle|GetVehiclePosition|GetWorldTransform)\b',re.I)
PLAYER_POSITION=re.compile(r'\b(?:GetPlayer|GetWorldPosition|GetPlayerPosition)\b',re.I)

def files_under(owner:Path,extensions:set[str])->list[Path]:
    result=[]
    for where,dirs,files in os.walk(owner,followlinks=False):
        dirs[:]=sorted(d for d in dirs if not (Path(where)/d).is_symlink())
        for name in sorted(files):
            f=Path(where)/name
            if f.is_symlink() or f.suffix.casefold() not in extensions:
                continue
            if f.is_file() and f.stat().st_size<=CAP:
                result.append(f)
            if len(result)>2000:
                raise ValueError("too many source files in "+str(owner))
    return result

def file_evidence(f:Path,root:Path,language:str)->dict:
    raw=f.read_bytes()
    counts={k:0 for k in PATTERNS}
    spans={}
    for line_no,line in enumerate(raw.decode("utf-8-sig",errors="replace").splitlines(),1):
        for kind,pattern in PATTERNS.items():
            n=len(pattern.findall(line))
            if n:
                counts[kind]+=n
                if len(spans.setdefault(kind,[]))<18:
                    spans[kind].append({"line":line_no,"excerpt":line.strip()[:180]})
    return {
        "path":f.relative_to(root).as_posix(),"language":language,
        "sha256":hashlib.sha256(raw).hexdigest(),"bytes":len(raw),
        "counts":counts,"spans":spans,
    }

def examine(name:str,language:str,paths:list[Path],root:Path)->dict:
    evidence=[file_evidence(f,root,language) for f in sorted(paths)]
    counts={k:sum(x["counts"][k] for x in evidence) for k in PATTERNS}
    same=[e["path"] for e in evidence if all(e["counts"][k] for k in REQUIRED)]
    cross=all(counts[k] for k in REQUIRED)
    classification=("SOURCE_REVIEW_SAME_FILE" if same else
                    "SOURCE_REVIEW_CROSS_FILE" if cross else
                    "PARTIAL_SIGNAL" if counts["literal_xyz"] and counts["distance"]
                    else "NOT_IDENTIFIED")
    source_hash=hashlib.sha256(json.dumps(
        [(e["path"],e["sha256"]) for e in evidence],
        separators=(",",":")).encode()).hexdigest()
    return {
        "owner":name,"runtime":language,"classification":classification,
        "source_review_candidate":bool(cross),
        "independent_wake_present":counts["independent_wake"]>0,
        "mutable_source_hint":counts["mutable_source"]>0,
        "source_fingerprint":source_hash,"evidence_counts":counts,
        "evidence_files":[e for e in evidence if any(e["counts"].values())],
        "source_files_inspected":len(evidence),
        "lane_policy":{
            "location_discovery":"SOURCE_AND_WAKE_PROOF_REQUIRED",
            "remote_phone_quest_ui":"PRESERVE_INDEPENDENT_LISTENERS",
            "far_waiting_for_proximity_wake":"KEEP_NATIVE_ANCHOR_ARMED",
            "location_stage_inactive":"DISARM_NATIVE_ANCHOR",
            "unknown":"ORIGINAL_AUTHOR_FAIL_OPEN",
        },
        "safe_to_patch":False,
    }

def scan(game:Path)->dict:
    root=game.expanduser().resolve(strict=True)
    if not root.is_dir():
        raise ValueError("game root must be directory")
    cet=root/CET
    reds=root/REDS
    if not cet.is_dir() and not reds.is_dir():
        raise ValueError("selected game root has neither CET mods nor r6/scripts")
    owners=[]
    if cet.is_dir():
        for p in sorted(cet.iterdir(),key=lambda v:v.name.casefold()):
            if p.is_dir() and not p.is_symlink():
                paths=files_under(p,{".lua",".json"})
                if paths:
                    owners.append(examine("CET:"+p.name,"CET_LUA",paths,root))
    if reds.is_dir():
        for p in sorted(reds.iterdir(),key=lambda v:v.name.casefold()):
            if p.is_symlink():
                continue
            if p.is_dir():
                paths=files_under(p,{".reds",".json"})
                if paths:
                    owners.append(examine("REDS:"+p.name,"REDSCRIPT",paths,root))
            elif p.is_file() and p.suffix.casefold()==".reds" and p.stat().st_size<=CAP:
                owners.append(examine("REDS:"+p.name,"REDSCRIPT",[p],root))
    owners.sort(key=lambda x:(x["runtime"],x["owner"].casefold()))
    return {
        "schema":"g-lookupcache-resolver-evidence-v1",
        "selected_game_root":str(root),
        "coverage":{"cet":cet.is_dir(),"redscript":reds.is_dir()},
        "owners_inspected":len(owners),
        "source_review_candidates":sum(x["source_review_candidate"] for x in owners),
        "approved_transformations":0,"generated_anchors":0,
        "native_lifecycle_contract":{
            "zero_armed_anchors":"PLAYER_RTTI_DORMANT",
            "far_mod_woken_by_proximity":"ANCHOR_REMAINS_ARMED",
            "inactive_location_stage":"ANCHOR_DISARMED",
            "phone_quest_remote_wakes":"ALWAYS_SEPARATE_FROM_LOCATION_GATE",
            "native_unavailable":"ORIGINAL_AUTHOR_FAIL_OPEN",
        },
        "owners":owners,
    }

def main(argv=None)->int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game",type=Path,required=True)
    parser.add_argument("--output",type=Path)
    args=parser.parse_args(argv)
    try:
        report=scan(args.game)
        output=json.dumps(report,indent=2,ensure_ascii=False)+"\n"
        if args.output:
            args.output.parent.mkdir(parents=True,exist_ok=True)
            args.output.write_text(output,encoding="utf-8")
        else:
            print(output,end="")
        print("G-LookupCache: %d owners, %d review candidates, zero automatic patches" %
              (report["owners_inspected"],report["source_review_candidates"]),file=sys.stderr)
        return 0
    except (OSError,ValueError) as error:
        print("G-LookupCache scan refused: "+str(error),file=sys.stderr)
        return 2

if __name__=="__main__":
    raise SystemExit(main())
