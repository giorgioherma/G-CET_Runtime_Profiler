#include "CETObserverBinding.hpp"
#include <array>
#include <atomic>
#include <cassert>
#include <cstdlib>
#include <iostream>
#include <thread>

using namespace gcet::cet_dispatch;
void Check(bool result,const char* reason){
    if(!result){std::cerr<<reason<<"\n";std::abort();}
}
struct Fixture {
    std::array<std::byte,CompiledChainView::Size> bytes{};
    uintptr_t address=0x1000,context=0x5000,callbackStorage=0x4000;
    int pointerReads=0;
    bool readable=true;
    Fixture(){
        Word(0,0x2000);Word(8,0x3000);
        Vector(16,callbackStorage,callbackStorage+8,callbackStorage+32);
    }
    void Word(size_t at,uintptr_t word){std::memcpy(bytes.data()+at,&word,sizeof(word));}
    void Vector(size_t at,uintptr_t first,uintptr_t last,uintptr_t end){
        Word(at+8,first);Word(at+16,last);Word(at+24,end);
    }
    SoleBeforeIdentity Read(){
        return CompiledChainView::Read(address,bytes,[&](uintptr_t p,uintptr_t& value) noexcept{
            ++pointerReads;
            if(!readable||p!=callbackStorage)return false;
            value=context;return true;
        });
    }
};
void MixedContexts(){
    DispatchEpoch epoch;
    IndividualObserverBindings<256> bindings(epoch,17);
    std::array<IndividualIdentity,35> ids{};
    std::array<uint64_t,35> handles{};
    for(size_t i=0;i<ids.size();++i){
        ids[i]={0x1000,0x4000+i*16,ObserverPhase::Before};
        handles[i]=bindings.Bind(17,epoch.Current(),ids[i]);
        Check(handles[i]!=0,"distinct context binding");
        Check(bindings.SetDemand(17,handles[i],false),"sleep distinct context");
    }
    // A foreign context is deliberately in the middle of the author order.
    const IndividualIdentity foreign{0x1000,0x9000,ObserverPhase::Before};
    const IndividualIdentity after{0x1000,ids[1].context,ObserverPhase::After};
    Check(!bindings.CanSkip(17,foreign)&&!bindings.CanSkip(17,after),"unknown phase/context retained");
    Check(!bindings.CanSkip(18,ids[0]),"foreign thread retained");
    Check(bindings.SetDemand(17,handles[0],true),"wake first");
    std::array<int,36> trace{};size_t used=0,eliminated=0;
    auto invoke=[&](size_t i){
        if(bindings.CanSkip(17,ids[i])){++eliminated;return;}
        trace[used++]=static_cast<int>(i);
        if(i==0)Check(bindings.SetDemand(17,handles[2],true),"same-dispatch wake later");
        if(i==2)Check(bindings.SetDemand(17,handles[3],false),"same-dispatch sleep later");
    };
    for(size_t i=0;i<35;++i){
        if(i==1&&!bindings.CanSkip(17,foreign))trace[used++]=99;
        invoke(i);
    }
    Check(used==3&&trace[0]==0&&trace[1]==99&&trace[2]==2&&eliminated==33,
          "mixed chain preserves order and same-dispatch demand");
    Check(bindings.SetDemand(17,handles[2],false),"sleep after invocation");
    Check(bindings.Release(17,handles[4])&&!bindings.CanSkip(17,ids[4]),"release retains stock");
    auto old=handles[5];
    {DispatchEpoch::Invalidation clear(epoch);
        Check(!bindings.CanSkip(17,ids[5]),"clear invalidates all contexts");}
    Check(!bindings.SetDemand(17,old,false)&&!bindings.CanSkip(17,ids[5]),"stale epoch");
    auto fresh=bindings.Bind(17,epoch.Current(),ids[5]);
    Check(fresh&&fresh!=old&&!bindings.CanSkip(17,ids[5]),"address reuse begins active");
    IndividualObserverBindings<2> bounded(epoch,17);
    auto a=bounded.Bind(17,epoch.Current(),ids[0]);
    Check(a&&bounded.SetDemand(17,a,false),"bounded slot");
    Check(!bounded.Bind(17,epoch.Current(),ids[2]),"collision refuses only affected binding");
    Check(bounded.CanSkip(17,ids[0])&&!bounded.CanSkip(17,ids[2]),"collision safe");
    std::cout<<"Individual mixed-context mirror: 33 of 35 modeled managed invocations eliminated, foreign order and immediate wake retained; no marshalling savings claimed\\n";
}
int main(){
    MixedContexts();
    Fixture f;
    auto first=f.Read();
    Check(static_cast<bool>(first)&&first.context==f.context&&f.pointerReads==1,"sole Before identity");
    // No pointer read for any foreign/mixed, GC or malformed extent.
    auto reject=[&](const char* why){int before=f.pointerReads;Check(!f.Read()&&f.pointerReads==before,why);};
    f.Vector(16,0x4000,0x4010,0x4020);reject("second Before not rejected");
    f.Vector(16,0x4000,0x4008,0x4020);
    f.Vector(48,0x6000,0x6008,0x6020);reject("After not rejected");
    f.Vector(48,0x6000,0x6000,0x6020); // allocated, logically empty is valid
    f.Vector(80,0x7000,0x7008,0x7020);reject("Override not rejected");
    f.Vector(80,0,0,0);
    f.bytes[113]=std::byte{1};reject("GC obligation not rejected");f.bytes[113]=std::byte{0};
    f.bytes[112]=std::byte{1};reject("stock-empty chain not rejected");f.bytes[112]=std::byte{0};
    f.Vector(16,0x4000,0x3ff8,0x4020);reject("inverted pointers");
    f.Vector(16,0x4000,0x4008,0x4000);reject("invalid capacity");
    f.Vector(16,0x4000,0x4008,0x4020);
    f.readable=false;Check(!f.Read(),"failed pointer read");f.readable=true;
    f.context=0;Check(!f.Read(),"null context");f.context=first.context;
    Check(f.Read()==first,"allocated empty After rejected");

    DispatchEpoch epoch;
    ObserverBinding binding(epoch,17);
    auto token=epoch.Current();
    Check(!binding.Bind(18,token,first)&&!binding.Bind(17,0,first),"foreign thread or invalid epoch admitted");
    auto handle=binding.Bind(17,token,first);
    Check(handle&&!binding.CanBypass(17,first),"binding must start active");
    Check(!binding.SetDemand(18,handle,false)&&!binding.SetDemand(17,handle+1,false),"foreign mutation");
    Check(binding.SetDemand(17,handle,false)&&binding.CanBypass(17,f.Read()),"idle admission");
    Check(binding.Bind(17,token,first)==handle&&binding.CanBypass(17,first),"repeat bind reset demand");
    f.context+=8;Check(!binding.CanBypass(17,f.Read())&&!binding.Bind(17,token,f.Read()),"foreign Context identity");
    f.context=first.context;f.address+=8;
    Check(!binding.CanBypass(17,f.Read()),"relocated chain");f.address=first.chain;
    f.Vector(80,0x7000,0x7008,0x7020);Check(!binding.CanBypass(17,f.Read()),"late override");
    f.Vector(80,0,0,0);
    Check(!binding.CanBypass(18,first),"foreign thread bypass");
    Check(binding.SetDemand(17,handle,true)&&!binding.CanBypass(17,first),"immediate reactivation");
    Check(binding.SetDemand(17,handle,false),"sleep transition");
    // Concurrent Clear invalidates before it could destroy/reuse Context;
    // no new binding or old handle can become eligible while Clear is open.
    std::atomic<bool> clearing=false,finish=false;
    std::thread reload([&]{
        DispatchEpoch::Invalidation outer(epoch);
        {DispatchEpoch::Invalidation nested(epoch);Check(!epoch.Current(),"nested Clear active");}
        clearing.store(true,std::memory_order_release);
        while(!finish.load(std::memory_order_acquire))std::this_thread::yield();
    });
    while(!clearing.load(std::memory_order_acquire))std::this_thread::yield();
    Check(!binding.CanBypass(17,first)&&!binding.SetDemand(17,handle,false)&&
          !binding.Bind(17,token,first),"Clear-in-flight admitted old binding");
    finish.store(true,std::memory_order_release);reload.join();
    Check(!epoch.Matches(token)&&!binding.CanBypass(17,first),"reused address after Clear");
    auto next=binding.Bind(17,epoch.Current(),first);
    Check(next&&next!=handle&&!binding.CanBypass(17,first),"fresh epoch must start active");
    Check(!binding.SetDemand(17,handle,false)&&!binding.Release(17,handle),"stale handle mutated new epoch");
    Check(binding.SetDemand(17,next,false)&&binding.Release(17,next)&&!binding.CanBypass(17,first),"release");
    auto again=binding.Bind(17,epoch.Current(),first);
    Check(again&&again!=next&&!binding.CanBypass(17,first),"released handle reuse");
    std::cout<<"Compiled membership, demand mirroring, foreign chains, reload and stale handles PASS\n";
}
