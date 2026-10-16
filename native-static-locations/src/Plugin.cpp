#include "StaticIndex.hpp"
#include "PositionCoordinator.hpp"
#include <RED4ext/Scripting/Utils.hpp>
#include <RED4ext/Scripting/Natives/ScriptGameInstance.hpp>
#include <RED4ext/Scripting/Natives/Generated/Vector4.hpp>
#include <RED4ext/RED4ext.hpp>
#include <RED4ext/RTTITypes.hpp>
#include <RED4ext/Scripting/IScriptable.hpp>
#include <windows.h>
#include <filesystem>
#include <string>
#include <vector>

namespace {
    gcet::static_locations::StaticIndex g_index;
    bool g_loaded = false;
    gcet::static_locations::PositionCoordinator g_coordinator(g_index);

    // RED4ext's Running-state callback provides ONE native position owner.
    bool SamplePlayer(float& x,float& y,float& z){
        RED4ext::ScriptGameInstance game;
        RED4ext::Handle<RED4ext::IScriptable> player;
        if(!RED4ext::ExecuteGlobalFunction("GetPlayer;GameInstance",&player,game) || !player.instance)
            return false;
        auto* klass=player.instance->GetType();
        if(!klass)return false;
        auto* method=klass->GetFunction("GetWorldPosition");
        if(!method)return false;
        RED4ext::Vector4 pos{};
        if(!RED4ext::ExecuteFunction(player.instance,method,&pos))return false;
        x=pos.X; y=pos.Y; z=pos.Z;
        return true;
    }
    bool RunningEnter(RED4ext::CGameApplication*){
        if(g_loaded)g_coordinator.Enter();
        return true;
    }
    bool RunningUpdate(RED4ext::CGameApplication*){
        if(g_loaded){
            float x=0,y=0,z=0;
            const bool available=SamplePlayer(x,y,z);
            g_coordinator.Tick(available,x,y,z);
        }
        return false;
    }
    bool RunningExit(RED4ext::CGameApplication*){
        if(g_loaded)g_coordinator.Exit();
        return true;
    }
    void CoordinatorStatus(RED4ext::IScriptable*,RED4ext::CStackFrame* frame,int32_t* out,int64_t){
        frame->code++;
        *out=g_loaded?g_coordinator.Status():0;
    }

    void StartSession(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, void*, int64_t) {
        frame->code++;
        g_index.ResetSession();
    }
    void Ready(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* out, int64_t) {
        frame->code++;
        *out = g_loaded;
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
    }
    void Unsubscribe(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=g_loaded && g_index.Unsubscribe(consumer);
    }
    void SnapshotNeeded(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=!g_loaded || g_index.NeedsSnapshot(consumer);
    }
    void AckSnapshot(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=g_loaded && g_index.AckSnapshot(consumer);
    }
    void Poll(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int32_t* out, int64_t) {
        int32_t consumer=0; RED4ext::GetParameter(f,&consumer); f->code++;
        *out=-1;
        if (!g_loaded) return;
        gcet::static_locations::TransitionJournal::Event event;
        const auto result=g_index.Poll(consumer,event);
        *out = result==gcet::static_locations::TransitionJournal::Result::EventReady ? event.id :
               (result==gcet::static_locations::TransitionJournal::Result::Empty ? 0 : -1);
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
    }
    void RemoveOwner(RED4ext::IScriptable*, RED4ext::CStackFrame* f, bool* out, int64_t) {
        int32_t owner=0; RED4ext::GetParameter(f,&owner); f->code++;
        *out=g_loaded && owner>0 && g_index.RemoveOwner(static_cast<uint32_t>(owner));
    }
    void Metric(RED4ext::IScriptable*, RED4ext::CStackFrame* f, int64_t* out, int64_t) {
        int32_t metric=0; RED4ext::GetParameter(f,&metric); f->code++;
        if(!g_loaded){*out=-1;return;}
        switch(metric){
            case 11:*out=static_cast<int64_t>(g_coordinator.Ticks());return;
            case 12:*out=static_cast<int64_t>(g_coordinator.UnavailableTicks());return;
            case 13:*out=static_cast<int64_t>(g_coordinator.AvailabilityLosses());return;
            default:*out=g_index.Metric(metric);return;
        }
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
        const char* armNames[]={"id","armed"};
        const char* armTypes[]={"Int32","Bool"};
        const char* positionNames[]={"x","y","z"};
        const char* positionTypes[]={"Float","Float","Float"};
        const char* idNames[]={"id"};
        const char* indexNames[]={"index"};
        const char* intTypes[]={"Int32"};
        RegisterFunction("GCETStaticReady",Ready,"Bool",nullptr,nullptr,0);
        RegisterFunction("GCETStaticCoordinatorState",CoordinatorStatus,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticCount",Count,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticResetSession",StartSession,nullptr,nullptr,nullptr,0);
        RegisterFunction("GCETStaticArm",Arm,"Bool",armNames,armTypes,2);
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
    }
    void LoadData() {
        // The location file is co-located with this DLL under red4ext/plugins.
        // There is no filesystem search and no runtime YAML interpreter.
        HMODULE module=nullptr;
        auto addr = reinterpret_cast<LPCWSTR>(&LoadData);
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                addr, &module)) return;
        wchar_t path[32768];
        DWORD count=GetModuleFileNameW(module,path,32768);
        if (!count || count>=32768) return;
        auto dataPath = std::filesystem::path(std::wstring(path,count)).parent_path() / L"G-CET.locations";
        std::vector<gcet::static_locations::Anchor> anchors;
        if (!gcet::static_locations::ReadCompiledFile(dataPath,anchors)) return;
        g_loaded = g_index.Load(anchors);
    }
}

RED4EXT_C_EXPORT bool RED4EXT_CALL Main(RED4ext::v1::PluginHandle handle,
                                        RED4ext::v1::EMainReason reason,
                                        const RED4ext::v1::Sdk* sdk) {
    RED4EXT_UNUSED_PARAMETER(handle);
    RED4EXT_UNUSED_PARAMETER(sdk);
    if (reason == RED4ext::v1::EMainReason::Load) {
        LoadData();
        RED4ext::v1::GameState running{};
        running.OnEnter=&RunningEnter;
        running.OnUpdate=&RunningUpdate;
        running.OnExit=&RunningExit;
        sdk->gameStates->Add(handle,RED4ext::EGameStateType::Running,&running);
        RED4ext::CRTTISystem::Get()->AddPostRegisterCallback(PostRegisterTypes);
    } else if (reason == RED4ext::v1::EMainReason::Unload) {
        g_coordinator.Exit(); g_index.Clear(); g_loaded=false;
    }
    return true;
}
RED4EXT_C_EXPORT void RED4EXT_CALL Query(RED4ext::v1::PluginInfo* info) {
    info->name=L"G-CET Static Locations (experimental)";
    info->author=L"G-CET";
    info->version=RED4EXT_V1_SEMVER(0,1,0);
    info->runtime=RED4EXT_V1_RUNTIME_VERSION_LATEST;
    info->sdk=RED4EXT_V1_SDK_VERSION_CURRENT;
}
RED4EXT_C_EXPORT uint32_t RED4EXT_CALL Supports() {
    return RED4EXT_API_VERSION_1;
}
