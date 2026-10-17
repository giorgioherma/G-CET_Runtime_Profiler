"""Defensive Lua anonymous callback extent extraction; leads, not proofs."""
import hashlib
import re

REG=re.compile(
 r'\b(?:Cron\s*\.\s*Every|Schedule\s*\.\s*Every|'
 r'(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*["\x27]on(?:Update|Draw)["\x27])',
 re.I)
TOK=re.compile(r'\b(?:function|if|for|while|do|repeat|until|end)\b')
START=re.compile(r'\bfunction\s*\(')
LIMIT=24

def masked(lua):
    """Preserve offsets and line breaks, replace comments/strings with spaces."""
    chars=list(lua)
    i,n=0,len(lua)
    def blank(begin,end):
        for x in range(begin,end):
            if lua[x]!="\n":chars[x]=" "
    while i<n:
        if lua.startswith("--",i):
            m=re.match(r'\[(=*)\[',lua[i+2:])
            if m:
                eq=m.group(1);delimiter="]"+eq+"]"
                start=i+2+m.end()
                found=lua.find(delimiter,start)
                end=n if found<0 else found+len(delimiter)
            else:
                pos=lua.find("\n",i+2)
                end=n if pos<0 else pos
            blank(i,end);i=end;continue
        if lua[i] in ("'",'"'):
            quote=lua[i];j=i+1
            while j<n:
                if lua[j]=="\\":j+=2;continue
                if lua[j]==quote:j+=1;break
                j+=1
            blank(i,min(n,j));i=min(n,j);continue
        if lua[i]=="[":
            m=re.match(r'\[(=*)\[',lua[i:])
            if m:
                delim="]"+m.group(1)+"]"
                found=lua.find(delim,i+m.end())
                j=n if found<0 else found+len(delim)
                blank(i,j);i=j;continue
        i+=1
    return "".join(chars)

def scope_end(sanitized,first):
    stack=[]
    pending_loop=0
    for m in TOK.finditer(sanitized,first):
        word=m.group(0)
        if word=="function":
            stack.append(word)
        elif word in ("for","while"):
            stack.append(word);pending_loop+=1
        elif word in ("if","repeat"):
            stack.append(word)
        elif word=="do":
            if pending_loop:pending_loop-=1
            else:stack.append(word)
        elif word=="until":
            if not stack or stack[-1]!="repeat":return None
            stack.pop()
        elif word=="end":
            if not stack or stack[-1]=="repeat":return None
            stack.pop()
            if not stack:return m.end()
    return None

def function_leads(lua):
    safe=masked(lua)
    seen=set()
    results=[]
    for registration in REG.finditer(lua):
        if safe[registration.start()].isspace():
            continue  # comments and strings are never registrations
        # A registration without an inlined anonymous function is not
        # automatically transformable by this family.
        begin=registration.end()
        match=START.search(safe,begin,min(len(safe),begin+1000))
        if not match or match.start() in seen:continue
        end=scope_end(safe,match.start())
        if end is None:continue
        seen.add(match.start())
        body=safe[match.start():end]
        original=lua[match.start():end]
        has_player=bool(re.search(r'\b(?:GetPlayer|GetWorldPosition|GetPlayerPosition)\b',body,re.I))
        has_distance=bool(re.search(r'\b(?:Distance|DistanceSquared|DistanceBetween|distSq|LengthSquared)\b',body,re.I))
        results.append({
          "start_line":lua.count("\n",0,match.start())+1,
          "end_line":lua.count("\n",0,end)+1,
          "registration_line":lua.count("\n",0,registration.start())+1,
          "sha256":hashlib.sha256(original.encode("utf-8")).hexdigest(),
          "player_in_callback":has_player,
          "distance_in_callback":has_distance,
          "scoped_location_lead":has_player and has_distance,
          "semantic_equivalence_proven":False,
        })
        if len(results)>=LIMIT:break
    return results
