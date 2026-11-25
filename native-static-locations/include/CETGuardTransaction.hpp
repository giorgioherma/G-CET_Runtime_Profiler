#pragma once
// A single guarded patch transaction for proven executable spans.
// The owner supplies the already-proven CET lock, allocation identity and
// lifetime lease. No recurring locking, registration or scheduling policy.
#include "CETExistingMutex.hpp"
#include "CETObserverBinding.hpp"
#include "CETCompletedUnwind.hpp"
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <cstdlib>
#include <new>
namespace gcet::cet_dispatch {
inline void GuardRequire(bool ok,const char* why){if(!ok)throw std::runtime_error(why);}
using PatchBytes=std::array<uint8_t,11>;
inline PatchBytes Redirect(uintptr_t from,uintptr_t to){
    const auto next=from+5;
    const auto distance=to>=next?to-next:next-to;
    GuardRequire(distance<=uint64_t(to>=next?INT32_MAX:2147483648ull),"branch outside rel32 range");
    int64_t delta=to>=next?int64_t(distance):-int64_t(distance);
    PatchBytes result{0xe9,0,0,0,0,0x90,0x90,0x90,0x90,0x90,0x90};
    int32_t rel=static_cast<int32_t>(delta);
    std::memcpy(result.data()+1,&rel,4);
    return result;
}
inline uintptr_t Destination(uintptr_t source,const PatchBytes& bytes){
    int32_t rel=0;std::memcpy(&rel,bytes.data()+1,4);
    GuardRequire(bytes[0]==0xe9,"not rel32 jump");
    return static_cast<uintptr_t>(static_cast<int64_t>(source+5)+rel);
}
inline bool Equal(const uint8_t* p,const PatchBytes& bytes){
    return std::memcmp(p,bytes.data(),bytes.size())==0;
}
enum class GuardResult { Success, Busy, Refused, RolledBack, Unsafe };
struct GuardEdit {
    uint8_t* address;
    PatchBytes stock, installed;
    size_t length=11;
};

template<size_t Count> class GuardTransaction {
    void* lock_;
    void* allocation_;
    DWORD type_;
    std::array<GuardEdit,Count> edits_;
    bool installed_=false,unsafe_=false;
public:
    GuardTransaction(void* lock,void* allocation,DWORD type,std::array<GuardEdit,Count> edits)
        :lock_(lock),allocation_(allocation),type_(type),edits_(edits){}
    GuardTransaction(const GuardTransaction&)=delete;
    GuardTransaction& operator=(const GuardTransaction&)=delete;
    bool MustRetain()const noexcept{return installed_||unsafe_;}
    GuardResult Change(const ExistingMutexLease& lease,bool install,int failBefore=-1){
        if(unsafe_)return GuardResult::Unsafe;
        if(!lease.Owns(lock_))return GuardResult::Busy;
        if(install&&installed_)return GuardResult::Refused;
        SYSTEM_INFO info{};GetSystemInfo(&info);
        const auto pageSize=static_cast<uintptr_t>(info.dwPageSize);
        if(!pageSize)return GuardResult::Refused;
        std::array<uint8_t*,Count> pages{};
        std::array<DWORD,Count> protections{};
        size_t pageCount=0;
        // Preflight BOTH sites and their complete pages before any mutation.
        for(const auto& e:edits_){
            auto at=reinterpret_cast<uintptr_t>(e.address);
            if(!at||!e.length||e.length>e.stock.size()||at%pageSize+e.length>pageSize)return GuardResult::Refused;
            MEMORY_BASIC_INFORMATION mbi{};
            if(VirtualQuery(e.address,&mbi,sizeof(mbi))!=sizeof(mbi)||
               mbi.State!=MEM_COMMIT||mbi.Type!=type_||(allocation_&&mbi.AllocationBase!=allocation_)||
               mbi.Protect!=PAGE_EXECUTE_READ||
               std::memcmp(e.address,(installed_?e.installed:e.stock).data(),e.length)!=0)return GuardResult::Refused;
            auto* page=reinterpret_cast<uint8_t*>(at-at%pageSize);
            bool seen=false;for(size_t i=0;i<pageCount;++i)if(pages[i]==page)seen=true;
            if(!seen)pages[pageCount++]=page;
        }
        for(size_t i=0;i<Count;++i)for(size_t j=i+1;j<Count;++j){
            const auto a=reinterpret_cast<uintptr_t>(edits_[i].address);
            const auto b=reinterpret_cast<uintptr_t>(edits_[j].address);
            if(a<=b?b-a<edits_[i].length:a-b<edits_[j].length)return GuardResult::Refused;
        }
        if(!install&&!installed_)return GuardResult::Success;
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
            std::memcpy(e.address,bytes.data(),e.length);++written;
            if(std::memcmp(e.address,bytes.data(),e.length)!=0){ok=false;break;}
        }
        auto flush=[&](){
            bool success=true;
            for(size_t i=0;i<written;++i)
                if(!FlushInstructionCache(GetCurrentProcess(),edits_[i].address,
                                          edits_[i].length))success=false;
            return success;
        };
        if(ok&&!flush())ok=false;
        bool rollbackOK=true;
        if(!ok){
            for(size_t i=0;i<written;++i){
                const auto& e=edits_[i];const auto& previous=installed_?e.installed:e.stock;
                std::memcpy(e.address,previous.data(),e.length);
                if(std::memcmp(e.address,previous.data(),e.length)!=0)rollbackOK=false;
            }
            if(!flush())rollbackOK=false;
        }
        if(!closePages()||!rollbackOK){unsafe_=true;return GuardResult::Unsafe;}
        if(ok)installed_=install;
        for(const auto& e:edits_){
            MEMORY_BASIC_INFORMATION mbi{};
            if(std::memcmp(e.address,(installed_?e.installed:e.stock).data(),e.length)!=0||
               VirtualQuery(e.address,&mbi,sizeof(mbi))!=sizeof(mbi)||
               mbi.Protect!=PAGE_EXECUTE_READ){unsafe_=true;return GuardResult::Unsafe;}
        }
        return ok?GuardResult::Success:GuardResult::RolledBack;
    }
};

 // Arena owns the machine code, binding state, epoch and registered table.
 // The caller prepares code/metadata before Seal; no table or guard is
 // published until all resources are allocated. Metadata is not reclaimed
 // merely because guard bytes have been restored.
