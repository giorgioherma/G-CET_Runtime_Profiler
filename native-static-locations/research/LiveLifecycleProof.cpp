// Isolated executable proof: actual Windows SEH dispatch/unwind, original
// scope cleanup, translated island ControlPc and synchronized patch lifetime.
// No game/CET loaded. The cooperative admission lock is a FIXTURE boundary,
// not evidence that the real CET dispatcher has such a boundary.
#define NOMINMAX
#include <windows.h>
#include "CETStacklessGate.hpp"
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
constexpr uint32_t Guard=5,Active=16,Empty=32,OriginalEnd=48,Island=128;
constexpr uint32_t OriginalThunk=896,BridgeThunk=928,FinallyThunk=960;
constexpr uint32_t OriginalInfo=1024,IslandInfo=1088;
struct Data {
    DispatchEpoch epoch;
    ObserverBinding binding{epoch,GetCurrentThreadId()};
};
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
    RUNTIME_FUNCTION functions[2]{};
    PEXCEPTION_ROUTINE personality=nullptr;
    std::atomic<unsigned> abnormalCleanups{0},unexpectedCleanups{0},mappedSearch{0},mappedUnwind{0};
};
FixtureContext* current=nullptr;

void NTAPI Finally(BOOLEAN abnormal,void*) {
    if(abnormal)++current->abnormalCleanups;
    else ++current->unexpectedCleanups;
}
// For the fixture's finally-only scope, translate the dispatch context before
// forwarding to the REAL Windows C-specific handler. The actual fault PC in
// EXCEPTION_RECORD is deliberately left untouched for diagnostics.
EXCEPTION_DISPOSITION NTAPI Bridge(PEXCEPTION_RECORD exception,ULONG64 frame,
                                  PCONTEXT context,PDISPATCHER_CONTEXT dispatch) {
    auto& f=*current;
    const auto base=reinterpret_cast<DWORD64>(f.base);
    Check(dispatch->ImageBase==base&&dispatch->ControlPc>=base+Island&&
          dispatch->ControlPc<base+f.functions[1].EndAddress,"unexpected island dispatcher context");
    DISPATCHER_CONTEXT mapped=*dispatch;
    mapped.ControlPc=base+Guard;
    mapped.FunctionEntry=&f.functions[0];
    mapped.HandlerData=f.base+OriginalInfo+12;
    mapped.LanguageHandler=f.personality;
    if(exception->ExceptionFlags&(EXCEPTION_UNWINDING|EXCEPTION_EXIT_UNWIND))
        ++f.mappedUnwind;
    else ++f.mappedSearch;
    auto result=f.personality(exception,frame,context,&mapped);
    dispatch->ScopeIndex=mapped.ScopeIndex;
    return result;
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
void UnwindInfo(uint8_t* dst,uint8_t prologue,uint8_t offset,uint32_t handler){
    // Version 1, EHANDLER|UHANDLER, one UWOP_PUSH_NONVOL r15,
    // padded unwind slots, followed by handler RVA.
    const uint8_t info[]={0x19,prologue,1,0,offset,0xf0,0,0};
    std::memcpy(dst,info,sizeof(info));Word32(dst+8,handler);
}
struct Arena{
    uint8_t* p=static_cast<uint8_t*>(VirtualAlloc(nullptr,8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    ~Arena(){if(p)VirtualFree(p,0,MEM_RELEASE);}
};
class FixturePatch{
    uint8_t* code_;
    std::array<uint8_t,11> stock_{},jump_{};
    bool installed_=false;
public:
    // Only callers holding the fixture's exclusive admission lock may write.
    explicit FixturePatch(uint8_t* code):code_(code){
        std::memcpy(stock_.data(),code+Guard,stock_.size());
        jump_.fill(0x90);jump_[0]=0xe9;
        const int32_t delta=Island-(Guard+5);std::memcpy(jump_.data()+1,&delta,4);
    }
    void Set(bool enabled){
        if(enabled==installed_)return;
        auto& expected=installed_?jump_:stock_;
        auto& next=enabled?jump_:stock_;
        Check(std::memcmp(code_+Guard,expected.data(),expected.size())==0,"fixture code drift");
        DWORD old=0,unused=0;
        Check(VirtualProtect(code_,4096,PAGE_READWRITE,&old)!=0&&old==PAGE_EXECUTE_READ,"fixture write protection");
        std::memcpy(code_+Guard,next.data(),next.size());
        Check(VirtualProtect(code_,4096,old,&unused)!=0,"fixture RX restore");
        Check(FlushInstructionCache(GetCurrentProcess(),code_,4096)!=0,"fixture instruction flush");
        installed_=enabled;
    }
    bool Stock() const {return !installed_&&std::memcmp(code_+Guard,stock_.data(),stock_.size())==0;}
};
int main(){
    Arena arena;Check(arena.p!=nullptr,"fixture arena");
    auto* p=arena.p;auto* data=new(p+4096) Data;
    FixtureContext f;f.base=p;current=&f;
    auto ntdll=GetModuleHandleW(L"ntdll.dll");
    f.personality=reinterpret_cast<PEXCEPTION_ROUTINE>(GetProcAddress(ntdll,"__C_specific_handler"));
    Check(f.personality!=nullptr,"Windows C-specific handler unavailable");
    // Real original guard and two returns. Push r15; mov r15,rcx;
    // cmp byte ptr [r15+70h],0; jne empty.
    const uint8_t original[]={0x41,0x57,0x49,0x89,0xcf,
        0x41,0x80,0x7f,0x70,0,0x0f,0x85,0x10,0,0,0};
    std::memcpy(p,original,sizeof(original));
    const uint8_t active[]={0xb8,1,0,0,0,0x41,0x5f,0xc3};
    const uint8_t empty[]={0xb8,2,0,0,0,0x41,0x5f,0xc3};
    std::memcpy(p+Active,active,sizeof(active));std::memcpy(p+Empty,empty,sizeof(empty));
    StacklessGate gate(reinterpret_cast<uintptr_t>(p+Island),true,data->binding.Guard(),data->epoch,
                       reinterpret_cast<uintptr_t>(p+Active),reinterpret_cast<uintptr_t>(p+Empty));
    Check(Island+gate.Code().size()<OriginalThunk,"gate overlaps handler thunks");
    std::memcpy(p+Island,gate.Code().data(),gate.Code().size());
    Thunk(p+OriginalThunk,reinterpret_cast<uintptr_t>(f.personality));
    Thunk(p+BridgeThunk,reinterpret_cast<uintptr_t>(&Bridge));
    Thunk(p+FinallyThunk,reinterpret_cast<uintptr_t>(&Finally));
    UnwindInfo(p+OriginalInfo,2,2,OriginalThunk);
    // Real SCOPE_TABLE_AMD64: one termination handler in original guard.
    Word32(p+OriginalInfo+12,1);
    Word32(p+OriginalInfo+16,Guard);
    Word32(p+OriginalInfo+20,Active);
    Word32(p+OriginalInfo+24,FinallyThunk);
    Word32(p+OriginalInfo+28,0);
    // The island establishes NO frame: use equivalent completed prologue
    // unwind slots and our translating language-handler entry.
    UnwindInfo(p+IslandInfo,0,0,BridgeThunk);
    f.functions[0]={0,OriginalEnd,OriginalInfo};
    f.functions[1]={Island,Island+static_cast<uint32_t>(gate.Code().size()),IslandInfo};
    Check(RtlAddFunctionTable(f.functions,2,reinterpret_cast<DWORD64>(p))!=FALSE,"register fixture unwind table");
    DWORD old=0;Check(VirtualProtect(p,4096,PAGE_EXECUTE_READ,&old)!=0,"fixture code RX");
    Check(FlushInstructionCache(GetCurrentProcess(),p,4096)!=0,"initial flush");
    const auto run=reinterpret_cast<Run>(p);
    FixturePatch patch(p);
    Chain chain;
    auto* inaccessible=VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_NOACCESS);
    Check(inaccessible!=nullptr,"fault page");
    Check(run(chain.bytes.data())==1,"stock dispatch");
    Check(FaultCall(run,inaccessible)==-1&&f.abnormalCleanups==1,"stock exception did not run original finally once");
    patch.Set(true);
    Check(FaultCall(run,inaccessible)==-1&&f.abnormalCleanups==2&&f.mappedSearch==1&&f.mappedUnwind==1,
          "island fault did not translate search and unwind to original finally");
    auto id=chain.Identity();Check(static_cast<bool>(id),"binding fixture identity");
    auto handle=data->binding.Bind(GetCurrentThreadId(),data->epoch.Current(),id);
    Check(handle&&data->binding.SetDemand(GetCurrentThreadId(),handle,false),"dormant binding");
    Check(run(chain.bytes.data())==2,"live patched fixture bypass");
    Check(data->binding.SetDemand(GetCurrentThreadId(),handle,true)&&run(chain.bytes.data())==1,"wake");
    patch.Set(false);
    Check(FaultCall(run,inaccessible)==-1&&f.abnormalCleanups==3&&f.mappedSearch==1&&f.mappedUnwind==1,
          "restored exception semantics");
    Check(f.unexpectedCleanups==0,"unexpected termination cleanup");
    std::cout<<"Real SEH stock/island/restored faults, translated search and unwind, original finally exactly once PASS\n";
    VirtualFree(inaccessible,0,MEM_RELEASE);

    // Fixture-owned admission boundary, not a guessed live CET lock.
    // Hold one call lease while attempting exclusive mutation, then stress
    // real running machine code across repeated install/restore cycles.
    std::shared_mutex admission;
    std::atomic<bool> held=false,release=false,stop=false;
    std::atomic<unsigned> calls=0;
    std::thread inFlight([&]{
        std::shared_lock read(admission);
        held.store(true,std::memory_order_release);
        while(!release.load(std::memory_order_acquire))std::this_thread::yield();
        Check(run(chain.bytes.data())==1,"held dispatch");
    });
    while(!held.load(std::memory_order_acquire))std::this_thread::yield();
    Check(!admission.try_lock(),"attachment admitted while dispatch lease held");
    release.store(true,std::memory_order_release);inFlight.join();
    std::vector<std::thread> workers;
    for(int i=0;i<4;++i)workers.emplace_back([&]{
        while(!stop.load(std::memory_order_acquire)){
            {
                std::shared_lock read(admission);
                Check(run(chain.bytes.data())==1,"concurrent dispatch changed");
                calls.fetch_add(1,std::memory_order_relaxed);
            }
            std::this_thread::yield();
        }
    });
    while(calls.load(std::memory_order_relaxed)<100)std::this_thread::yield();
    for(int i=0;i<32;++i){
        {
            std::unique_lock write(admission);
            patch.Set(true);Check(run(chain.bytes.data())==1,"active owner after attach");
        }
        std::this_thread::yield();
        {
            std::unique_lock write(admission);
            patch.Set(false);Check(patch.Stock(),"stock bytes after concurrent restore");
        }
    }
    stop.store(true,std::memory_order_release);
    for(auto& t:workers)t.join();
    // Only after admission is closed and all users joined is metadata/code
    // eligible for removal. Check actual OS table removal.
    Check(patch.Stock()&&RtlDeleteFunctionTable(f.functions)!=FALSE,"teardown registration");
    DWORD64 base=0;
    Check(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(p+Island),&base,nullptr)==nullptr,"stale function table");
    data->~Data();current=nullptr;
    std::cout<<"Concurrent fixture dispatch, in-flight refusal, 32 synchronized attach/restore cycles, "
             <<calls.load()<<" calls and drained function-table removal PASS\n";
}
