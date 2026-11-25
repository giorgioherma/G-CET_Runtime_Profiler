#pragma once
// Research only. This contract accepts a DISPOSABLE private image, never a
// loaded module. It does not install a game hook or execute copied CET code.
#include "CETDispatchIdentity.hpp"
#include "CETStacklessGate.hpp"
#include "GuardUnwindProof.hpp"
#include "GuardAttachment.hpp"
#include <windows.h>
#include <array>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <new>
#include <stdexcept>
#include <atomic>
#include <memory>
#include <shared_mutex>
#include <thread>
#include <cstdlib>

namespace gcet::research {
inline void Require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
struct GuardSite {
    uint32_t guard,active,empty;
    bool r15;
    std::array<uint8_t,11> stock;
};
inline constexpr GuardSite Sites[]={
    {0x10834f,0x10835a,0x1087e9,true,{0x41,0x80,0x7f,0x70,0,0x0f,0x85,0x8f,0x04,0,0}},
    {0x108b41,0x108b4c,0x109200,false,{0x41,0x80,0x7e,0x70,0,0x0f,0x85,0xb4,0x06,0,0}}
};
class PrivateGuards {
    std::shared_ptr<std::shared_mutex> owner_=std::make_shared<std::shared_mutex>();
    std::array<GuardEdit,2> edits_{};
    std::unique_ptr<SynchronizedPrivateGuards> transaction_;
public:
    PrivateGuards(uint8_t* image,std::array<uintptr_t,2> islands){
        MEMORY_BASIC_INFORMATION mbi{};
        Require(VirtualQuery(image,&mbi,sizeof(mbi))==sizeof(mbi)&&
                mbi.AllocationBase==image&&mbi.Type==MEM_PRIVATE,"test requires private allocation");
        for(size_t i=0;i<2;++i){
            auto* address=image+Sites[i].guard;
            Require(Equal(address,Sites[i].stock),"guard bytes differ");
            edits_[i]={address,Sites[i].stock,Redirect(reinterpret_cast<uintptr_t>(address),islands[i])};
        }
        transaction_=std::make_unique<SynchronizedPrivateGuards>(owner_,edits_);
    }
    bool Apply(int failBefore=-1){
        auto result=transaction_->Change(true,failBefore);
        Require(result!=GuardResult::Unsafe,"private guard apply unsafe");
        return result==GuardResult::Success;
    }
    void Restore(){Require(transaction_->Change(false)==GuardResult::Success,"private guard restoration failed");}
    const PatchBytes& Patch(size_t i)const{return edits_[i].installed;}
};

// Two independently callable sites share the same RX page with unrelated
// code. The two guarded callers use the fixture's existing shared lock;
// the unrelated caller does not. Prove execute remains available throughout
// repeated attachment/restore, and explicit owner retention across busy
// refusal, rollback and foreign-byte refusal.
inline void CheckSynchronizedGuardLifetime(){
    struct Arena {
        uint8_t* p=static_cast<uint8_t*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        ~Arena(){if(p)VirtualFree(p,0,MEM_RELEASE);}
    } arena;
    Require(arena.p!=nullptr,"synchronized guard arena");
    auto* p=arena.p;
    PatchBytes stock{0xb8,1,0,0,0,0xc3,0x90,0x90,0x90,0x90,0x90};
    PatchBytes installed=stock;installed[1]=2;
    const std::array<GuardEdit,2> edits{{{p,stock,installed},{p+32,stock,installed}}};
    for(const auto& e:edits)std::memcpy(e.address,stock.data(),stock.size());
    std::memcpy(p+64,stock.data(),stock.size());
    DWORD old=0;
    Require(VirtualProtect(p,4096,PAGE_EXECUTE_READ,&old)!=0&&
            FlushInstructionCache(GetCurrentProcess(),p,4096)!=0,"fixture code RX");
    using Run=int(*)();
    auto first=reinterpret_cast<Run>(p),second=reinterpret_cast<Run>(p+32),unrelated=reinterpret_cast<Run>(p+64);
    auto owner=std::make_shared<std::shared_mutex>();
    std::weak_ptr<std::shared_mutex> weak=owner;
    {
        SynchronizedPrivateGuards absent({},edits);
        Require(absent.Change(true)==GuardResult::Refused,"unowned lock admitted");
        SynchronizedPrivateGuards tx(owner,edits);
        owner.reset();
        Require(!weak.expired(),"transaction did not retain lock owner");
        auto retained=weak.lock();
        std::atomic<bool> held=false,release=false,stop=false,invalid=false;
        std::atomic<unsigned> calls=0,unrelatedCalls=0;
        std::thread inFlight([&]{
            std::shared_lock read(*retained);
            held.store(true,std::memory_order_release);
            while(!release.load(std::memory_order_acquire))std::this_thread::yield();
        });
        while(!held.load(std::memory_order_acquire))std::this_thread::yield();
        const auto busy=tx.Change(true);
        release.store(true,std::memory_order_release);inFlight.join();
        Require(busy==GuardResult::Busy&&first()==1&&second()==1,"busy mutated guard pair");
        Require(tx.Change(true,1)==GuardResult::RolledBack&&first()==1&&second()==1,"second-site rollback");
        Require(tx.Change(true)==GuardResult::Success&&first()==2&&second()==2,"pair install");
        Require(tx.Change(false,1)==GuardResult::RolledBack&&first()==2&&second()==2,"restore rollback");
        Require(tx.Change(false)==GuardResult::Success&&first()==1&&second()==1,"pair restore");
        // Repeated restore must not claim success on foreign bytes even
        // when the transaction already considers itself retired.
        DWORD prior=0,discard=0;
        Require(VirtualProtect(p,4096,PAGE_EXECUTE_READWRITE,&prior)!=0,"foreign stock test protection");
        p[33]=3;
        Require(VirtualProtect(p,4096,prior,&discard)!=0&&
                FlushInstructionCache(GetCurrentProcess(),p+32,11)!=0,"foreign stock test RX");
        Require(tx.Change(false)==GuardResult::Refused&&p[33]==3,"retired restore accepted foreign stock");
        Require(VirtualProtect(p,4096,PAGE_EXECUTE_READWRITE,&prior)!=0,"repair scratch test protection");
        p[33]=1;
        Require(VirtualProtect(p,4096,prior,&discard)!=0&&
                FlushInstructionCache(GetCurrentProcess(),p+32,11)!=0,"repair scratch test RX");
        Require(tx.Change(false)==GuardResult::Success,"verified repeated restore");
        std::thread worker([&]{
            while(!stop.load(std::memory_order_acquire)){
                {std::shared_lock read(*retained);
                 if(first()!=second())invalid=true;
                 ++calls;}
                std::this_thread::yield();
            }
        });
        std::thread neighbor([&]{
            while(!stop.load(std::memory_order_acquire)){
                if(unrelated()!=1)invalid=true;
                ++unrelatedCalls;
                std::this_thread::yield();
            }
        });
        while(calls.load()<100||unrelatedCalls.load()<100)std::this_thread::yield();
        // Retrying only in this stress fixture, not an attachment policy.
        for(int i=0;i<32;++i){
            for(bool enable:{true,false}){
                GuardResult result;
                do {result=tx.Change(enable);if(result==GuardResult::Busy)std::this_thread::yield();}
                while(result==GuardResult::Busy);
                if(result!=GuardResult::Success)std::abort();
            }
        }
        stop.store(true,std::memory_order_release);worker.join();neighbor.join();
        Require(!invalid&&first()==1&&second()==1&&unrelated()==1,"concurrent pair or neighbor changed");
        Require(calls>=100&&unrelatedCalls>=100,"insufficient concurrent execution");
        retained.reset();
        Require(!weak.expired(),"owner retired before transaction destruction");
        std::cout<<"Synchronized two-site attach/restore, owner retention, busy and rollback, "
                 <<calls.load()<<" guarded and "<<unrelatedCalls.load()<<" unrelated RX-page calls PASS\n";
    }
    Require(weak.expired(),"restored transaction retained owner indefinitely");
}
struct GuardData {
    cet_dispatch::DispatchEpoch epoch;
    cet_dispatch::ObserverBinding binding{epoch,GetCurrentThreadId()};
};

inline void CheckGuardRedirect(const std::filesystem::path& path,uint8_t* image,size_t imageSize){
    using namespace cet_dispatch;
    CheckSynchronizedGuardLifetime();
    Require(CheckImage(path,image)==Identity::ExactImage,"private guard baseline");
    auto* code=image+imageSize;
    auto* data=new(code+4096) GuardData;
    std::array<uintptr_t,2> islands{};
    std::array<uint32_t,2> gateSizes{};
    for(size_t i=0;i<2;++i){
        auto site=Sites[i];
        islands[i]=reinterpret_cast<uintptr_t>(code+i*1024);
        StacklessGate gate(islands[i],site.r15,data->binding.Guard(),data->epoch,
            reinterpret_cast<uintptr_t>(image+site.active),reinterpret_cast<uintptr_t>(image+site.empty));
        Require(gate.Code().size()<1024,"island capacity");
        std::memcpy(code+i*1024,gate.Code().data(),gate.Code().size());
        gateSizes[i]=static_cast<uint32_t>(gate.Code().size());
        // Chained metadata will live in the separate private data page.
    }
    DWORD old=0;
    Require(VirtualProtect(code,4096,PAGE_EXECUTE_READ,&old)!=0,"private island W^X");
    Require(FlushInstructionCache(GetCurrentProcess(),code,4096)!=0,"flush private islands");
    for(size_t i=0;i<2;++i){
        CheckChainedUnwind(image,Sites[i].guard,static_cast<uint32_t>(imageSize+i*1024),
            gateSizes[i],static_cast<uint32_t>(imageSize+4096+2048+i*128));
        CheckChainedUnwind(image,Sites[i].guard,static_cast<uint32_t>(imageSize+i*1024),
            gateSizes[i],static_cast<uint32_t>(imageSize+4096+2304+i*128),true);
    }
    // The address-range contract must refuse rather than truncate.
    bool refused=false;
    try{(void)Redirect(0x10000,0x80010006ull);}catch(const std::runtime_error&){refused=true;}
    Require(refused,"out-of-range target admitted");
    PrivateGuards transaction(image,islands);
    Require(!transaction.Apply(1),"injected second guard failure accepted");
    Require(CheckImage(path,image)==Identity::ExactImage,"partial application not rolled back");
    for(int iteration=0;iteration<3;++iteration){
        Require(transaction.Apply(),"apply both private guards");
        for(size_t i=0;i<2;++i){
            const auto at=reinterpret_cast<uintptr_t>(image+Sites[i].guard);
            Require(Equal(image+Sites[i].guard,transaction.Patch(i)),"redirect bytes");
            Require(Destination(at,transaction.Patch(i))==islands[i],"redirect destination");
            MEMORY_BASIC_INFORMATION mbi{};
            Require(VirtualQuery(image+Sites[i].guard,&mbi,sizeof(mbi))==sizeof(mbi)&&
                    mbi.Protect==PAGE_EXECUTE_READ,"guard protection not restored");
        }
        Require(CheckImage(path,image)==Identity::LoadedCodeMismatch,"patched image not rejected");
        Require(!transaction.Apply(),"duplicate apply accepted");
        transaction.Restore();transaction.Restore();
        Require(CheckImage(path,image)==Identity::ExactImage,"complete original identity not restored");
    }
    // Verify a foreign alteration in the SECOND guard refuses before touching
    // the first. Only the isolated scratch copy is modified here.
    auto* second=image+Sites[1].guard;
    Require(VirtualProtect(second,11,PAGE_READWRITE,&old)!=0,"scratch writable");
    second[0]^=1;
    DWORD unused=0;
    Require(VirtualProtect(second,11,old,&unused)!=0,"scratch protection");
    Require(!transaction.Apply()&&Equal(image+Sites[0].guard,Sites[0].stock),"partial install on foreign code");
    Require(VirtualProtect(second,11,PAGE_READWRITE,&old)!=0,"scratch restore writable");
    second[0]^=1;
    Require(VirtualProtect(second,11,old,&unused)!=0,"scratch restore protection");
    Require(CheckImage(path,image)==Identity::ExactImage,"foreign test baseline restore");
    data->~GuardData();
    std::cout<<"Both actual CET guard redirections, partial rollback, foreign-code refusal, W^X and exact restoration PASS\n";
}
}
