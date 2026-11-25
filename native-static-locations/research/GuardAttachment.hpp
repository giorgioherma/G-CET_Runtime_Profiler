#pragma once
// Shared research transaction and one-owner attachment lifecycle. No loaded
// CET admission: this unit deliberately accepts only proven MEM_PRIVATE
// guard spans under a fixture-owned existing SRW lock. It is not linked into
// the product. No recurring scheduler, polling, or extra dispatch lock.
#include "CETExistingMutex.hpp"
#include "CETObserverBinding.hpp"
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <memory>
#include <shared_mutex>
#include <stdexcept>
#include <cstdlib>
#include <utility>
#include <new>
namespace gcet::research {
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
        if(install&&installed_)return GuardResult::Refused;
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

 // Arena owns the machine code, binding state, epoch and registered table.
 // The caller prepares code/metadata before Seal; no table or guard is
 // published until all resources are allocated. Metadata is not reclaimed
 // merely because guard bytes have been restored.
class GuardArena {
    uint8_t* base_=nullptr;
    static constexpr size_t Bytes=8192,CodeBytes=4096;
    struct NativeData {
        cet_dispatch::DispatchEpoch epoch;
        cet_dispatch::ObserverBinding binding{epoch,GetCurrentThreadId()};
    };
    NativeData* data_=nullptr;
    std::array<RUNTIME_FUNCTION,4> functions_{};
    size_t count_=0;
    bool registered_=false;
public:
    GuardArena(){
        base_=static_cast<uint8_t*>(VirtualAlloc(nullptr,Bytes,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(base_)data_=new(base_+CodeBytes) NativeData;
    }
    GuardArena(const GuardArena&)=delete;
    GuardArena& operator=(const GuardArena&)=delete;
    ~GuardArena(){
        // The attachment owner must retire the table before destruction.
        if(registered_)std::abort();
        if(data_)data_->~NativeData();
        if(base_)VirtualFree(base_,0,MEM_RELEASE);
    }
    uint8_t* Base()const noexcept{return base_;}
    cet_dispatch::DispatchEpoch& Epoch()noexcept{return data_->epoch;}
    cet_dispatch::ObserverBinding& Binding()noexcept{return data_->binding;}
    const auto& Entries()const noexcept{return functions_;}
    bool Registered()const noexcept{return registered_;}
    bool Seal(const std::array<RUNTIME_FUNCTION,4>& functions,size_t count)noexcept{
        if(!base_||registered_||!count||count>functions_.size())return false;
        DWORD previous=0;
        for(size_t i=0;i<count;++i){
            const auto& f=functions[i];
            if(f.BeginAddress<previous||f.BeginAddress>=f.EndAddress||
               f.EndAddress>CodeBytes||f.UnwindData>=CodeBytes)return false;
            previous=f.EndAddress;
        }
        functions_=functions;count_=count;
        DWORD old=0;
        if(!VirtualProtect(base_,CodeBytes,PAGE_EXECUTE_READ,&old)||
           !FlushInstructionCache(GetCurrentProcess(),base_,CodeBytes))return false;
        registered_=RtlAddFunctionTable(functions_.data(),static_cast<DWORD>(count_),
                                       reinterpret_cast<DWORD64>(base_))!=FALSE;
        return registered_;
    }
    // Caller owns the independent drain proof. A failed deletion retains
    // the whole arena; no table pointer or code may be freed underneath OS.
    bool ReclaimMetadataAfterDrain()noexcept{
        if(!registered_)return true;
        if(!RtlDeleteFunctionTable(functions_.data()))return false;
        registered_=false;
        for(size_t i=0;i<count_;++i){
            DWORD64 foundBase=0;
            if(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(base_)+functions_[i].BeginAddress,
                                      &foundBase,nullptr)!=nullptr){
                // Unexpected outstanding table identity; keep storage.
                return false;
            }
        }
        return true;
    }
};

enum class AttachmentPhase { Prepared, Attached, Retiring, Restored, Unsafe };
class GuardAttachment {
    struct State {
        std::unique_ptr<GuardArena> arena;
        // Existing owner is retained by the same guard transaction.
        SynchronizedPrivateGuards guards;
        const DWORD ownerThread;
        AttachmentPhase phase=AttachmentPhase::Prepared;
        State(std::unique_ptr<GuardArena> a,std::shared_ptr<std::shared_mutex> lock,
              std::array<GuardEdit,2> edits)
            :arena(std::move(a)),guards(std::move(lock),edits),ownerThread(GetCurrentThreadId()){}
    };
    std::unique_ptr<State> state_;
public:
    GuardAttachment(std::unique_ptr<GuardArena> arena,std::shared_ptr<std::shared_mutex> owner,
                    std::array<GuardEdit,2> edits)
        :state_(std::make_unique<State>(std::move(arena),std::move(owner),edits)){}
    GuardAttachment(const GuardAttachment&)=delete;
    GuardAttachment& operator=(const GuardAttachment&)=delete;
    ~GuardAttachment(){
        // Explicit retirement is mandatory. If the caller abandons a live
        // or uncertain session, retain the complete backing state rather
        // than release code/table/lock references. This is a bounded one
        // session safety fallback in the research process, not a live CET
        // unload or process-lifetime module-pinning policy.
        if(state_&&state_->arena&&state_->arena->Registered())(void)state_.release();
    }
    AttachmentPhase Phase()const noexcept{return state_?state_->phase:AttachmentPhase::Restored;}
    GuardArena* Arena()const noexcept{return state_?state_->arena.get():nullptr;}
    GuardResult Attach(int failBefore=-1){
        if(!Owned()||!state_->arena||!state_->arena->Registered()||
           (state_->phase!=AttachmentPhase::Prepared&&state_->phase!=AttachmentPhase::Restored))
            return GuardResult::Refused;
        auto r=state_->guards.Change(true,failBefore);
        if(r==GuardResult::Success)state_->phase=AttachmentPhase::Attached;
        if(r==GuardResult::Unsafe)state_->phase=AttachmentPhase::Unsafe;
        return r;
    }
    GuardResult Retire(int failBefore=-1){
        if(!Owned()||state_->phase==AttachmentPhase::Unsafe)return GuardResult::Refused;
        // Invalidate the old binding even if the exclusive lease is busy.
        // A failed restore cannot leave a stale dormant handle authorized.
        if(state_->phase==AttachmentPhase::Attached){
            state_->phase=AttachmentPhase::Retiring;
            cet_dispatch::DispatchEpoch::Invalidation invalidate(state_->arena->Epoch());
        }
        auto r=state_->guards.Change(false,failBefore);
        if(r==GuardResult::Success)state_->phase=AttachmentPhase::Restored;
        else if(r==GuardResult::Unsafe)state_->phase=AttachmentPhase::Unsafe;
        return r;
    }
    // This admission is ONLY for the original verified callback in a
    // fixture invocation. A production entry adapter must prove the exact
    // managed callback/RTTI identity and its current dispatch scope.
    cet_dispatch::ObserverBinding::Handle Bind(cet_dispatch::SoleBeforeIdentity identity)noexcept{
        if(!Owned()||state_->phase!=AttachmentPhase::Attached)return 0;
        return state_->arena->Binding().Bind(GetCurrentThreadId(),state_->arena->Epoch().Current(),identity);
    }
    bool SetDemand(cet_dispatch::ObserverBinding::Handle handle,bool active)noexcept{
        return Owned()&&state_->phase==AttachmentPhase::Attached&&
               state_->arena->Binding().SetDemand(GetCurrentThreadId(),handle,active);
    }
    bool ReclaimAfterDrain(bool allUsersJoined){
        if(!Owned()||!allUsersJoined||state_->phase!=AttachmentPhase::Restored)return false;
        // Recheck stock spans under the same lease before metadata removal.
        if(state_->guards.Change(false)!=GuardResult::Success)return false;
        if(!state_->arena->ReclaimMetadataAfterDrain())return false;
        state_.reset();
        return true;
    }
private:
    bool Owned()const noexcept{return state_&&state_->ownerThread==GetCurrentThreadId();}
};
}
