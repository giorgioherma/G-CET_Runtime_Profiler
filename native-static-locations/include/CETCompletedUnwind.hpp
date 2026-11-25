#pragma once
// Windows x64 completed-frame exception adapter for an already-established
// original frame. Install-time only metadata generation. No FH4 decoding,
// writes to original metadata, hot-path helper, scheduling or live patching.
// The caller must prove original code/prologue, stable lifetime, a same-state
// PC mapping, and register a valid island/near handler thunk before publish.
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <span>
#include <vector>

namespace gcet::cet_dispatch {
inline bool BuildCompletedFrameUnwind(std::span<const uint8_t> original,
                                      uint32_t handlerRva,
                                      std::vector<uint8_t>& output) {
    if(original.size()<4)return false;
    const bool handler=original[0]==0x19;
    if((!handler&&original[0]!=0x01)||handler!=bool(handlerRva))return false;
    const unsigned count=original[2],padded=(count+1)&~1u;
    const size_t end=4+2*padded, size=end+(handler?4:0);
    if(!count||original.size()<size)return false;
    std::vector<uint8_t> next(original.begin(),original.begin()+size);
    next[1]=0;
    unsigned previous=original[1];
    for(unsigned i=0;i<count;){
        auto* code=next.data()+4+i*2;
        const unsigned offset=code[0],op=code[1]&15,arg=code[1]>>4;
        if(offset>previous)return false;
        previous=offset;
        unsigned slots=1;
        switch(op){
        case 0:case 2:break;
        case 3:if(arg)return false;break;
        case 1:if(arg>1)return false;slots=arg?3:2;break;
        case 4:case 8:slots=2;break;
        case 5:case 9:slots=3;break;
        // A machine-frame prologue is not an ordinary CET C++ frame.
        default:return false;
        }
        if(i+slots>count)return false;
        code[0]=0;i+=slots;
    }
    if(handler)std::memcpy(next.data()+end,&handlerRva,4);
    output.swap(next);
    return true;
}

enum class ControlPcMode { FixedGuard, RelocatedReturn };
struct PersonalityMap {
    DWORD64 imageBase=0;
    RUNTIME_FUNCTION original{},island{};
    PEXCEPTION_ROUTINE originalHandler=nullptr;
    PVOID originalHandlerData=nullptr;
    DWORD64 originalPc=0,islandReturn=0;
    ControlPcMode mode=ControlPcMode::FixedGuard;

    // One immutable plan per registered island. The exception handler
    // supplies the OS dispatcher context; never infer identity from the
    // exception address alone (which can refer to a different callee).
    bool Translate(const DISPATCHER_CONTEXT& source,DISPATCHER_CONTEXT& mapped) noexcept {
        if(!imageBase||!originalHandler||!originalHandlerData||source.ImageBase!=imageBase||
           !source.FunctionEntry||source.FunctionEntry->BeginAddress!=island.BeginAddress||
           source.FunctionEntry->EndAddress!=island.EndAddress||
           source.FunctionEntry->UnwindData!=island.UnwindData||
           original.BeginAddress>=original.EndAddress||island.BeginAddress>=island.EndAddress||
           source.ControlPc<imageBase+island.BeginAddress||source.ControlPc>=imageBase+island.EndAddress)
            return false;
        DWORD64 pc=originalPc;
        if(mode==ControlPcMode::RelocatedReturn){
            if(source.ControlPc!=islandReturn&&source.ControlPc+1!=islandReturn)return false;
            if(pc<islandReturn-source.ControlPc)return false;
            pc-=islandReturn-source.ControlPc;
        }
        if(pc<imageBase+original.BeginAddress||pc>=imageBase+original.EndAddress)return false;
        mapped=source;
        mapped.ControlPc=pc;
        mapped.FunctionEntry=&original;
        mapped.HandlerData=originalHandlerData;
        mapped.LanguageHandler=originalHandler;
        return true;
    }
    // Use only after successful Translate. Preserve the original exception
    // record/context; the original /GS and FH4 personality owns cleanup.
    EXCEPTION_DISPOSITION Forward(PEXCEPTION_RECORD record,PVOID frame,PCONTEXT context,
                                  DISPATCHER_CONTEXT& source,DISPATCHER_CONTEXT& mapped) {
        auto result=originalHandler(record,frame,context,&mapped);
        source.ScopeIndex=mapped.ScopeIndex;
        return result;
    }
};
}
