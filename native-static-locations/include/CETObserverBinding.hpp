#pragma once
// Compiled-layout admission building block for official CET 1.37.1.
// It is deliberately NOT an exposed RTTI function or live hook yet.
// Native entry/branch adapters must supply proven current CET lock ownership,
// dispatch identity and lifetime. No scheduler, no CET object mutation.
#include <atomic>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <span>

namespace gcet::cet_dispatch {
static_assert(sizeof(uintptr_t)==8,"official CET target is x64");

struct SoleBeforeIdentity {
    uintptr_t chain=0, trampoline=0, scripting=0, context=0, storage=0;
    bool operator==(const SoleBeforeIdentity&) const = default;
    explicit operator bool() const noexcept {
        return chain&&trampoline&&scripting&&context&&storage;
    }
};

class CompiledChainView {
public:
    static constexpr size_t Size=120;
    static constexpr size_t Before=16,After=48,Overrides=80;
    static constexpr size_t Empty=112,GC=113;
    using Bytes=std::span<const std::byte,Size>;

    // Caller proves stable readable chain under CET's existing lock. Reader
    // copies ONE owning Context pointer from that same protected vector.
    // No cross-DLL std::vector/unique_ptr construction, no scans, no writes.
    template<class ReadPointer>
    static SoleBeforeIdentity Read(uintptr_t chain,Bytes bytes,ReadPointer&& readPointer) noexcept {
        if(!chain||(chain&7)||bytes[Empty]!=std::byte{0}||bytes[GC]!=std::byte{0})return {};
        const auto b=Vector(bytes,Before),a=Vector(bytes,After),o=Vector(bytes,Overrides);
        if(!b.Valid()||!a.Valid()||!o.Valid()||b.last-b.first!=8||
            a.first!=a.last||o.first!=o.last)return {};
        SoleBeforeIdentity result{chain,ReadWord(bytes,0),ReadWord(bytes,8),0,b.first};
        if(!readPointer(b.first,result.context)||!result||((result.context|result.trampoline|result.scripting)&7))
            return {};
        return result;
    }

private:
    struct Extent {
        uintptr_t first,last,end;
        bool Valid() const noexcept {
            if(!first)return !last&&!end;
            return !((first|last|end)&7)&&first<=last&&last<=end;
        }
    };
    static uintptr_t ReadWord(Bytes bytes,size_t offset) noexcept {
        uintptr_t value=0;
        std::memcpy(&value,bytes.data()+offset,sizeof(value));
        return value;
    }
    static Extent Vector(Bytes bytes,size_t offset) noexcept {
        // PDB: 32-byte vector; begin/end/capacity fields at +8/+16/+24.
        return {ReadWord(bytes,offset+8),ReadWord(bytes,offset+16),ReadWord(bytes,offset+24)};
    }
};

// A process-local epoch for CET Clear/reload. Multiple/nested invalidations
// fail open until the last completes. A stale pointer can never reauthorize
// an old handle just because the CET allocator reuses the same address.
// Clear may begin on any thread; the callback mirror is owner-thread-only.
class DispatchEpoch {
public:
    using Token=uint64_t;
    class Invalidation {
    public:
        explicit Invalidation(DispatchEpoch& owner) noexcept : owner_(owner){
            owner_.clearDepth_.fetch_add(1,std::memory_order_acq_rel);
            owner_.Advance();
        }
        ~Invalidation(){
            owner_.Advance();
            owner_.clearDepth_.fetch_sub(1,std::memory_order_release);
        }
        Invalidation(const Invalidation&)=delete;
        Invalidation& operator=(const Invalidation&)=delete;
    private:DispatchEpoch& owner_;
    };
    Token Current() const noexcept {
        if(clearDepth_.load(std::memory_order_acquire))return 0;
        auto a=epoch_.load(std::memory_order_acquire);
        if(!a||a==std::numeric_limits<Token>::max()||
           clearDepth_.load(std::memory_order_acquire)||
           a!=epoch_.load(std::memory_order_acquire))return 0;
        return a;
    }
    bool Matches(Token token) const noexcept {return token&&Current()==token;}
    // The compiled Clear site already holds exclusive CET ownership. Its
    // stackless island writes this sentinel without touching registers/flags.
    // Only a verified new invocation under the shared lock may rearm it.
    static constexpr uint64_t LockedClearSentinel=std::numeric_limits<uint64_t>::max();
    void Disable() noexcept {clearDepth_.store(LockedClearSentinel,std::memory_order_release);}
    bool CompleteLockedClear() noexcept {
        if(clearDepth_.load(std::memory_order_acquire)!=LockedClearSentinel)return false;
        Advance();clearDepth_.store(0,std::memory_order_release);return Current()!=0;
    }
    // Read-only access for a verified x64 machine gate. The epoch remains
    // authoritative here; no separate invalidation mirror.
    const std::atomic<Token>& EpochWord() const noexcept {return epoch_;}
    const std::atomic<uint64_t>& ClearDepthWord() const noexcept {return clearDepth_;}
private:
    void Advance() noexcept {
        auto v=epoch_.load(std::memory_order_relaxed);
        while(v!=std::numeric_limits<Token>::max()&&
              !epoch_.compare_exchange_weak(v,v+1,std::memory_order_acq_rel,std::memory_order_relaxed)){}
    }
    std::atomic<Token> epoch_{1};
    std::atomic<uint64_t> clearDepth_{0};
};

// One binding record, reusable for each source-proven native chain. This
// mirrors the canonical Scheduler's already-computed boolean demand only.
// No consumer counting, timer, frame polling or native scheduling policy.
// Caller MUST serialize these methods on the proven dispatch/owner thread,
// except a nonowner query with its own real thread id (short-circuit before
// touching owner-only fields). Hook lifecycle must establish that owner
// before publishing the record and keep it immutable through attachment.
class ObserverBinding {
public:
    using Handle=uint64_t;
    using Thread=uint64_t;
    struct GuardState {
        const Thread owner;
        Handle handle=0;
        DispatchEpoch::Token token=0;
        SoleBeforeIdentity identity{};
        uint8_t active=1;
    };
    const GuardState& Guard() const noexcept {return state_;}
    ObserverBinding(const DispatchEpoch& epoch,Thread owner) noexcept:epoch_(epoch),state_{owner}{}
    ObserverBinding(const ObserverBinding&)=delete;
    ObserverBinding& operator=(const ObserverBinding&)=delete;

