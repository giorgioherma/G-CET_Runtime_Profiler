// Execute both emitted machine gates in a disposable Windows process.
// The actual CET image is never executed or modified. The entry fixture
// reproduces both chain-register choices and active/empty successor edges.
#define NOMINMAX
#include <windows.h>
#include <intrin.h>
#include "CETStacklessGate.hpp"
#include <array>
#include <iostream>
#include <new>
#include <thread>
#include <algorithm>
#include <chrono>
#include "CETObserverIslands.hpp"
#include <cstdlib>
using namespace gcet::cet_dispatch;
void Check(bool ok,const char* reason){if(!ok){std::cerr<<reason<<"\n";std::abort();}}
struct Chain {
    std::array<std::byte,120> bytes{};
    alignas(8) uintptr_t callback=0x5000;
    void Word(size_t at,uintptr_t v){std::memcpy(bytes.data()+at,&v,8);}
    void Vector(size_t at,uintptr_t first,uintptr_t last,uintptr_t end){
        Word(at+8,first);Word(at+16,last);Word(at+24,end);
    }
    Chain(){Word(0,0x2000);Word(8,0x3000);
        auto first=reinterpret_cast<uintptr_t>(&callback);Vector(16,first,first+8,first+8);}
    SoleBeforeIdentity Identity(){
        return CompiledChainView::Read(reinterpret_cast<uintptr_t>(bytes.data()),bytes,
            [&](uintptr_t at,uintptr_t& v) noexcept{
                if(at!=reinterpret_cast<uintptr_t>(&callback))return false;v=callback;return true;
            });
    }
};
struct Arena {
    uint8_t* p=static_cast<uint8_t*>(VirtualAlloc(nullptr,8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    ~Arena(){if(p)VirtualFree(p,0,MEM_RELEASE);}
};
struct Data {
    DispatchEpoch epoch;
    ObserverBinding binding{epoch,GetCurrentThreadId()};
};
using Run=int(*)(const void*);
// Executable Windows x64 fixture for the individual loop. The original
// iterator and callback argument load execute as emitted machine code;
// a C++ callback stands in for the Lua invocation, not the actual CET ASI.
struct MultiData {
    DispatchEpoch epoch;
    IndividualObserverBindings<256> bindings{epoch,GetCurrentThreadId()};
};
struct alignas(16) FakeContext {uint64_t reserved[2]{};uint32_t tag=0;};
static_assert(offsetof(FakeContext,tag)==16);
struct MultiArgs {
    uintptr_t chain=0;
    uintptr_t* begin=nullptr;
    uintptr_t* end=nullptr;
    void (*callback)(MultiArgs*,uintptr_t,uint32_t)=nullptr;
    MultiData* data=nullptr;
    std::array<uint64_t,35> handles{};
    std::array<uint32_t,36> trace{};
    uint64_t calls=0;
    size_t used=0;
    bool record=false,wake=false;
};
static_assert(offsetof(MultiArgs,chain)==0&&offsetof(MultiArgs,begin)==8&&
              offsetof(MultiArgs,end)==16&&offsetof(MultiArgs,callback)==24);
void MultiInvoke(MultiArgs* a,uintptr_t context,uint32_t tag){
    Check(reinterpret_cast<const FakeContext*>(context)->tag==tag,"original argument load changed");
    ++a->calls;
    if(a->record){Check(a->used<a->trace.size(),"trace overflow");a->trace[a->used++]=tag;}
    if(a->wake&&tag==0)Check(a->data->bindings.SetDemand(GetCurrentThreadId(),a->handles[2],true),"same-dispatch wake");
}
void IndividualLoop(){
    auto* p=static_cast<uint8_t*>(VirtualAlloc(nullptr,65536,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    Check(p!=nullptr,"multi-context fixture arena");
    auto* data=new(p+4096) MultiData;
    auto emit=[](uint8_t* at,std::initializer_list<uint8_t> bytes){
        std::copy(bytes.begin(),bytes.end(),at);return at+bytes.size();
    };
    using Dispatch=void(*)(MultiArgs*);
    Dispatch run[2]{};
    for(int i=0;i<2;++i){
        auto* e=p+i*1024;auto* loop=e+64;auto* active=e+128;auto* tail=e+192;auto* exit=e+256;
        auto* island=e+320;
        auto jump=[&](uint8_t* at,uint8_t* dst){*at=0xe9;WriteRelative(at+1,reinterpret_cast<uintptr_t>(dst));};
        // Three saved registers + 128-byte frame preserve Win64 alignment,
        // original stack slot +0x68 and callback home-space.
        auto* b=emit(e,{0x57,0x56,0x53,0x48,0x81,0xec,0x80,0,0,0,
            0x48,0x89,0xcb,0x48,0x8b,0x03,0x48,0x89,0x44,0x24,0x68,
            0x48,0x8b,0x73,0x08,0x48,0x8b,0x43,0x10,0x48,0x89,0x44,0x24,0x70,
            0x48,0x39,0xc6,0x0f,0x84,0,0,0,0});
        WriteRelative(b-4,reinterpret_cast<uintptr_t>(exit));jump(b,loop);
        if(i==0){auto* x=emit(loop,{0x48,0x8b,0x3e,0x8b,0x47,0x10});jump(x,active);}
        else{
            jump(loop,island);
            StacklessGate gate(reinterpret_cast<uintptr_t>(island),data->bindings,data->epoch,
                reinterpret_cast<uintptr_t>(active),reinterpret_cast<uintptr_t>(tail));
            Check(gate.Code().size()<704,"individual gate capacity");
            std::memcpy(island,gate.Code().data(),gate.Code().size());
            std::cout<<"Individual stackless guard bytes="<<gate.Code().size()<<"\n";
        }
        // Actual machine call only when the individual gate admits it.
        b=emit(active,{0x48,0x89,0xd9,0x48,0x89,0xfa,0x41,0x89,0xc0,0xff,0x53,0x18});
        jump(b,tail);
        b=emit(tail,{0x48,0x83,0xc6,0x08,0x48,0x3b,0x74,0x24,0x70,0x0f,0x85,0,0,0,0});
        WriteRelative(b-4,reinterpret_cast<uintptr_t>(loop));jump(b,exit);
        emit(exit,{0x48,0x81,0xc4,0x80,0,0,0,0x5b,0x5e,0x5f,0xc3});
        run[i]=reinterpret_cast<Dispatch>(e);
    }
    DWORD old=0;Check(VirtualProtect(p,4096,PAGE_EXECUTE_READ,&old)!=0,"individual RX code");
    Check(FlushInstructionCache(GetCurrentProcess(),p,4096)!=0,"individual flush");
    std::array<FakeContext,36> contexts{};
    std::array<uintptr_t,36> pointers{};
    MultiArgs a;a.chain=reinterpret_cast<uintptr_t>(&a);a.begin=pointers.data();a.end=a.begin+pointers.size();
    a.data=data;a.callback=&MultiInvoke;
    const auto thread=GetCurrentThreadId();
    for(size_t i=0;i<35;++i){
        contexts[i].tag=static_cast<uint32_t>(i);
        auto id=IndividualIdentity{a.chain,reinterpret_cast<uintptr_t>(&contexts[i]),ObserverPhase::Before};
        a.handles[i]=data->bindings.Bind(thread,data->epoch.Current(),id);
        Check(a.handles[i]!=0,"individual distinct live storage");
        pointers[i+(i>=1?1:0)]=id.context;
    }
    contexts[35].tag=99;pointers[1]=reinterpret_cast<uintptr_t>(&contexts[35]);
    a.record=true;run[0](&a);
    Check(a.used==36&&a.trace[0]==0&&a.trace[1]==99&&a.trace[3]==2,"original loop order");
    a.used=0;a.calls=0;
    for(auto handle:a.handles)Check(data->bindings.SetDemand(thread,handle,false),"sleep all");
    Check(data->bindings.SetDemand(thread,a.handles[0],true),"wake first");
    a.wake=true;run[1](&a);
    Check(a.calls==3&&a.used==3&&a.trace[0]==0&&a.trace[1]==99&&a.trace[2]==2,
          "emitted mixed-context skip/order/immediate wake");
    a.wake=false;a.record=false;a.calls=0;
    // Foreign chain using the same pointers still executes every callback.
    auto chain=a.chain;a.chain+=16;run[1](&a);
    Check(a.calls==36,"foreign chain identity");
    a.chain=chain;a.calls=0;
    {DispatchEpoch::Invalidation invalidate(data->epoch);run[1](&a);}
    Check(a.calls==36,"invalid epoch stock execution");
    for(size_t i=0;i<35;++i){
        a.handles[i]=data->bindings.Bind(thread,data->epoch.Current(),
            {a.chain,reinterpret_cast<uintptr_t>(&contexts[i]),ObserverPhase::Before});
        Check(a.handles[i]!=0,"new epoch bind");
    }
    // Report CPU cost, never make wall-clock timing a CI pass threshold.
    // Both paths invoke the same no-allocation C++ callback. The second
    // measurement includes the full identity lookup on a foreign chain.
    constexpr size_t passes=20000;
    auto measure=[&](Dispatch fn){
        const auto before=a.calls;const auto start=std::chrono::steady_clock::now();
        for(size_t i=0;i<passes;++i)fn(&a);
        const auto elapsed=std::chrono::duration<double,std::nano>(std::chrono::steady_clock::now()-start).count();
        Check(a.calls-before==passes*36,"all-active benchmark callback count");
        return elapsed/(passes*36);
    };
    std::array<double,5> stock{},guard{},lookup{};
    for(size_t i=0;i<stock.size();++i){
        stock[i]=measure(run[0]);guard[i]=measure(run[1]);
    }
    Check(data->bindings.SetDemand(thread,a.handles[0],false),"one dormant entry");
    a.chain+=16;
    for(auto& value:lookup)value=measure(run[1]);
    std::sort(stock.begin(),stock.end());std::sort(guard.begin(),guard.end());std::sort(lookup.begin(),lookup.end());
    std::cout<<"Individual guard synthetic ns/slot: stock="<<stock[2]
        <<", no sleepers="<<guard[2]<<", unknown chain with sleeper="<<lookup[2]<<"\n";
    std::cout<<"Emitted individual guard: 36 -> 3 callback invocations, shared marshalling not eliminated (synthetic fixture) PASS\n";
    data->~MultiData();VirtualFree(p,0,MEM_RELEASE);
}
int main(){
    IndividualLoop();
    Check(__readgsqword(0x48)==GetCurrentThreadId(),"TEB owner thread identity");
    Arena a;Check(a.p!=nullptr,"allocate research gate arena");
    auto* d=new(a.p+4096) Data;
    // Fixture prologue: push preserved chain register; mov r14/r15,rcx;
    // jmp island. Active successor tests the original cmp flags (ZF=1).
    // Empty successor overwrites flags as in CET and returns 2.
    Run runs[2]{};
    for(int i=0;i<2;++i){
        bool r15=i==1;
        auto* entry=a.p+i*1024;
        uint8_t prologue[]={0x41,r15?uint8_t{0x57}:uint8_t{0x56},
                            0x49,0x89,r15?uint8_t{0xcf}:uint8_t{0xce},
                            0xe9,0,0,0,0};
        // island at +128; jump displacement from end of prologue.
        int32_t displacement=128-sizeof(prologue);
        std::memcpy(prologue+6,&displacement,4);std::memcpy(entry,prologue,sizeof(prologue));
        uint8_t active[]={0x0f,0x94,0xc0,0x0f,0xb6,0xc0,0x41,r15?uint8_t{0x5f}:uint8_t{0x5e},0xc3};
        uint8_t empty[]={0xb8,2,0,0,0,0x41,r15?uint8_t{0x5f}:uint8_t{0x5e},0xc3};
        std::memcpy(entry+32,active,sizeof(active));std::memcpy(entry+64,empty,sizeof(empty));
        StacklessGate gate(reinterpret_cast<uintptr_t>(entry+128),r15,d->binding.Guard(),d->epoch,
            reinterpret_cast<uintptr_t>(entry+32),reinterpret_cast<uintptr_t>(entry+64));
        Check(gate.Code().size()<896,"gate fixture overflow");
        std::memcpy(entry+128,gate.Code().data(),gate.Code().size());
        std::cout<<"Stackless gate "<<(r15?"r15":"r14")<<" bytes="<<gate.Code().size()<<"\n";
        runs[i]=reinterpret_cast<Run>(entry);
    }
    DWORD old=0;Check(VirtualProtect(a.p,4096,PAGE_EXECUTE_READ,&old)!=0,"W^X code page");
    Check(FlushInstructionCache(GetCurrentProcess(),a.p,4096)!=0,"flush fixture");
    alignas(8) Chain f;alignas(8) Chain foreign;
    const auto thread=GetCurrentThreadId();
    auto verify=[&](Chain& chain,int expected,const char* reason){
        for(auto run:runs)Check(run(chain.bytes.data())==expected,reason);
    };
    verify(f,1,"no binding must preserve stock active");
    f.bytes[112]=std::byte{1};verify(f,2,"stock empty always wins");f.bytes[112]=std::byte{0};
    auto id=f.Identity();Check(static_cast<bool>(id),"fixture membership");
    auto handle=d->binding.Bind(thread,d->epoch.Current(),id);
    Check(handle!=0,"bind");verify(f,1,"new binding starts active");
    Check(d->binding.SetDemand(thread,handle,false),"sleep");
    verify(f,2,"dormant bypass");verify(foreign,1,"unmanaged chain");
    f.bytes[113]=std::byte{1};verify(f,1,"GC obligation");f.bytes[113]=std::byte{0};
    f.Word(32,id.storage+16);f.Word(40,id.storage+16);verify(f,1,"mixed Before");
    f.Word(32,id.storage+8);f.Word(40,id.storage+8);
    f.Vector(48,0x6000,0x6008,0x6010);verify(f,1,"After");
    f.Vector(48,0x6000,0x6000,0x6010);verify(f,2,"allocated empty After");
    f.Vector(48,0,0,8);verify(f,1,"malformed After capacity");f.Vector(48,0,0,0);
    f.Vector(80,0x7000,0x7008,0x7010);verify(f,1,"Override");f.Vector(80,0,0,0);
    f.callback+=8;verify(f,1,"different Context");f.callback-=8;
    f.Word(24,0x8000);verify(f,1,"unknown vector storage must not dereference");f.Word(24,id.storage);
    f.Word(0,0x9000);verify(f,1,"different trampoline");f.Word(0,id.trampoline);
    std::thread other([&]{
        Check(__readgsqword(0x48)==GetCurrentThreadId(),"foreign TEB identity");
        verify(f,1,"foreign dispatch thread");
    });other.join();
    Check(d->binding.SetDemand(thread,handle,true),"wake");verify(f,1,"immediate wake");
    Check(d->binding.SetDemand(thread,handle,false),"sleep again");verify(f,2,"immediate sleep");
    {DispatchEpoch::Invalidation clear(d->epoch);verify(f,1,"Clear in progress");}
    verify(f,1,"pointer reuse after Clear");
    auto next=d->binding.Bind(thread,d->epoch.Current(),id);
    Check(next&&next!=handle,"new handle");verify(f,1,"rebound active");
    Check(!d->binding.SetDemand(thread,handle,false),"stale transition");
    Check(d->binding.SetDemand(thread,next,false),"new sleep");verify(f,2,"new sleep executes");
    Check(d->binding.Release(thread,next),"release");verify(f,1,"released fallback");
    d->~Data();
    std::cout<<"Both emitted x64 stackless guards: stock, dormant, wake, foreign, GC, reload PASS\n";
}
