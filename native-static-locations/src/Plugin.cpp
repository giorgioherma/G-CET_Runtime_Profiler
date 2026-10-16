#include "StaticIndex.hpp"
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
        // Exactly one coordinator should call this with the authoritative player
        // position each engine tick. Invalid position is fail-open (unknown).
        *out = g_loaded && g_index.Update(x,y,z) ?
            static_cast<int32_t>(g_index.Generation()) : -1;
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
        RegisterFunction("GCETStaticCount",Count,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticResetSession",StartSession,nullptr,nullptr,nullptr,0);
        RegisterFunction("GCETStaticArm",Arm,"Bool",armNames,armTypes,2);
        RegisterFunction("GCETStaticUpdatePlayer",Update,"Int32",positionNames,positionTypes,3);
        RegisterFunction("GCETStaticState",State,"Int32",idNames,intTypes,1);
        RegisterFunction("GCETStaticChangeCount",ChangeCount,"Int32",nullptr,nullptr,0);
        RegisterFunction("GCETStaticChangeId",ChangeId,"Int32",indexNames,intTypes,1);
        RegisterFunction("GCETStaticChangeNear",ChangeNear,"Bool",indexNames,intTypes,1);
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
        RED4ext::CRTTISystem::Get()->AddPostRegisterCallback(PostRegisterTypes);
    } else if (reason == RED4ext::v1::EMainReason::Unload) {
        g_index.Clear(); g_loaded=false;
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