    // Only invoke from a source-proven original managed Before callback,
    // scoped to its exact ExecuteChain invocation. The live entry adapter
    // must verify the target function/owner and actual callback identity.
    // Binding initially remains active; a separate demand transition may sleep.
    Handle Bind(Thread thread,DispatchEpoch::Token token,SoleBeforeIdentity identity) noexcept {
        if(!Owner(thread)||!identity||!epoch_.Matches(token))return 0;
        if(state_.handle&&epoch_.Matches(state_.token)){
            // Never replace or reset an existing live binding silently.
            return state_.identity==identity?state_.handle:0;
        }
        if(next_==std::numeric_limits<Handle>::max())return 0;
        state_.token=token;state_.identity=identity;state_.active=true;state_.handle=++next_;
        return state_.handle;
    }
    bool SetDemand(Thread thread,Handle handle,bool active) noexcept {
        if(!Owner(thread)||!Valid(handle))return false;
        state_.active=active;
        return true;
    }
    bool Release(Thread thread,Handle handle) noexcept {
        if(!Owner(thread)||!Valid(handle))return false;
        state_.handle=0;state_.token=0;state_.identity={};state_.active=true;
        return true;
    }
    bool CanBypass(Thread thread,SoleBeforeIdentity actual) const noexcept {
        // Actual membership is sampled under CET's own existing shared
        // lock on THIS dispatch. If a foreign observer was appended, a
        // callback changed, GC is owed, or the chain moved, retain stock.
        return Owner(thread)&&actual&&state_.handle&&!state_.active&&actual==state_.identity&&epoch_.Matches(state_.token);
    }
private:
    bool Owner(Thread thread) const noexcept {return state_.owner&&thread==state_.owner;}
    bool Valid(Handle handle) const noexcept {return handle&&handle==state_.handle&&epoch_.Matches(state_.token);}
    const DispatchEpoch& epoch_;
    GuardState state_;
    Handle next_=0;
};

