// Isolated executable proof: actual Windows SEH dispatch/unwind, original
// scope cleanup, translated island ControlPc and synchronized patch lifetime.
// No game/CET loaded. The cooperative admission lock is a FIXTURE boundary,
// not evidence that the real CET dispatcher has such a boundary.
#define NOMINMAX
#include <windows.h>
#include "CETStacklessGate.hpp"
#include "GuardAttachment.hpp"
#include "CETCompletedUnwind.hpp"
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <mutex>
#include <new>
#include <shared_mutex>
#include <thread>
#include <vector>
#include <cstdlib>

using namespace gcet::cet_dispatch;
void Check(bool ok,const char* why){if(!ok){std::cerr<<why<<"\n";std::abort();}}
using Run=int(*)(const void*);
using namespace gcet::research;
constexpr uint32_t Guard=5,Active=16,Empty=32,OriginalEnd=48;
constexpr uint32_t Entries[]={0,128},Islands[]={512,1024};
constexpr uint32_t OriginalThunk=2048,BridgeThunk=2080,FinallyThunk=2112;
constexpr uint32_t OriginalInfos[]={2304,2432},IslandInfos[]={2560,2688};
struct alignas(8) Chain {
    std::array<std::byte,120> bytes{};
    uintptr_t callback=0x5000;
    void Word(size_t at,uintptr_t v){std::memcpy(bytes.data()+at,&v,8);}
    Chain(){
        Word(0,0x2000);Word(8,0x3000);
        auto ptr=reinterpret_cast<uintptr_t>(&callback);
        Word(24,ptr);Word(32,ptr+8);Word(40,ptr+8);
    }
    SoleBeforeIdentity Identity(){
        return CompiledChainView::Read(reinterpret_cast<uintptr_t>(bytes.data()),bytes,
            [&](uintptr_t p,uintptr_t& v)noexcept{
                if(p!=reinterpret_cast<uintptr_t>(&callback))return false;
                v=callback;return true;
            });
    }
};
struct FixtureContext {
    uint8_t* base=nullptr;
    GuardArena* arena=nullptr;
    PEXCEPTION_ROUTINE personality=nullptr;
    std::atomic<unsigned> abnormalCleanups{0},unexpectedCleanups{0},mappedSearch{0},mappedUnwind{0};
};
FixtureContext* current=nullptr;

