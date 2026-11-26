#include "StaticIndex.hpp"
#include "PositionCoordinator.hpp"
#include "PlayerSnapshot.hpp"
#include "DemandFrameCache.hpp"
#include "DiagnosticProbe.hpp"
#include "CETLiveObserver.hpp"
#include "ProducerReads.hpp"
#include "NativeDiagnostics.hpp"
#include <cmath>
#include <RED4ext/Scripting/Utils.hpp>
#include <RED4ext/Scripting/Natives/ScriptGameInstance.hpp>
#include <RED4ext/Scripting/Natives/Generated/Vector4.hpp>
#include <RED4ext/Scripting/Natives/entEntity.hpp>
#include <RED4ext/RED4ext.hpp>
#include <RED4ext/RTTITypes.hpp>
#include <RED4ext/Scripting/IScriptable.hpp>
#include <windows.h>
#include <filesystem>
#include <string>
#include <vector>

namespace {
    using gcet::diagnostics::Lane;
    using gcet::diagnostics::Code;
    using gcet::diagnostics::Record;
    using gcet::diagnostics::Outcome;
    gcet::static_locations::StaticIndex g_index;
    bool g_loaded = false;
    gcet::static_locations::ManifestLoadStatus g_manifestStatus =
        gcet::static_locations::ManifestLoadStatus::Invalid;
    gcet::static_locations::PositionCoordinator g_coordinator(g_index);
    gcet::static_locations::PlayerSnapshot g_playerSnapshot;
    gcet::static_locations::DemandFrameCache g_onDemandPosition;
    bool g_positionLastReadValid=false;
    uint64_t g_spatialPhysicalReads=0;
    gcet::static_locations::DiagnosticProbe g_probe(g_index,g_coordinator);
    const RED4ext::v1::Logger* g_logger=nullptr;
    RED4ext::v1::PluginHandle g_handle{};
    int32_t g_lastPlayerCode=-1;
    uint32_t g_reportedReasons=0;
    bool g_reportedReady=false;
    uint32_t g_logBudget=32;
    // Explicit opt-in file ONLY in developer smoke overlays. No production
    // manifest can silently enable the self-test or author wake replacement.
    bool g_autoProbe=false;

    void Log(const char* message) {
        if(g_logger && g_logger->Info && g_logBudget) {
            --g_logBudget;
            g_logger->Info(g_handle,message);
        }
    }
    // Diagnostic logger deliberately bypasses the old 32-message startup
    // budget. First-occurrence and summary bounds are owned by Counters.
    void DiagnosticLog(const char* message){
        if(g_logger&&g_logger->Info)g_logger->Info(g_handle,message);
    }
    void DiagnosticSummary(const char* boundary){
        gcet::diagnostics::counters.Summary(boundary);
        char line[384];
        std::snprintf(line,sizeof(line),
            "G-NativeRuntime DIAG cumulative acquisitions boundary=%s vector_reads=%llu vector_hits=%llu spatial_reads=%llu spatial_shared_hits=%llu snapshot_samples=%llu anchors=%llu active=%llu subscribers=%llu",
            boundary,
            static_cast<unsigned long long>(g_onDemandPosition.Reads()),
            static_cast<unsigned long long>(g_onDemandPosition.Hits()),
            static_cast<unsigned long long>(g_spatialPhysicalReads),
            static_cast<unsigned long long>(g_onDemandPosition.SharedSpatialHits()),
            static_cast<unsigned long long>(g_playerSnapshot.Samples()),
            static_cast<unsigned long long>(g_index.Count()),
            static_cast<unsigned long long>(g_index.ActiveCount()),
            static_cast<unsigned long long>(g_playerSnapshot.SubscriberCount()));
        DiagnosticLog(line);
    }
    void LogErrorCodeOnce(int32_t code) {
        if(code<=0 || code>5)return;
        const uint32_t bit=uint32_t(1) << static_cast<uint32_t>(code);
        if(g_reportedReasons & bit)return;
        g_reportedReasons|=bit;
        switch(code) {
            case 1:Log("G-NativeRuntime: player unavailable / GameInstance.GetPlayer failed");break;
            case 2:Log("G-NativeRuntime: player RTTI class unavailable");break;
            case 3:Log("G-NativeRuntime: player GetWorldPosition method not found");break;
            case 4:Log("G-NativeRuntime: player GetWorldPosition invocation failed");break;
            case 5:Log("G-NativeRuntime: player position invalid or out of bounds");break;
        }
    }

