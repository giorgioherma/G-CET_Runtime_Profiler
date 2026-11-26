using System.Diagnostics;
using System.Text;
using GCETRuntimeProfiler.Core.Services;
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
var root = Path.Combine(Path.GetTempPath(), "gcet-demand-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var scheduler = File.ReadAllText("payload/0-Engine/modules/G-CET/Scheduler.lua");
    var host = """
local Proximity = require('modules/Proximity')
local SpatialHash = require('modules/SpatialHash')
local currentFrame = 0
local function UpdateFrame(delta)
    currentFrame = currentFrame + 1
    local pos = {x=0}
    Proximity.SetPlayerPos(pos)
    pcall(Proximity.Update, currentFrame)
    SpatialHash.SetPlayerPos(pos)
    pcall(SpatialHash.Update, currentFrame)
end
return UpdateFrame
""";
    var proximity = """
local Proximity = {}
local zones = {}
local zoneCounter = 0
function Proximity.RegisterZone(config)
    zoneCounter = zoneCounter + 1
    local zone = {throttle=config.throttle or 5, callback=config.callback}
    zones[zoneCounter] = zone
    local capturedId = zoneCounter
    return {unregister=function()
        zones[capturedId] = nil
    end}
end
function Proximity.SetPlayerPos(pos) end
function Proximity.Update(frame)
    count("proximity-pass")
    for _, zone in pairs(zones) do
        if frame % zone.throttle == 0 then zone.callback(frame) end
    end
end
return Proximity
""";
    var spatial = """
local SpatialHash = {}
local sets = {}
function SpatialHash.RegisterSpatialSet(name, entries, config, source)
    local setData = {config=config}
    sets[name] = setData
    return {unregister=function()
        sets[name] = nil
    end}
end
function SpatialHash.SetPlayerPos(pos) end
function SpatialHash.Update(frame)
    count("spatial-pass")
    for _, setData in pairs(sets) do
        local cfg = setData.config
        if cfg.pollRadiusSq and frame % cfg.pollThrottle == 0 then cfg.callback(frame) end
    end
end
return SpatialHash
""";
    var derived = File.ReadAllText("tests/ZeroEngineDemandContract/DerivedFixture.lua");
    var derivedHost = File.ReadAllText("tests/ZeroEngineDemandContract/DerivedHostFixture.lua");
    var lifecycle = File.ReadAllText("tests/ZeroEngineDemandContract/LifecycleFixture.lua");
    var moduleDir = Path.Combine(root, "0-Engine", "modules");
    Directory.CreateDirectory(moduleDir);
    var privateSource = args.Length == 2 && args[0] == "--stock";
    if (privateSource)
    {
        var author = Path.GetFullPath(args[1]);
        host = File.ReadAllText(Path.Combine(author, "init.lua"));
        proximity = File.ReadAllText(Path.Combine(author, "modules", "Proximity.lua"));
        spatial = File.ReadAllText(Path.Combine(author, "modules", "SpatialHash.lua"));
        derived = File.ReadAllText(Path.Combine(author, "modules", "DerivedState.lua"));
        derivedHost = host;
        lifecycle = File.ReadAllText(Path.Combine(author, "modules", "Lifecycle.lua"));
    }
    File.WriteAllText(Path.Combine(moduleDir, "Proximity.lua"), proximity);
    File.WriteAllText(Path.Combine(moduleDir, "SpatialHash.lua"), spatial);
    var files = new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(host),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(scheduler)
    };
    var framework = new FixedZeroEngineBuild(files, "", "", "", "");
    var staged = new Dictionary<string, byte[]>();
    var counts = new Dictionary<string, int>();
    var manifest = new List<object>(); var skipped = new List<object>();
    Check(ZeroEngineDemandSource.TryStage(root, ref framework, staged, counts, manifest, skipped) == 2,
        "expected both central producer integrations");
    Check(staged.Count == 2 && skipped.Count == 0, "producer admission");
    var generatedHost = framework.Files[ZeroEngineDemandSource.InitPath];
    var generatedScheduler = framework.Files[ZeroEngineDemandSource.SchedulerPath];
    var originalStaged = staged.ToDictionary(x => x.Key, x => x.Value);
    var fastScheduler = Encoding.UTF8.GetString(generatedScheduler).Replace("\r\n","\n");
    var oldDemandScheduler = fastScheduler.Replace(ZeroEngineDemandSource.EveryFrameFastPath+"\n","",StringComparison.Ordinal);
    var upgradedOk=ZeroEngineDemandSource.TryScheduler(oldDemandScheduler,out var upgradedDemandScheduler);
    Check(oldDemandScheduler!=fastScheduler && upgradedOk &&
        upgradedDemandScheduler==fastScheduler,"older every-frame demand controller upgrade");
    Check(ZeroEngineDemandSource.TryScheduler(upgradedDemandScheduler,out var repeatDemandScheduler) &&
        repeatDemandScheduler==upgradedDemandScheduler,"every-frame demand controller repeat");
    var customDemandScheduler=oldDemandScheduler.Replace("local due, earliest = false, math.huge",
        "local due, earliest = false, math.huge -- author custom");
    Check(ZeroEngineDemandSource.TryScheduler(customDemandScheduler,out var retainedDemandScheduler) &&
        retainedDemandScheduler==customDemandScheduler,"custom demand implementation must remain intact");
    Check(ZeroEngineDemandSource.TryStage(root, ref framework, staged, counts, manifest, skipped) == 0,
        "second generation must apply zero changes");
    Check(generatedHost.SequenceEqual(framework.Files[ZeroEngineDemandSource.InitPath]) &&
        generatedScheduler.SequenceEqual(framework.Files[ZeroEngineDemandSource.SchedulerPath]) &&
        originalStaged.All(x => x.Value.SequenceEqual(staged[x.Key])), "repeat generation changed bytes");
    // Partial install: producer already generated but host and scheduler stock.
    var partial = new FixedZeroEngineBuild(files, "", "", "", "");
    var partialStage = new Dictionary<string, byte[]>(originalStaged);
    Check(ZeroEngineDemandSource.TryStage(root, ref partial, partialStage, new(), new(), new()) == 2,
        "partial installation must complete missing scheduler/host");
    Check(generatedHost.SequenceEqual(partial.Files[ZeroEngineDemandSource.InitPath]) &&
        generatedScheduler.SequenceEqual(partial.Files[ZeroEngineDemandSource.SchedulerPath]),
        "partial completion differs");
    Check(!ZeroEngineDemandSource.TryProducer(proximity.Replace(
        "zones[zoneCounter] = zone", "zones[zoneCounter] = zone\n    zones[999] = zone"),
        "Proximity", out _), "unproved extra registry mutation accepted");


    // Use the canonical root shared-provider shape and original module
    // readers. This is independent of the observer/dormancy contracts.
    var sharedGetterNames=new[]{"GetCameraSystem","GetTransactionSystem","GetWorkspotSystem","GetTimeSystem","GetScriptableSystemsContainer"};
    var sharedModule=privateSource?derived:"""
local DerivedState = {}
local Events
function DerivedState.Init(events)
    Events = events
end
function DerivedState.Update(player)
    local cam = Game.GetCameraSystem()
    local value = cam:GetActiveCameraForward()
    local ts = Game.GetTransactionSystem()
    local item = ts:GetItemInSlot(player)
    local ws = Game.GetWorkspotSystem()
    local active = ws:IsActorInWorkspot(player)
    local time = Game.GetTimeSystem()
    local clock = time:GetGameTime()
    local container = Game.GetScriptableSystemsContainer()
    return value,item,active,clock,container:Get("AnySystem")
end
return DerivedState
""";
    var sharedHost="""
local DerivedState = require("modules/DerivedState")
local Engine = {}
local State = {player=function() return TestPlayer end}
local function GetPlayer()
    return State.player()
end
local __gcetSharedProviderValues = {}
local __gcetScriptableSystemValues = {}
local __gcetSharedProviderEpoch = nil
local __gcetSharedProviderGetters = {
"""+ "\n"+string.Join("\n",SharedProviderCatalog.RuntimeProviders.Select(p=>
        "    "+p.Provider+" = function() return Game."+p.Getter+"() end,"))+ "\n"+"""
}
local function __gcetSyncSharedProviderEpoch()
    local player = GetPlayer()
    if __gcetSharedProviderEpoch ~= player then
        __gcetSharedProviderEpoch = player
        __gcetSharedProviderValues = {}
        __gcetScriptableSystemValues = {}
    end
end
local function __gcetGetSharedProvider(key)
    __gcetSyncSharedProviderEpoch()
    local value = __gcetSharedProviderValues[key]
    if value == nil then
        local getter = __gcetSharedProviderGetters[key]
        if getter ~= nil then
            value = getter()
            if value ~= nil then
                __gcetSharedProviderValues[key] = value
            end
        end
    end
    return value
end
"""+ "\n"+string.Join("\n",SharedProviderCatalog.RuntimeProviders.Select(p=>
        "function Engine."+p.Getter+"()\n    return __gcetGetSharedProvider(\""+p.Provider+"\")\nend"))+"\n"+"""
function Engine.Init(Events)
    DerivedState.Init(Events)
end
function Engine.Update(player)
    return DerivedState.Update(player)
end
return Engine
""";
    Check(ZeroEngineDemandSource.TrySharedDerivedProviders(sharedModule,sharedHost,out var sd,out var sh,out var sg)
        && sg.OrderBy(x=>x).SequenceEqual(sharedGetterNames.OrderBy(x=>x)), "shared derived stable getter proof");
    Check(ZeroEngineDemandSource.TrySharedDerivedProviders(sd,sh,out var sdRepeat,out var shRepeat,out _)&&
        sd==sdRepeat&&sh==shRepeat,"shared derived repeated generation");
    Check(ZeroEngineDemandSource.TrySharedDerivedProviders(sharedModule,sh,out var sdPartial,out var shPartial,out _)&&
        sd==sdPartial&&sh==shPartial,"shared derived host-only partial");
    Check(ZeroEngineDemandSource.TrySharedDerivedProviders(sd,sharedHost,out sdPartial,out shPartial,out _)&&
        sd==sdPartial&&sh==shPartial,"shared derived module-only partial");
    var sharedProviderDrift=sharedHost.Replace("return __gcetGetSharedProvider(\"CAMERA_SYSTEM\")","return Game.GetCameraSystem()");
    Check(ZeroEngineDemandSource.TrySharedDerivedProviders(sharedModule,sharedProviderDrift,out var sdDrift,out _,out var sgDrift)&&
        sgDrift.Length==4&&sdDrift.Contains("Game.GetCameraSystem()")&&!sgDrift.Contains("GetCameraSystem"),
        "independent provider refusal must not reject all other dependencies");
    Check(!ZeroEngineDemandSource.TrySharedDerivedProviders("local Game = {}\n"+sharedModule,sharedHost,out _,out _,out _),
        "shadowed Game refused");
    if(!privateSource)
    {
        File.WriteAllText(Path.Combine(root,"shared-stock-module.lua"),sharedModule);
        File.WriteAllText(Path.Combine(root,"shared-generated-module.lua"),sd);
        File.WriteAllText(Path.Combine(root,"shared-stock-host.lua"),sharedHost);
        File.WriteAllText(Path.Combine(root,"shared-generated-host.lua"),sh);
        var psi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
        psi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteSharedDerived.lua");
        psi.ArgumentList.Add(root);
        using var process=Process.Start(psi)!;process.WaitForExit();
        Check(process.ExitCode==0,"shared derived actual generated execution");
    }
    Console.WriteLine("DerivedState existing shared-provider dependencies: five stable families, independent refusal, partial and repeat PASS");

    // Actual canonical provider body, source-shaped original session hooks.
    var sessionHost=sharedHost.Replace("local __gcetSharedProviderValues = {}","""
local isSessionLoaded
local function UpdateFrame(delta)
    local okLive, liveLoaded = pcall(GameSession.IsLoaded)
    if okLive then
        isSessionLoaded = liveLoaded
    end
end
local __gcetSharedProviderValues = {}
""");
    sessionHost=sessionHost.Replace("return Engine", """
function Engine.GetScriptableSystem(name)
    __gcetSyncSharedProviderEpoch()
    local key = tostring(name or "")
    if key == "" then return nil end
    local value = __gcetScriptableSystemValues[key]
    if value ~= nil then return value end
    local container = Engine.GetScriptableSystemsContainer()
    if container == nil then return nil end
    local ok, resolved = pcall(function() return container:Get(CName.new(key)) end)
    if ok and resolved ~= nil then
        __gcetScriptableSystemValues[key] = resolved
        return resolved
    end
    return nil
end
local Events={PlayerInvalidated={subscribe=function(_,fn) Engine.Invalidate=fn end}}
GameSession.On(function(state)
    local loaded = state.isLoaded
    isSessionLoaded = loaded
end)
local sessionState = GameSession.GetState()
isSessionLoaded = sessionState.isLoaded
Events.PlayerInvalidated:subscribe(function()
    DerivedState.Reset()
end)
Engine.Tick=UpdateFrame
return Engine
""");
    Check(ZeroEngineDemandSource.TrySharedProviderLifecycle(sessionHost,out var sessionNext)&&sessionNext!=sessionHost,
        "canonical shared-provider session lifetime");
    Check(ZeroEngineDemandSource.TrySharedProviderLifecycle(sessionNext,out var sessionAgain)&&sessionAgain==sessionNext,
        "provider session repeat");
    Check(ZeroEngineDemandSource.TrySharedProviderLifecycle(sessionNext.Replace("\r\n","\n").Replace(
        "    Engine.__gcetObserveProviderSession(loaded)\n",""),out var sessionPartial)&&sessionPartial==sessionNext.Replace("\r\n","\n"),
        "provider session missing event transition");
    var missingReset=System.Text.RegularExpressions.Regex.Replace(sessionNext.Replace("\r\n","\n"),
        @"(?m)^function Engine\.__gcetResetSharedProviderCaches\(\)\n    __gcetSharedProviderEpoch = nil\n    __gcetSharedProviderValues = \{\}\n    __gcetScriptableSystemValues = \{\}\nend\n\n","");
    Check(missingReset!=sessionNext.Replace("\r\n","\n")&&ZeroEngineDemandSource.TrySharedProviderLifecycle(missingReset,out var recoveredReset)&&
        recoveredReset==sessionNext.Replace("\r\n","\n"),"provider session missing reset function");
    Check(!ZeroEngineDemandSource.TrySharedProviderLifecycle(sessionHost.Replace(
        "value = getter()","value = customGetter()"),out _),"unknown provider getter must remain stock");
    Check(!ZeroEngineDemandSource.TrySharedProviderLifecycle(sessionNext.Replace(
        "function Engine.__gcetResetSharedProviderCaches()","function Engine.__gcetResetSharedProviderCaches()\n    customReset()"),out _),
        "unknown reset must refuse rather than duplicate");
    // Former root-only generated host can acquire the known compatibility
    // namespace. Check the full source composition, including partial roots.
    var canonicalSession=sessionHost.Replace("\r\n","\n");
    var providerStart=canonicalSession.IndexOf("local __gcetSharedProviderValues = {}",StringComparison.Ordinal);
    var providerEnd=canonicalSession.IndexOf("function Engine.Init(Events)",providerStart,StringComparison.Ordinal);
    var namedStart=canonicalSession.IndexOf("function Engine.GetScriptableSystem(name)",StringComparison.Ordinal);
    var namedEnd=canonicalSession.IndexOf("local Events=",namedStart,StringComparison.Ordinal);
    var adapterSource=canonicalSession[providerStart..providerEnd]+canonicalSession[namedStart..namedEnd];
    adapterSource=adapterSource.Replace("Engine.","__gcetApi.",StringComparison.Ordinal)
        .Replace("local player = GetPlayer()","local player = __gcetApi.GetPlayer()",StringComparison.Ordinal);
    var adapterBlock="\n-- G-CET host compatibility bridge v1\ndo\n    local __gcetApi = {}\n    Engine.GCET = __gcetApi\n"+
        string.Join("\n",adapterSource.TrimEnd('\n').Split('\n').Select(line=>"        "+line))+"\nend\n";
    string AddAdapter(string host)
    {
        host=host.Replace("\r\n","\n");
        var at=host.LastIndexOf("return Engine",StringComparison.Ordinal);
        if(at<0)throw new InvalidOperationException("provider fixture root return missing");
        return host.Insert(at,adapterBlock);
    }
    Check(ZeroEngineDemandSource.TrySharedProviderLifecycle(AddAdapter(sessionHost),out var dualNext)&&
        dualNext.Contains("function __gcetApi.__gcetResetSharedProviderCaches()",StringComparison.Ordinal)&&
        dualNext.Contains("api.__gcetResetSharedProviderCaches()",StringComparison.Ordinal),
        "root and compatibility cache lifetime");
    Check(ZeroEngineDemandSource.TrySharedProviderLifecycle(AddAdapter(sessionNext),out var dualPartial)&&dualPartial==dualNext,
        "root-only partial gains adapter without duplicate reset");
    Check(ZeroEngineDemandSource.TrySharedProviderLifecycle(dualNext,out var dualRepeat)&&dualRepeat==dualNext,
        "dual namespace repeat");
    if(!privateSource){
        File.WriteAllText(Path.Combine(root,"provider-session.lua"),sessionNext);
        var providerPsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
        providerPsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteProviderLifecycle.lua");
        providerPsi.ArgumentList.Add(root);
        using var providerProc=Process.Start(providerPsi)!;providerProc.WaitForExit();
        Check(providerProc.ExitCode==0,"shared provider session execution");
    }
    Console.WriteLine("Shared provider session invalidation: source, partial, repeat and drift PASS");


    // Actual pose closure from the installed host. Verify generated output
    // keeps the receiver, call order, nil handling and failure barrier.
    var poseFixture="""
local function UpdateFrame(delta)
    local player = CurrentPlayer()
    if not player then return end
    local posOriOk, pos, orientation, yaw = pcall(function()
        local p = player:GetWorldPosition()
        local o = player:GetWorldOrientation()
        return p, o, o:ToEulerAngles().yaw
    end)
    if not posOriOk then return end
    State.pos = pos
    State.orientation = orientation
    State.yaw = yaw
    downstream()
end
return UpdateFrame
""";
    Check(ZeroEngineDemandSource.TryPoseReader(poseFixture,out var poseNext)&&poseNext!=poseFixture,
        "pose protected function reuse");
    Check(ZeroEngineDemandSource.TryPoseReader(poseNext,out var poseRepeat)&&poseRepeat==poseNext,
        "pose reader idempotence");
    var poseCustom="-- compatible header\r\n"+poseFixture.Replace("\n","\r\n")+"\r\n-- custom trailer";
    Check(ZeroEngineDemandSource.TryPoseReader(poseCustom,out var poseCustomNext)&&
        poseCustomNext.StartsWith("-- compatible header\r\n")&&
        poseCustomNext.EndsWith("\r\n-- custom trailer"),"pose custom source preservation");
    Check(!ZeroEngineDemandSource.TryPoseReader(poseFixture.Replace(
        "return p, o, o:ToEulerAngles().yaw","audit(player)\n        return p, o, o:ToEulerAngles().yaw"),out _),
        "unknown closure body must remain original");
    Check(ZeroEngineDemandSource.TryComposePoseReader(poseFixture,true,out var typedPose)&&
        typedPose.Contains("Game.GCETReadPlayerPoseV1"),"typed pose source admission");
    Check(ZeroEngineDemandSource.TryComposePoseReader(typedPose,true,out var typedRepeat)&&
        typedRepeat==typedPose,"typed pose repeat");
    Check(ZeroEngineDemandSource.TryComposePoseReader(typedPose,false,out var typedDowngrade)&&
        typedDowngrade==poseNext,"older native bundle must restore original protected helper");
    Check(ZeroEngineDemandSource.TryComposePoseReader(typedPose.Replace(
        "local __gcetPoseNative, __gcetPoseNativeResolved -- G-CET typed pose backend v1\n",""),
        true,out var typedPartial)&&typedPartial==typedPose,"missing typed pose state must converge");
    Check(!ZeroEngineDemandSource.TryComposePoseReader(typedPose.Replace(
        "if status ~= 0 then","if status ~= 2 then"),true,out _),"changed native status protocol refused");
    // The optional spatial path is atomic with the exact existing pose
    // owner and registration source. It cannot silently enable the new
    // freshness contract on the old production DLL.
    var spatialSource=File.ReadAllText("resolver-runtime/0-Engine/modules/GCETNativeLocationHost.lua");
    Check(ZeroEngineDemandSource.TryNativeLocationRegistrationDemand(spatialSource,out var locationDemand)&&locationDemand!=spatialSource,
        "actual native location registration demand admission");
    Check(ZeroEngineDemandSource.TryNativeLocationRegistrationDemand(locationDemand,out var locationRepeat)&&locationRepeat==locationDemand,
        "native location registration demand repeat");
    Check(ZeroEngineDemandSource.TryNativeLocationRegistrationDemand(locationDemand.Replace(
        "    __gcetFinishLocationAttach(self)\n    return false","    return false"),out var locationPartial)&&locationPartial==locationDemand,
        "native location partial failure cleanup completion");
    Check(!ZeroEngineDemandSource.TryNativeLocationRegistrationDemand(spatialSource.Replace(
        "self.job=job","self.job=foreignJob"),out _),"unknown scheduler handle ownership refused");
    Check(ZeroEngineDemandSource.TrySharedSpatialHost(locationDemand,true,out var locationShared)&&
        ZeroEngineDemandSource.TryNativeLocationRegistrationDemand(locationShared,out var locationSharedRepeat)&&
        locationSharedRepeat==locationShared,"coarse spatial and registration cleanup compose");
    Check(ZeroEngineDemandSource.TrySharedSpatialHost(spatialSource,true,out var spatialShared)&&
          spatialShared.Contains("GCETStaticArmCoarseSpatialV1"),"actual spatial host registration admission");
    Check(ZeroEngineDemandSource.TrySharedSpatialHost(spatialShared,true,out var spatialRepeat)&&
          spatialRepeat==spatialShared,"coarse spatial repeat");
    Check(ZeroEngineDemandSource.TrySharedSpatialHost(spatialShared,false,out var spatialExact)&&
          spatialExact==spatialSource,"older DLL exact host restoration");
    Check(!ZeroEngineDemandSource.TrySharedSpatialHost(spatialSource.Replace(
        "assert(native(\"GCETStaticArm\",key,true)==true)","customArm(key)"),true,out _),
        "foreign registration drift must not authorize coarse freshness");
    Check(!ZeroEngineDemandSource.TrySharedSpatialHost(spatialSource.Replace(
        "Schedule.Every(0.10,","Schedule.Every(0.01,"),true,out _),
        "changed host delivery cadence must keep exact acquisition");
    var commentSpatial="--[=[\nassert(native(\"GCETStaticArm\",key,true)==true)\n]=]\n"+spatialSource;
    Check(ZeroEngineDemandSource.TrySharedSpatialHost(commentSpatial,true,out var commentShared)&&
        commentShared.StartsWith(commentSpatial[..commentSpatial.IndexOf("-- G-CET native location host",StringComparison.Ordinal)],StringComparison.Ordinal),
        "comment-only fake arm must not affect source admission or be rewritten");
    Check(ZeroEngineDemandSource.TryComposePoseReader(typedPose,true,out var sharedPose,true)&&
          sharedPose.Contains("Game.GCETReadSharedPlayerPoseV1"),"shared typed backend upgrade");
    Check(ZeroEngineDemandSource.TryComposePoseReader(sharedPose,true,out var sharedPoseAgain,true)&&
          sharedPoseAgain==sharedPose,"shared typed backend repeat");
    Check(ZeroEngineDemandSource.TryComposePoseReader(sharedPose,true,out var sharedPoseOld)&&
          sharedPoseOld==typedPose,"exact typed fallback");
    var sharedFramework=new FixedZeroEngineBuild(new Dictionary<string,byte[]>{
        [ZeroEngineDemandSource.InitPath]=Encoding.UTF8.GetBytes(poseFixture),
        [ZeroEngineDemandSource.SpatialHostPath]=Encoding.UTF8.GetBytes(spatialSource)
    },"","","","");
    Check(ZeroEngineDemandSource.TryStagePose(ref sharedFramework,new(),true,true)==1&&
          Encoding.UTF8.GetString(sharedFramework.Files[ZeroEngineDemandSource.InitPath]).Contains("GCETReadSharedPlayerPoseV1")&&
          Encoding.UTF8.GetString(sharedFramework.Files[ZeroEngineDemandSource.SpatialHostPath]).Contains("GCETStaticArmCoarseSpatialV1"),
          "atomic host/producer generation");
    Check(ZeroEngineDemandSource.TryStagePose(ref sharedFramework,new(),true,true)==0,
        "atomic shared production idempotence");
    Check(ZeroEngineDemandSource.TryStagePose(ref sharedFramework,new(),true,false)==1&&
          ZeroEngineDemandSource.TryStagePose(ref sharedFramework,new(),true,false)==0,
          "atomic exact fallback repeat");
    // Existing exact ordered native producer, two independent consumer
    // shapes: full EulerAngles and yaw scalar. No frame-cache substitution.
    var exactPairsSource="""
local function first()
    local player = Game.GetPlayer()
    if not player then return end
    local pos = player:GetWorldPosition()
    local angles = player:GetWorldOrientation():ToEulerAngles()
    return pos, angles
end
local function second()
    local p = Game.GetPlayer()
    if not p then return end
    if condition() then
        unchanged(p)
    end
    local where = p:GetWorldPosition()
    local yaw = p:GetWorldOrientation():ToEulerAngles().yaw
    return where, yaw
end
return {first, second}
""".Replace("\r\n","\n",StringComparison.Ordinal);
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(exactPairsSource,true,out var exactPairsGenerated,out var exactPairCount)&&exactPairCount==2,
        "two exact position/Euler source consumers");
    Check(!ZeroEngineDemandSource.TryComposeExactPosePairs(exactPairsGenerated,true,out var exactPairsRepeat,out _)&&exactPairsRepeat==exactPairsGenerated,
        "exact pair source idempotence");
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(exactPairsGenerated,false,out var exactPairsOld,out _)&&exactPairsOld==exactPairsSource,
        "older ABI restores original exact pose source");
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(exactPairsGenerated.Replace(
        "local __gcetExactEulerNative, __gcetExactEulerResolved\n",""),true,out var exactPairsPartial,out _)&&exactPairsPartial==exactPairsGenerated,
        "missing exact pair helper state completed");
    var partialPairSource=exactPairsGenerated.Replace(
        "local where, yaw = __gcetReadExactPositionEuler(p, true)",
        "local where = p:GetWorldPosition()\n    local yaw = p:GetWorldOrientation():ToEulerAngles().yaw");
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(partialPairSource,true,out var partialPairOutput,out _)&&partialPairOutput==exactPairsGenerated,
        "mixed original and generated exact pairs completed");
    var mixedPairSource=exactPairsSource.Replace("unchanged(p)","p = replacement()");
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(mixedPairSource,true,out var mixedPairOutput,out var mixedPairCount)&&
        mixedPairCount==1&&mixedPairOutput.Contains("local where = p:GetWorldPosition()"),
        "receiver rebound: only affected pose pair retained");
    var closurePairSource=exactPairsSource.Replace("unchanged(p)","register(function() p = replacement() end)");
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(closurePairSource,true,out var closurePairOutput,out var closurePairCount)&&closurePairCount==1&&
        closurePairOutput.Contains("local where = p:GetWorldPosition()"),
        "closure capture refuses only affected pair");
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(exactPairsSource.Replace("\n","\r\n"),true,out var exactPairsCrlf,out var exactPairsCrlfCount)&&
        exactPairsCrlfCount==2&&!ZeroEngineDemandSource.TryComposeExactPosePairs(exactPairsCrlf,true,out _,out _),
        "exact pair CRLF source convergence");
    Check(!ZeroEngineDemandSource.TryComposeExactPosePairs("local Game = foreign\n"+exactPairsSource,true,out _,out _),
        "shadowed Game facade cannot authorize typed pose");
    var inlinePlayerProvider="""
-- G-CET shared provider: PLAYER
local __gcetSharedPlayerProvider = nil
local function __gcetGetPlayer()
    return Game.GetPlayer()
end
""".Replace("\r\n","\n",StringComparison.Ordinal);
    var composedPair=inlinePlayerProvider+"\n\n"+exactPairsGenerated.Replace(
        "Game.GetPlayer()","__gcetGetPlayer()",StringComparison.Ordinal);
    Check(!ZeroEngineDemandSource.TryComposeExactPosePairs(composedPair,true,out var composedPairRepeat,out _,inlinePlayerProvider)&&
        composedPairRepeat==composedPair,"downstream inline shared provider must not undo generated pose pairs");
    var formattedProvider=inlinePlayerProvider.Replace("    return","\n  return",StringComparison.Ordinal);
    var formattedPair=formattedProvider+"\n\n"+exactPairsGenerated.Replace("Game.GetPlayer()","__gcetGetPlayer()",StringComparison.Ordinal);
    Check(!ZeroEngineDemandSource.TryComposeExactPosePairs(formattedPair,true,out var formattedPairRepeat,out _,inlinePlayerProvider)&&
        formattedPairRepeat==formattedPair,"compatible provider formatting must converge");
    var fakeInline="--[=[\n"+inlinePlayerProvider+"\n]=]\n"+
        "local function __gcetGetPlayer() return foreign() end\n"+
        exactPairsSource.Replace("Game.GetPlayer()","__gcetGetPlayer()",StringComparison.Ordinal);
    Check(!ZeroEngineDemandSource.TryComposeExactPosePairs(fakeInline,true,out _,out _,inlinePlayerProvider),
        "commented provider cannot authorize a foreign local getter");
    var coordinatedPair="-- G-CET generic native reads coordinator:v1 GCET_Semantic/example\n"+
        "local __gcetNativeReads = require(\"GCET_Semantic/example\")\n"+
        exactPairsGenerated.Replace("Game.GetPlayer()","__gcetNativeReads.GetPlayer()",StringComparison.Ordinal);
    Check(!ZeroEngineDemandSource.TryComposeExactPosePairs(coordinatedPair,true,out var coordinatedPairRepeat,out _,null,true)&&
        coordinatedPairRepeat==coordinatedPair,"verified downstream coordinator must retain existing pose pairs");
    var coordinatorStock=coordinatedPair[..coordinatedPair.IndexOf("local __gcetExactEulerNative",StringComparison.Ordinal)]+
        exactPairsSource.Replace("Game.GetPlayer()","__gcetNativeReads.GetPlayer()",StringComparison.Ordinal);
    Check(ZeroEngineDemandSource.TryComposeExactPosePairs(coordinatorStock,true,out var coordinatorGenerated,out var coordinatorPairs,null,true)&&
        coordinatorPairs==2&&coordinatorGenerated==coordinatedPair,"known coordinator stays at its original prefix");
        var psmNames=new[]{"locomotion","locomotionDetailed","upperBody","weapon","vision","swimming","sceneTier","takedown","landing","bodyCarrying","carrying","mounted"};
    var psmKeys=new[]{"Locomotion","LocomotionDetailed","UpperBody","Weapon","Vision","Swimming","SceneTier","Takedown","Landing","BodyCarrying","Carrying","MountedToVehicle"};
    var psmReadSource="local BlackboardCache = {}\nlocal playerBB\nlocal __gcetPsmFields = {}\n"+
        string.Join("\n",psmKeys.Select((key,i)=>"__gcetPsmFields."+key+" = "+(i+1)))+
        "\nfunction BlackboardCache.Update(player)\n    if not playerBB then return end\n"+
        string.Join("\n",psmNames.Select((name,i)=>"    local "+name+" = playerBB:"+(i==11?"GetBool":"GetInt")+"(__gcetPsmFields."+psmKeys[i]+")"))+
        "\n    publish("+string.Join(", ",psmNames)+")\nend\nfunction BlackboardCache.Attach(board) playerBB = board end\n"+
        "function BlackboardCache.Reset() playerBB = nil end\n"+
        "function BlackboardCache.Init() __gcetPsmFields = {"+
        string.Join(", ",psmKeys.Select((key,i)=>key+" = "+(i+21)))+"} end\nreturn BlackboardCache\n";
    Check(ZeroEngineDemandSource.TryComposeLegacyPsmBatchFixture(psmReadSource,out var psmBatchGenerated,out var psmBatchCount)&&psmBatchCount==3,
        "12 PSM state reads -> three exact ordered crossings");
    Check(ZeroEngineDemandSource.TryNormalizePsmBatches(psmBatchGenerated,out var psmPlain)&&psmPlain==psmReadSource&&
        ZeroEngineDemandSource.TryComposeLegacyPsmBatchFixture(psmPlain,out var psmAgain,out _)&&psmAgain==psmBatchGenerated,
        "PSM old ABI and repeated composition");
    Check(ZeroEngineDemandSource.TryNormalizePsmBatches(psmBatchGenerated.Replace(
        "local __gcetPsmBatchInt, __gcetPsmBatchBool, __gcetPsmBatchResolved\n",""),out var psmPartial)&&
        psmPartial==psmReadSource,"PSM partial state normalization");
    var psmDrift=psmReadSource.Replace("    local weapon =","    observeIntermediate()\n    local weapon =");
    Check(ZeroEngineDemandSource.TryComposeLegacyPsmBatchFixture(psmDrift,out var psmDriftOutput,out var psmDriftBatches)&&
        psmDriftBatches==2&&psmDriftOutput.Contains("observeIntermediate()"),
        "PSM groups never cross intervening gameplay calls");
    if(privateSource)
    {
        var poseStock=new FixedZeroEngineBuild(new Dictionary<string,byte[]>{
            [ZeroEngineDemandSource.InitPath]=generatedHost
        },"","","","");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new())==1,
            "actual private host pose stage");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new())==0,
            "actual private pose stage repeat");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new(),true)==1,
            "actual private typed pose stage");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new(),true)==0,
            "actual private typed pose repeat");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new(),false)==1&&
            ZeroEngineDemandSource.TryStagePose(ref poseStock,new(),false)==0,
            "actual private old-bundle downgrade");
        var actualSharedFiles=poseStock.Files.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        actualSharedFiles[ZeroEngineDemandSource.SpatialHostPath]=Encoding.UTF8.GetBytes(spatialSource);
        poseStock=poseStock with{Files=actualSharedFiles};
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new(),true,true)==1&&
            Encoding.UTF8.GetString(poseStock.Files[ZeroEngineDemandSource.InitPath]).Contains("GCETReadSharedPlayerPoseV1")&&
            Encoding.UTF8.GetString(poseStock.Files[ZeroEngineDemandSource.SpatialHostPath]).Contains("GCETStaticArmCoarseSpatialV1"),
            "actual private untouched host and shared spatial registration compose");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new(),true,true)==0,
            "actual private shared production repeat");
        var modRoot=Directory.GetParent(args[1])!.FullName;
        var exactOwners=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int exactPrivateSites=0;
        foreach(var file in Directory.EnumerateFiles(modRoot,"*.lua",SearchOption.AllDirectories))
        {
            var rel=Path.GetRelativePath(modRoot,file).Replace('\\','/');
            var owner=rel.Split('/')[0];
            if(owner.Equals("0-Engine",StringComparison.OrdinalIgnoreCase)||owner.StartsWith("G-CET",StringComparison.OrdinalIgnoreCase)||
                new FileInfo(file).Length>2_000_000)continue;
            var stock=File.ReadAllText(file);
            if(!ZeroEngineDemandSource.TryComposeExactPosePairs(stock,true,out var transformed,out var count))continue;
            if(count==0)continue;
            exactOwners.Add(owner);exactPrivateSites+=count;
            Check(!ZeroEngineDemandSource.TryComposeExactPosePairs(transformed,true,out var repeated,out _)&&repeated==transformed,
                "actual private exact pair repeat: "+rel);
        }
        Check(exactOwners.Count>=2&&exactPrivateSites>=3,
            "generic exact pair actual installed multi-owner admission: "+exactOwners.Count+"/"+exactPrivateSites);
        Console.WriteLine($"Actual untouched collection exact position/Euler: {exactPrivateSites} source-proven pairs across {exactOwners.Count} owners, repeated source zero changes. Engine acquisition count unchanged.");

    }
    else
    {
        File.WriteAllText(Path.Combine(root,"pose-original.lua"),poseFixture);
        File.WriteAllText(Path.Combine(root,"pose-generated.lua"),poseNext);
        File.WriteAllText(Path.Combine(root,"pose-typed.lua"),typedPose);
        File.WriteAllText(Path.Combine(root,"pose-shared.lua"),sharedPose);
        File.WriteAllText(Path.Combine(root,"spatial-shared.lua"),spatialShared);
        File.WriteAllText(Path.Combine(root,"spatial-location-demand.lua"),locationShared);
        File.WriteAllText(Path.Combine(root,"exact-pairs-original.lua"),exactPairsSource);
        File.WriteAllText(Path.Combine(root,"exact-pairs-generated.lua"),exactPairsGenerated);
        File.WriteAllText(Path.Combine(root,"psm-original.lua"),psmReadSource);
        File.WriteAllText(Path.Combine(root,"psm-generated.lua"),psmBatchGenerated);
        File.WriteAllText(Path.Combine(root,"psm-restored.lua"),psmPlain);
        var posePsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
        posePsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecutePose.lua");
        posePsi.ArgumentList.Add(root);
        using var poseProcess=Process.Start(posePsi)!;poseProcess.WaitForExit();
        Check(poseProcess.ExitCode==0,"generated pose execution failed");
    }


    // The host's original frame registry is another consumer of the shared
    // successful-pose phase. Exercise actual registry-owned demand, not an
    // independent timer or synthetic callback queue.
    var frameEmitterHost="""
local EventEmitter = require('modules/EventEmitter')
local Engine = {}
local frameEmitters = {}
local currentFrame = 0
local function UpdateFrame(delta)
    currentFrame = currentFrame + 1
    for interval, emitter in pairs(frameEmitters) do
        if currentFrame % interval == 0 then
            emitter:trigger(currentFrame)
        end
    end
end
function Engine.OnFrame(interval, fn, source)
    if type(interval) ~= "number" or interval < 1 then
        return { unsubscribe = function() end }
    end
    interval = math.floor(interval)
    if not frameEmitters[interval] then
        frameEmitters[interval] = EventEmitter.new()
    end
    local innerHandle = frameEmitters[interval]:subscribe(fn, source)
    return {
        unsubscribe = function()
            innerHandle.unsubscribe()
            if frameEmitters[interval] and frameEmitters[interval]:getListenerCount() == 0 then
                frameEmitters[interval] = nil
            end
        end
    }
end
Engine.UpdateFrame=UpdateFrame
return Engine
""";
    frameEmitterHost=frameEmitterHost.Replace("\r\n","\n",StringComparison.Ordinal);
    Check(ZeroEngineDemandSource.TryFrameEmitters(frameEmitterHost,out var frameEmitterNext)&&
        frameEmitterNext!=frameEmitterHost,"frame emitter producer demand");
    Check(ZeroEngineDemandSource.TryFrameEmitters(frameEmitterNext,out var frameEmitterRepeat)&&
        frameEmitterRepeat==frameEmitterNext,"frame emitter repeat");
    foreach(var missing in new[]{"state","register","remove","dispatch"})
    {
        // Strip only one complete generated fragment from the production
        // result. The remaining source must be completed byte-identically.
        var incomplete=missing switch {
            "state"=>System.Text.RegularExpressions.Regex.Replace(frameEmitterNext,
                @"(?m)^-- G-CET frame emitter demand v1\r?\nlocal __gcetFrameDemandHandles = \{\}\r?\nlocal __gcetFrameDemandReady = [^\r\n]+\r?\n    type\(__gcetDemand.TrackFrame\)[^\r\n]+\r?\n",""),
            "register"=>System.Text.RegularExpressions.Regex.Replace(frameEmitterNext,
                @"(?m)^        if __gcetFrameDemandReady then\r?\n            __gcetFrameDemandHandles\[interval\] = [^\r\n]+\r?\n        end\r?\n",""),
            "remove"=>System.Text.RegularExpressions.Regex.Replace(frameEmitterNext,
                @"(?m)^                local demandHandle = __gcetFrameDemandHandles\[interval\]\r?\n                if demandHandle then\r?\n                    demandHandle.Cancel\(\)\r?\n                    __gcetFrameDemandHandles\[interval\] = nil\r?\n                end\r?\n",""),
            _=>frameEmitterNext.Replace(
                "    if not __gcetFrameDemandReady or __gcetDemand.IsDue(\"FrameEmitters\", currentFrame) then\n        for interval, emitter in pairs(frameEmitters) do\n            if currentFrame % interval == 0 then\n                emitter:trigger(currentFrame)\n            end\n        end\n    end",
                "    for interval, emitter in pairs(frameEmitters) do\n        if currentFrame % interval == 0 then\n            emitter:trigger(currentFrame)\n        end\n    end")
        };
        Check(incomplete!=frameEmitterNext&&ZeroEngineDemandSource.TryFrameEmitters(incomplete,out var completed)&&
            completed==frameEmitterNext,"frame emitter partial completion: "+missing);
    }
    var frameEmitterCustom="-- custom header\r\n"+frameEmitterHost.Replace("\n","\r\n")+"\r\n-- custom trailer";
    Check(ZeroEngineDemandSource.TryFrameEmitters(frameEmitterCustom,out var frameEmitterCustomNext)&&
        frameEmitterCustomNext.StartsWith("-- custom header\r\n")&&
        frameEmitterCustomNext.EndsWith("\r\n-- custom trailer"),"frame emitter compatible source");
    Check(!ZeroEngineDemandSource.TryFrameEmitters(frameEmitterHost.Replace(
        "local frameEmitters = {}","local frameEmitters = {}\nlocal external = frameEmitters"),out _),
        "escaped frame emitter registry must remain unchanged");
    if(privateSource)
    {
        Check(ZeroEngineDemandSource.TryFrameEmitters(Encoding.UTF8.GetString(generatedHost),out var actualFrameHost),
            "private stock frame emitter ownership");
        Check(ZeroEngineDemandSource.TryFrameEmitters(actualFrameHost,out var actualFrameRepeat)&&
            actualFrameRepeat==actualFrameHost,"private frame emitter idempotence");
    }
    else
    {
        var frameRoot=Path.Combine(root,"frame");
        var frameModules=Path.Combine(frameRoot,"modules","G-CET");
        Directory.CreateDirectory(frameModules);
        File.WriteAllText(Path.Combine(frameRoot,"original.lua"),frameEmitterHost);
        File.WriteAllText(Path.Combine(frameRoot,"generated.lua"),frameEmitterNext);
        File.WriteAllBytes(Path.Combine(frameModules,"Scheduler.lua"),generatedScheduler);
        File.WriteAllText(Path.Combine(frameRoot,"old-scheduler.lua"),scheduler);
        var framePsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
        framePsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteFrameEmitters.lua");
        framePsi.ArgumentList.Add(frameRoot);
        using var frameProcess=Process.Start(framePsi)!;frameProcess.WaitForExit();
        Check(frameProcess.ExitCode==0,"generated frame emitter execution");
    }


    // Real author lifecycle body with all transition paths. Exercise the
    // source generator against the live module as well as generated Lua.
    var playerLifecycle=File.ReadAllText("tests/ZeroEngineDemandContract/PlayerLifecycleFixture.lua");
    var playerHost=File.ReadAllText("tests/ZeroEngineDemandContract/PlayerLifecycleHostFixture.lua");
    if(privateSource){
        playerLifecycle=lifecycle;
        playerHost=host;
    }
    Check(ZeroEngineDemandSource.TryLifecyclePlayerReuse(playerLifecycle,out var playerLifecycleNext)&&
        playerLifecycleNext!=playerLifecycle,"same-phase lifecycle observation admission");
    Check(ZeroEngineDemandSource.TryLifecyclePlayerHost(playerHost,out var playerHostNext)&&
        playerHostNext!=playerHost,"same-phase host observation admission");
    Check(ZeroEngineDemandSource.TryLifecyclePlayerReuse(playerLifecycleNext,out var playerLifecycleRepeat)&&
        playerLifecycleRepeat==playerLifecycleNext,"same-phase lifecycle repeat");
    Check(ZeroEngineDemandSource.TryLifecyclePlayerHost(playerHostNext,out var playerHostRepeat)&&
        playerHostRepeat==playerHostNext,"same-phase host repeat");
    Check(!ZeroEngineDemandSource.TryLifecyclePlayerReuse(playerLifecycle.Replace(
        "failFrames = 0  -- valid frame, reset counter","failFrames = 0  -- valid frame, reset counter\n    externalCallback()"),out _),
        "unproven callback path must not reuse player observation");
    if(!privateSource){
        var playerRoot=Path.Combine(root,"player-reuse");
        Directory.CreateDirectory(playerRoot);
        File.WriteAllText(Path.Combine(playerRoot,"stock-module.lua"),playerLifecycle);
        File.WriteAllText(Path.Combine(playerRoot,"stock-host.lua"),playerHost);
        File.WriteAllText(Path.Combine(playerRoot,"module.lua"),playerLifecycleNext);
        File.WriteAllText(Path.Combine(playerRoot,"host.lua"),playerHostNext);
        var playerPsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
        playerPsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecutePlayerLifecycle.lua");
        playerPsi.ArgumentList.Add(playerRoot);
        using var playerProcess=Process.Start(playerPsi)!;playerProcess.WaitForExit();
        Check(playerProcess.ExitCode==0,"same-phase player observation execution");
    }

    // Exercise the actual generated Cron lane, not a parallel timer.
    var cronSource=File.ReadAllText(privateSource?Path.Combine(Path.GetFullPath(args[1]),"external","Cron.lua"):
        "tests/ZeroEngineDemandContract/CronFixture.lua");
    var cronHostSource=privateSource?host:File.ReadAllText("tests/ZeroEngineDemandContract/CronHostFixture.lua");
    Check(ZeroEngineDemandSource.TryCronProducer(cronSource,out var cronGenerated)&&cronSource!=cronGenerated,"Cron source ownership");
    Check(ZeroEngineDemandSource.TryCronHost(cronHostSource,out var cronHostGenerated)&&cronHostSource!=cronHostGenerated,"Cron host ownership");
    Check(ZeroEngineDemandSource.TryCronProducer(cronGenerated,out var cronAgain)&&cronAgain==cronGenerated&&
        ZeroEngineDemandSource.TryCronHost(cronHostGenerated,out var cronHostAgain)&&cronHostAgain==cronHostGenerated,"Cron source repeat");
    foreach(var eol in new[]{"\n","\r\n"})
    {
        string CronLines(string text)=>text.Replace("\r\n","\n").Replace("\n",eol);
        var complete=CronLines(cronGenerated);
        var partialCron=complete.Replace(CronLines("    table.insert(timers, timer)\n    __gcetCronActive = __gcetCronActive + 1\n    __gcetCronSync()"),
            CronLines("    table.insert(timers, timer)"));
        Check(partialCron!=complete&&ZeroEngineDemandSource.TryCronProducer(partialCron,out var cronCompleted)&&cronCompleted==complete,
            "Cron partial completion and EOL compatibility");
    }
    Check(!ZeroEngineDemandSource.TryCronProducer(cronSource.Replace("return Cron","external(timers)\nreturn Cron"),out _),"Cron registry escape must refuse");
    Check(!ZeroEngineDemandSource.TryCronProducer(cronSource.Replace("timer.delay = timer.delay - delta","timer.delay = timer.delay - delta; timer.active = false"),out _),
        "Cron unknown active-state mutation must refuse");
    if(!privateSource)
    {
        var cronRoot=Path.Combine(root,"cron");
        Directory.CreateDirectory(Path.Combine(cronRoot,"external"));
        Directory.CreateDirectory(Path.Combine(cronRoot,"modules","G-CET"));
        File.WriteAllText(Path.Combine(cronRoot,"external","Cron.lua"),cronGenerated);
        File.WriteAllText(Path.Combine(cronRoot,"init.lua"),cronHostGenerated);
        File.WriteAllText(Path.Combine(cronRoot,"stock-cron.lua"),cronSource);
        File.WriteAllText(Path.Combine(cronRoot,"stock-host.lua"),cronHostSource);
        File.WriteAllBytes(Path.Combine(cronRoot,"modules","G-CET","Scheduler.lua"),generatedScheduler);
        var cronPsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
        cronPsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteCron.lua");
        cronPsi.ArgumentList.Add(cronRoot);
        using var cronProcess=Process.Start(cronPsi)!;cronProcess.WaitForExit();
        Check(cronProcess.ExitCode==0,"Cron actual generated cadence");
    }

    // Keep the existing spatial fixture independent; exercise the new
    // scalar transformation using the same production generator stage.
    File.WriteAllText(Path.Combine(moduleDir, "DerivedState.lua"), derived);
    File.WriteAllText(Path.Combine(moduleDir, "Lifecycle.lua"), lifecycle);
    Check(ZeroEngineDemandSource.TryDerivedProducer(derived, out _), "actual camera producer proof");
    Check(ZeroEngineDemandSource.TryDerivedHost(derivedHost, out _), "actual scalar host proof");
    Check(ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle, out _), "actual raw Lifecycle ownership proof");
    Check(!ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle.Replace("State = state", "State = state\n    externalState = State"), out _), "unproved raw Lifecycle escape accepted");
    // Stock production must not add historical demand machinery.
    var scalarFramework = new FixedZeroEngineBuild(new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(derivedHost),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(scheduler)
    }, "", "", "", "");
    var scalarStage = new Dictionary<string, byte[]>();
    var scalarCount = ZeroEngineDemandSource.TryStage(root, ref scalarFramework, scalarStage, new(), new(), new());
    Check(scalarCount == (privateSource ? 2 : 0), "stock historical path must not create sample slots: " + scalarCount);
    Check(!scalarStage.ContainsKey("0-Engine/modules/DerivedState.lua")&&
        !Encoding.UTF8.GetString(scalarFramework.Files[ZeroEngineDemandSource.SchedulerPath]).Contains("function demand.DefineSample("),
        "stock generation added redundant historical layer");
    // Generate historical overlays using the original source templates,
    // solely to test migration and mixed-version execution.
    Check(ZeroEngineDemandSource.TryDerivedProducer(derived,out var historicalModule),"historical module fixture");
    Check(ZeroEngineDemandSource.TryDerivedHost(derivedHost,out var historicalScalarHost),"historical host fixture");
    Check(ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle,out var historicalLifecycle),"historical lifecycle fixture");
    Check(ZeroEngineDemandSource.TrySampleController(Encoding.UTF8.GetString(generatedScheduler),out var historicalScheduler),"historical scheduler fixture");
    var scalarHost=Encoding.UTF8.GetBytes(historicalScalarHost);
    var scalarScheduler=Encoding.UTF8.GetBytes(historicalScheduler);
    var scalarModule=Encoding.UTF8.GetBytes(historicalModule);
    var scalarLifecycle=Encoding.UTF8.GetBytes(historicalLifecycle);
    // Independent, actual pure scalar producer fields; original six-frame
    // offer cadence and raw backing-table exposure must survive.
    var cadenceDerived=privateSource?derived:File.ReadAllText("tests/ZeroEngineDemandContract/CadencedDerivedFixture.lua");
    var cadenceHost=privateSource?derivedHost:derivedHost.Replace(
        "local valid = true","local valid = true\nlocal function GetPlayer()\n    return State.player()\nend")
        .Replace("local State = {derived = {}}","local State = {derived = {}, player = function() return TestPlayer end}")
        .Replace("local player = {}","local player = GetPlayer()");
    Check(ZeroEngineDemandSource.TryDerivedProducer(cadenceDerived,out var cadencePitch),
        "cadence scalar base module proof");
    Check(ZeroEngineDemandSource.TryDerivedHost(cadenceHost,out var cadencePitchHost),
        "cadence scalar base host proof");
    Check(ZeroEngineDemandSource.TrySampleController(Encoding.UTF8.GetString(generatedScheduler),out var cadencePitchScheduler),
        "cadence scalar base controller proof");
    Check(ZeroEngineDemandSource.TryCadencedDerivedProducer(cadencePitch,out var cadenceModuleNext)&&cadenceModuleNext!=cadencePitch,
        "pure cadenced fields admission");
    Check(ZeroEngineDemandSource.TryCadencedDerivedHost(cadencePitchHost,out var cadenceHostNext)&&cadenceHostNext!=cadencePitchHost,
        "pure cadenced fields host admission");
    Check(ZeroEngineDemandSource.TryCadencedController(cadencePitchScheduler,out var cadenceSchedulerNext),
        "cadenced sample controller admission");
    Check(ZeroEngineDemandSource.TryCadencedDerivedProducer(cadenceModuleNext,out var cadenceRepeat)&&cadenceRepeat==cadenceModuleNext&&
        ZeroEngineDemandSource.TryCadencedDerivedHost(cadenceHostNext,out var cadenceHostRepeat)&&cadenceHostRepeat==cadenceHostNext&&
        ZeroEngineDemandSource.TryCadencedController(cadenceSchedulerNext,out var cadenceSchedulerRepeat)&&cadenceSchedulerRepeat==cadenceSchedulerNext,
        "cadenced scalar repeat generation");
    Check(ZeroEngineDemandSource.TrySampleController(cadenceSchedulerNext,out var originalSampleRepeat)&&
        originalSampleRepeat==cadenceSchedulerNext,"original scalar scope must compose with cadence slots");
    // Upgrade the exact formerly deferred v1 components at their source owners.
    var legacyModule=cadenceModuleNext.Replace("controller.SampleEpochVersion ~= 2","controller.SampleEpochVersion ~= 1")
        .Replace("controller.CadencedSampleVersion ~= 2","controller.CadencedSampleVersion ~= 1");
    var legacyScheduler=cadenceSchedulerNext.Replace("demand.SampleEpochVersion = 2","demand.SampleEpochVersion = 1")
        .Replace("demand.CadencedSampleVersion = 2","demand.CadencedSampleVersion = 1");
    legacyScheduler=legacyScheduler.Replace("available, sampled = true, true","available, sampled = true, false");
    legacyScheduler=System.Text.RegularExpressions.Regex.Replace(legacyScheduler,
        @"(offeredEpoch = epoch\(\)\r*\n[ \t]*)produce\(\)","$1if exposed then sample.Read() end");
    legacyScheduler=System.Text.RegularExpressions.Regex.Replace(legacyScheduler,
        @"(function sample\.Offer\(player\)\r*\n)([ \t]*)-- The author's receiver[^\r\n]*\r*\n[ \t]*-- A preceding getter[^\r\n]*\r*\n[ \t]*-- clock;[^\r\n]*\r*\n[ \t]*pending = false\r*\n[ \t]*produce\(player\)",
        "$1$2pending = true\n$2if exposed then sample.Read(player) end");
    Check(ZeroEngineDemandSource.TryDerivedProducer(legacyModule,out var migratedPitch)&&
        ZeroEngineDemandSource.TryCadencedDerivedProducer(migratedPitch,out var migratedModule)&&migratedModule==cadenceModuleNext,
        "former deferred module must upgrade in one generation");
    Check(ZeroEngineDemandSource.TrySampleController(legacyScheduler,out var migratedController),
        "former deferred camera slot must upgrade in one generation");
    Check(ZeroEngineDemandSource.TryCadencedController(migratedController,out var migratedScheduler),
        "former deferred six-frame slots must upgrade in one generation");
    Check(migratedScheduler==cadenceSchedulerNext,
        "former deferred scheduler must converge byte-for-byte to current output");
    // Exercise both checkout line-ending forms even on Linux, where the
    // raw C# template and actual installed Lua files can otherwise differ.
    foreach(var eol in new[]{"\n","\r\n"})
    {
        string Lines(string value)=>value.Replace("\r\n","\n").Replace("\n",eol);
        Check(ZeroEngineDemandSource.TrySampleController(Lines(legacyScheduler),out var migratedLinePitch),
            "camera migration failed for line-ending form");
        Check(ZeroEngineDemandSource.TryCadencedController(migratedLinePitch,out var migratedLineCadenced)&&
            migratedLineCadenced==Lines(cadenceSchedulerNext),
            "cadenced migration failed for line-ending form");
    }
    // Production retirement is independent of whether the old host,
    // module, or controller is present. Keep all original behavior tests.
    var cadenceStageRoot=Path.Combine(root,"cadence-stage");
    var cadenceStageDir=Path.Combine(cadenceStageRoot,"0-Engine","modules");
    Directory.CreateDirectory(cadenceStageDir);
    foreach(var shape in new[]{"stock","pitch","full","host-only","module-only","legacy","partial","custom"})
    {
        var h=shape is "stock" or "module-only"?cadenceHost:shape=="pitch"?cadencePitchHost:cadenceHostNext;
        var m=shape is "stock" or "host-only"?cadenceDerived:shape=="pitch"?cadencePitch:shape=="legacy"?legacyModule:cadenceModuleNext;
        if(shape=="partial")m=m.Replace("if __gcetCadencedClock then __gcetCadencedEpoch = __gcetCadencedClock() end","-- original phase retained");
        if(shape=="custom")m="-- author custom header\n"+m;
        var sc=shape=="stock"?scheduler:shape=="legacy"?legacyScheduler:cadenceSchedulerNext;
        File.WriteAllText(Path.Combine(cadenceStageDir,"DerivedState.lua"),m);
        var fw=new FixedZeroEngineBuild(new Dictionary<string,byte[]>{
            [ZeroEngineDemandSource.InitPath]=Encoding.UTF8.GetBytes(h),
            [ZeroEngineDemandSource.SchedulerPath]=Encoding.UTF8.GetBytes(sc)
        },"","","","");
        var ss=new Dictionary<string,byte[]>();
        Check(ZeroEngineDemandSource.TryStage(cadenceStageRoot,ref fw,ss,new(),new(),new())==(shape=="stock"?0:1),
            "historical retirement: "+shape);
        var mh=Encoding.UTF8.GetString(fw.Files[ZeroEngineDemandSource.InitPath]);
        var mm=ss.TryGetValue("0-Engine/modules/DerivedState.lua",out var mb)?Encoding.UTF8.GetString(mb):m;
        Check(!mh.Contains("__gcetDerivedDemand")&&!mm.Contains("__gcetPitchSample")&&!mm.Contains("__gcetPlayerSamples")&&
            mm.Contains("cached.lookPitch = resolveLookPitch()")&&mm.Contains("cached.hasWeapon = resolveHasWeapon(player)")&&
            mm.Contains("cached.isInWorkspot = resolveIsInWorkspot(player)"),
            "historical retirement left hot bookkeeping: "+shape);
        Check(shape!="custom"||mm.StartsWith("-- author custom header"),"author edit lost");
        Check(ZeroEngineDemandSource.TryStage(cadenceStageRoot,ref fw,ss,new(),new(),new())==0,
            "historical retirement repeat: "+shape);
    }
    Check(!ZeroEngineDemandSource.TryRetireHistoricalModule(cadenceModuleNext+"\nexternal(__gcetPlayerSamples)",out _),
        "unknown generated state escape must refuse");
    Check(!ZeroEngineDemandSource.TryRetireHistoricalHost(cadenceHostNext+"\nexternal(__gcetDerivedDemand)",out _),
        "unknown host state escape must refuse");
    Check(ZeroEngineDemandSource.TryRetireHistoricalModule(cadenceModuleNext,out var retiredModule),"full migrated module");
    Check(ZeroEngineDemandSource.TryRetireHistoricalHost(cadenceHostNext,out var retiredHost),"full migrated host");
    Check(!ZeroEngineDemandSource.TryRetireHistoricalModule(cadenceModuleNext+"\nexternal(DerivedState[\"__gcetPeek\"])",out _),
        "quoted unknown API reference must refuse");
    var comment="--[=[\nif __gcetPitchSample then __gcetPitchSample.Offer() else cached.lookPitch = resolveLookPitch() end\n]=]\n";
    Check(ZeroEngineDemandSource.TryRetireHistoricalModule(comment+cadenceModuleNext,out var commentedRetired)&&
        commentedRetired.StartsWith(comment,StringComparison.Ordinal),
        "preserve exact-looking generated fragments in author long comments");
    Check(!ZeroEngineDemandSource.TryCadencedDerivedProducer(cadencePitch.Replace(
        "cached.hasWeapon = resolveHasWeapon(player)","cached.hasWeapon = resolveHasWeapon(player)\n        externalConsumer(cached.hasWeapon)"),out _),
        "internal field dependency must stay eager");
    if(!privateSource){
        var cadenceRoot=Path.Combine(root,"cadenced");
        var cadenceModules=Path.Combine(cadenceRoot,"modules");
        Directory.CreateDirectory(Path.Combine(cadenceModules,"G-CET"));
        File.WriteAllText(Path.Combine(cadenceModules,"DerivedState.lua"),cadenceModuleNext);
        File.WriteAllText(Path.Combine(cadenceModules,"Lifecycle.lua"),Encoding.UTF8.GetString(scalarLifecycle));
        File.WriteAllText(Path.Combine(cadenceModules,"G-CET","Scheduler.lua"),cadenceSchedulerNext);
        File.WriteAllText(Path.Combine(cadenceRoot,"init.lua"),cadenceHostNext);
        File.WriteAllText(Path.Combine(cadenceRoot,"stock-module.lua"),cadencePitch);
        File.WriteAllText(Path.Combine(cadenceRoot,"stock-host.lua"),cadencePitchHost);
        File.WriteAllText(Path.Combine(cadenceRoot,"stock-scheduler.lua"),cadencePitchScheduler);
        File.WriteAllText(Path.Combine(cadenceRoot,"legacy-module.lua"),legacyModule);
        File.WriteAllText(Path.Combine(cadenceRoot,"legacy-scheduler.lua"),legacyScheduler);
        foreach(var scenario in new[]{"stock","no-demand","mixed-fields","raw","reset","old-module","old-host","old-scheduler","legacy-module","legacy-scheduler","reentrant","stock-reentrant"}){
            var cadencePsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
            cadencePsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteCadencedDerived.lua");
            cadencePsi.ArgumentList.Add(cadenceRoot);
            cadencePsi.ArgumentList.Add(scenario);
            using var cadenceProcess=Process.Start(cadencePsi)!;cadenceProcess.WaitForExit();
            Check(cadenceProcess.ExitCode==0,"cadenced scalar execution: "+scenario);
        }
        // Execute the actual retired source, not just the old generated
        // templates. Mixed installs must keep historical sampling without
        // registering unused sample consumers.
        File.WriteAllText(Path.Combine(cadenceModules,"DerivedState.lua"),retiredModule);
        File.WriteAllText(Path.Combine(cadenceRoot,"init.lua"),retiredHost);
        foreach(var scenario in new[]{"no-demand","mixed-fields","raw","reset","old-module","old-host","old-scheduler","legacy-module","legacy-scheduler","reentrant","stock-reentrant"}){
            var retiredPsi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
            retiredPsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteCadencedDerived.lua");
            retiredPsi.ArgumentList.Add(cadenceRoot);
            retiredPsi.ArgumentList.Add(scenario);
            retiredPsi.ArgumentList.Add("retired");
            using var retiredProcess=Process.Start(retiredPsi)!;retiredProcess.WaitForExit();
            Check(retiredProcess.ExitCode==0,"retired scalar execution: "+scenario);
        }
    }

    Check(!ZeroEngineDemandSource.TryDerivedProducer(derived.Replace(
        "cached.lookPitch = resolveLookPitch()", "cached.lookPitch = resolveLookPitch()\n    print(cached.lookPitch)"),
        out _), "internal scalar dependency must refuse");
    var idleHostFixture = File.ReadAllText("tests/ZeroEngineDemandContract/IdleHostFixture.lua");
    Check(ZeroEngineDemandSource.TryIdleScheduler(scheduler, out var idleScheduler), "canonical scheduler active job accounting proof");
    Check(ZeroEngineDemandSource.TryIdleHost(idleHostFixture, out var idleHost), "canonical idle host proof");
    // Historical Windows generation produced CRCRLF only in the callback
    // block. This compatible source must still be admitted; preserve custom
    // unrelated contents and complete without duplicated adapters.
    var historicalHost = idleHostFixture.Replace("\r\n", "\n").Replace("\n", "\r\r\n");
    Check(ZeroEngineDemandSource.TryIdleHost(historicalHost, out var historicalIdle) &&
        historicalIdle.Contains("-- G-CET scheduler idle host v1"), "historical Windows host admission");
    var customizedHost = "-- custom header\r\n" + historicalHost + "\r\n-- custom trailer";
    Check(ZeroEngineDemandSource.TryIdleHost(customizedHost, out var customizedIdle) &&
        customizedIdle.StartsWith("-- custom header\r\n") &&
        customizedIdle.EndsWith("\r\n-- custom trailer"), "compatible custom host preservation");
    Check(ZeroEngineDemandSource.TryIdleScheduler(idleScheduler, out var idleRepeat) &&
        idleRepeat == idleScheduler, "idle scheduler idempotence");
    Check(ZeroEngineDemandSource.TryIdleHost(idleHost, out var idleHostRepeat) &&
        idleHostRepeat == idleHost, "idle host idempotence");
    Check(!ZeroEngineDemandSource.TryIdleScheduler(scheduler.Replace(
        "function Scheduler.SetGeneration(value)", "function Scheduler.SetGeneration(value)\n    job.active = false"),
        out _), "unproved job state mutation must refuse");
    // Independent framework-only stage, including missing host/scheduler.
    var idleRoot = Path.Combine(root, "idle");
    Directory.CreateDirectory(idleRoot);
    FixedZeroEngineBuild IdleFramework(string h, string s) => new(new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(h),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(s)
    }, "", "", "", "");
    var idleBuild = IdleFramework(idleHostFixture, scheduler);
    Check(ZeroEngineDemandSource.TryStage(idleRoot, ref idleBuild, new(), new(), new(), new()) == 1,
        "framework-only idle scheduler stage");
    var idleFullScheduler = Encoding.UTF8.GetString(idleBuild.Files[ZeroEngineDemandSource.SchedulerPath]);
    foreach (var partialPair in new[] { (idleHost, scheduler), (idleHostFixture, idleFullScheduler) })
    {
        var incomplete = IdleFramework(partialPair.Item1, partialPair.Item2);
        Check(ZeroEngineDemandSource.TryStage(idleRoot, ref incomplete, new(), new(), new(), new()) == 1,
            "complete missing idle integration");
        Check(incomplete.Files[ZeroEngineDemandSource.InitPath].SequenceEqual(idleBuild.Files[ZeroEngineDemandSource.InitPath]) &&
            incomplete.Files[ZeroEngineDemandSource.SchedulerPath].SequenceEqual(idleBuild.Files[ZeroEngineDemandSource.SchedulerPath]),
            "partial idle completion output differs");
    }
    Check(ZeroEngineDemandSource.TryStage(idleRoot, ref idleBuild, new(), new(), new(), new()) == 0,
        "repeat framework-only idle stage");
    // Actual bundled router + production transform, not a hand-written
    // approximation of the generated demand lane.
    var observerRouter=Encoding.UTF8.GetString(Convert.FromBase64String(
        File.ReadAllText("resolver-runtime/0-Engine/modules/ActionRouter.lua.b64")));
    var observerHost="""
local Router = require("modules/G-CET/ActionRouter")
Observe("PlayerPuppet", "OnAction", function(player, action, consumer)
    Router.Dispatch(player, action, consumer)
end)
return Router
""";
    FixedZeroEngineBuild ObserverFramework(string h,string r,string sc) => new(
        new Dictionary<string,byte[]>{
            [ZeroEngineDemandSource.InitPath]=Encoding.UTF8.GetBytes(h),
            [ZeroEngineObserverDemandSource.RouterPath]=Encoding.UTF8.GetBytes(r),
            [ZeroEngineDemandSource.SchedulerPath]=Encoding.UTF8.GetBytes(sc)
        },"","","","");
    var observerBuild=ObserverFramework(observerHost,observerRouter,scheduler);
    Check(ZeroEngineObserverDemandSource.TryStage(ref observerBuild,true,out var integrated)==1&&integrated,
        "sole managed observer integration");
    var observerGenerated=observerBuild.Files.ToDictionary(p=>p.Key,p=>p.Value);
    Check(ZeroEngineObserverDemandSource.TryStage(ref observerBuild,true,out integrated)==0&&integrated&&
        observerGenerated.All(p=>p.Value.SequenceEqual(observerBuild.Files[p.Key])),
        "observer repeat generation must be byte-identical");
    foreach(var partialPath in observerGenerated.Keys)
    {
        var partialFiles=observerGenerated.ToDictionary(p=>p.Key,p=>p.Value);
        partialFiles[partialPath]=ObserverFramework(observerHost,observerRouter,scheduler).Files[partialPath];
        var incomplete=observerBuild with {Files=partialFiles};
        Check(ZeroEngineObserverDemandSource.TryStage(ref incomplete,true,out integrated)==1&&integrated&&
            observerGenerated.All(p=>p.Value.SequenceEqual(incomplete.Files[p.Key])),
            "observer partial completion failed: "+partialPath);
    }
    var mixed=ObserverFramework(observerHost.Replace("return Router",
        "Observe('PlayerPuppet','OnAction',function(_, action) Events.PlayerAction:trigger(action) end)\nreturn Router"),
        observerRouter,scheduler);
    Check(ZeroEngineObserverDemandSource.TryStage(ref mixed,true,out integrated)==0&&!integrated,
        "independent author observer must prevent native attachment");
    // An additive host adapter delegates to an unknown host registry.
    // Even if its fallback observer has the expected body, it does not
    // establish demand ownership over the foreign host.
    var foreignObserverHost=observerHost.Replace(
        "local Router = require(\"modules/G-CET/ActionRouter\")",
        "local ok, candidate = pcall(require, \"modules/G-CET/ActionRouter\")\nlocal Router = candidate");
    var foreignObserver=ObserverFramework(foreignObserverHost,observerRouter,scheduler);
    Check(ZeroEngineObserverDemandSource.TryStage(ref foreignObserver,true,out integrated)==0&&!integrated&&
        Encoding.UTF8.GetString(foreignObserver.Files[ZeroEngineObserverDemandSource.RouterPath])==observerRouter,
        "foreign compatibility bridge must not modify the author router");
    var drift=ObserverFramework(observerHost,observerRouter.Replace("if beforeActive == 0 then return end",
        "print('author work')\n    if beforeActive == 0 then return end"),scheduler);
    Check(ZeroEngineObserverDemandSource.TryStage(ref drift,true,out integrated)==0&&!integrated,
        "author work before idle guard must not be suppressed");
    var oldNative=ObserverFramework(observerHost,observerRouter,scheduler);
    Check(ZeroEngineObserverDemandSource.TryStage(ref oldNative,false,out integrated)==0&&!integrated,
        "old DLL must not emit incomplete integration");
    if(!privateSource)
    {
        var observerDir=Path.Combine(root,"observer");
        foreach(var pair in observerGenerated)
        {
            var dest=Path.Combine(observerDir,pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest,pair.Value);
        }
        foreach(var scenario in new[]{"complete","missing-native","prepare-refused","bind-refused","wake-refused","cancel-before-prepare"})
        {
            var psi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
            psi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteObserver.lua");
            psi.ArgumentList.Add(Path.Combine(observerDir,"0-Engine"));
            psi.ArgumentList.Add(scenario);
            using var process=Process.Start(psi)!;process.WaitForExit();
            Check(process.ExitCode==0,"generated observer execution failed: "+scenario);
        }
    }
    // Legacy notification path uses the same scheduler and native gate.
    var legacyHost=privateSource?host:File.ReadAllText("tests/ZeroEngineDemandContract/LegacyPlayerActionFixture.lua");
    var legacyEmitter=File.ReadAllText(privateSource?Path.Combine(args[1],"modules","EventEmitter.lua"):
        "tests/ZeroEngineDemandContract/LegacyPlayerActionEmitterFixture.lua");
    Check(ZeroEngineObserverDemandSource.LegacyEmitterProof(legacyEmitter),"legacy emitter ownership proof");
    Check(ZeroEngineObserverDemandSource.TryLegacyHost(legacyHost,out var legacyNext),"legacy PlayerAction host proof");
    Check(ZeroEngineObserverDemandSource.TryLegacyHost(legacyNext,out var legacyRepeat)&&legacyRepeat==legacyNext,
        "legacy PlayerAction idempotence");
    Check(!ZeroEngineObserverDemandSource.TryLegacyHost(legacyHost.Replace("return Engine","local escaped = Events\nreturn Engine"),out _),
        "escaped event bus must refuse native suppression");
    Check(!ZeroEngineObserverDemandSource.LegacyEmitterProof(legacyEmitter.Replace("local n = #listeners","local n = #listeners + 1")),
        "changed emitter behavior must refuse native suppression");
    if(privateSource)
    {
        foreach(var (name,file) in new[]{("Lifecycle","Lifecycle"),("DerivedState","DerivedState"),("BlackboardCache","BlackboardCache2")})
            Check(ZeroEngineObserverDemandSource.LegacyBusRecipientProof(File.ReadAllText(Path.Combine(args[1],"modules",file+".lua")),name),
                "legacy event bus recipient proof: "+name);
    }
    if(!privateSource)
    {
        Check(ZeroEngineObserverDemandSource.TryScheduler(scheduler,out var legacyController),"legacy canonical observer controller");
        var legacyDir=Path.Combine(root,"legacy-observer");
        Directory.CreateDirectory(Path.Combine(legacyDir,"modules","G-CET"));
        File.WriteAllText(Path.Combine(legacyDir,"init.lua"),legacyNext);
        File.WriteAllText(Path.Combine(legacyDir,"modules","EventEmitter.lua"),legacyEmitter);
        File.WriteAllText(Path.Combine(legacyDir,"modules","G-CET","Scheduler.lua"),legacyController);
        foreach(var scenario in new[]{"complete","pre-init","mixed","missing-native","wake-refused"})
        {
            var psi=new ProcessStartInfo("lua5.4"){UseShellExecute=false};
            psi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteLegacyPlayerAction.lua");
            psi.ArgumentList.Add(legacyDir);psi.ArgumentList.Add(scenario);
            using var process=Process.Start(psi)!;process.WaitForExit();
            Check(process.ExitCode==0,"legacy generated observer execution failed: "+scenario);
        }
    }
    // Generic leading-guard recognition: no method/class/mod/name allowlist.
    var genericGuard="""
local enabled = false
Observe("ArbitraryClass", "AnyEvent", function(...)
    if not enabled then return end
    work(...)
end)
ObserveBefore("AnotherClass", "UnrelatedEvent", function()
    if enabled then return end
    other()
end)
registerForEvent("onInit", function()
    enabled = true
end)
registerForEvent("onShutdown", function()
    enabled = nil
end)
""";
    var gs=GenericObserverGuardSource.Analyze(genericGuard);
    Check(gs.Count==2&&gs.All(x=>x.SourceProven&&x.Writes.Count==2)&&gs[0].ActiveWhenTruthy&&!gs[1].ActiveWhenTruthy,
        "generic simple state recognition, polarity and every literal write");
    foreach(var (changed,reason) in new[]{
        (genericGuard.Replace("local enabled = false","enabled = false"),"NO_UNIQUE_CHUNK_LITERAL_LOCAL"),
        (genericGuard.Replace("function(...)","function(enabled)"),"CALLBACK_PARAMETER_SHADOW"),
        (genericGuard.Replace("enabled = true","enabled = readState()"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard.Replace("enabled = true","enabled = true\n    and readState()"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard.Replace("enabled = true","local enabled = true"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard.Replace("enabled = true","local a, enabled = true, false"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard.Replace("enabled = true","local t = {\n        enabled = true\n    }"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard.Replace("enabled = true","local t = { a = false;\n        enabled = true;\n    }"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard.Replace("enabled = true","other, enabled = true, false"),"UNPROVEN_USE_SHADOW_OR_WRITE"),
        (genericGuard+"\nlocal a = debug.setupvalue","REFLECTION_OR_DYNAMIC_ENVIRONMENT"),
        (genericGuard+"\nlocal original = Observe","OBSERVER_API_ALIAS_OR_REBIND"),
        (genericGuard.Replace("if not enabled then return end","if not enabled then return 7 end"),"COMPLEX_OR_NONEMPTY_RETURN")
    }) Check(GenericObserverGuardSource.Analyze(changed).First().Reason==reason,"generic refusal: "+reason);
    Check(GenericObserverGuardSource.Analyze("-- Observe('X','Y',function() if not state then return end end)\n"+genericGuard).Count==2,
        "comments are not registrations");
    Check(GenericObserverGuardSource.Analyze(genericGuard.Replace("ObserveBefore(","ObserveAfter("))[1].Reason=="AFTER_SOURCE_PROVEN_NATIVE_PENDING",
        "After proof separate from native admission");
    Console.WriteLine("Generic simple lexical observer guard proof PASS");
    if(privateSource)
    {
        var collection=Directory.GetParent(Path.GetFullPath(args[1]))!.FullName;
        var sites=new List<GenericObserverGuardSource.Site>();
        var luaFiles=0;
        foreach(var file in Directory.EnumerateFiles(collection,"*.lua",SearchOption.AllDirectories))
        {
            luaFiles++;
            sites.AddRange(GenericObserverGuardSource.Analyze(File.ReadAllText(file)));
        }
        Console.WriteLine($"Generic observer source audit: {luaFiles} Lua files, {sites.Count} literal registration sites; source proof only, native admission not established.");
        foreach(var g in sites.GroupBy(x=>x.Reason).OrderBy(x=>x.Key))
            Console.WriteLine($"Observer source audit {g.Key}: {g.Count()}");
    }
    if (privateSource)
    {
        Console.WriteLine("Private stock 0-Engine central demand generation and idempotence PASS");
        return;
    }
    var ns = Path.Combine(moduleDir, "G-CET"); Directory.CreateDirectory(ns);
    File.WriteAllBytes(Path.Combine(ns, "Scheduler.lua"), generatedScheduler);
    File.WriteAllBytes(Path.Combine(root, "0-Engine", "init.lua"), generatedHost);
    foreach (var pair in staged) File.WriteAllBytes(Path.Combine(root, pair.Key), pair.Value);
    void RunLua(string scenario)
    {
        var psi = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
        psi.ArgumentList.Add("tests/ZeroEngineDemandContract/Execute.lua");
        psi.ArgumentList.Add(Path.Combine(root, "0-Engine"));
        psi.ArgumentList.Add(scenario);
        using var process = Process.Start(psi)!; process.WaitForExit();
        Check(process.ExitCode == 0, "actual generated execution failed: " + scenario);
    }
    RunLua("complete");
    // An overlay may contain the generated host but an author producer wins
    // source precedence. It must retain stock work and delivery, not silently
    // lose callbacks just because no demand was tracked.
    File.WriteAllText(Path.Combine(moduleDir, "Proximity.lua"), proximity);
    RunLua("missing-producer");
    File.WriteAllBytes(Path.Combine(moduleDir, "Proximity.lua"),
        staged["0-Engine/modules/Proximity.lua"]);
    File.WriteAllText(Path.Combine(ns, "Scheduler.lua"), scheduler);
    RunLua("missing-controller");
    File.WriteAllBytes(Path.Combine(ns, "Scheduler.lua"), scalarScheduler);
    File.WriteAllBytes(Path.Combine(root, "0-Engine", "init.lua"), scalarHost);
    File.WriteAllBytes(Path.Combine(moduleDir, "DerivedState.lua"), scalarModule);
    File.WriteAllBytes(Path.Combine(moduleDir, "Lifecycle.lua"), scalarLifecycle);
    void RunScalar(string scenario)
    {
        var psi = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
        psi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteDerived.lua");
        psi.ArgumentList.Add(Path.Combine(root, "0-Engine"));
        psi.ArgumentList.Add(scenario);
        using var process = Process.Start(psi)!; process.WaitForExit();
        Check(process.ExitCode == 0, "generated scalar execution failed: " + scenario);
    }
    foreach (var scenario in new[] { "scalar", "invalid-epoch", "raw-root", "raw-derived", "raw-module", "raw-root-before-update" })
        RunScalar(scenario);
    File.WriteAllText(Path.Combine(moduleDir, "DerivedState.lua"), derived);
    RunScalar("old-producer");
    File.WriteAllBytes(Path.Combine(moduleDir, "DerivedState.lua"), scalarModule);
    File.WriteAllText(Path.Combine(moduleDir, "Lifecycle.lua"), lifecycle);
    RunScalar("old-lifecycle");
    File.WriteAllBytes(Path.Combine(moduleDir, "Lifecycle.lua"), scalarLifecycle);
    File.WriteAllBytes(Path.Combine(moduleDir, "DerivedState.lua"), scalarModule);
    File.WriteAllText(Path.Combine(ns, "Scheduler.lua"), scheduler);
    RunScalar("old-controller");
    File.WriteAllText(Path.Combine(root, "0-Engine", "init.lua"), derivedHost);
    File.WriteAllBytes(Path.Combine(ns, "Scheduler.lua"), scalarScheduler);
    RunScalar("old-host");
    var idleSchedulerPath = Path.Combine(ns, "Scheduler.lua");
    File.WriteAllBytes(idleSchedulerPath, idleBuild.Files[ZeroEngineDemandSource.SchedulerPath]);
    File.WriteAllText(Path.Combine(root, "idle-original.lua"), scheduler);
    File.WriteAllText(Path.Combine(root, "idle-host-original.lua"), idleHostFixture);
    File.WriteAllBytes(Path.Combine(root, "idle-host-generated.lua"), idleBuild.Files[ZeroEngineDemandSource.InitPath]);
    var idlePsi = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
    idlePsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteIdle.lua");
    idlePsi.ArgumentList.Add(root);
    idlePsi.ArgumentList.Add(idleSchedulerPath);
    using (var idleProcess = Process.Start(idlePsi)!)
    {
        idleProcess.WaitForExit();
        Check(idleProcess.ExitCode == 0, "generated idle scheduler execution");
    }

}
finally { Directory.Delete(root, true); }
