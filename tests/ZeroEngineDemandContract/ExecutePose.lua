-- Execute the actual generated pose path; compare every native read and
-- downstream invocation while injecting failures and changing receivers.
local root=assert(arg[1])
local originalPcall=pcall
local function run(path,backend)
    local trace,identities={},{}
    local current,fail
    local calls=0
    Game={}
    if backend and backend~="missing" then
        local function typed(player)
            calls=calls+1
            assert(player==current,"receiver changed")
            -- The native typed ABI cannot represent a nil Vector4; refuse
            -- this synthetic source-shaped case before any engine read.
            if backend=="unsupported" or fail==2 then return 0 end
            local ok,p,o,yaw=originalPcall(function()
                local position=player:GetWorldPosition()
                local orientation=player:GetWorldOrientation()
                return position,orientation,orientation:ToEulerAngles().yaw
            end)
            if not ok then
                if backend=="throws" then error("native invocation failed") end
                return 2
            end
            return 1,p,o,yaw
        end
        Game.GCETReadPlayerPoseV1=typed
        Game.GCETReadSharedPlayerPoseV1=typed
    end
    State={}
    function CurrentPlayer()return current end
    function downstream()
        trace[#trace+1]="downstream:"..tostring(State.pos and State.pos.x)..":"..tostring(State.yaw)
    end
    function pcall(fn,...)
        identities[fn]=true
        return originalPcall(fn,...)
    end
    local update=assert(dofile(path))
    for i=1,32 do
        fail=i%8
        local id=i
        current={
            GetWorldPosition=function(self)
                assert(self==current)
                trace[#trace+1]="pos:"..id
                if fail==1 then error("position failure") end
                if fail==2 then return nil end
                return {x=id}
            end,
            GetWorldOrientation=function(self)
                assert(self==current)
                trace[#trace+1]="orientation:"..id
                if fail==3 then error("orientation failure") end
                if fail==4 then return nil end
                return {ToEulerAngles=function()
                    trace[#trace+1]="euler:"..id
                    if fail==5 then error("Euler failure") end
                    return {yaw=id*2}
                end}
            end
        }
        if fail==6 then current=nil end
        update(0)
        trace[#trace+1]="state:"..tostring(State.pos and State.pos.x)..":"..tostring(State.yaw)
    end
    pcall=originalPcall
    local count=0
    for _ in pairs(identities) do count=count+1 end
    return table.concat(trace,"|"),count,calls
end
local a,ac=run(root.."/pose-original.lua")
local b,bc=run(root.."/pose-generated.lua")
assert(a==b,"pose native reads/failure/state trace differs")
assert(ac==28 and bc==1,"expected 28 original closures vs one reused function: "..ac.."/"..bc)
print("Pose: exact lookup/failure traces, 28 protected closures -> 1 reusable function PASS")

for _,backend in ipairs({"complete","unsupported","missing","throws"}) do
    local trace,_,calls=run(root.."/pose-typed.lua",backend)
    assert(trace==a,"typed pose original read/failure/publication trace differs: "..backend)
    assert(calls==(backend=="missing" and 0 or 28),"unexpected native backend attempt count")
end
print("Typed pose source: original receiver/read/order/state trace, unsupported-only fallback, missing ABI, native failure and throw without replay PASS")

for _,backend in ipairs({"complete","unsupported","missing","throws"}) do
    local trace,_,calls=run(root.."/pose-shared.lua",backend)
    assert(trace==a,"shared pose exact read/failure trace differs: "..backend)
    assert(calls==(backend=="missing" and 0 or 28),"unexpected shared backend attempt count")
end
-- Actual generated host registration, two independent owners. Explicit
-- coarse arming only while supported; disarm remains original exact ABI.
for _,supported in ipairs({true,false}) do
    local coarse,exact,disarm,scheduled=0,0,0,0
    Game={
        GCETStaticReady=function()return true end,
        GCETStaticOwnerCount=function(id)return 1 end,
        GCETStaticOwnerAnchor=function(id,n)return id*10+n end,
        GCETStaticArm=function(id,armed)
            if armed then exact=exact+1 else disarm=disarm+1 end
            return true
        end
    }
    if supported then Game.GCETStaticArmCoarseSpatialV1=function(id,armed)
        assert(armed);coarse=coarse+1;return true
    end end
    local host=assert(dofile(root.."/spatial-shared.lua")).new({
        Register=function()return {Schedule={Every=function()
            scheduled=scheduled+1;return {Cancel=function()end}
        end}}end
    })
    local cb=function()return true end
    assert(host:attachCompiled(1,1,cb,cb,cb))
    assert(host:attachCompiled(2,1,cb,cb,cb))
    assert(scheduled==1 and coarse==(supported and 2 or 0) and exact==(supported and 0 or 2))
    host:shutdown()
    assert(disarm==2)
end
print("Shared exact pose + explicit two-owner coarse spatial registration, older ABI and cleanup PASS")

-- Execute the actual generated exact pair paths, including failure after
-- each original operation and unsupported-only replay. Values are NOT
-- shared across exact callers even if a coarse consumer could reuse one.
local function exactRun(path,mode)
    local trace,attempts,current={},0,nil
    local fail=0
    Game={GetPlayer=function()return current end}
    if mode~="missing" then
        Game.GCETReadPlayerPositionEulerV1=function(player)
            attempts=attempts+1
            assert(player==current)
            if mode=="unsupported" then return 0 end
            local ok,p,e=originalPcall(function()
                return player:GetWorldPosition(),player:GetWorldOrientation():ToEulerAngles()
            end)
            if not ok then
                if mode=="throws" then error("native failed") end
                return 2
            end
            return 1,p,e
        end
    end
    condition=function()return true end
    unchanged=function(p)assert(p==current);trace[#trace+1]="unchanged" end
    local funcs=assert(dofile(path))
    for i=1,12 do
        fail=i%4
        current={
            GetWorldPosition=function(self)
                assert(self==current);trace[#trace+1]="position:"..i
                if fail==1 then error("position") end
                return {x=i,w=1}
            end,
            GetWorldOrientation=function(self)
                assert(self==current);trace[#trace+1]="orientation:"..i
                if fail==2 then error("orientation") end
                return {ToEulerAngles=function()
                    trace[#trace+1]="euler:"..i
                    if fail==3 then error("euler") end
                    return {roll=i+1,pitch=i+2,yaw=i+3}
                end}
            end
        }
        for index,f in ipairs(funcs) do
            local ok,p,e=originalPcall(f)
            trace[#trace+1]="result:"..tostring(ok)
            if ok then
                trace[#trace+1]=tostring(p.x)..":"..tostring(p.w)..":"..
                    (index==1 and (e.roll..":"..e.pitch..":"..e.yaw) or tostring(e))
            end
        end
    end
    return table.concat(trace,"|"),attempts
end
local exactOriginal=exactRun(root.."/exact-pairs-original.lua","missing")
for _,mode in ipairs({"complete","unsupported","missing","throws"}) do
    local trace,attempts=exactRun(root.."/exact-pairs-generated.lua",mode)
    assert(trace==exactOriginal,"exact ordered Euler trace changed: "..mode)
    assert(attempts==(mode=="missing" and 0 or 24))
end
print("Exact position/Euler pairs: same three engine reads, receiver/order/failure/full Euler/yaw results preserved; one successful typed crossing per pair PASS")

-- Same 12 ordered PSM reads, exactly the same published values and
-- callback/failure trace; native path never replays after a failed getter.
local function boardRun(path,mode)
    local trace,attempts,current,fail={},0,nil,0
    Game={}
    local function batch(board,k1,k2,k3,k4,lastBool)
        attempts=attempts+1
        assert(board==current)
        if mode=="unsupported" then return 0 end
        local ok,a,b,c,d=originalPcall(function()
            local x=board:GetInt(k1)
            local y=board:GetInt(k2)
            local z=board:GetInt(k3)
            if lastBool then return x,y,z,board:GetBool(k4) end
            return x,y,z,board:GetInt(k4)
        end)
        if not ok then
            if mode=="throws" then error("native board failed") end
            return 2
        end
        return 1,a,b,c,d
    end
    if mode~="missing" then
        Game.GCETReadBlackboard4IntV1=function(board,a,b,c,d)return batch(board,a,b,c,d,false) end
        Game.GCETReadBlackboard3IntBoolV1=function(board,a,b,c,d)return batch(board,a,b,c,d,true) end
    end
    publish=function(...)
        local out={}
        for i=1,select("#",...) do out[i]=tostring(select(i,...)) end
        trace[#trace+1]="publish:"..table.concat(out,",")
    end
    local cache=assert(dofile(path))
    local successfulAttempts=0
    for i=0,12 do
        fail=i
        current={
            GetInt=function(self,k)
                assert(self==current);trace[#trace+1]="int:"..k
                if fail==k then error("int failure") end
                return k*10+i
            end,
            GetBool=function(self,k)
                assert(self==current);trace[#trace+1]="bool:"..k
                if fail==k then error("bool failure") end
                return i%2==0
            end
        }
        cache.Attach(current)
        local ok=originalPcall(cache.Update,current)
        trace[#trace+1]="result:"..tostring(ok)
        successfulAttempts=successfulAttempts+(i==0 and 3 or math.ceil(i/4))
    end
    return table.concat(trace,"|"),attempts,successfulAttempts
end
local boardOriginal=boardRun(root.."/psm-original.lua","missing")
for _,mode in ipairs({"complete","unsupported","missing","throws"}) do
    local trace,attempts,expected=boardRun(root.."/psm-generated.lua",mode)
    assert(trace==boardOriginal,"ordered PSM read or event trace changed: "..mode)
    assert(attempts==(mode=="missing" and 0 or expected))
end
print("PSM native batches: 12 original engine reads, three successful typed crossings; ordered values, publication, board replacement, every failure index and older ABI PASS")