class GuardArena {
    uint8_t* base_=nullptr;
    DWORD64 tableBase_=0;
    static constexpr size_t Bytes=8192,CodeBytes=4096;
    struct NativeData {
        cet_dispatch::DispatchEpoch epoch;
        cet_dispatch::ObserverBinding binding{epoch,GetCurrentThreadId()};
    };
    NativeData* data_=nullptr;
    std::array<RUNTIME_FUNCTION,4> functions_{};
    std::array<cet_dispatch::PersonalityMap,2> maps_{};
    size_t count_=0;
    bool registered_=false, uncertain_=false;
public:
    explicit GuardArena(void* address=nullptr,DWORD64 tableBase=0){
        base_=static_cast<uint8_t*>(VirtualAlloc(address,Bytes,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        tableBase_=tableBase?tableBase:reinterpret_cast<DWORD64>(base_);
        if(base_)data_=new(base_+CodeBytes) NativeData;
    }
    GuardArena(const GuardArena&)=delete;
    GuardArena& operator=(const GuardArena&)=delete;
    ~GuardArena(){
        // The attachment owner must retire the table before destruction.
        if(registered_||uncertain_)std::abort();
        if(data_)data_->~NativeData();
        if(base_)VirtualFree(base_,0,MEM_RELEASE);
    }
    uint8_t* Base()const noexcept{return base_;}
    cet_dispatch::DispatchEpoch& Epoch()noexcept{return data_->epoch;}
    cet_dispatch::ObserverBinding& Binding()noexcept{return data_->binding;}
    const auto& Entries()const noexcept{return functions_;}
    bool Registered()const noexcept{return registered_;}
    bool MustRetain()const noexcept{return registered_||uncertain_;}
    cet_dispatch::PersonalityMap& Map(size_t i)noexcept{return maps_[i];}
    const auto& Maps()const noexcept{return maps_;}
    bool Seal(const std::array<RUNTIME_FUNCTION,4>& functions,size_t count)noexcept{
        if(!base_||registered_||uncertain_||!count||count>functions_.size())return false;
        const auto address=reinterpret_cast<DWORD64>(base_);
        if(address<tableBase_||address-tableBase_>UINT32_MAX-CodeBytes)return false;
        const auto offset=static_cast<DWORD>(address-tableBase_);
        DWORD previous=offset;
        for(size_t i=0;i<count;++i){
            const auto& f=functions[i];
            if(f.BeginAddress<previous||f.BeginAddress>=f.EndAddress||
               f.EndAddress>offset+CodeBytes||f.UnwindData<offset||f.UnwindData>=offset+CodeBytes)return false;
            previous=f.EndAddress;
        }
        functions_=functions;count_=count;
        DWORD old=0;
        if(!VirtualProtect(base_,CodeBytes,PAGE_EXECUTE_READ,&old)||
           !FlushInstructionCache(GetCurrentProcess(),base_,CodeBytes))return false;
        registered_=RtlAddFunctionTable(functions_.data(),static_cast<DWORD>(count_),
                                       tableBase_)!=FALSE;
        return registered_;
    }
    // Caller owns the independent drain proof. A failed deletion retains
    // the whole arena; no table pointer or code may be freed underneath OS.
    bool ReclaimMetadataAfterDrain()noexcept{
        if(uncertain_)return false;
        if(!registered_)return true;
        if(!RtlDeleteFunctionTable(functions_.data()))return false;
        registered_=false;
        for(size_t i=0;i<count_;++i){
            DWORD64 foundBase=0;
            if(RtlLookupFunctionEntry(tableBase_+functions_[i].BeginAddress,
                                      &foundBase,nullptr)!=nullptr){
                // Unexpected outstanding table identity; keep storage.
                uncertain_=true;
                return false;
            }
        }
        return true;
    }
};


}
