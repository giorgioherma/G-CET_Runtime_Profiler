#define NOMINMAX
#include "CETLiveObserver.hpp"
#include "CETDispatchIdentity.hpp"
#include "CETGuardTransaction.hpp"
#include "CETStacklessGate.hpp"
#include "CETObserverIslands.hpp"
#include <windows.h>
#include <atomic>
#include <array>
#include <vector>
#include <memory>
#include <filesystem>
#include <limits>
#include <cstring>

namespace gcet::cet_dispatch {
namespace {
constexpr uint32_t ExecuteRva=0x1092d0,ClearRva=0x107f0c,ClearStart=0x107ee0;
constexpr uint32_t ImageSize=7536640,PersonalityRva=0x564a28;
constexpr std::array<uint8_t,5> ClearStock{0x49,0x8b,0x5c,0x24,0x30};
struct Site {uint32_t guard,active,empty,start,end,unwind;bool r15;PatchBytes bytes;};
constexpr Site Sites[]={
    {0x10834f,0x10835a,0x1087e9,0x1080b0,0x108882,0x675204,true,
     {0x41,0x80,0x7f,0x70,0,0x0f,0x85,0x8f,0x04,0,0}},
    {0x108b41,0x108b4c,0x109200,0x108890,0x1092c8,0x6752c4,false,
     {0x41,0x80,0x7e,0x70,0,0x0f,0x85,0xb4,0x06,0,0}}
};
using ExecuteFn=bool (*)(const void*,void*,void*,void*,void*,void*,void*,void*,char*,uint8_t);
struct ModuleRef {
    HMODULE h=nullptr;
    ~ModuleRef(){if(h)FreeLibrary(h);}
};
struct Discovery {std::atomic<uint8_t> armed{0};uint64_t fullName=0;};
static_assert(sizeof(std::atomic<uint8_t>)==1&&std::atomic<uint8_t>::is_always_lock_free);
struct Session {
    ModuleRef cet,self;
    uintptr_t root=0,parent=0;
    uint64_t fullName=0;
    DWORD owner=0;
    void* mutex=nullptr;
    ExecuteFn execute=nullptr;
    std::unique_ptr<GuardArena> arena;
    Discovery* discovery=nullptr;
    std::unique_ptr<GuardTransaction<5>> patches;
    std::atomic<unsigned> scopes{0},handlers{0};
    std::atomic<bool> retiring{false};
};
std::atomic<Session*> session{nullptr};
std::atomic<ExecuteFn> originalEntry{nullptr};
std::atomic<bool> closed{false};
// Control-only serialization. Never acquired by a dispatcher, callback
// scope, demand transition, or exception handler.
SRWLOCK lifecycle=SRWLOCK_INIT;
thread_local const void* currentChain=nullptr;

template<class T> bool Read(uintptr_t p,T& value) noexcept {
    SIZE_T read=0;
    return p&&p<=UINTPTR_MAX-sizeof(T)&&
           ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(p),&value,sizeof(T),&read)&&read==sizeof(T);
}
bool Root(Session& s) noexcept {
    const auto base=reinterpret_cast<uintptr_t>(s.cet.h);
    uintptr_t value=0;
    return InspectOwnerSnapshot(s.cet.h)==OwnerStatus::ReadySnapshot&&
           Read(base+OwnerLayout::RootSlot,value)&&value==s.root;
}
bool Target(Session& s,const SoleBeforeIdentity& id)noexcept {
    if(!id||id.scripting!=s.root+OwnerLayout::ScriptingOffset)return false;
    uintptr_t parent=0;uint64_t name=0;
    // Pinned RED4ext SDK CBaseFunction::fullName +8 and class parent +0xb0.
    return Read(id.trampoline+8,name)&&Read(id.trampoline+0xb0,parent)&&
           parent==s.parent&&name==s.fullName;
}
SoleBeforeIdentity Chain(const void* chain)noexcept {
    std::array<std::byte,CompiledChainView::Size> bytes{};
    if(!Read(reinterpret_cast<uintptr_t>(chain),bytes))return {};
    return CompiledChainView::Read(reinterpret_cast<uintptr_t>(chain),bytes,
                                  [](uintptr_t at,uintptr_t& out)noexcept{return Read(at,out);});
}
// The two callsites execute with the original shared SRW ownership.
// The tail enters here only during discovery; no copied CET prologue.
bool ExecuteScope(const void* chain,void* lock,void* context,void* args,void* result,
                  void* out,void* stack,void* frame,char* code,uint8_t parameter){
    const auto original=originalEntry.load(std::memory_order_acquire);
    auto* s=session.load(std::memory_order_acquire);
    if(!s||s->retiring.load(std::memory_order_acquire)||
       s->owner!=GetCurrentThreadId()||!s->discovery->armed.load(std::memory_order_acquire)||
       !Target(*s,Chain(chain)))
        return original(chain,lock,context,args,result,out,stack,frame,code,parameter);
    const void* previous=currentChain;
    s->scopes.fetch_add(1,std::memory_order_acq_rel);
    currentChain=chain;
    bool answer=false;
    __try {answer=original(chain,lock,context,args,result,out,stack,frame,code,parameter);}
    __finally {currentChain=previous;s->scopes.fetch_sub(1,std::memory_order_release);}
    return answer;
}
EXCEPTION_DISPOSITION NTAPI Bridge(PEXCEPTION_RECORD record,PVOID frame,PCONTEXT context,PVOID opaque){
    auto* s=session.load(std::memory_order_acquire);
    if(!s)return ExceptionContinueSearch;
    s->handlers.fetch_add(1,std::memory_order_acq_rel);
    auto* dispatch=static_cast<PDISPATCHER_CONTEXT>(opaque);
    DISPATCHER_CONTEXT mapped{};
    EXCEPTION_DISPOSITION result=ExceptionContinueSearch;
    __try {
        for(size_t i=0;i<2;++i){
            auto& map=s->arena->Map(i);
            if(map.Translate(*dispatch,mapped)){result=map.Forward(record,frame,context,*dispatch,mapped);break;}
        }
    }__finally{s->handlers.fetch_sub(1,std::memory_order_release);}
    return result;
}
std::unique_ptr<GuardArena> NearArena(uintptr_t base){
    SYSTEM_INFO info{};GetSystemInfo(&info);const uintptr_t gran=info.dwAllocationGranularity;
    if(!gran||base>UINTPTR_MAX-0x70000000)return {};
    auto align=[&](uintptr_t x){return (x+gran-1)&~(gran-1);};
    uintptr_t at=align(base+ImageSize),limit=base+0x70000000;
    while(at<limit){
        MEMORY_BASIC_INFORMATION mbi{};
        if(VirtualQuery(reinterpret_cast<void*>(at),&mbi,sizeof(mbi))!=sizeof(mbi))return {};
        const auto end=reinterpret_cast<uintptr_t>(mbi.BaseAddress)+mbi.RegionSize;
        if(end<=at)return {};
        if(mbi.State==MEM_FREE&&end-at>=8192){
            auto a=std::make_unique<GuardArena>(reinterpret_cast<void*>(at),base);
            if(a->Base())return a;
        }
        at=align(end);
    }
    return {};
}
uint32_t RVA(uintptr_t base,uintptr_t address){
    GuardRequire(address>=base&&address-base<=UINT32_MAX,"observer RVA");
    return static_cast<uint32_t>(address-base);
}
uint32_t FindExecuteCall(const uint8_t* image,const Site& site){
    uint32_t found=0;
    // The exact-image admission and matched-PDB CI prove this unique E8
    // search against actual decoded boundaries and locked CFG paths.
    for(uint32_t r=site.start;r+5<=site.end;++r){
        if(image[r]!=0xe8)continue;
        int32_t delta=0;std::memcpy(&delta,image+r+1,4);
        if(int64_t(r)+5+delta!=ExecuteRva)continue;
        GuardRequire(!found,"ambiguous ExecuteChain callsite");found=r;
    }
    GuardRequire(found!=0,"missing ExecuteChain callsite");return found;
}
RUNTIME_FUNCTION Original(uintptr_t base,uint32_t pc,uint32_t begin,uint32_t unwind){
    DWORD64 actualBase=0;auto* entry=RtlLookupFunctionEntry(base+pc,&actualBase,nullptr);
    GuardRequire(entry&&actualBase==base&&entry->BeginAddress==begin&&
                 entry->UnwindData==unwind&&pc<entry->EndAddress,"original CET unwind identity");
    return *entry;
}
std::vector<uint8_t> Completed(uintptr_t base,const RUNTIME_FUNCTION& original,uint32_t guard,
                               uint32_t handlerRva,uint32_t originalHandler){
    const auto* info=reinterpret_cast<const uint8_t*>(base+original.UnwindData);
    GuardRequire(guard-original.BeginAddress>info[1],"incomplete original prologue");
    const size_t end=4+2*((info[2]+1)&~1u);
    if(handlerRva){
        uint32_t handler=0;std::memcpy(&handler,info+end,4);
        GuardRequire(handler==originalHandler,"original CET personality changed");
    }
    std::vector<uint8_t> completed;
    GuardRequire(BuildCompletedFrameUnwind(std::span<const uint8_t>(info,end+(handlerRva?4:0)),
                                           handlerRva,completed),"completed CET frame");
    return completed;
}
bool PrepareCode(Session& s){
    auto* image=reinterpret_cast<uint8_t*>(s.cet.h);
    const auto base=reinterpret_cast<uintptr_t>(image);
    s.arena=NearArena(base);if(!s.arena)return false;
    auto* p=s.arena->Base();const auto address=reinterpret_cast<uintptr_t>(p);
    s.discovery=new(p+4096+1024) Discovery;
    s.discovery->fullName=s.fullName;
    std::array<RUNTIME_FUNCTION,4> functions{};
    std::array<GuardEdit,5> edits{};
    constexpr uint32_t BridgeThunk=3328,CallThunk=3072,ClearIsland=2048,ClearInfo=2816;
    AbsoluteTail(p+BridgeThunk,reinterpret_cast<uintptr_t>(&Bridge));
    EmitDiscoveryTail(p+CallThunk,reinterpret_cast<uintptr_t>(&s.discovery->armed),
                      reinterpret_cast<uintptr_t>(&s.arena->Binding().Guard().owner),
                      reinterpret_cast<uintptr_t>(&s.discovery->fullName),
                      reinterpret_cast<uintptr_t>(&ExecuteScope),base+ExecuteRva);
    for(size_t i=0;i<2;++i){
        const auto& site=Sites[i];const uint32_t island=static_cast<uint32_t>(i*1024);
        const uint32_t metadata=2560+static_cast<uint32_t>(i*128);
        GuardRequire(Equal(image+site.guard,site.bytes),"CET guard bytes changed");
        StacklessGate gate(address+island,site.r15,s.arena->Binding().Guard(),s.arena->Epoch(),
                           base+site.active,base+site.empty);
        GuardRequire(gate.Code().size()<1024,"observer gate capacity");
        std::memcpy(p+island,gate.Code().data(),gate.Code().size());
        const auto original=Original(base,site.guard,site.start,site.unwind);
        GuardRequire(original.EndAddress==site.end,"CET dispatcher extent changed");
        auto completed=Completed(base,original,site.guard,RVA(base,address+BridgeThunk),PersonalityRva);
        GuardRequire(completed.size()<128,"observer unwind capacity");
        std::memcpy(p+metadata,completed.data(),completed.size());
        functions[i]={RVA(base,address+island),RVA(base,address+island+gate.Code().size()),
                      RVA(base,address+metadata)};
        auto& map=s.arena->Map(i);
        const auto* info=reinterpret_cast<const uint8_t*>(base+original.UnwindData);
        const size_t end=4+2*((info[2]+1)&~1u);
        map.imageBase=base;map.original=original;map.island=functions[i];
        map.originalPc=base+site.guard;map.originalHandler=reinterpret_cast<PEXCEPTION_ROUTINE>(base+PersonalityRva);
        map.originalHandlerData=const_cast<uint8_t*>(info+end+4);
        edits[i]={image+site.guard,site.bytes,Redirect(base+site.guard,address+island)};
        const auto call=FindExecuteCall(image,site);
        auto& edit=edits[i+2];edit.address=image+call;edit.length=5;
        std::memcpy(edit.stock.data(),edit.address,5);
        edit.installed=Redirect(base+call,address+CallThunk);edit.installed[0]=0xe8;
    }
    GuardRequire(std::memcmp(image+ClearRva,ClearStock.data(),5)==0,"CET Clear locked span changed");
    EmitLockedClear(p+ClearIsland,reinterpret_cast<uintptr_t>(&s.arena->Epoch().ClearDepthWord()),
                    reinterpret_cast<uintptr_t>(&s.discovery->armed),ClearStock,base+ClearRva+5);
    auto clear=Original(base,ClearRva,ClearStart,0x6751c4);
    auto completed=Completed(base,clear,ClearRva,0,0);
    GuardRequire(completed.size()<128,"Clear completed unwind capacity");
    std::memcpy(p+ClearInfo,completed.data(),completed.size());
    functions[2]={RVA(base,address+ClearIsland),RVA(base,address+ClearIsland+LockedClearIslandSize),
                  RVA(base,address+ClearInfo)};
    edits[4].address=image+ClearRva;edits[4].length=5;
    std::memcpy(edits[4].stock.data(),ClearStock.data(),5);
    edits[4].installed=Redirect(base+ClearRva,address+ClearIsland);
    s.patches=std::make_unique<GuardTransaction<5>>(s.mutex,s.cet.h,MEM_IMAGE,edits);
    return s.arena->Seal(functions,3);
}
void Disable(Session& s)noexcept {
    s.retiring.store(true,std::memory_order_release);
    if(s.arena)s.arena->Epoch().Disable();
    if(s.discovery)s.discovery->armed.store(0,std::memory_order_release);
}
void RetainOnUncertain(std::unique_ptr<Session>& s)noexcept {
    if(!s)return;
    Disable(*s);
    session.store(s.release(),std::memory_order_release);
    closed.store(true,std::memory_order_release);
}
}

