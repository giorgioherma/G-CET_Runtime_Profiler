#pragma once
// Actual Windows unwind of synthetic stack frames using the exact official
// CET .pdata/.xdata copied by EntryLifecycleProof. No CET instructions or
// language-specific exception handlers are executed.
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <stdexcept>

namespace gcet::research {
inline void UnwindRequire(bool ok,const char* what){
    if(!ok)throw std::runtime_error(what);
}
inline RUNTIME_FUNCTION FindRuntimeFunction(const uint8_t* image,uint32_t pc){
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(image+dos->e_lfanew);
    const auto dir=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    UnwindRequire(dir.Size%sizeof(RUNTIME_FUNCTION)==0&&dir.Size!=0,"invalid exception directory");
    auto* records=reinterpret_cast<const RUNTIME_FUNCTION*>(image+dir.VirtualAddress);
    for(size_t i=0;i<dir.Size/sizeof(RUNTIME_FUNCTION);++i){
        if(records[i].BeginAddress<=pc&&pc<records[i].EndAddress)return records[i];
    }
    throw std::runtime_error("no exact CET runtime function for guard");
}
struct UnwindResult {
    CONTEXT context{};
    DWORD64 frame=0;
    PVOID handlerData=nullptr;
    PEXCEPTION_ROUTINE handler=nullptr;
};
inline UnwindResult UnwindAt(uint8_t* image,RUNTIME_FUNCTION& entry,uint32_t pc,
                             const CONTEXT& initial,DWORD kind){
    UnwindResult r;
    r.context=initial;
    r.context.Rip=reinterpret_cast<DWORD64>(image)+pc;
    r.handler=RtlVirtualUnwind(kind,reinterpret_cast<DWORD64>(image),r.context.Rip,
        &entry,&r.context,&r.handlerData,&r.frame,nullptr);
    return r;
}
inline bool SameFrame(const UnwindResult& a,const UnwindResult& b){
    const auto& x=a.context;const auto& y=b.context;
    return x.Rip==y.Rip&&x.Rsp==y.Rsp&&x.Rbx==y.Rbx&&x.Rbp==y.Rbp&&
           x.Rsi==y.Rsi&&x.Rdi==y.Rdi&&x.R12==y.R12&&x.R13==y.R13&&
           x.R14==y.R14&&x.R15==y.R15&&a.frame==b.frame&&
           std::memcmp(&x.Xmm6,&y.Xmm6,10*sizeof(M128A))==0;
}
// The island does not establish a new frame; CHAININFO refers to the
// already-established original CET frame and its original handler data.
// Windows recognizes an unconditional terminal jump as an epilogue.
// The revised emitter uses conditional exits; test every exit PC.
inline void CheckChainedUnwind(uint8_t* image,uint32_t guard,uint32_t island,
                               uint32_t size,uint32_t metadata){
    auto original=FindRuntimeFunction(image,guard);
    const auto* info=image+original.UnwindData;
    UnwindRequire((info[0]&7)==1&&(info[0]>>3)==3&&guard-original.BeginAddress>info[1],
                  "unexpected original CET unwind flags/prologue");
    UnwindRequire(info[3]==0,"unexpected frame register");
    auto* chain=image+metadata;
    // version 1, UNW_FLAG_CHAININFO, zero local prologue/codes/frame.
    chain[0]=0x21;chain[1]=0;chain[2]=0;chain[3]=0;
    std::memcpy(chain+4,&original,sizeof(original));
    RUNTIME_FUNCTION entry{island,island+size,metadata};
    const DWORD64 base=reinterpret_cast<DWORD64>(image);
    UnwindRequire(RtlAddFunctionTable(&entry,1,base)!=FALSE,"register private chained unwind");
    struct Registration{
        RUNTIME_FUNCTION* entry;
        ~Registration(){RtlDeleteFunctionTable(entry);}
    } registration{&entry};
    alignas(16) std::array<uint64_t,8192> stack{};
    for(size_t i=0;i<stack.size();++i)stack[i]=base+0x1000+i*8;
    CONTEXT initial{};
    initial.ContextFlags=CONTEXT_FULL;
    initial.Rsp=reinterpret_cast<DWORD64>(stack.data()+64);
    initial.Rbp=0x1122334455667788ull;initial.Rbx=0x2233445566778899ull;
    initial.Rsi=0x3333;initial.Rdi=0x4444;
    initial.R12=0x1212;initial.R13=0x1313;initial.R14=0x1414;initial.R15=0x1515;
    UnwindRequire(size>=25,"short gate");
    // Emitter tail: CMP EAX,EAX; JE empty; original CMP; JE active; JNE empty.
    const uint32_t pcs[]={island,island+size-25,island+size-23,
                         island+size-17,island+size-12,island+size-6};
    UnwindRequire(image[pcs[1]]==0x39&&image[pcs[2]]==0x0f&&image[pcs[2]+1]==0x84&&
        image[pcs[3]]==0x41&&image[pcs[4]]==0x0f&&image[pcs[4]+1]==0x84&&
        image[pcs[5]]==0x0f&&image[pcs[5]+1]==0x85,"unexpected conditional gate tail");
    for(DWORD kind:{DWORD{UNW_FLAG_NHANDLER},DWORD{UNW_FLAG_EHANDLER},DWORD{UNW_FLAG_UHANDLER}}){
        auto expected=UnwindAt(image,original,guard,initial,kind);
        UnwindRequire(expected.context.Rsp>initial.Rsp&&expected.context.Rsp<
            reinterpret_cast<DWORD64>(stack.data()+stack.size()),"invalid reference unwind");
        if(kind)UnwindRequire(expected.handler&&expected.handlerData,"original CET handler not returned");
        for(uint32_t pc:pcs){
            DWORD64 foundBase=0;
            auto* found=RtlLookupFunctionEntry(base+pc,&foundBase,nullptr);
            UnwindRequire(found&&foundBase==base&&found->BeginAddress==island&&
                          found->EndAddress==island+size,"island function-table lookup");
            auto actual=UnwindAt(image,*found,pc,initial,kind);
            const bool frame=SameFrame(expected,actual);
            const bool handler=actual.handler==expected.handler&&
                               (!kind||actual.handlerData==expected.handlerData);
            std::cout<<"Chained unwind guard=0x"<<std::hex<<guard<<" pc=0x"<<pc<<std::dec
                     <<" kind="<<kind<<" frame="<<frame<<" handler="<<handler<<"\n";
            // This is an evidence gate, not a proof of language-handler
            // semantics. If a jump is interpreted as an epilogue, reject
            // CHAININFO for production rather than relaxing the test.
            UnwindRequire(frame&&handler,"chained island unwind differs from original guard");
        }
    }
    std::cout<<"Official CET guard frame/handler-data chained unwind PASS\n";
    // Even matching handler pointer/data is NOT sufficient: the language
    // handler's dispatcher ControlPc still points outside its original
    // function. It needs separately proven state translation.
}
}
