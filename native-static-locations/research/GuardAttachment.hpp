#pragma once
// Shared research transaction and one-owner attachment lifecycle. No loaded
// CET admission: this unit deliberately accepts only proven MEM_PRIVATE
// guard spans under a fixture-owned existing SRW lock. It is not linked into
// the product. No recurring scheduler, polling, or extra dispatch lock.
#include "CETGuardTransaction.hpp"
#include "CETObserverBinding.hpp"
#include "CETCompletedUnwind.hpp"
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
using cet_dispatch::GuardRequire;
using cet_dispatch::PatchBytes;
using cet_dispatch::Redirect;
using cet_dispatch::Destination;
using cet_dispatch::Equal;
using cet_dispatch::GuardResult;
using cet_dispatch::GuardEdit;
using cet_dispatch::GuardArena;
class SynchronizedPrivateGuards {
    std::shared_ptr<std::shared_mutex> owner_;
    cet_dispatch::GuardTransaction<2> transaction_;
public:
    SynchronizedPrivateGuards(std::shared_ptr<std::shared_mutex> owner,std::array<GuardEdit,2> edits)
        :owner_(std::move(owner)),transaction_(owner_?owner_->native_handle():nullptr,nullptr,MEM_PRIVATE,edits){}
    ~SynchronizedPrivateGuards(){if(transaction_.MustRetain())std::abort();}
    GuardResult Change(bool install,int failBefore=-1){
        if(!owner_)return GuardResult::Refused;
        cet_dispatch::ExistingMutexLease lease(owner_->native_handle());
        return transaction_.Change(lease,install,failBefore);
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
        if(state_&&state_->arena&&(state_->arena->MustRetain()||state_->phase==AttachmentPhase::Unsafe))(void)state_.release();
    }
    AttachmentPhase Phase()const noexcept{return state_?state_->phase:AttachmentPhase::Restored;}
    GuardArena* Arena()const noexcept{return state_?state_->arena.get():nullptr;}
    bool Seal(const std::array<RUNTIME_FUNCTION,4>& functions,size_t count)noexcept{
        return Owned()&&state_->phase==AttachmentPhase::Prepared&&state_->arena&&state_->arena->Seal(functions,count);
    }
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
        if(!state_->arena->ReclaimMetadataAfterDrain()){
            if(!state_->arena->Registered())state_->phase=AttachmentPhase::Unsafe;
            return false;
        }
        state_.reset();
        return true;
    }
private:
    bool Owned()const noexcept{return state_&&state_->ownerThread==GetCurrentThreadId();}
};
}