    // RED4ext's Running-state callback provides ONE native position owner.
    bool ReadFrameNumber(uint64_t& number);
    using PlayerHandle=RED4ext::Handle<RED4ext::IScriptable>;
    bool CurrentPlayer(PlayerHandle& player){
        RED4ext::ScriptGameInstance game;
        return RED4ext::ExecuteGlobalFunction("GetPlayer;GameInstance",&player,game)&&player.instance;
    }
    gcet::static_locations::PositionReceiver ReceiverToken(RED4ext::IScriptable* player){
        if(!player)return {};
        auto* klass=player->GetType();
        auto* rtti=RED4ext::CRTTISystem::Get();
        auto* entity=rtti?rtti->GetClass("entEntity"):nullptr;
        if(!entity)return {};
        for(unsigned depth=0;klass&&depth<32;klass=klass->parent,++depth){
            if(klass==entity){
                auto* instance=static_cast<RED4ext::ent::Entity*>(player);
                return {reinterpret_cast<uintptr_t>(instance),instance->entityID.hash};
            }
        }
        return {};
    }
    int32_t SamplePlayerHandle(const PlayerHandle& player,float& x,float& y,float& z,float& w){
        if(!player.instance)return 1;
        auto* klass=player.instance->GetType();
        if(!klass)return 2;
        auto* method=klass->GetFunction("GetWorldPosition");
        if(!method)return 3;
        RED4ext::Vector4 pos{};
        if(!RED4ext::ExecuteFunction(player.instance,method,&pos))return 4;
        x=pos.X; y=pos.Y; z=pos.Z; w=pos.W;
        if(!std::isfinite(x)||!std::isfinite(y)||!std::isfinite(z)||!std::isfinite(w)||
           std::abs(x)>1000000.0f||std::abs(y)>1000000.0f||std::abs(z)>1000000.0f)
            return 5;
        return 0;
    }
    int32_t SamplePlayer(float& x,float& y,float& z,float& w){
        PlayerHandle player;
        if(!CurrentPlayer(player))return 1;
        return SamplePlayerHandle(player,x,y,z,w);
    }
    // Only the existing canonical location host explicitly admits coarse
    // frame freshness. Snapshot subscribers and diagnostic probes retain
    // the exact original native acquisition phase.
    bool SharedSpatialDemand(){
        return g_loaded&&g_index.AllActiveCoarseSpatial()&&
               !g_playerSnapshot.HasDemand()&&!g_autoProbe&&g_probe.Id()==0;
    }
    bool RunningEnter(RED4ext::CGameApplication*){
        gcet::producer::ResetProducerReads();
        g_onDemandPosition.Reset();g_positionLastReadValid=false;
        // Player snapshot is independent of location manifest readiness.
        g_playerSnapshot.Enter();
        if(g_loaded) {
            g_reportedReasons=0;
            g_reportedReady=false;
            g_lastPlayerCode=-1;
            g_coordinator.Enter();
            Log("G-NativeRuntime: running-state entered; awaiting valid player position");
        }
        return true;
    }
    bool RunningUpdate(RED4ext::CGameApplication*){
        Record(Lane::Running,Code::Attempt);
        const bool locationDemand=g_loaded && (g_autoProbe || g_index.ActiveCount()>0);
        const bool playerDemand=g_playerSnapshot.HasDemand();
        Record(Lane::Running,locationDemand?(playerDemand?Code::BothDemand:Code::LocationDemand):(playerDemand?Code::SnapshotDemand:Code::NoDemand));
        if(!locationDemand && !playerDemand) {
            // Native dormancy: zero player getter calls without any live owner.
            if(g_loaded && g_coordinator.Status()==2)
                g_coordinator.Tick(false,0,0,0);
            g_lastPlayerCode=-1;
            return false;
        }
        // Exactly one native player RTTI read, regardless of active clients.
        const auto generation=g_onDemandPosition.Generation();
        float x=0,y=0,z=0,w=0;
        int32_t code=1;
        uint64_t currentFrame=0;
        bool gotFrame=false,reused=false;
        if(SharedSpatialDemand()&&g_onDemandPosition.HasSharedSpatialSample()){
            PlayerHandle player;
            gotFrame=ReadFrameNumber(currentFrame);
            if(g_onDemandPosition.Generation()!=generation)return false;
            const bool available=CurrentPlayer(player);
            const auto token=available?ReceiverToken(player.instance):gcet::static_locations::PositionReceiver{};
            if(g_onDemandPosition.Generation()!=generation)return false;
            gcet::static_locations::Position4 shared{};
            if(available&&SharedSpatialDemand()&&
               g_onDemandPosition.ReadSharedSpatial(currentFrame,gotFrame,token,shared)){
                x=shared.x;y=shared.y;z=shared.z;w=shared.w;code=0;reused=true;
            }else if(available){
                ++g_spatialPhysicalReads;
                code=SamplePlayerHandle(player,x,y,z,w);
            }
        }else{
            ++g_spatialPhysicalReads;
            code=SamplePlayer(x,y,z,w);
        }
        if(g_onDemandPosition.Generation()!=generation)return false;
        // An exact native sample replaces the previous host tag. A reused
        // host sample keeps the same single cache and its original identity.
        if(!reused)gotFrame=code==0 && ReadFrameNumber(currentFrame);
        if(g_onDemandPosition.Generation()!=generation)return false;
        g_lastPlayerCode=code;
        Outcome(Lane::Running,code==0);
        if(gotFrame&&!reused)
            g_onDemandPosition.Seed(currentFrame,true,{x,y,z,w});
        else if(!gotFrame)
            g_onDemandPosition.Reset();
        if(playerDemand)g_playerSnapshot.Tick(code==0,x,y,z,w);
        if(g_loaded && locationDemand){
            g_coordinator.Tick(code==0,x,y,z);
            if(code==0) {
                if(g_autoProbe && g_probe.Id()==0) {
                    if(g_probe.Create(40.f)>0)
                        Log("G-NativeRuntime: diagnostic anchor ARMED at player location (40m, no mod callbacks)");
                }
                if(g_probe.Id()>0) {
                    const auto changed=g_index.Changes();
                    for(const auto& transition:changed) {
                        if(transition.id==g_probe.Id())
                            Log(transition.inside ?
                                "G-NativeRuntime: diagnostic anchor NEAR" :
                                "G-NativeRuntime: diagnostic anchor FAR");
                    }
                }
                if(!g_reportedReady) {
                    Log("G-NativeRuntime: player world position VALID; native coordinator active");
                    g_reportedReady=true;
                }
            } else {
                if(g_probe.Id()>0) {
                    g_probe.Remove();
                    Log("G-NativeRuntime: diagnostic anchor invalidated with unavailable player");
                }
                LogErrorCodeOnce(code);
            }
        } else if(g_loaded && g_coordinator.Status()==2){
            // Do not leave stale native anchor state when the final anchor sleeps.
            g_coordinator.Tick(false,0,0,0);
        }
        return false;
    }
    bool RunningExit(RED4ext::CGameApplication*){
        DiagnosticSummary("RunningExit");
        gcet::producer::ResetProducerReads();
        g_onDemandPosition.Reset();g_positionLastReadValid=false;
        g_playerSnapshot.Exit();
        if(g_loaded) {
            if(g_probe.Id()>0)g_probe.Remove();
            Log("G-NativeRuntime: running-state exited; native session invalidated");
            g_coordinator.Exit();
            g_lastPlayerCode=-1;
        }
        return true;
    }
    // Fixed-size aggregate subscriptions: 0-Engine needs one consumer,
    // not a separate native registration for every participating Lua mod.
    // Stable, runtime-discoverable capability contract for the existing shared
    // host. No anchor manifest, CET hook, or mod-specific registration required.
    // API v1 bits: 1=position, 2=availability, 4=change poll,
    // 8=session epoch, 16=independent of static-location manifests.
    void PlayerApiVersion(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        f->code++;*out=1;
    }
    void PlayerCapabilities(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        // Bit 128 opts display-only consumers into the epoch-guarded frame
        // sample. This is not an assertion of exact cross-phase gameplay
        // freshness. All callers share the existing position producer.
        f->code++;*out=1|2|4|8|16|32|64|128;
    }
    void PlayerConsumerCount(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        f->code++;*out=static_cast<int32_t>(g_playerSnapshot.SubscriberCount());
    }
    void PlayerSubscribe(RED4ext::IScriptable*,RED4ext::CStackFrame* f,bool* out,int64_t){
        int32_t consumer=0,fields=0;
        RED4ext::GetParameter(f,&consumer);RED4ext::GetParameter(f,&fields);f->code++;
        *out=g_playerSnapshot.Subscribe(consumer,fields);
        Record(Lane::PlayerSubscribe,Code::Attempt);Outcome(Lane::PlayerSubscribe,*out);
    }
    void PlayerUnsubscribe(RED4ext::IScriptable*,RED4ext::CStackFrame* f,bool* out,int64_t){
        int32_t consumer=0;RED4ext::GetParameter(f,&consumer);f->code++;
        *out=g_playerSnapshot.Unsubscribe(consumer);
        Record(Lane::PlayerUnsubscribe,Code::Attempt);Outcome(Lane::PlayerUnsubscribe,*out);
    }
    void PlayerPoll(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        int32_t consumer=0;RED4ext::GetParameter(f,&consumer);f->code++;
        *out=g_playerSnapshot.Poll(consumer);
    }
    void PlayerStatus(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        f->code++;*out=g_playerSnapshot.Status();
    }
    void PlayerEpoch(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        f->code++;*out=static_cast<int32_t>(g_playerSnapshot.Epoch());
    }
    void PlayerRevision(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t){
        f->code++;*out=static_cast<int32_t>(g_playerSnapshot.Revision());
    }
    void PlayerX(RED4ext::IScriptable*,RED4ext::CStackFrame* f,float* out,int64_t){
        f->code++;*out=g_playerSnapshot.X();
    }
    void PlayerY(RED4ext::IScriptable*,RED4ext::CStackFrame* f,float* out,int64_t){
        f->code++;*out=g_playerSnapshot.Y();
    }
    void PlayerZ(RED4ext::IScriptable*,RED4ext::CStackFrame* f,float* out,int64_t){
        f->code++;*out=g_playerSnapshot.Z();
    }
    // Single typed RTTI crossing. Retain the exact four-component Vector4
    // returned by the existing native player sample (including W). A missing
    // or stale snapshot produces the zero sentinel; callers MUST check
    // GCETPlayerStatus()==2 and phase freshness before using it. This is
    // deliberately not yet an automatic replacement for GetWorldPosition().
    // Single typed return. Read through the engine directly if the game's
    // frame-number RTTI method cannot be resolved; never return an old sample
    // merely because the profiler host or native subscriber has not ticked.
    bool ReadFrameNumber(uint64_t& number){
        auto* rtti=RED4ext::CRTTISystem::Get();
        auto* cls=rtti ? rtti->GetClass("ScriptGameInstance") : nullptr;
        auto* method=cls ? cls->GetFunction("GetFrameNumber") : nullptr;
        Record(Lane::FrameNumber,Code::Attempt);
        if(!method){Record(Lane::FrameNumber,!rtti?Code::NoRtti:!cls?Code::NoFrameClass:Code::NoFrameMethod);Record(Lane::FrameNumber,Code::Failed);return false;}
        RED4ext::ScriptGameInstance game;
        number=0;
        const bool ok=RED4ext::ExecuteFunction(&game,method,&number);
        if(!ok)Record(Lane::FrameNumber,Code::InvokeFailed);
        Outcome(Lane::FrameNumber,ok);return ok;
    }
    bool ReadPositionNative(gcet::static_locations::Position4& pos){
        float x=0,y=0,z=0,w=0;
        Record(Lane::PlayerSample,Code::Attempt);
        const int32_t code=SamplePlayer(x,y,z,w);
        Outcome(Lane::PlayerSample,code==0);
        if(code!=0){Record(Lane::PlayerSample,code==1?Code::NoPlayer:code==2?Code::NoClass:code==3?Code::PositionMethod:code==4?Code::PositionRead:Code::InvalidSample);return false;}
        pos={x,y,z,w};
        return true;
    }
    void PlayerVector4(RED4ext::IScriptable*,RED4ext::CStackFrame* f,
                       RED4ext::Vector4* out,int64_t) {
        Record(Lane::VectorFrame,Code::Attempt);
        f->code++;
        *out=RED4ext::Vector4{};
        g_positionLastReadValid=false;
        const auto generation=g_onDemandPosition.Generation();
        uint64_t frame=0;
        bool gotFrame=ReadFrameNumber(frame);
        gcet::static_locations::Position4 pos{};
        if(g_onDemandPosition.Generation()!=generation||
           !g_onDemandPosition.Read(frame,gotFrame,ReadPositionNative,pos)){
            Record(Lane::VectorFrame,Code::Failed);
            g_positionLastReadValid=false;
            return;
        }
        out->X=pos.x;out->Y=pos.y;out->Z=pos.z;out->W=pos.w;
        g_positionLastReadValid=true;
        Record(Lane::VectorFrame,Code::Complete);
    }
    bool ReadLiveSpatialDonor(gcet::static_locations::Position4& pos){
        // Reuse the SAME handle already required for the exact acquisition.
        // Frame identity is a dependency of donation, not exact gameplay.
        return g_onDemandPosition.ReadLiveForDemand(SharedSpatialDemand,ReadFrameNumber,ReadPositionNative,
            [&](gcet::static_locations::Position4& value,gcet::static_locations::PositionReceiver& token){
                PlayerHandle player;
                if(!CurrentPlayer(player))return false;
                token=ReceiverToken(player.instance);
                float x=0,y=0,z=0,w=0;
                if(SamplePlayerHandle(player,x,y,z,w)!=0)return false;
                value={x,y,z,w};
                return true;
            },pos);
    }
    void PlayerVector4Live(RED4ext::IScriptable*,RED4ext::CStackFrame* f,
                           RED4ext::Vector4* out,int64_t) {
        Record(Lane::VectorLive,Code::Attempt);
        f->code++;*out=RED4ext::Vector4{};
        g_positionLastReadValid=false;
        gcet::static_locations::Position4 pos{};
        if(!ReadLiveSpatialDonor(pos)){
            Record(Lane::VectorLive,Code::Failed);
            g_positionLastReadValid=false;
            return;
        }
        out->X=pos.x;out->Y=pos.y;out->Z=pos.z;out->W=pos.w;
        g_positionLastReadValid=true;
        Record(Lane::VectorLive,Code::Complete);
    }
    void PlayerVector4Valid(RED4ext::IScriptable*,RED4ext::CStackFrame* f,
                            bool* out,int64_t){
        f->code++;*out=g_positionLastReadValid;
        Record(Lane::VectorValid,Code::Attempt);Outcome(Lane::VectorValid,*out);
    }
    void PlayerPositionCacheHits(RED4ext::IScriptable*,RED4ext::CStackFrame* f,
                                 int64_t* out,int64_t){
        f->code++;*out=static_cast<int64_t>(g_onDemandPosition.Hits());
    }
    void PlayerPositionEngineReads(RED4ext::IScriptable*,RED4ext::CStackFrame* f,
                                   int64_t* out,int64_t){
        f->code++;*out=static_cast<int64_t>(g_onDemandPosition.Reads());
    }
    void CoordinatorStatus(RED4ext::IScriptable*,RED4ext::CStackFrame* frame,int32_t* out,int64_t){
        frame->code++;
        *out=g_loaded?g_coordinator.Status():0;
        Record(Lane::StaticCoordinator,Code::Attempt);Record(Lane::StaticCoordinator,*out==2?Code::Complete:Code::Unknown);
    }

