-- G-CET semantic:ziggy-last-play-navigation-sentinel
local N={age=0,retries=0,scanKey=nil,lastMappins={}}
local function valid(v)return v~=nil and IsDefined(v)end
local function point(v)return v and{x=v.x,y=v.y,z=v.z}or nil end
local function unsigned(n)return n<0 and n+4294967296 or n end
function N.reset()N.age=0;N.path=nil;N.retries=0 end
function N.update(dt,path,phase,playerKey,generation,hooks,pinId,runtime,gameplay)
 N.age=N.age+dt;if N.age<2 then return end;N.age=0
 if N.path~=path then N.path=path;N.retries=0 end
 local report={at=os.time(),phase=phase,objective=path,playerKey=playerKey,generation=generation,retries=N.retries,markerType='gameJournalQuestMultiMapPin'}
 report.runtime=runtime;report.pinId=pinId or'destination_v6'
 local ok,why=pcall(function()
  if gameplay~=false then hooks:SyncJournalPins(path or'',report.pinId)end
  report.retiredPins=hooks.retiredPins;report.activePins=hooks.activePins
  if not path then return end
  local manager=Game.GetJournalManager();local system=Game.GetMappinSystem()
  local objective=manager:GetEntryByString(path,'gameJournalQuestObjective')
  local pin=manager:GetEntryByString(path..'/'..report.pinId,'gameJournalQuestMultiMapPin')
  report.objectiveExists=valid(objective);report.pinExists=valid(pin)
  if not valid(objective)or not valid(pin)then return end
  report.tracked=manager:IsEntryTracked(objective);report.objectiveState=tostring(manager:GetEntryState(objective))
  report.enableGPS=pin.enableGPS;report.offset=point(pin.offset);report.referenceCount=#pin.references
  local hash=unsigned(manager:GetEntryHash(pin));report.hash=hash
  local found,pos=system:GetQuestMappinPosition(hash)
  report.destinationFound=found;if found then report.position=point(pos)end
  -- Match Below the Surface: multi-pins register when their parent activates.
  -- An objective activated during loading can miss its mappin callback.
  -- Refresh only this objective, only while it is selected, with bounded retries.
  if gameplay~=false and not found and report.tracked and N.retries<3 then
   N.retries=N.retries+1
   hooks:Journal(path,'gameJournalQuestObjective',0,true)
   hooks:Journal(path,'gameJournalQuestObjective',1,true)
   hooks:Track(path)
   hooks:SyncJournalPins(path,report.pinId)
   report.refreshed=true
  end
  local scanKey=table.concat({tostring(path or ''),tostring(phase or ''),tostring(generation or ''),tostring(found==true),tostring(report.tracked==true),tostring(N.retries)},'|')
  if not found or N.scanKey ~= scanKey then
   local mappins={}
   for _,m in ipairs(system:GetAllMappins())do
    if m:IsQuestMappin()and unsigned(m:GetJournalPathHash())==hash then
     mappins[#mappins+1]={active=m:IsActive(),visible=m:IsVisible(),tracked=m:IsPlayerTracked(),position=point(m:GetWorldPosition())}
    end
   end
   N.scanKey=scanKey
   N.lastMappins=mappins
  end
  report.mappins=N.lastMappins or {}
 end)
 if not ok then report.error=tostring(why)end
 local f=io.open('navigation-status.json','w');if f then f:write(json.encode(report));f:close()end
end
return N