void NTAPI Finally(BOOLEAN abnormal,void*) {
    if(abnormal)++current->abnormalCleanups;
    else ++current->unexpectedCleanups;
}
EXCEPTION_DISPOSITION NTAPI Bridge(PEXCEPTION_RECORD exception,PVOID frame,
                                  PCONTEXT context,PVOID dispatcherContext) {
    auto* dispatch=static_cast<PDISPATCHER_CONTEXT>(dispatcherContext);
    auto& f=*current;
    DISPATCHER_CONTEXT mapped{};
    for(size_t i=0;i<2;++i){
        auto& map=f.arena->Map(i);
        if(!map.Translate(*dispatch,mapped))continue;
        if(exception->ExceptionFlags&(EXCEPTION_UNWINDING|EXCEPTION_EXIT_UNWIND))
            ++f.mappedUnwind;
        else ++f.mappedSearch;
        return map.Forward(exception,frame,context,*dispatch,mapped);
    }
    Check(false,"foreign island dispatcher context");
    return ExceptionContinueSearch;
}
int FaultCall(Run run,const void* chain){
    // Separate POD-only function: MSVC disallows __try in functions requiring
    // C++ object unwinding under /EHsc.
    __try { return run(chain); }
    __except(GetExceptionCode()==EXCEPTION_ACCESS_VIOLATION?
             EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH) { return -1; }
}
void Thunk(uint8_t* dst,uintptr_t target){
    const uint8_t body[]={0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xe0};
    std::memcpy(dst,body,sizeof(body));std::memcpy(dst+2,&target,8);
}
void Word32(uint8_t* dst,uint32_t word){std::memcpy(dst,&word,4);}
void UnwindInfo(uint8_t* dst,uint8_t prologue,uint8_t offset,uint32_t handler,bool r15){
    // Version 1, EHANDLER|UHANDLER, one UWOP_PUSH_NONVOL r15,
    // padded unwind slots, followed by handler RVA.
    const uint8_t info[]={0x19,prologue,1,0,offset,r15?uint8_t{0xf0}:uint8_t{0xe0},0,0};
    std::memcpy(dst,info,sizeof(info));Word32(dst+8,handler);
}
int main(){
    auto arena=std::make_unique<GuardArena>();Check(arena->Base()!=nullptr,"fixture arena");
    auto* p=arena->Base();
    auto owner=std::make_shared<std::shared_mutex>();
    auto* nativeHandle=owner->native_handle();
    Check(sizeof(*owner)==sizeof(void*)&&reinterpret_cast<void*>(nativeHandle)==owner.get(),
          "MSVC compiled shared mutex ABI differs");
    Check(sizeof(SRWLOCK)==sizeof(*owner),"native Windows SRW storage differs");
    std::weak_ptr<std::shared_mutex> weak=owner;
    FixtureContext f;f.base=p;f.arena=arena.get();current=&f;
    auto ntdll=GetModuleHandleW(L"ntdll.dll");
    f.personality=reinterpret_cast<PEXCEPTION_ROUTINE>(GetProcAddress(ntdll,"__C_specific_handler"));
    Check(f.personality!=nullptr,"Windows C-specific handler unavailable");
    const auto base=reinterpret_cast<DWORD64>(p);
    std::array<GuardEdit,2> edits{};
    std::array<RUNTIME_FUNCTION,4> table{};
    std::array<Run,2> runs{};
    Thunk(p+OriginalThunk,reinterpret_cast<uintptr_t>(f.personality));
    Thunk(p+BridgeThunk,reinterpret_cast<uintptr_t>(&Bridge));
    Thunk(p+FinallyThunk,reinterpret_cast<uintptr_t>(&Finally));
    for(size_t i=0;i<2;++i){
        const bool r15=i==0;const auto entry=Entries[i],island=Islands[i];
        const uint8_t original[]={0x41,r15?uint8_t{0x57}:uint8_t{0x56},0x49,0x89,r15?uint8_t{0xcf}:uint8_t{0xce},
            0x41,0x80,r15?uint8_t{0x7f}:uint8_t{0x7e},0x70,0,0x0f,0x85,0x10,0,0,0};
        const uint8_t active[]={0xb8,1,0,0,0,0x41,r15?uint8_t{0x5f}:uint8_t{0x5e},0xc3};
        const uint8_t empty[]={0xb8,2,0,0,0,0x41,r15?uint8_t{0x5f}:uint8_t{0x5e},0xc3};
        std::memcpy(p+entry,original,sizeof(original));
        std::memcpy(p+entry+Active,active,sizeof(active));
        std::memcpy(p+entry+Empty,empty,sizeof(empty));
        StacklessGate gate(base+island,r15,arena->Binding().Guard(),arena->Epoch(),
                           base+entry+Active,base+entry+Empty);
        Check(gate.Code().size()<512,"gate overlap");
        std::memcpy(p+island,gate.Code().data(),gate.Code().size());
        const auto info=OriginalInfos[i],completedInfo=IslandInfos[i];
        UnwindInfo(p+info,2,2,OriginalThunk,r15);
        Word32(p+info+12,1);Word32(p+info+16,entry+Guard);
        Word32(p+info+20,entry+Active);Word32(p+info+24,FinallyThunk);Word32(p+info+28,0);
        std::vector<uint8_t> completed;
        Check(BuildCompletedFrameUnwind(std::span<const uint8_t>(p+info,12),BridgeThunk,completed),
              "completed frame metadata");
        Check(completed.size()<128,"metadata capacity");
        std::memcpy(p+completedInfo,completed.data(),completed.size());
        table[i]={entry,entry+OriginalEnd,info};
        table[i+2]={island,island+static_cast<uint32_t>(gate.Code().size()),completedInfo};
        auto& map=arena->Map(i);map.imageBase=base;map.original=table[i];map.island=table[i+2];
        map.originalHandler=f.personality;map.originalHandlerData=p+info+12;map.originalPc=base+entry+Guard;
        auto& edit=edits[i];edit.address=p+entry+Guard;
        std::memcpy(edit.stock.data(),edit.address,edit.stock.size());
        edit.installed=Redirect(base+entry+Guard,base+island);
        runs[i]=reinterpret_cast<Run>(p+entry);
    }
    GuardAttachment attachment(std::move(arena),owner,edits);
    Check(attachment.Seal(table,table.size()),"register owned two-gate arena");
    owner.reset();
    Check(!weak.expired(),"attachment did not retain lock owner");
    auto retained=weak.lock();
    Chain chain,foreign;
    auto verify=[&](const void* bytes,int expected,const char* why){
        std::shared_lock read(*retained);
        for(auto run:runs)Check(run(bytes)==expected,why);
    };
    auto faultBoth=[&](const void* bytes){
        std::shared_lock read(*retained);
        for(auto run:runs)Check(FaultCall(run,bytes)==-1,"SEH fault propagation");
    };
    auto* inaccessible=VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_NOACCESS);
    Check(inaccessible!=nullptr,"fault page");
    verify(chain.bytes.data(),1,"stock dispatch");
    faultBoth(inaccessible);Check(f.abnormalCleanups==2,"stock original finally counts");
    Check(attachment.Attach(1)==GuardResult::RolledBack,"injected attach rollback");
    verify(chain.bytes.data(),1,"rolled back attachment");
    Check(attachment.Attach()==GuardResult::Success,"attach both actual gates");
    faultBoth(inaccessible);
    Check(f.abnormalCleanups==4&&f.mappedSearch==2&&f.mappedUnwind==2,
          "both island faults must preserve search, unwind, finally");
    auto id=chain.Identity();Check(static_cast<bool>(id),"binding fixture identity");
    auto handle=attachment.Bind(id);
    Check(handle&&attachment.SetDemand(handle,false),"demand sleep");
    verify(chain.bytes.data(),2,"both gates dormant");
    verify(foreign.bytes.data(),1,"foreign chain remains active");
    Check(attachment.SetDemand(handle,true),"demand wake");
    verify(chain.bytes.data(),1,"immediate wake");
    Check(attachment.SetDemand(handle,false),"second sleep");
    {DispatchEpoch::Invalidation clear(attachment.Arena()->Epoch());
     verify(chain.bytes.data(),1,"Clear in progress");}
    verify(chain.bytes.data(),1,"reused Context after Clear");
    Check(!attachment.SetDemand(handle,false),"stale handle after reload");
    auto rebound=attachment.Bind(id);
    Check(rebound&&rebound!=handle&&attachment.SetDemand(rebound,false),"rebind new epoch");
    verify(chain.bytes.data(),2,"new epoch sleep");
    // Retirement while in flight: invalidate before refusing busy, then
    // retain all backing resources and deny further stale demand changes.
    std::atomic<bool> held=false,release=false,stop=false,invalid=false;
    std::atomic<unsigned> calls=0;
    std::thread inFlight([&]{
        std::shared_lock read(*retained);
        held.store(true,std::memory_order_release);
        while(!release.load(std::memory_order_acquire))std::this_thread::yield();
        for(auto run:runs)if(run(chain.bytes.data())!=1)invalid=true;
    });
    while(!held.load(std::memory_order_acquire))std::this_thread::yield();
    const auto busy=attachment.Retire();
    Check(busy==GuardResult::Busy&&attachment.Phase()==AttachmentPhase::Retiring&&
          attachment.Arena()->Registered()&&!attachment.SetDemand(rebound,false),"busy retirement lost resources");
    Check(!attachment.ReclaimAfterDrain(false)&&!attachment.ReclaimAfterDrain(true),"unrestored resources reclaimed");
    release.store(true,std::memory_order_release);inFlight.join();
    verify(chain.bytes.data(),1,"busy retirement must fail open");
    Check(attachment.Retire(1)==GuardResult::RolledBack&&attachment.Arena()->Registered(),
          "restore rollback retained registered arena");
    verify(chain.bytes.data(),1,"rolled-back retirement must stay active");
    Check(attachment.Retire()==GuardResult::Success,"restore both gates");
    faultBoth(inaccessible);
    Check(f.abnormalCleanups==6&&f.mappedSearch==2&&f.mappedUnwind==2&&f.unexpectedCleanups==0,
          "restored original exception semantics");
    VirtualFree(inaccessible,0,MEM_RELEASE);
    Check(!invalid,"foreign in-flight dispatch changed");
    // Preserve the existing concurrent native shared/exclusive lock proof;
    // now the real paired gate transaction and its resources are exercised.
    std::vector<std::thread> workers;
    for(int i=0;i<4;++i)workers.emplace_back([&]{
        while(!stop.load(std::memory_order_acquire)){
            {std::shared_lock read(*retained);
             for(auto run:runs)if(run(chain.bytes.data())!=1)invalid=true;
             ++calls;}
            std::this_thread::yield();
        }
    });
    while(calls.load(std::memory_order_relaxed)<100)std::this_thread::yield();
    unsigned busyCount=0;
    for(int i=0;i<32;++i){
        for(bool install:{true,false}){
            for(;;){
                auto result=install?attachment.Attach():attachment.Retire();
                if(result==GuardResult::Success)break;
                if(result!=GuardResult::Busy)std::abort();
                ++busyCount;std::this_thread::yield();
            }
        }
    }
    // Original shared_mutex reader exclusion through the same native handle.
    // Workers are stopped before this deterministic nonblocking assertion.
    Check(!attachment.ReclaimAfterDrain(false),"metadata reclaimed before drain");
    stop.store(true,std::memory_order_release);for(auto& t:workers)t.join();
    Check(!invalid&&calls>=100,"concurrent dispatch changed");
    {ExistingMutexLease lease(nativeHandle);Check(static_cast<bool>(lease),"idle original mutex lease");
     std::atomic<bool> blocked=false;
     std::thread challenger([&]{bool got=retained->try_lock_shared();blocked=!got;if(got)retained->unlock_shared();});
     challenger.join();Check(blocked,"exclusive lease failed to block shared reader");}
    Check(retained->try_lock(),"original mutex not released by adapter");retained->unlock();
    Check(attachment.Phase()==AttachmentPhase::Restored&&attachment.Arena()->Registered(),
          "restored state did not retain arena until drain");
    for(const auto& e:edits)Check(Equal(e.address,e.stock),"exact guard restoration");
    retained.reset();Check(!weak.expired(),"owner released before reclaim");
    Check(attachment.ReclaimAfterDrain(true)&&attachment.Arena()==nullptr&&weak.expired(),
          "drained table/arena/owner reclamation");
    for(auto island:Islands){
        DWORD64 foundBase=0;
        Check(RtlLookupFunctionEntry(base+island,&foundBase,nullptr)==nullptr,"stale runtime function table");
    }
    current=nullptr;f.arena=nullptr;
    std::cout<<"Integrated paired gate lifecycle: actual SEH search/unwind, demand/reload, busy retirement, "
             <<"rollback and exact restoration; 32 synchronized cycles, "<<calls.load()
             <<" concurrent calls, "<<busyCount<<" busy refusals, drained arena/table/owner PASS\n";
}
