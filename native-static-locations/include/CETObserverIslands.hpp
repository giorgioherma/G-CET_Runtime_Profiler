#pragma once
#include "CETGuardTransaction.hpp"
#include <cstring>
namespace gcet::cet_dispatch {
inline void WriteRelative(uint8_t* at,uintptr_t target,size_t trailing=0){
    const auto from=reinterpret_cast<uintptr_t>(at)+4+trailing;
    const auto distance=target>=from?target-from:from-target;
    GuardRequire(distance<=uint64_t(target>=from?INT32_MAX:2147483648ull),"observer rel32");
    const int32_t value=static_cast<int32_t>(target>=from?int64_t(distance):-int64_t(distance));
    std::memcpy(at,&value,4);
}
inline void AbsoluteTail(uint8_t* at,uintptr_t target){
    const uint8_t code[]={0xff,0x25,0,0,0,0};
    std::memcpy(at,code,sizeof(code));std::memcpy(at+6,&target,8);
}
// No frame, no register arguments or stack arguments changed. Stock path
// uses one byte comparison and two direct edges; the native scope exists
// only while a source-owned callback is being bound.
inline void EmitDiscoveryTail(uint8_t* t,uintptr_t armed,uintptr_t owner,uintptr_t name,
                              uintptr_t scope,uintptr_t original){
    const uint8_t code[]={
        0x80,0x3d,0,0,0,0,0,             // cmp armed,0
        0x0f,0x84,0,0,0,0,              // je stock
        0x65,0x48,0x8b,0x04,0x25,0x48,0,0,0, // TEB owner thread
        0x48,0x3b,0x05,0,0,0,0,         // cmp owner
        0x0f,0x85,0,0,0,0,              // jne stock
        0x48,0x8b,0x01,                 // mov rax,[rcx] (trampoline)
        0x48,0x85,0xc0,                 // test rax,rax
        0x0f,0x84,0,0,0,0,              // je stock
        0x48,0x8b,0x40,0x08,            // mov rax,[rax+8] (fullName)
        0x48,0x3b,0x05,0,0,0,0,         // cmp name
        0x0f,0x85,0,0,0,0               // jne stock
    };
    static_assert(sizeof(code)==64);
    std::memcpy(t,code,sizeof(code));
    const auto stock=reinterpret_cast<uintptr_t>(t+78);
    WriteRelative(t+2,armed,1);WriteRelative(t+9,stock);
    WriteRelative(t+25,owner);WriteRelative(t+31,stock);
    WriteRelative(t+43,stock);WriteRelative(t+54,name);WriteRelative(t+60,stock);
    AbsoluteTail(t+64,scope);t[78]=0xe9;WriteRelative(t+79,original);
}
inline constexpr size_t LockedClearIslandSize=35;
// Only at the proven post-acquisition Clear instruction, under CET's
// existing exclusive lock. Preserve FLAGS, registers, original instruction.
inline void EmitLockedClear(uint8_t* c,uintptr_t depth,uintptr_t armed,
                            const std::array<uint8_t,5>& stock,uintptr_t resume){
    const uint8_t mark[]={0x48,0xc7,0x05,0,0,0,0,0xff,0xff,0xff,0xff};
    std::memcpy(c,mark,sizeof(mark));WriteRelative(c+3,depth,4);
    const uint8_t disarm[]={0xc6,0x05,0,0,0,0,0};
    std::memcpy(c+11,disarm,sizeof(disarm));WriteRelative(c+13,armed,1);
    std::memcpy(c+18,stock.data(),5);
    c[23]=0x0f;c[24]=0x84;WriteRelative(c+25,resume);
    c[29]=0x0f;c[30]=0x85;WriteRelative(c+31,resume);
}
}
