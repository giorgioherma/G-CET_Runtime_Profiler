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
int main(){
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