bool PrepareObserver(uintptr_t parent,uint64_t fullName) noexcept {
    if(!parent||!fullName||closed.load(std::memory_order_acquire))return false;
    ExistingMutexLease control(&lifecycle);
    if(!control||closed.load(std::memory_order_acquire))return false;
    if(auto* s=session.load(std::memory_order_acquire)){
        if(s->retiring||s->owner!=GetCurrentThreadId()||s->parent!=parent||s->fullName!=fullName)return false;
        if(!s->arena->Epoch().Matches(s->arena->Binding().Guard().token))
            s->discovery->armed.store(1,std::memory_order_release);
        if(closed.load(std::memory_order_acquire)){Disable(*s);return false;}
        return true;
    }
    std::unique_ptr<Session> s;
    try{
        s=std::make_unique<Session>();
        if(!GetModuleHandleExW(0,L"cyber_engine_tweaks.asi",&s->cet.h))return false;
        wchar_t path[32768];
        const auto n=GetModuleFileNameW(s->cet.h,path,static_cast<DWORD>(std::size(path)));
        if(!n||n>=std::size(path)||CheckImage(std::filesystem::path(std::wstring(path,n)),s->cet.h)!=Identity::ExactImage)
            return false;
        const auto base=reinterpret_cast<uintptr_t>(s->cet.h);
        if(!Read(base+OwnerLayout::RootSlot,s->root)||!Root(*s))return false;
        // Exact upstream owner persists through normal loaded lifetime:
        // hold ordinary module references, not permanent Windows PINs.
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
              reinterpret_cast<LPCWSTR>(&PrepareObserver),&s->self.h))return false;
        s->parent=parent;s->fullName=fullName;s->owner=GetCurrentThreadId();
        s->mutex=reinterpret_cast<void*>(s->root+OwnerLayout::MutexOffset);
        s->execute=reinterpret_cast<ExecuteFn>(base+ExecuteRva);
        ExistingMutexLease lease(s->mutex);
        if(!lease||!Root(*s))return false;
        if(CheckImage(std::filesystem::path(std::wstring(path,n)),s->cet.h)!=Identity::ExactImage)return false;
        if(!PrepareCode(*s)){
            if(s->arena&&s->arena->MustRetain())RetainOnUncertain(s);
            return false;
        }
        if(closed.load(std::memory_order_acquire)){
            if(!s->arena->ReclaimMetadataAfterDrain())RetainOnUncertain(s);
            return false;
        }
        originalEntry.store(s->execute,std::memory_order_release);
        const auto result=s->patches->Change(lease,true);
        if(result==GuardResult::Success){
            auto* stable=s.release();
            stable->discovery->armed.store(1,std::memory_order_release);
            session.store(stable,std::memory_order_release);
            // No guard/callsite executes until the existing exclusive
            // lease releases. Retirement can now safely see stable state.
            if(closed.load(std::memory_order_acquire)){Disable(*stable);return false;}
            return true;
        }
        if(s->patches->MustRetain()||!s->arena->ReclaimMetadataAfterDrain()){
            RetainOnUncertain(s);return false;
        }
        return false;
    }catch(...){
        if(s&&s->arena&&s->arena->MustRetain())RetainOnUncertain(s);
        return false;
    }
}
int32_t BindObserver() noexcept {
    auto* s=session.load(std::memory_order_acquire);
    if(!s||s->retiring||closed||s->owner!=GetCurrentThreadId())return 0;
    if(!currentChain||!s->discovery->armed.load(std::memory_order_acquire)){
        s->discovery->armed.store(0,std::memory_order_release);return 0;
    }
    auto id=Chain(currentChain);
    if(!Target(*s,id)){s->discovery->armed.store(0,std::memory_order_release);return 0;}
    auto& epoch=s->arena->Epoch();
    if(!epoch.Current()&&!epoch.CompleteLockedClear())return 0;
    const auto handle=s->arena->Binding().Bind(s->owner,epoch.Current(),id);
    s->discovery->armed.store(0,std::memory_order_release);
    if(handle>INT32_MAX||s->retiring||closed){epoch.Disable();return 0;}
    return static_cast<int32_t>(handle);
}
bool SetObserverDemand(int32_t handle,bool active) noexcept {
    auto* s=session.load(std::memory_order_acquire);
    if(!s||s->retiring||closed)return false;
    const bool ok=handle>0&&s->arena->Binding().SetDemand(GetCurrentThreadId(),static_cast<uint64_t>(handle),active);
    // A failed wake must never leave an earlier dormant authorization.
    if(!ok)s->arena->Epoch().Disable();
    return ok;
}
bool ReleaseObserver(int32_t handle) noexcept {
    auto* s=session.load(std::memory_order_acquire);
    if(!s)return false;
    s->discovery->armed.store(0,std::memory_order_release);
    const bool ok=handle>0&&s->arena->Binding().Release(GetCurrentThreadId(),static_cast<uint64_t>(handle));
    if(!ok)s->arena->Epoch().Disable();
    return ok;
}
void RetireObserver() noexcept {
    closed.store(true,std::memory_order_release);
    ExistingMutexLease control(&lifecycle);
    auto* s=session.load(std::memory_order_acquire);
    if(!s)return;
    Disable(*s);
    if(!control)return; // stable published state retained; no unsafe mutation
    if(!Root(*s))return;
    ExistingMutexLease lease(s->mutex);
    if(!lease||!Root(*s)||s->patches->Change(lease,false)!=GuardResult::Success)return;
    if(s->scopes.load(std::memory_order_acquire)||s->handlers.load(std::memory_order_acquire))return;
    if(!s->arena->ReclaimMetadataAfterDrain())return;
    // Unload may be a loader callback. Keep the single bounded session and
    // normal module references until process exit, rather than FreeLibrary
    // inside loader teardown or race a previously entered native API.
    session.store(nullptr,std::memory_order_release);
}
}
