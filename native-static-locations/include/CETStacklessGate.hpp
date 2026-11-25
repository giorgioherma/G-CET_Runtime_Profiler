#pragma once
// Research-only x64 branch-island emitter. No patch installer. The only
// writable state is ObserverBinding's existing owner-thread GuardState.
// A production installer must establish lock, identity, address range,
// unwind/EH, thread quiescence and complete restoration independently.
#include "CETObserverBinding.hpp"
#include <initializer_list>
#include <stdexcept>
#include <vector>
#include <type_traits>

namespace gcet::cet_dispatch {
class StacklessGate {
    using State=ObserverBinding::GuardState;
    static_assert(std::is_standard_layout_v<State>);
    static_assert(sizeof(std::atomic<uint64_t>)==8&&std::atomic<uint64_t>::is_always_lock_free);
    uintptr_t base_;
    bool r15_;
    std::vector<uint8_t> code_;
    std::vector<size_t> fallback_,sleep_;
    void Bytes(std::initializer_list<uint8_t> v){code_.insert(code_.end(),v);}
    void Dword(uint32_t v){for(int i=0;i<4;++i)code_.push_back(static_cast<uint8_t>(v>>(8*i)));}
    void Fix(size_t at,uintptr_t dest){
        const auto from=base_+at+4;
        // Integer address difference without unsigned wrap.
        int64_t delta=dest>=from?static_cast<int64_t>(dest-from):-static_cast<int64_t>(from-dest);
        if(delta<INT32_MIN||delta>INT32_MAX)throw std::runtime_error("gate target outside rel32");
        uint32_t bits=static_cast<uint32_t>(static_cast<int32_t>(delta));
        for(int i=0;i<4;++i)code_[at+i]=static_cast<uint8_t>(bits>>(8*i));
    }
    void Rip(uintptr_t target){size_t at=code_.size();Dword(0);Fix(at,target);}
    void Branch(uint8_t condition,std::vector<size_t>& list){
        Bytes({0x0f,condition});list.push_back(code_.size());Dword(0);
    }
    void NE(){Branch(0x85,fallback_);}
    void E(){Branch(0x84,fallback_);}
    void Exit(uint8_t condition,uintptr_t target){Bytes({0x0f,condition});Rip(target);}
    uint8_t ModRM() const {return r15_?0x47:0x46;}
    void Load(uint8_t off){Bytes({0x49,0x8b,ModRM(),off});} // mov rax,[r14/r15+disp8]
    void CmpChain(uint8_t off){Bytes({0x49,0x3b,ModRM(),off});}
    void CmpState(uintptr_t at){Bytes({0x48,0x3b,0x05});Rip(at);}
    // RIP-relative group instructions have a trailing immediate byte.
    void CmpZero(uintptr_t at,bool wide){
        if(wide)Bytes({0x48,0x83,0x3d});else Bytes({0x80,0x3d});
        size_t disp=code_.size();Dword(0);Bytes({0});
        // rel32 base is AFTER the trailing imm8.
        auto from=base_+disp+5;
        int64_t delta=at>=from?static_cast<int64_t>(at-from):-static_cast<int64_t>(from-at);
        if(delta<INT32_MIN||delta>INT32_MAX)throw std::runtime_error("gate state outside rel32");
        uint32_t bits=static_cast<uint32_t>(static_cast<int32_t>(delta));
        for(int i=0;i<4;++i)code_[disp+i]=static_cast<uint8_t>(bits>>(8*i));
    }
    void LoadState(uintptr_t at){Bytes({0x48,0x8b,0x05});Rip(at);}
    void StockCompare(){Bytes({0x41,0x80,r15_?uint8_t{0x7f}:uint8_t{0x7e},0x70,0});}
    void Epoch(uintptr_t epoch,uintptr_t depth,uintptr_t token){
        CmpZero(depth,true);NE();LoadState(epoch);CmpState(token);NE();
    }
    void AlignedChainWord(uint8_t off){
        Bytes({0x41,0xf6,r15_?uint8_t{0x47}:uint8_t{0x46},off,7});NE();
    }
    void EmptyVector(uint8_t first,uint8_t last,uint8_t end){
        Load(first);CmpChain(last);NE();
        CmpChain(end);Branch(0x87,fallback_); // begin > capacity
        AlignedChainWord(first);AlignedChainWord(end);
        // If begin==0 capacity must also be zero; otherwise valid.
        Bytes({0x48,0x85,0xc0});
        // jne local skip (five-byte cmp + six-byte jne = 11)
        Bytes({0x75,0x0b});
        Bytes({0x49,0x83,r15_?uint8_t{0x7f}:uint8_t{0x7e},end,0});NE();
    }
public:
    StacklessGate(uintptr_t base,bool r15,const State& state,const DispatchEpoch& epoch,
                  uintptr_t activeEdge,uintptr_t emptyEdge):base_(base),r15_(r15){
        const uintptr_t s=reinterpret_cast<uintptr_t>(&state);
        const uintptr_t id=s+offsetof(State,identity);
        const uintptr_t token=s+offsetof(State,token);
        const auto ep=reinterpret_cast<uintptr_t>(&epoch.EpochWord());
        const auto depth=reinterpret_cast<uintptr_t>(&epoch.ClearDepthWord());
        // Original CET empty branch ALWAYS wins, even if no G-CET binding.
        StockCompare();Branch(0x85,sleep_);
        // TEB ClientId.UniqueThread: independently validated against
        // GetCurrentThreadId on the actual Windows test threads.
        Bytes({0x65,0x48,0x8b,0x04,0x25,0x48,0,0,0});
        CmpState(s+offsetof(State,owner));NE();
        CmpZero(s+offsetof(State,handle),true);E();
        CmpZero(s+offsetof(State,active),false);NE();
        Bytes({0x4c,0x3b,r15_?uint8_t{0x3d}:uint8_t{0x35}});
        Rip(id+offsetof(SoleBeforeIdentity,chain));NE();
        Epoch(ep,depth,token);
        Bytes({0x41,0x80,r15_?uint8_t{0x7f}:uint8_t{0x7e},0x71,0});NE();
        Load(0);CmpState(id+offsetof(SoleBeforeIdentity,trampoline));NE();
        Load(8);CmpState(id+offsetof(SoleBeforeIdentity,scripting));NE();
        // Require the originally verified owning storage, not merely a
        // matching Context pointer in an arbitrary changed vector.
        Load(24);CmpState(id+offsetof(SoleBeforeIdentity,storage));NE();
        Bytes({0x48,0x85,0xc0});E();
        Bytes({0xa8,7});NE();
        Bytes({0x48,0x83,0xc0,8});Branch(0x82,fallback_);
        CmpChain(32);NE();CmpChain(40);Branch(0x87,fallback_);
        AlignedChainWord(40);
        EmptyVector(56,64,72);EmptyVector(88,96,104);
        Load(24);Bytes({0x48,0x8b,0});CmpState(id+offsetof(SoleBeforeIdentity,context));NE();
        Epoch(ep,depth,token);
        const auto empty=base_+code_.size();
        // Windows x64 recognizes an unconditional tail jump as an epilogue,
        // even though this island did not unwind CET's frame. A conditional
        // branch is not an epilogue instruction. Empty edge resets flags
        // immediately; CMP EAX,EAX makes the JE unconditional in meaning.
        Bytes({0x39,0xc0});Exit(0x84,emptyEdge);
        const auto fallback=base_+code_.size();
        StockCompare(); // restore the actual original branch flags
        Exit(0x84,activeEdge);
        Exit(0x85,emptyEdge); // exhaustive original compare outcome
        for(auto at:fallback_)Fix(at,fallback);
        for(auto at:sleep_)Fix(at,empty);
    }
    const std::vector<uint8_t>& Code() const noexcept {return code_;}
};
}
