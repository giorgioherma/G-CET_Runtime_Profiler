local root=assert(arg[1])
local function run(module,host)
    local reads,trace,perFrame=0,{},{}
    local frame,frameReads=0,0
    local current,failFirst
    local function player(id)
        return {id=id,GetEntityID=function(self)return {hash=self.id}end}
    end
    local a,b,c=player(1),player(2),player(3)
    local invalid={id=99,GetEntityID=function()error("invalid")end}
    local state={inMenu=false,player={set=function(self,value)self.value=value end}}
    local function event(name,nextPlayer)
        return {trigger=function(_,p)
            trace[#trace+1]=name..":"..tostring(p and p.id)
            if nextPlayer then current=nextPlayer end
        end}
    end
    local E={PlayerReady=event("ready",b),PlayerRecreated=event("recreated",c),PlayerInvalidated=event("invalid",a)}
    CurrentLifecycle=assert(dofile(root.."/"..module))
    CurrentLifecycle.Init(E,state,{})
    CurrentBlackboard={Update=function(p)trace[#trace+1]="bb:"..p.id end}
    Game={GetPlayer=function()
        reads=reads+1;frameReads=frameReads+1
        if failFirst and frameReads==1 then error("first getter failed")end
        return current
    end}
    local tick=assert(dofile(root.."/"..host))
    local function step(p,failure,menu)
        frame=frame+1;frameReads=0
        current=p;failFirst=failure;state.inMenu=menu
        tick(0);perFrame[frame]=frameReads
    end
    for i=1,18 do step(a)end
    step(b) -- recreation callback changes current to c
    for i=1,8 do step(c)end
    step(c,true)
    step(invalid)
    step(nil,false,true)
    for i=1,30 do step(nil)end
    for i=1,18 do step(a)end
    assert(CurrentLifecycle.Update()==nil,"public lifecycle API return changed")
    return table.concat(trace,"|"),reads,perFrame
end
local a,ar,af=run("stock-module.lua","stock-host.lua")
local b,br,bf=run("module.lua","host.lua")
local c,cr=run("stock-module.lua","host.lua")
local d,dr=run("module.lua","stock-host.lua")
assert(a==b and a==c and a==d,"lifecycle or Blackboard delivery changed")
assert(cr==ar and dr==ar,"mixed overlay must preserve original getters")
assert(bf[18]==2 and bf[19]==2 and bf[28]==2 and bf[29]==2 and bf[30]==2 and bf[60]==2,
    "transition/error paths must retain the second native getter")
assert(bf[1]==1 and bf[21]==1 and br<ar,"stable observations not shared")
print("Lifecycle native GetPlayer reads "..ar.." -> "..br.."; exact transitions, retry and mixed overlay PASS")
