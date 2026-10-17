"""Candidate coordinates in source assets; no wake or site-set proof."""
import json
import math
import re
from pathlib import Path

N=r'[-+]?(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][-+]?\d+)?'
XYZ=re.compile(r'\b["\x27]?x["\x27]?\s*[:=]\s*('+N+
 r')\s*,\s*["\x27]?y["\x27]?\s*[:=]\s*('+N+
 r')\s*,\s*["\x27]?z["\x27]?\s*[:=]\s*('+N+')',re.I)
VECTOR=re.compile(r'\bVector4\s*(?:\.\s*new)?\s*\(\s*('+N+
 r')\s*,\s*('+N+r')\s*,\s*('+N+')',re.I)
CENTER=re.compile(r'\bcenter\s*=\s*\{([^{}]{0,180})\}([^{}]{0,240})',re.I|re.S)
XY=re.compile(r'\bx\s*=\s*('+N+r')\s*,\s*y\s*=\s*('+N+')',re.I)
FLOOR=re.compile(r'\bfloorZ\s*=\s*('+N+')',re.I)
MAP=re.compile(r'^\s*('+N+r')\s+('+N+r')\s+('+N+
 r')(?:\s+'+N+r')?\s*(?:#.*)?$')
LIMIT=10000

def extract_sites(path,content):
    """Only geometric leads; a selected asset is not necessarily active."""
    suffix=Path(path).suffix.lower()
    sites=[]
    def add(line,kind,coords,source_key):
        xyz=[float(x) for x in coords]
        if all(math.isfinite(v) for v in xyz):
            if len(sites)>=LIMIT: raise ValueError("source geometry cap exceeded")
            sites.append({"line":line,"form":kind,"xyz":xyz,"source_key":source_key})
    if suffix==".map":
        for i,row in enumerate(content.splitlines(),1):
            m=MAP.fullmatch(row)
            if m:add(i,"MAP_ROW",m.groups()[:3],str(i))
    elif suffix==".json":
        try: obj=json.loads(content)
        except ValueError:return []
        def walk(x,key,depth):
            if depth>64:return
            if isinstance(x,dict):
                if all(k in x for k in ("x","y","z")):
                    xyz=[x[k] for k in ("x","y","z")]
                    if all(isinstance(v,(int,float)) and not isinstance(v,bool) for v in xyz):
                        add(None,"JSON_XYZ",xyz,key)
                for name,value in x.items():
                    if isinstance(value,(dict,list)):walk(value,key+"/"+str(name),depth+1)
            elif isinstance(x,list):
                for i,value in enumerate(x):
                    if isinstance(value,(dict,list)):walk(value,key+"/"+str(i),depth+1)
        walk(obj,"$",0)
    else:
        for rx,kind in ((XYZ,"SOURCE_XYZ"),(VECTOR,"VECTOR4_LITERAL")):
            for m in rx.finditer(content):
                add(content.count("\n",0,m.start())+1,kind,m.groups()[:3],str(m.start()))
        if suffix==".lua":
            for m in CENTER.finditer(content):
                center=XY.search(m.group(1))
                floor=FLOOR.search(m.group(2))
                if center and floor:
                    add(content.count("\n",0,m.start())+1,"LUA_CENTER_FLOORZ",
                        (*center.groups(),floor.group(1)),str(m.start()))
    return sites