    void LastPlayerCode(RED4ext::IScriptable*,RED4ext::CStackFrame* frame,int32_t* out,int64_t){
        frame->code++;
        *out=g_loaded?g_lastPlayerCode:-1;
    }
    void ProbeHere(RED4ext::IScriptable*,RED4ext::CStackFrame* frame,int32_t* out,int64_t){
        float radius=0;
        RED4ext::GetParameter(frame,&radius);
        frame->code++;
        *out=g_loaded?g_probe.Create(radius):-1;
        if(*out>0)Log("G-NativeRuntime: diagnostic-only anchor created (no author callbacks)");
    }
    void ProbeId(RED4ext::IScriptable*,RED4ext::CStackFrame* frame,int32_t* out,int64_t){
        frame->code++;
        *out=g_loaded?g_probe.Id():-1;
    }
    void ProbeRemove(RED4ext::IScriptable*,RED4ext::CStackFrame* frame,bool* out,int64_t){
        frame->code++;
        *out=g_loaded && g_probe.Remove();
        if(*out)Log("G-NativeRuntime: diagnostic-only anchor removed");
    }
    void StartSession(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, void*, int64_t) {
        frame->code++;
        g_index.ResetSession();
        g_onDemandPosition.Reset();g_positionLastReadValid=false;
    }
    void Ready(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* out, int64_t) {
        frame->code++;
        *out = g_loaded;
        Record(Lane::StaticReady,Code::Attempt);Outcome(Lane::StaticReady,*out);
    }
    void Count(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, int32_t* out, int64_t) {
        frame->code++;
        *out = g_loaded ? static_cast<int32_t>(g_index.Count()) : 0;
    }
    void Arm(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* out, int64_t) {
        int32_t id = 0; bool armed = false;
        RED4ext::GetParameter(frame, &id);
        RED4ext::GetParameter(frame, &armed);
        frame->code++;
        *out = g_loaded && g_index.SetArmed(id, armed);
        Record(Lane::StaticArm,Code::Attempt);Outcome(Lane::StaticArm,*out);
    }
    void ArmCoarseSpatial(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* out, int64_t) {
        int32_t id=0;bool armed=false;
        RED4ext::GetParameter(frame,&id);RED4ext::GetParameter(frame,&armed);frame->code++;
        *out=g_loaded&&g_index.SetArmed(id,armed,true);
        Record(Lane::StaticArmCoarse,Code::Attempt);Outcome(Lane::StaticArmCoarse,*out);
    }
    void Update(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, int32_t* out, int64_t) {
        float x=0,y=0,z=0;
        RED4ext::GetParameter(frame,&x);
        RED4ext::GetParameter(frame,&y);
        RED4ext::GetParameter(frame,&z);
        frame->code++;
        // Disabled: native Running-state owns position, never two writers.
        (void)x; (void)y; (void)z;
        *out=-1;
    }
    void State(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, int32_t* out, int64_t) {
        int32_t id=0;
        RED4ext::GetParameter(frame,&id);
        frame->code++;
        *out = g_loaded ? g_index.State(id) : -1;
    }
    void ChangeCount(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, int32_t* out, int64_t) {
        frame->code++;
        *out = g_loaded ? static_cast<int32_t>(g_index.Changes().size()) : 0;
    }
    void ChangeId(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, int32_t* out, int64_t) {
        int32_t i=-1;
        RED4ext::GetParameter(frame,&i);
        frame->code++;
        const auto changes = g_index.Changes();
        *out = g_loaded && i>=0 && static_cast<size_t>(i)<changes.size() ? changes[i].id : -1;
    }
    void ChangeNear(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* out, int64_t) {
        int32_t i=-1;
        RED4ext::GetParameter(frame,&i);
        frame->code++;
        const auto changes=g_index.Changes();
        *out = g_loaded && i>=0 && static_cast<size_t>(i)<changes.size() && changes[i].inside;
    }
    // Generation/cursor-driven cross-runtime delivery. All calls are main-thread
    // only; consuming a CET event never consumes the REDscript subscriber's event.
    void Subscribe(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0,owner=0;
        RED4ext::GetParameter(f,&consumer); RED4ext::GetParameter(f,&owner); f->code++;
        *out=g_loaded && owner>=0 && g_index.Subscribe(consumer,static_cast<uint32_t>(owner));
        Record(Lane::StaticSubscribe,Code::Attempt);Outcome(Lane::StaticSubscribe,*out);
    }
    void Unsubscribe(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=g_loaded && g_index.Unsubscribe(consumer);
        Record(Lane::StaticUnsubscribe,Code::Attempt);Outcome(Lane::StaticUnsubscribe,*out);
    }
    void SnapshotNeeded(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=!g_loaded || g_index.NeedsSnapshot(consumer);
        Record(Lane::StaticSnapshot,Code::Attempt);Record(Lane::StaticSnapshot,*out?Code::Resync:Code::Complete);
    }
    void AckSnapshot(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=g_loaded && g_index.AckSnapshot(consumer);
        Record(Lane::StaticAck,Code::Attempt);Outcome(Lane::StaticAck,*out);
    }
    void Poll(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        Record(Lane::StaticPoll,Code::Attempt);
        *out=-1;
        if (!g_loaded){Record(Lane::StaticPoll,Code::NotReady);return;}
        gcet::static_locations::TransitionJournal::Event event;
        const auto result=g_index.Poll(consumer,event);
        *out = result==gcet::static_locations::TransitionJournal::Result::EventReady ? event.id :
               (result==gcet::static_locations::TransitionJournal::Result::Empty ? 0 : -1);
        Record(Lane::StaticPoll,*out>0?Code::Event:*out==0?Code::Empty:Code::Resync);
    }
    void PolledState(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=g_loaded?g_index.LastPolledState(consumer):-1;
    }
    void PolledOwner(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=g_loaded?g_index.LastPolledOwner(consumer):-1;
    }
    void AddAnchor(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t id=0,owner=0; float x=0,y=0,z=0,radius=0; bool xyOnly=false;
        RED4ext::GetParameter(f,&id);
        RED4ext::GetParameter(f,&x);
        RED4ext::GetParameter(f,&y);
        RED4ext::GetParameter(f,&z);
        RED4ext::GetParameter(f,&radius);
        RED4ext::GetParameter(f,&xyOnly);
        RED4ext::GetParameter(f,&owner);
        f->code++;
        *out=g_loaded && owner>0 &&
            g_index.AddAnchor({id,x,y,z,radius,xyOnly,static_cast<uint32_t>(owner)});
        Record(Lane::StaticAddAnchor,Code::Attempt);Outcome(Lane::StaticAddAnchor,*out);
    }
    void OwnerCount(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t owner=0; RED4ext::GetParameter(f,&owner); f->code++;
        *out=g_loaded && owner>0 ? static_cast<int32_t>(g_index.OwnerCount(static_cast<uint32_t>(owner))) : 0;
    }
    void OwnerAnchor(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t owner=0,index=-1;
        RED4ext::GetParameter(f,&owner); RED4ext::GetParameter(f,&index); f->code++;
        *out=g_loaded && owner>0 && index>=0 ? g_index.OwnerAnchor(static_cast<uint32_t>(owner),static_cast<size_t>(index)) : -1;
    }
    void AnchorAt(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t index=-1; RED4ext::GetParameter(f,&index); f->code++;
        *out=g_loaded && index>=0 ? g_index.AnchorAt(static_cast<size_t>(index)) : -1;
    }
    void RemoveAnchor(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t id=0; RED4ext::GetParameter(f,&id); f->code++;
        *out=g_loaded && g_index.RemoveAnchor(id);
        Record(Lane::StaticRemoveAnchor,Code::Attempt);Outcome(Lane::StaticRemoveAnchor,*out);
    }
    void RemoveOwner(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t owner=0; RED4ext::GetParameter(f,&owner); f->code++;
        *out=g_loaded && owner>0 && g_index.RemoveOwner(static_cast<uint32_t>(owner));
        Record(Lane::StaticRemoveOwner,Code::Attempt);Outcome(Lane::StaticRemoveOwner,*out);
    }
    void Metric(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int64_t* out, int64_t) {
        int32_t metric=0; RED4ext::GetParameter(f,&metric); f->code++;
        if(!g_loaded){*out=-1;return;}
        switch(metric){
            case 11:*out=static_cast<int64_t>(g_coordinator.Ticks());return;
            case 12:*out=static_cast<int64_t>(g_coordinator.UnavailableTicks());return;
            case 13:*out=static_cast<int64_t>(g_coordinator.AvailabilityLosses());return;
            case 14:*out=static_cast<int64_t>(g_spatialPhysicalReads);return;
            case 15:*out=static_cast<int64_t>(g_onDemandPosition.SharedSpatialHits());return;
            default:*out=g_index.Metric(metric);return;
        }
    }
    void ObserverPrepare(RED4ext::IScriptable*,RED4ext::CStackFrame* f,bool* out,int64_t) {
        RED4ext::CName klass{},method{};
        RED4ext::GetParameter(f,&klass);RED4ext::GetParameter(f,&method);f->code++;
        auto* rtti=RED4ext::CRTTISystem::Get();
        auto* type=rtti?rtti->GetClass(klass):nullptr;
        auto* target=type?type->GetFunction(method):nullptr;
        *out=target&&target->parent&&gcet::cet_dispatch::PrepareObserver(
            reinterpret_cast<uintptr_t>(target->parent),target->fullName.hash);
    }
    void ObserverBind(RED4ext::IScriptable*,RED4ext::CStackFrame* f,int32_t* out,int64_t) {
        f->code++;*out=gcet::cet_dispatch::BindObserver();
    }
    void ObserverDemand(RED4ext::IScriptable*,RED4ext::CStackFrame* f,bool* out,int64_t) {
        int32_t handle=0;bool active=true;
        RED4ext::GetParameter(f,&handle);RED4ext::GetParameter(f,&active);f->code++;
        *out=gcet::cet_dispatch::SetObserverDemand(handle,active);
    }
    void ObserverRelease(RED4ext::IScriptable*,RED4ext::CStackFrame* f,bool* out,int64_t) {
        int32_t handle=0;RED4ext::GetParameter(f,&handle);f->code++;
        *out=gcet::cet_dispatch::ReleaseObserver(handle);
    }
    // Called by the canonical Scheduler's one-shot cleanup outside a
    // callback when a discovered native chain cannot be admitted.
    void ObserverRetire(RED4ext::IScriptable*,RED4ext::CStackFrame* f,void*,int64_t) {
        f->code++;gcet::cet_dispatch::RetireObserver();
    }
    template <typename T>
    void RegisterFunction(const char* name, RED4ext::ScriptingFunction_t<T> handler,
                          const char* outType, const char* const* paramNames,
                          const char* const* paramTypes, size_t paramCount) {
        auto rtti = RED4ext::CRTTISystem::Get();
        auto fn = RED4ext::CGlobalFunction::Create(name, name, handler);
        fn->flags = {.isNative=true, .isStatic=true};
        for (size_t i=0;i<paramCount;++i) fn->AddParam(paramTypes[i], paramNames[i]);
        if (outType) fn->SetReturnType(outType);
        rtti->RegisterFunction(fn);
    }
    void PostRegisterTypes() {
        gcet::producer::RegisterProducerReads();
        const char* armNames[]={"id","armed"};
        const char* armTypes[]={"Int32","Bool"};
        const char* positionNames[]={"x","y","z"};
        const char* positionTypes[]={"Float","Float","Float"};
        const char* idNames[]={"id"};
        const char* indexNames[]={"index"};
        const char* intTypes[]={"Int32"};
        RegisterFunction("GCETStaticReady",Ready,"Bool",nullptr,nullptr,0);
        const char* observerNames[]={"klass","method"};
        const char* observerTypes[]={"CName","CName"};
        const char* demandNames[]={"handle","active"};
        const char* demandTypes[]={"Int32","Bool"};
        const char* handleNames[]={"handle"};
        RegisterFunction("GCETObserverPrepare",ObserverPrepare,"Bool",observerNames,observerTypes,2);
        RegisterFunction("GCETObserverBind",ObserverBind,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETObserverSetDemand",ObserverDemand,"Bool",demandNames,demandTypes,2);
        RegisterFunction("GCETObserverRelease",ObserverRelease,"Bool",handleNames,intTypes,1);
        RegisterFunction("GCETObserverRetire",ObserverRetire,nullptr,nullptr,nullptr,0);
        RegisterFunction("GCETStaticCoordinatorState",CoordinatorStatus,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticLastPlayerError",LastPlayerCode,"Int32",nullptr,nullptr,0);
        const char* radiusNames[]={"radius"};
        const char* radiusTypes[]={"Float"};
        RegisterFunction("GCETStaticProbeHere",ProbeHere,"Int32",radiusNames,radiusTypes,1);
        RegisterFunction("GCETStaticProbeId",ProbeId,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticProbeRemove",ProbeRemove,"Bool",nullptr,nullptr,0);
        RegisterFunction("GCETStaticCount",Count,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticResetSession",StartSession,nullptr,nullptr,nullptr,0);
        RegisterFunction("GCETStaticArm",Arm,"Bool",armNames,armTypes,2);
        RegisterFunction("GCETStaticArmCoarseSpatialV1",ArmCoarseSpatial,"Bool",armNames,armTypes,2);
        RegisterFunction("GCETStaticUpdatePlayer",Update,"Int32",positionNames,positionTypes,3);
        RegisterFunction("GCETStaticState",State,"Int32",idNames,intTypes,1);
        RegisterFunction("GCETStaticChangeCount",ChangeCount,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticChangeId",ChangeId,"Int32",indexNames,intTypes,1);
        RegisterFunction("GCETStaticChangeNear",ChangeNear,"Bool",indexNames,intTypes,1);
        const char* consumerNames[]={"consumer"};
        const char* subscriptionNames[]={"consumer","owner"};
        const char* subscriptionTypes[]={"Int32","Int32"};
        const char* ownerNames[]={"owner"};
        const char* ownerIndexNames[]={"owner","index"};
        const char* metricNames[]={"metric"};
        RegisterFunction("GCETStaticSubscribe",Subscribe,"Bool",subscriptionNames,subscriptionTypes,2);
        RegisterFunction("GCETStaticUnsubscribe",Unsubscribe,"Bool",consumerNames,intTypes,1);
        RegisterFunction("GCETStaticNeedsSnapshot",SnapshotNeeded,"Bool",consumerNames,intTypes,1);
        RegisterFunction("GCETStaticAckSnapshot",AckSnapshot,"Bool",consumerNames,intTypes,1);
        RegisterFunction("GCETStaticPoll",Poll,"Int32",consumerNames,intTypes,1);
        RegisterFunction("GCETStaticPolledState",PolledState,"Int32",consumerNames,intTypes,1);
        RegisterFunction("GCETStaticPolledOwner",PolledOwner,"Int32",consumerNames,intTypes,1);
        const char* addNames[]={"id","x","y","z","radius","xyOnly","owner"};
        const char* addTypes[]={"Int32","Float","Float","Float","Float","Bool","Int32"};
        RegisterFunction("GCETStaticAddAnchor",AddAnchor,"Bool",addNames,addTypes,7);
        RegisterFunction("GCETStaticOwnerCount",OwnerCount,"Int32",ownerNames,intTypes,1);
        RegisterFunction("GCETStaticOwnerAnchor",OwnerAnchor,"Int32",ownerIndexNames,subscriptionTypes,2);
        RegisterFunction("GCETStaticAnchorAt",AnchorAt,"Int32",indexNames,intTypes,1);
        RegisterFunction("GCETStaticRemoveAnchor",RemoveAnchor,"Bool",idNames,intTypes,1);
        RegisterFunction("GCETStaticRemoveOwner",RemoveOwner,"Bool",ownerNames,intTypes,1);
        RegisterFunction("GCETStaticMetric",Metric,"Int64",metricNames,intTypes,1);
        // Independent player-state service; works without G-Native.locations.
        const char* pcNames[]={"consumer","fields"};
        const char* pcTypes[]={"Int32","Int32"};
        RegisterFunction("GCETPlayerApiVersion",PlayerApiVersion,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerCapabilities",PlayerCapabilities,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerConsumerCount",PlayerConsumerCount,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerSubscribe",PlayerSubscribe,"Bool",pcNames,pcTypes,2);
        RegisterFunction("GCETPlayerUnsubscribe",PlayerUnsubscribe,"Bool",consumerNames,intTypes,1);
        RegisterFunction("GCETPlayerPoll",PlayerPoll,"Int32",consumerNames,intTypes,1);
        RegisterFunction("GCETPlayerStatus",PlayerStatus,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerEpoch",PlayerEpoch,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerRevision",PlayerRevision,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerX",PlayerX,"Float",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerY",PlayerY,"Float",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerZ",PlayerZ,"Float",nullptr,nullptr,0);
        // ABI v1 additive capability bit 32 (typed Vector4). Keep all older
        // scalar/near-far API consumers compatible.
        RegisterFunction("GCETPlayerVector4",PlayerVector4,"Vector4",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerVector4Live",PlayerVector4Live,"Vector4",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerVector4Valid",PlayerVector4Valid,"Bool",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerPositionCacheHits",PlayerPositionCacheHits,"Int64",nullptr,nullptr,0);
        RegisterFunction("GCETPlayerPositionEngineReads",PlayerPositionEngineReads,"Int64",nullptr,nullptr,0);
        DiagnosticLog("G-NativeRuntime DIAG legacy/static/player RTTI registration sequence finished; typed producer registration outcomes above");

    }
    bool LoadData() {
        // The location file is co-located with this DLL under red4ext/plugins.
        // There is no filesystem search and no runtime YAML interpreter.
        HMODULE module=nullptr;
        auto addr = reinterpret_cast<LPCWSTR>(&LoadData);
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                addr, &module)) return false;
        wchar_t path[32768];
        DWORD count=GetModuleFileNameW(module,path,32768);
        if (!count || count>=32768) return false;
        auto root = std::filesystem::path(std::wstring(path,count)).parent_path();
        // The former DLL has the same RTTI exports and running callbacks.
        // Refuse to register a second owner, regardless of load order.
        // Never delete a user's existing overlay or its files.
        std::error_code ec;
        const auto legacy = root.parent_path() / L"G-CET-StaticLocations" / L"GCETStaticLocations.dll";
        if (std::filesystem::exists(legacy,ec) || ec) {
            Log("G-NativeRuntime: disable/remove the legacy G-CET-StaticLocations overlay before loading G-Engine.dll");
            return false;
        }
        const auto localLegacy = root / L"GCETStaticLocations.dll";
        if (std::filesystem::exists(localLegacy,ec) || ec) {
            Log("G-NativeRuntime: legacy DLL alongside G-Engine.dll; refusing duplicate owner");
            return false;
        }
        auto dataPath = root / L"G-Native.locations";
        g_autoProbe=std::filesystem::exists(root / L"G-Native.smoke-probe",ec);
        g_manifestStatus = gcet::static_locations::LoadOptionalManifest(dataPath,g_index);
        g_loaded = g_manifestStatus != gcet::static_locations::ManifestLoadStatus::Invalid;
        return true;
    }
}

namespace gcet::producer {
SharedSpatialStamp BeginSharedSpatialPose(RED4ext::IScriptable* receiver){
    SharedSpatialStamp stamp{};
    if(!SharedSpatialDemand())return stamp;
    stamp.generation=g_onDemandPosition.Generation();
    // The exact pose already receives the author-owned player handle.
    // Do not issue another GetPlayer here. The later spatial consumer
    // validates the actual current player against this value-only token.
    stamp.receiver=ReceiverToken(receiver);
    stamp.valid=stamp.receiver.Valid()&&ReadFrameNumber(stamp.frame)&&
        g_onDemandPosition.Generation()==stamp.generation&&SharedSpatialDemand();
    return stamp;
}
void CompleteSharedSpatialPose(const SharedSpatialStamp& stamp,static_locations::Position4 value){
    if(!stamp.valid||!SharedSpatialDemand()||g_onDemandPosition.Generation()!=stamp.generation)return;
    uint64_t frame=0;
    if(!ReadFrameNumber(frame)||frame!=stamp.frame||!SharedSpatialDemand())return;
    g_onDemandPosition.SeedSharedSpatial(frame,true,stamp.receiver,stamp.generation,value);
}
}

RED4EXT_C_EXPORT bool RED4EXT_CALL Main(RED4ext::v1::PluginHandle handle,
                                        RED4ext::v1::EMainReason reason,
                                        const RED4ext::v1::Sdk* sdk) {
    if (reason == RED4ext::v1::EMainReason::Load) {
        g_handle=handle;
        g_logger=sdk?sdk->logger:nullptr;
        gcet::diagnostics::counters.SetSink(DiagnosticLog);
        DiagnosticLog("G-NativeRuntime DIAG v2 enabled; cumulative counters; CET 1.37.1 ABI snapshots; no safety bypass");
        if(!LoadData()) { gcet::diagnostics::counters.SetSink(nullptr);g_logger=nullptr; return false; }
        if(g_loaded) {
            if(g_manifestStatus == gcet::static_locations::ManifestLoadStatus::Missing)
                Log("G-NativeRuntime: no manifest; dynamic location registration ready");
            else
                Log("G-NativeRuntime: native location manifest accepted");
            if(g_autoProbe)
                Log("G-NativeRuntime: developer smoke-probe ENABLED by marker file");
        }
        else
            Log("G-NativeRuntime: native manifest missing or invalid; cache inactive");
        RED4ext::v1::GameState running{};
        running.OnEnter=&RunningEnter;
        running.OnUpdate=&RunningUpdate;
        running.OnExit=&RunningExit;
        if(!sdk || !sdk->gameStates ||
           !sdk->gameStates->Add(handle,RED4ext::EGameStateType::Running,&running)) {
            Log("G-NativeRuntime: ERROR native running-state callback registration failed");
        } else {
            Log("G-NativeRuntime: native running-state callbacks registered");
        }
        RED4ext::CRTTISystem::Get()->AddPostRegisterCallback(PostRegisterTypes);
    } else if (reason == RED4ext::v1::EMainReason::Unload) {
        DiagnosticSummary("Unload");
        gcet::diagnostics::counters.SetSink(nullptr);
        gcet::producer::ResetProducerReads();
        gcet::cet_dispatch::RetireObserver();
        g_onDemandPosition.Reset();g_positionLastReadValid=false;
        g_playerSnapshot.Exit();g_coordinator.Exit(); g_index.Clear(); g_loaded=false;
        g_logger=nullptr;
    }
    return true;
}
RED4EXT_C_EXPORT void RED4EXT_CALL Query(RED4ext::v1::PluginInfo* info) {
    info->name=L"G-NativeRuntime";
    info->author=L"G-CET";
    info->version=RED4EXT_V1_SEMVER(0,2,0);
    info->runtime=RED4EXT_V1_RUNTIME_VERSION_LATEST;
    info->sdk=RED4EXT_V1_SDK_VERSION_CURRENT;
}
RED4EXT_C_EXPORT uint32_t RED4EXT_CALL Supports() {
    return RED4EXT_API_VERSION_1;
}
