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
    if(privateSource)
    {
        var poseStock=new FixedZeroEngineBuild(new Dictionary<string,byte[]>{
            [ZeroEngineDemandSource.InitPath]=generatedHost
        },"","","","");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new())==1,
            "actual private host pose stage");
        Check(ZeroEngineDemandSource.TryStagePose(ref poseStock,new())==0,
            "actual private pose stage repeat");
    }
    else
    {
        File.WriteAllText(Path.Combine(root,"pose-original.lua"),poseFixture);
        File.WriteAllText(Path.Combine(root,"pose-generated.lua"),poseNext);
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

    // Keep the existing spatial fixture independent; exercise the new
    // scalar transformation using the same production generator stage.
    File.WriteAllText(Path.Combine(moduleDir, "DerivedState.lua"), derived);
    File.WriteAllText(Path.Combine(moduleDir, "Lifecycle.lua"), lifecycle);
    Check(ZeroEngineDemandSource.TryDerivedProducer(derived, out _), "actual camera producer proof");
    Check(ZeroEngineDemandSource.TryDerivedHost(derivedHost, out _), "actual scalar host proof");
    Check(ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle, out _), "actual raw Lifecycle ownership proof");
    Check(!ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle.Replace("State = state", "State = state\n    externalState = State"), out _), "unproved raw Lifecycle escape accepted");
    var scalarFramework = new FixedZeroEngineBuild(new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(derivedHost),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(scheduler)
    }, "", "", "", "");
    var scalarStage = new Dictionary<string, byte[]>();
    var scalarCount = ZeroEngineDemandSource.TryStage(root, ref scalarFramework, scalarStage, new(), new(), new());
    Check(scalarCount == (privateSource ? 3 : 1), "central scalar sample admission: " + scalarCount);
    var scalarHost = scalarFramework.Files[ZeroEngineDemandSource.InitPath];
    var scalarScheduler = scalarFramework.Files[ZeroEngineDemandSource.SchedulerPath];
    var scalarModule = scalarStage["0-Engine/modules/DerivedState.lua"];
    var scalarLifecycle = scalarStage["0-Engine/modules/Lifecycle.lua"];
    Check(ZeroEngineDemandSource.TryStage(root, ref scalarFramework, scalarStage, new(), new(), new()) == 0,
        "central scalar repeat generation");
    Check(scalarHost.SequenceEqual(scalarFramework.Files[ZeroEngineDemandSource.InitPath]) &&
        scalarScheduler.SequenceEqual(scalarFramework.Files[ZeroEngineDemandSource.SchedulerPath]) &&
        scalarModule.SequenceEqual(scalarStage["0-Engine/modules/DerivedState.lua"]), "scalar repeat changed bytes");

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
    // Exercise the production stage with every independent missing
    // component, then repeat the complete output through its real entrypoint.
    var cadenceStageRoot=Path.Combine(root,"cadence-stage");
    var cadenceStageDir=Path.Combine(cadenceStageRoot,"0-Engine","modules");
    Directory.CreateDirectory(cadenceStageDir);
    foreach(var missing in new[]{"all","host","module","scheduler","none"})
    {
        var h=missing is "all" or "host"?cadencePitchHost:cadenceHostNext;
        var m=missing is "all" or "module"?cadencePitch:cadenceModuleNext;
        var sc=missing is "all" or "scheduler"?cadencePitchScheduler:cadenceSchedulerNext;
        File.WriteAllText(Path.Combine(cadenceStageDir,"DerivedState.lua"),m);
        var fw=new FixedZeroEngineBuild(new Dictionary<string,byte[]>{
            [ZeroEngineDemandSource.InitPath]=Encoding.UTF8.GetBytes(h),
            [ZeroEngineDemandSource.SchedulerPath]=Encoding.UTF8.GetBytes(sc)
        },"","","","");
        var ss=new Dictionary<string,byte[]>();
        Check(ZeroEngineDemandSource.TryStageCadencedDerived(cadenceStageRoot,ref fw,ss,new(),new())==
            (missing=="none"?0:1),"complete cadenced source: "+missing);
        Check(Encoding.UTF8.GetString(fw.Files[ZeroEngineDemandSource.InitPath])==cadenceHostNext&&
            Encoding.UTF8.GetString(fw.Files[ZeroEngineDemandSource.SchedulerPath])==cadenceSchedulerNext&&
            (ss.TryGetValue("0-Engine/modules/DerivedState.lua",out var mb)?Encoding.UTF8.GetString(mb):m)==cadenceModuleNext,
            "cadenced partial output differs: "+missing);
        Check(ZeroEngineDemandSource.TryStageCadencedDerived(cadenceStageRoot,ref fw,ss,new(),new())==0,
            "cadenced repeat after actual stage: "+missing);
    }
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
