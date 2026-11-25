-- Execute the actual generated pose path; compare every native read and
-- downstream invocation while injecting failures and changing receivers.
local root=assert(arg[1])
local originalPcall=pcall
local function run(path)
    local trace,identities={},{}
    local current,fail
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
    return table.concat(trace,"|"),count
end
local a,ac=run(root.."/pose-original.lua")
local b,bc=run(root.."/pose-generated.lua")
assert(a==b,"pose native reads/failure/state trace differs")
assert(ac==28 and bc==1,"expected 28 original closures vs one reused function: "..ac.."/"..bc)
print("Pose: exact lookup/failure traces, 28 protected closures -> 1 reusable function PASS")
