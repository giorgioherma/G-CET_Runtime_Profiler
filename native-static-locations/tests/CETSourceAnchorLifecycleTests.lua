local Life=dofile("native-static-locations/adapters/CETSourceAnchorLifecycle.lua")
local entries,removed={},{}
local changed=0
local failAdd,failArm=nil,nil
local native={
 GCETStaticAddAnchor=function(id,x,y,z,radius,xyOnly,owner)
  assert(owner==42 and radius>0)
  if failAdd==id then return false end
  assert(not entries[id] and not removed[id])
  entries[id]={armed=false,x=x,xy=xyOnly}
  return true
 end,
 GCETStaticArm=function(id,armed)
  if failArm==id then return false end
  if not entries[id] then return false end
  entries[id].armed=armed
  return true
 end,
 GCETStaticRemoveAnchor=function(id)
  if not entries[id] then return false end
  entries[id]=nil;removed[id]=true
  return true
 end,
}
local l=Life.new(native,42,function() changed=changed+1 end)
local a={{id=101,x=1,y=2,z=3,radius=70,metric="xyz"},
         {id=102,x=-1,y=-2,z=4,radius=70,metric="xy"}}
assert(l:register("map-v1","hash-of-source-v1",a,true))
assert(entries[101].armed and entries[102].armed and entries[102].xy)
local prior=changed
assert(l:register("map-v1","hash-of-source-v1",a,true) and changed==prior)
assert(not l:register("map-v1","changed-source",a,true))
assert(l:setRelevant(false))
assert(not entries[101].armed and not entries[102].armed)
-- FAR_DORMANT waiting for entry means relevant=true, so anchor stays armed.
assert(l:setRelevant(true) and entries[101].armed and entries[102].armed)
local b={{id=201,x=90,y=20,z=4,radius=40,metric="xyz"}}
assert(l:register("map-v2","hash-of-source-v2",b,true))
assert(entries[201].armed and not entries[101] and not entries[102])
assert(removed[101] and removed[102])
assert(not l:register("map-v3","hash-of-source-v3",a,true))
-- A failed new epoch does not destroy prior valid native owner.
failAdd=301
local bad={{id=300,x=10,y=20,z=30,radius=30,metric="xyz"},
           {id=301,x=20,y=30,z=40,radius=30,metric="xy"}}
assert(not l:register("map-v3","hash-of-source-v3",bad,true))
assert(entries[201] and entries[201].armed and removed[300])
assert(not l:register("map-v4","hash-of-source-v4",bad,true))
failAdd=nil
failArm=302
assert(not l:register("map-v4","hash-of-source-v4",
 {{id=302,x=10,y=20,z=30,radius=30,metric="xyz"}},true))
assert(entries[201] and removed[302])
failArm=nil
assert(l:retire() and not entries[201] and removed[201])
assert(not l:register("map-v5","hash-of-source-v5",b,true))
assert(changed>=5)
print("PASS native source epochs, arm/disarm, atomic replace, tombstones")
