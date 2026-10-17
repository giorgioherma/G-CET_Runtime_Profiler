#!/usr/bin/env python3
"""One opt-in source-reviewed CET pilot; NOT a named-mod production admission.

The reviewed input is a supplied fixture. This program refuses different
source epochs and does not discover/optimize other mods. The reusable
transaction engine and shared runtime live outside this example.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import io
import zipfile

from stage_source_pass import SCHEMA, REQUIRED_PROOFS, Refused, stage

SOURCE="bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/init.lua"
DATA="bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/StreetTables.lua"
SOURCE_HASH="e4594ed8eb7a1ad0a889068db0a5be258c498fab719a6f2ab13df408e6e9af84"
DATA_HASH="a79edb7eca3632531d6a35d304857592a9f61f6f7c00337ecf9e0ff8346d0099"
OWNER_ID=1730200
FIRST_SITE_ID=1730201
NUM=r"[-+]?(?:\d+(?:\.\d+)?|\.\d+)"
TABLE_ROW=re.compile(
    r"id\s*=\s*'([^']+)'\s*,.*?"
    r"center\s*=\s*\{\s*x\s*=\s*("+NUM+r")\s*,"
    r"\s*y\s*=\s*("+NUM+r")\s*\}.*?"
    r"floorZ\s*=\s*("+NUM+r")",re.S)

def get_sites(data:str)->list[dict]:
    token="StreetTables.locations = {"
    if data.count(token)!=1:
        raise Refused("source-authored table roster no longer matches")
    section=data.split(token,1)[1].split("\n}\n",1)
    if len(section)!=2:
        raise Refused("location roster close not found")
    rows=TABLE_ROW.findall(section[0])
    if len(rows)!=40 or len({r[0] for r in rows})!=40:
        raise Refused("reviewed 40-site source epoch changed")
    if data.count(token)!=1 or re.search(
        r"(?:StreetTables\.locations\s*=|"
        r"table\.(?:insert|remove)\s*\(\s*StreetTables\.locations)",
        section[1]):
        raise Refused("location table can change after initialization")
    return [{"name":name,"id":FIRST_SITE_ID+i,
             "x":float(x),"y":float(y),"z":float(z),
             "radius":45.0,"metric":"xyz"}
            for i,(name,x,y,z) in enumerate(rows)]

def build_contract(game:Path)->dict:
    source=(game/SOURCE).read_bytes()
    data=(game/DATA).read_bytes()
    if (hashlib.sha256(source).hexdigest()!=SOURCE_HASH or
        hashlib.sha256(data).hexdigest()!=DATA_HASH):
        raise Refused("reviewed author-source epoch does not match")
    sites=get_sites(data.decode("utf-8"))
    # Native 45m gate is conservative; exact author 25m work remains unchanged.
    # Deterministic, source-backed roster. Lua checks it again against the
    # actual loaded author tables before registering any native IDs.
    entries="\n".join('      ["'+s["name"]+'"]={'+
                 ','.join(str(s[v]) for v in ("id","x","y","z"))+'},'
                 for s in sites)
    binding='''        local okLookup, LookupBinding = pcall(require, "GCETLookupWakeBinding")
        if okLookup and type(LookupBinding)=="table" then
          gcetLookupBinding=LookupBinding.new(function()
            local okEngine,loadedEngine=pcall(GetMod,"0-Engine")
            if not okEngine or type(loadedEngine)~="table" then return nil end
            local api=loadedEngine.GCET
            if not api or not api.LookupCacheHost then
              local okMod,cacheMod=pcall(GetMod,"G-LookupCache")
              if okMod and type(cacheMod)=="table"
                  and type(cacheMod.TryAttach)=="function" then
                pcall(cacheMod.TryAttach)
              end
            end
            return type(loadedEngine.GCET)=="table"
                and loadedEngine.GCET.LookupCacheHost or nil
          end,OWNER,farWorldJob,{nearWorldJob,nearSeatJob},
          function() return pokerIsActive() or InteractionMenu.visible end,
          function() setNearMode(false) end,
          {epoch="poker-source-"..SOURCE_EPOCH,
           fingerprint=TABLE_EPOCH,relevant=inGame,
           sites=function()
             local expected={
EXPECTED_ENTRIES
             }
             if #StreetTables.locations~=EXPECTED_COUNT then return nil end
             local seen,result={},{}
             for _,loc in ipairs(StreetTables.locations) do
               local v=expected[loc.id]
               if not v or seen[loc.id] or not loc.center
                   or loc.center.x~=v[2] or loc.center.y~=v[3]
                   or loc.floorZ~=v[4] then return nil end
               seen[loc.id]=true
               result[#result+1]={id=v[1],x=v[2],y=v[3],z=v[4],
                                  radius=45.0,metric="xyz"}
             end
             if #result~=EXPECTED_COUNT then return nil end
             return result
           end},
          function()
            -- Native FAR is an original author cleanup wake, not an
            -- instruction to launch/advance gameplay. Run once per exit.
            if pokerRuntimeReady() then
              local exactNear=StreetTables.update(0.0,POKER_WAKE_RADIUS)
              if not exactNear then setNearMode(false) end
            end
            return true
          end)
        end'''.replace("OWNER",str(OWNER_ID)).replace(
            "SOURCE_EPOCH",json.dumps(SOURCE_HASH)).replace(
            "TABLE_EPOCH",json.dumps(DATA_HASH)).replace(
            "EXPECTED_ENTRIES",entries).replace(
            "EXPECTED_COUNT",str(len(sites)))
    edits=[
      ("top_state","local TryAttachPokerPass4",
       "local gcetLookupBinding = nil\n"),
      ("session_enter",'        StreetTables.registerMapPins()\n        DualPrint("session started; map pins refreshed")',
       '        if gcetLookupBinding then gcetLookupBinding:setRelevant(true) end\n'),
      ("session_leave",'        cleanup("session_end")',
       '        if gcetLookupBinding then gcetLookupBinding:setRelevant(false) end\n'),
      ("source_mode", '        end\n\n        farWorldJob = client.Schedule.Every(1.0, {',
       '          if gcetLookupBinding then gcetLookupBinding:reconcile() end\n'),
      ("far_retry",'            if near then setNearMode(true) end',
       '            if gcetLookupBinding then gcetLookupBinding:attempt() end\n'),
      ("near_retry",'            if not near then setNearMode(false) end',
       '            if near then setNearMode(true) end\n'
       '            if gcetLookupBinding then gcetLookupBinding:attempt() end\n'),
      ("site_bind",'        local realtimeJob = client.Schedule.EveryFrame({',
       binding+"\n"),
      ("rollback",'        for _, handle in ipairs(created) do',
       '        if gcetLookupBinding then gcetLookupBinding:shutdown(); gcetLookupBinding=nil end\n'),
    ]
    return {
      "schema":SCHEMA,"source":SOURCE,"original_sha256":SOURCE_HASH,
      "dependency_sha256":{DATA:DATA_HASH},
      "proof":{x:True for x in REQUIRED_PROOFS},
      "sites":[{k:v for k,v in site.items() if k!="name"} for site in sites],
      "insertions":[{"name":name,"needle":needle,"before":snippet,"after":""}
                    for name,needle,snippet in edits],
      "status":"EXPERIMENTAL_SOURCE_REVIEW_ONLY",
    }

def bundle_runtime(game:Path,overlay:bytes)->bytes:
    """Package the one shared host, without touching the user's game files.

    A native DLL is deliberately NOT bundled, and this stage is not declared
    gameplay-stable until original wake-equivalence is tested in game.
    """
    repo=Path(__file__).resolve().parents[2]
    adapters=repo/"native-static-locations"/"adapters"
    modroot="bin/x64/plugins/cyber_engine_tweaks/mods/"
    shared=modroot+"G-LookupCache/"
    requested={
       modroot+"StreetGamesPoker/GCETLookupWakeBinding.lua":
           adapters/"CETSourceWakeBinding.lua",
       shared+"init.lua":adapters/"CETLookupCacheModEntry.lua",
    }
    for name in ("CETNativeWakeHost.lua","CETStaticLocationConsumer.lua",
                 "CETAnchorSetGate.lua","CETNativeJobGate.lua",
                 "CETSourceAnchorLifecycle.lua","CETNativeWakeBootstrap.lua"):
        requested[shared+"modules/"+name]=adapters/name
    original=zipfile.ZipFile(io.BytesIO(overlay),"r")
    with original:
        result={name:original.read(name) for name in original.namelist()}
    for path,source in requested.items():
        data=source.read_bytes()
        target=game/path
        if target.exists():
            if not target.is_file() or target.is_symlink() or target.read_bytes()!=data:
                raise Refused("existing installed support file differs: "+path)
            continue
        if path in result and result[path]!=data:
            raise Refused("shared runtime output collision")
        result[path]=data
    result["G-LookupCache/EXPERIMENTAL-NATIVE-DEPENDENCY.txt"]=(
      "Experimental source pass; not gameplay verified.\\n"
      "Requires the matching GCETStaticLocations RED4ext native DLL.\\n"
      "Never install this bundle on an unverified source variant.\\n"
    ).encode("utf-8")
    output=io.BytesIO()
    with zipfile.ZipFile(output,"w",compression=zipfile.ZIP_DEFLATED) as z:
        for name in sorted(result):
            info=zipfile.ZipInfo(name,date_time=(1980,1,1,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=0o644<<16
            z.writestr(info,result[name])
    return output.getvalue()

def main(argv=None)->int:
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--game",type=Path,required=True)
    p.add_argument("--contract-output",type=Path,required=True)
    p.add_argument("--stage-output",type=Path,required=True)
    args=p.parse_args(argv)
    try:
        contract=build_contract(args.game)
        _,bundle=stage(args.game,contract)
        bundle=bundle_runtime(args.game,bundle)
        args.contract_output.parent.mkdir(parents=True,exist_ok=True)
        args.contract_output.write_text(json.dumps(contract,indent=2)+"\n")
        args.stage_output.write_bytes(bundle)
        print("EXPERIMENTAL reviewed StreetGames scheduler source staged; NOT install-ready")
        return 0
    except (OSError,Refused,ValueError) as exc:
        print("G-LookupCache pilot refused: "+str(exc),file=sys.stderr)
        return 2

if __name__=="__main__":raise SystemExit(main())