 // Generic individual-context mirror. Experimental until the live
 // registration/slot identity and authoritative mutation boundary are proven.
 // No native gameplay predicate, scheduler or per-dispatch subscriber scan.
 enum class ObserverPhase : uint8_t { Before, After };
 struct IndividualIdentity {
     uintptr_t chain=0, context=0;
     ObserverPhase phase=ObserverPhase::Before;
     bool operator==(const IndividualIdentity&)const=default;
     explicit operator bool()const noexcept{return chain&&context;}
 };
 template<size_t Capacity=256>
 class IndividualObserverBindings {
     static_assert(Capacity>=2 && (Capacity&(Capacity-1))==0);
     using Handle=uint64_t;
     struct Entry {
         IndividualIdentity id{};
         DispatchEpoch::Token token=0;
         Handle handle=0;
         bool active=true;
     };
     const DispatchEpoch& epoch_;
     const uint64_t owner_;
     std::array<Entry,Capacity> entries_{};
     Handle next_=0;
     bool Owner(uint64_t thread)const noexcept{return owner_&&thread==owner_;}
     static size_t Index(IndividualIdentity id)noexcept{
         // Direct-mapped bounded lookup; collision refuses only the affected
         // binding. No probing, allocation or chain-wide search on dispatch.
         auto h=(id.context>>4)^(id.chain>>4)^(id.context>>13);
         h^=static_cast<uint64_t>(id.phase)*0x9e3779b97f4a7c15ull;
         return static_cast<size_t>(h)&(Capacity-1);
     }
     Entry* ByHandle(Handle h)noexcept{
         if(!h)return nullptr;
         auto& e=entries_[h&(Capacity-1)];
         return e.handle==h&&epoch_.Matches(e.token)?&e:nullptr;
     }
 public:
     IndividualObserverBindings(const DispatchEpoch& epoch,uint64_t owner)noexcept:epoch_(epoch),owner_(owner){}
     IndividualObserverBindings(const IndividualObserverBindings&)=delete;
     IndividualObserverBindings& operator=(const IndividualObserverBindings&)=delete;
     // The caller must prove exact original callback ownership, and invalidate
     // the epoch before any mutation that can destroy/reuse a Context.
     Handle Bind(uint64_t thread,DispatchEpoch::Token token,IndividualIdentity id)noexcept{
         if(!Owner(thread)||!id||!epoch_.Matches(token))return 0;
         const auto slot=Index(id);auto& e=entries_[slot];
         if(e.handle&&epoch_.Matches(e.token))return e.id==id?e.handle:0;
         if(next_>=(std::numeric_limits<Handle>::max()/Capacity)-1)return 0;
         const auto handle=(++next_)*Capacity+slot;
         e={id,token,handle,true};return handle;
     }
     bool SetDemand(uint64_t thread,Handle h,bool active)noexcept{
         if(!Owner(thread))return false;
         auto* e=ByHandle(h);if(!e)return false;
         e->active=active;return true;
     }
     bool Release(uint64_t thread,Handle h)noexcept{
         if(!Owner(thread))return false;
         auto* e=ByHandle(h);if(!e)return false;
         *e={};return true;
     }
     bool CanSkip(uint64_t thread,IndividualIdentity id)const noexcept{
         if(!Owner(thread)||!id)return false;
         const auto& e=entries_[Index(id)];
         return e.handle&&!e.active&&e.id==id&&epoch_.Matches(e.token);
     }
 };
}
