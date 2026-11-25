#pragma once
// Research only. This contract accepts a DISPOSABLE private image, never a
// loaded module. It does not install a game hook or execute copied CET code.
#include "CETDispatchIdentity.hpp"
#include "CETStacklessGate.hpp"
#include "GuardUnwindProof.hpp"
#include "CETExistingMutex.hpp"
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
using PatchBytes=std::array<uint8_t,11>;
inline PatchBytes Redirect(uintptr_t from,uintptr_t to){
    const auto next=from+5;
    const auto distance=to>=next?to-next:next-to;
    Require(distance<=uint64_t(to>=next?INT32_MAX:2147483648ull),"branch outside rel32 range");
    int64_t delta=to>=next?int64_t(distance):-int64_t(distance);
    PatchBytes result{0xe9,0,0,0,0,0x90,0x90,0x90,0x90,0x90,0x90};
    int32_t rel=static_cast<int32_t>(delta);
    std::memcpy(result.data()+1,&rel,4);
    return result;
}
inline uintptr_t Destination(uintptr_t source,const PatchBytes& bytes){
    int32_t rel=0;std::memcpy(&rel,bytes.data()+1,4);
    Require(bytes[0]==0xe9,"not rel32 jump");
    return static_cast<uintptr_t>(static_cast<int64_t>(source+5)+rel);
}
inline bool Equal(const uint8_t* p,const PatchBytes& bytes){
    return std::memcmp(p,bytes.data(),bytes.size())==0;
}
// Research transaction. Only MEM_PRIVATE executable pages are admitted.
// The caller supplies an owning reference to the lock and keeps the
// private image/islands alive until exact restoration. The live CET owner
// and production lifetime admission are deliberately NOT inferred here.
enum class GuardResult { Success, Busy, Refused, RolledBack, Unsafe };
struct GuardEdit {
    uint8_t* address;
    PatchBytes stock, installed;
};
class SynchronizedPrivateGuards {
    std::shared_ptr<std::shared_mutex> owner_;
    std::array<GuardEdit,2> edits_;
    bool installed_=false, unsafe_=false;
public:
    SynchronizedPrivateGuards(std::shared_ptr<std::shared_mutex> owner,
                              std::array<GuardEdit,2> edits)
        :owner_(std::move(owner)),edits_(edits){}
    SynchronizedPrivateGuards(const SynchronizedPrivateGuards&)=delete;
    SynchronizedPrivateGuards& operator=(const SynchronizedPrivateGuards&)=delete;
    // Never silently release the fixture's owner while code might still
    // refer to its islands. Production retirement requires a retained arena
    // on failure, not this test-only fatal invariant.
    ~SynchronizedPrivateGuards(){if(installed_||unsafe_)std::abort();}
    GuardResult Change(bool install,int failBefore=-1){
        if(unsafe_)return GuardResult::Unsafe;
        if(!owner_)return GuardResult::Refused;
        cet_dispatch::ExistingMutexLease lease(owner_->native_handle());
        if(!lease)return GuardResult::Busy;
        if(install==installed_)return install?GuardResult::Refused:GuardResult::Success;
        SYSTEM_INFO info{};GetSystemInfo(&info);
        const auto pageSize=static_cast<uintptr_t>(info.dwPageSize);
        if(!pageSize)return GuardResult::Refused;
        std::array<uint8_t*,2> pages{};
        std::array<DWORD,2> protections{};
        size_t pageCount=0;
        // Preflight BOTH sites and their complete pages before any mutation.
        for(const auto& e:edits_){
            auto at=reinterpret_cast<uintptr_t>(e.address);
            if(!at||at%pageSize+e.stock.size()>pageSize)return GuardResult::Refused;
            MEMORY_BASIC_INFORMATION mbi{};
            if(VirtualQuery(e.address,&mbi,sizeof(mbi))!=sizeof(mbi)||
               mbi.State!=MEM_COMMIT||mbi.Type!=MEM_PRIVATE||
               mbi.Protect!=PAGE_EXECUTE_READ||
               !Equal(e.address,installed_?e.installed:e.stock))return GuardResult::Refused;
            auto* page=reinterpret_cast<uint8_t*>(at-at%pageSize);
            if(!pageCount||pages[0]!=page)pages[pageCount++]=page;
        }
        const auto a=reinterpret_cast<uintptr_t>(edits_[0].address);
        const auto b=reinterpret_cast<uintptr_t>(edits_[1].address);
        if((a<=b?b-a:a-b)<edits_[0].stock.size())return GuardResult::Refused;
        size_t opened=0;
        auto closePages=[&](){
            bool ok=true;
            while(opened){
                --opened;DWORD unused=0;
                if(!VirtualProtect(pages[opened],pageSize,protections[opened],&unused))ok=false;
            }
            return ok;
        };
        // Keep execute permission for unrelated instructions on these pages.
        // Only the two proven guarded spans may be rewritten.
        for(size_t i=0;i<pageCount;++i){
            DWORD old=0;
            if(!VirtualProtect(pages[i],pageSize,PAGE_EXECUTE_READWRITE,&old)){
                if(!closePages()){unsafe_=true;return GuardResult::Unsafe;}
                return GuardResult::Refused;
            }
            protections[opened++]=old;
            if(old!=PAGE_EXECUTE_READ){
                if(!closePages()){unsafe_=true;return GuardResult::Unsafe;}
                return GuardResult::Refused;
            }
        }
        size_t written=0;
        bool ok=true;
        for(size_t i=0;i<edits_.size();++i){
            if(static_cast<int>(i)==failBefore){ok=false;break;}
            const auto& e=edits_[i];const auto& bytes=install?e.installed:e.stock;
            std::memcpy(e.address,bytes.data(),bytes.size());++written;
            if(!Equal(e.address,bytes)){ok=false;break;}
        }
        auto flush=[&](){
            bool success=true;
            for(size_t i=0;i<written;++i)
                if(!FlushInstructionCache(GetCurrentProcess(),edits_[i].address,
                                          edits_[i].stock.size()))success=false;
            return success;
        };
        if(ok&&!flush())ok=false;
        bool rollbackOK=true;
        if(!ok){
            for(size_t i=0;i<written;++i){
                const auto& e=edits_[i];const auto& previous=installed_?e.installed:e.stock;
                std::memcpy(e.address,previous.data(),previous.size());
                if(!Equal(e.address,previous))rollbackOK=false;
            }
            if(!flush())rollbackOK=false;
        }
        if(!closePages()||!rollbackOK){unsafe_=true;return GuardResult::Unsafe;}
        if(ok)installed_=install;
        for(const auto& e:edits_){
            MEMORY_BASIC_INFORMATION mbi{};
            if(!Equal(e.address,installed_?e.installed:e.stock)||
               VirtualQuery(e.address,&mbi,sizeof(mbi))!=sizeof(mbi)||
               mbi.Protect!=PAGE_EXECUTE_READ){unsafe_=true;return GuardResult::Unsafe;}
        }
        return ok?GuardResult::Success:GuardResult::RolledBack;
    }
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
                 <<calls.load()<<" guarded and "<<unrelatedCalls.load()<<" unrelated RX-page calls PASS\\n";
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
