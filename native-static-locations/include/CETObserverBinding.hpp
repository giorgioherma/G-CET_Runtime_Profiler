#pragma once
// Compiled-layout admission building block for official CET 1.37.1.
// It is deliberately NOT an exposed RTTI function or live hook yet.
// Native entry/branch adapters must supply proven current CET lock ownership,
// dispatch identity and lifetime. No scheduler, no CET object mutation.
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <span>

namespace gcet::cet_dispatch {
static_assert(sizeof(uintptr_t)==8,"official CET target is x64");

struct SoleBeforeIdentity {
    uintptr_t chain=0, trampoline=0, scripting=0, context=0;
    bool operator==(const SoleBeforeIdentity&) const = default;
    explicit operator bool() const noexcept {
        return chain&&trampoline&&scripting&&context;
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
        SoleBeforeIdentity result{chain,ReadWord(bytes,0),ReadWord(bytes,8),0};
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
    ObserverBinding(const DispatchEpoch& epoch,Thread owner) noexcept:epoch_(epoch),owner_(owner){}
    ObserverBinding(const ObserverBinding&)=delete;
    ObserverBinding& operator=(const ObserverBinding&)=delete;

    // Only invoke from a source-proven original managed Before callback,
    // scoped to its exact ExecuteChain invocation. The live entry adapter
    // must verify the target function/owner and actual callback identity.
    // Binding initially remains active; a separate demand transition may sleep.
    Handle Bind(Thread thread,DispatchEpoch::Token token,SoleBeforeIdentity identity) noexcept {
        if(!Owner(thread)||!identity||!epoch_.Matches(token))return 0;
        if(handle_&&epoch_.Matches(token_)){
            // Never replace or reset an existing live binding silently.
            return identity_==identity?handle_:0;
        }
        if(next_==std::numeric_limits<Handle>::max())return 0;
        token_=token;identity_=identity;active_=true;handle_=++next_;
        return handle_;
    }
    bool SetDemand(Thread thread,Handle handle,bool active) noexcept {
        if(!Owner(thread)||!Valid(handle))return false;
        active_=active;
        return true;
    }
    bool Release(Thread thread,Handle handle) noexcept {
        if(!Owner(thread)||!Valid(handle))return false;
        handle_=0;token_=0;identity_={};active_=true;
        return true;
    }
    bool CanBypass(Thread thread,SoleBeforeIdentity actual) const noexcept {
        // Actual membership is sampled under CET's own existing shared
        // lock on THIS dispatch. If a foreign observer was appended, a
        // callback changed, GC is owed, or the chain moved, retain stock.
        return Owner(thread)&&actual&&handle_&&!active_&&actual==identity_&&epoch_.Matches(token_);
    }
private:
    bool Owner(Thread thread) const noexcept {return owner_&&thread==owner_;}
    bool Valid(Handle handle) const noexcept {return handle&&handle==handle_&&epoch_.Matches(token_);}
    const DispatchEpoch& epoch_;
    const Thread owner_;
    Handle next_=0,handle_=0;
    DispatchEpoch::Token token_=0;
    SoleBeforeIdentity identity_{};
    bool active_=true;
};
}
