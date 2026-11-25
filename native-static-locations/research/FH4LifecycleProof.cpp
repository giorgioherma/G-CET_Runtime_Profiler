// Isolated MSVC x64 FH4 + /GS executable contract. No CET module or game
// loaded. Patch ONLY this executable's source-proven fixture callsite.
// A test call island preserves the compiled caller frame but relocates the
// return PC into generated code, so a real throw must use our personality
// bridge. This deliberately tests C++ EH, not the stackless hot gate itself.
#define NOMINMAX
#include <windows.h>
#include <intrin.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <iostream>
#include <limits>

extern "C" EXCEPTION_DISPOSITION __GSHandlerCheck_EH4(PEXCEPTION_RECORD,PVOID,PCONTEXT,PVOID);
namespace {
void Check(bool ok,const char* why){
    if(!ok){std::cerr<<why<<"\n";std::abort();}
}
struct LocalError{};
struct OuterError{};
unsigned destroyed[4]{},caughtLocal=0,caughtOuter=0;
volatile uintptr_t observedReturn=0;
struct Marker{
    unsigned index;
    __declspec(noinline) ~Marker(){++destroyed[index];}
};
__declspec(noinline) void Touch(volatile char* buf){buf[0]=3;buf[63]=5;}
__declspec(noinline) int Thrower(int mode){
    Marker marker{2};
    observedReturn=reinterpret_cast<uintptr_t>(_ReturnAddress());
    if(mode==1)throw LocalError{};
    if(mode==2)throw OuterError{};
    return 7;
}
// Keep the patch site on its own executable image section. Only the
// single-threaded fixture's own code is writable during the transaction.
#pragma strict_gs_check(push, on)
#pragma optimize("", off)
#pragma code_seg(push,".gctest")
__declspec(noinline) int Fixture(int mode){
    char buf[64];
    Touch(buf);
    Marker outer{0};
    try{
        Marker inner{1};
        const int value=Thrower(mode);
        Touch(buf);
        return value+buf[0]+buf[63];
    }catch(const LocalError&){
        ++caughtLocal;
        return 77;
    }
}
#pragma code_seg(pop)
#pragma optimize("", on)
#pragma strict_gs_check(pop)
int Invoke(int mode){
    Marker nearSide{3};
    try{return Fixture(mode);}
    catch(const OuterError&){++caughtOuter;return 88;}
}
void Reset(){
    for(auto& n:destroyed)n=0;
    caughtLocal=caughtOuter=0;observedReturn=0;
}
void Verify(int mode){
    const int expected=mode==0?15:mode==1?77:88;
    Check(Invoke(mode)==expected,"C++ return/catch changed");
    for(auto n:destroyed)Check(n==1,"RAII destructor not executed exactly once");
    Check(caughtLocal==unsigned(mode==1)&&caughtOuter==unsigned(mode==2),
          "wrong C++ catch target");
}
using Address=uintptr_t;
int32_t Rel32(Address next,Address target){
    const uint64_t n=target>=next?target-next:next-target;
    Check(n<=uint64_t(target>=next?INT32_MAX:2147483648ull),"rel32 overflow");
    return static_cast<int32_t>(target>=next?int64_t(n):-int64_t(n));
}
void Dword(uint8_t* p,uint32_t v){std::memcpy(p,&v,4);}
uint32_t Rva(Address base,Address p){
    Check(p>=base&&p-base<=UINT32_MAX,"RVA overflow");
    return static_cast<uint32_t>(p-base);
}
uint8_t* AllocateAfterImage(Address base){
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
    Check(dos->e_magic==IMAGE_DOS_SIGNATURE&&nt->Signature==IMAGE_NT_SIGNATURE,"fixture PE identity");
    SYSTEM_INFO si{};GetSystemInfo(&si);
    const Address gran=si.dwAllocationGranularity;
    auto align=[&](Address a){return (a+gran-1)&~(gran-1);};
    Address cursor=align(base+nt->OptionalHeader.SizeOfImage);
    const Address limit=base+0x70000000;
    while(cursor<limit){
        MEMORY_BASIC_INFORMATION mbi{};
        if(VirtualQuery(reinterpret_cast<void*>(cursor),&mbi,sizeof(mbi))!=sizeof(mbi))break;
        const Address end=reinterpret_cast<Address>(mbi.BaseAddress)+mbi.RegionSize;
        if(mbi.State==MEM_FREE&&end>=cursor+4096){
            if(auto* p=VirtualAlloc(reinterpret_cast<void*>(cursor),4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE))
                return static_cast<uint8_t*>(p);
        }
        Check(end>cursor,"nonprogressing near allocation");
        cursor=align(end);
    }
    return nullptr;
}
struct Context {
    Address base=0,originalReturn=0,islandReturn=0;
    RUNTIME_FUNCTION original{},island{};
    PEXCEPTION_ROUTINE personality=nullptr;
    void* handlerData=nullptr;
    unsigned search=0,unwind=0;
} state;
EXCEPTION_DISPOSITION NTAPI Bridge(PEXCEPTION_RECORD record,PVOID frame,
                                   PCONTEXT context,PVOID opaque){
    auto* dispatch=static_cast<PDISPATCHER_CONTEXT>(opaque);
    Check(dispatch->ImageBase==state.base,"FH4 island image base mismatch");
    const auto pc=dispatch->ControlPc;
    // The relocated call has the same logical return address as the
    // original. Preserve any one-byte control-PC adjustment by the OS.
    Check(pc==state.islandReturn||pc+1==state.islandReturn,
          "unexpected FH4 control PC in test island");
    DISPATCHER_CONTEXT mapped=*dispatch;
    mapped.ControlPc=state.originalReturn-(state.islandReturn-pc);
    mapped.FunctionEntry=&state.original;
    mapped.HandlerData=state.handlerData;
    mapped.LanguageHandler=state.personality;
    if(record->ExceptionFlags&(EXCEPTION_UNWINDING|EXCEPTION_EXIT_UNWIND))++state.unwind;
    else ++state.search;
    // Forward the original stack-cookie-protected CRT personality. Do not
    // decode, copy or rewrite compressed FuncInfo4 or GS cookie data.
    const auto result=state.personality(record,frame,context,&mapped);
    dispatch->ScopeIndex=mapped.ScopeIndex;
    return result;
}
void BuildUnwind(uint8_t* info,const uint8_t* original,uint32_t handler){
    Check((original[0]&7)==1&&(original[0]>>3)==3,"fixture must have EH+UH unwind");
    const unsigned count=original[2],padded=(count+1)&~1u;
    Check(count&&padded<=64,"unsupported fixture unwind slot count");
    std::memcpy(info,original,4+2*padded);
    // The island begins AFTER the original frame is fully established.
    // Reuse all original stack restoration operations with offset zero.
    // Extra operand slots must not be interpreted as operations.
    info[1]=0;
    for(unsigned i=0;i<count;){
        auto* code=info+4+i*2;
        const unsigned op=code[1]&15,opinfo=code[1]>>4;
        unsigned slots=1;
        switch(op){
        case 0:case 2:case 3:case 10:break;
        case 1:Check(opinfo<=1,"unsupported large allocation");slots=opinfo?3:2;break;
        case 4:case 8:slots=2;break;
        case 5:case 9:slots=3;break;
        default:Check(false,"unsupported unwind operation");
        }
        Check(i+slots<=count,"truncated unwind operation");
        code[0]=0;i+=slots;
    }
    Dword(info+4+2*padded,handler);
}
void WriteCallsite(uint8_t* site,const std::array<uint8_t,5>& bytes){
    DWORD old=0,unused=0;
    Check(VirtualProtect(site,bytes.size(),PAGE_READWRITE,&old)!=0&&old==PAGE_EXECUTE_READ,
          "fixture callsite protection");
    std::memcpy(site,bytes.data(),bytes.size());
    Check(VirtualProtect(site,bytes.size(),old,&unused)!=0,"fixture RX restoration");
    Check(FlushInstructionCache(GetCurrentProcess(),site,bytes.size())!=0,"flush fixture callsite");
}
}
int main(){
    // Baseline tests include real local catch and propagation across caller.
    for(int mode=0;mode<3;++mode){Reset();Verify(mode);}
    const Address ret=observedReturn;
    Check(ret>5,"missing source-proven call return");
    auto* site=reinterpret_cast<uint8_t*>(ret-5);
    Check(site[0]==0xe8,"fixture call is not direct rel32");
    int32_t oldRel=0;std::memcpy(&oldRel,site+1,4);
    const auto target=static_cast<Address>(static_cast<int64_t>(ret)+oldRel);
    Check(target==reinterpret_cast<Address>(&Thrower),"fixture call target differs");
    DWORD64 base=0;
    auto* original=RtlLookupFunctionEntry(ret-1,&base,nullptr);
    Check(original&&base==reinterpret_cast<Address>(GetModuleHandleW(nullptr))&&
          ret-5>=base+original->BeginAddress&&ret<base+original->EndAddress,
          "original runtime function identity");
    const auto* info=reinterpret_cast<const uint8_t*>(base+original->UnwindData);
    const unsigned padded=(info[2]+1)&~1u;
    uint32_t handler=0;std::memcpy(&handler,info+4+padded*2,4);
    auto personality=reinterpret_cast<PEXCEPTION_ROUTINE>(base+handler);
    std::cout<<"Fixture runtime function RVA=0x"<<std::hex<<original->BeginAddress
             <<" unwind=0x"<<original->UnwindData<<" flags="<<unsigned(info[0]>>3)
             <<" actual personality=0x"<<handler<<" expected GS/FH4=0x"
             <<(reinterpret_cast<Address>(&__GSHandlerCheck_EH4)-base)<<std::dec<<std::endl;
    Check(personality==&__GSHandlerCheck_EH4,"compiled fixture is not CET's GS/FH4 personality");
    auto runtime=GetModuleHandleW(L"vcruntime140_1.dll");
    Check(runtime&&GetProcAddress(runtime,"__CxxFrameHandler4"),"FH4 runtime import unavailable");
    state.base=base;state.originalReturn=ret;state.original=*original;
    state.personality=personality;state.handlerData=const_cast<uint8_t*>(info+8+padded*2);

    auto* island=AllocateAfterImage(base);
    Check(island!=nullptr,"cannot allocate isolated island near fixture image");
    constexpr unsigned thunk=128,metadata=256;
    const Address addr=reinterpret_cast<Address>(island);
    // CALL original target from original stack frame; then conditionally
    // branch to the original return PC, preserving EAX. Unconditional
    // tail JMP would be misinterpreted as an epilogue by Windows.
    island[0]=0xe8;Dword(island+1,static_cast<uint32_t>(Rel32(addr+5,target)));
    island[5]=0x39;island[6]=0xc0;island[7]=0x0f;island[8]=0x84;
    Dword(island+9,static_cast<uint32_t>(Rel32(addr+13,ret)));
    island[13]=0x0f;island[14]=0x0b;
    const uint8_t absolute[]={0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xe0};
    std::memcpy(island+thunk,absolute,sizeof(absolute));
    auto bridge=reinterpret_cast<Address>(&Bridge);std::memcpy(island+thunk+2,&bridge,8);
    BuildUnwind(island+metadata,info,Rva(base,addr+thunk));
    state.islandReturn=addr+5;
    state.island={Rva(base,addr),Rva(base,addr+15),Rva(base,addr+metadata)};
    DWORD old=0;Check(VirtualProtect(island,4096,PAGE_EXECUTE_READ,&old)!=0,"island RX");
    Check(FlushInstructionCache(GetCurrentProcess(),island,4096)!=0,"island flush");
    Check(RtlAddFunctionTable(&state.island,1,base)!=FALSE,"register FH4 island");
    DWORD64 foundBase=0;
    auto* found=RtlLookupFunctionEntry(state.islandReturn,&foundBase,nullptr);
    Check(found&&foundBase==base&&found->BeginAddress==state.island.BeginAddress,
          "FH4 island runtime function lookup");
    std::array<uint8_t,5> stock{},patched{0xe9,0,0,0,0};
    std::memcpy(stock.data(),site,5);auto delta=Rel32(ret,addr);
    std::memcpy(patched.data()+1,&delta,4);
    WriteCallsite(site,patched);
    for(int mode=0;mode<3;++mode){
        Reset();state.search=state.unwind=0;
        Verify(mode);
        Check(observedReturn==state.islandReturn,"execution missed the relocated call");
        std::cout<<"GS/FH4 mode="<<mode<<" search="<<state.search<<" unwind="<<state.unwind<<"\n";
        if(mode)Check(state.search>0&&state.unwind>0,"FH4 search/unwind did not enter bridge");
        else Check(state.search==0&&state.unwind==0,"normal execution entered EH bridge");
    }
    WriteCallsite(site,stock);
    Check(std::memcmp(site,stock.data(),5)==0,"exact fixture restoration");
    Check(RtlDeleteFunctionTable(&state.island)!=FALSE,"unregister FH4 island");
    Check(RtlLookupFunctionEntry(state.islandReturn,&foundBase,nullptr)==nullptr,"stale FH4 metadata");
    Check(VirtualFree(island,0,MEM_RELEASE)!=FALSE,"release drained test island");
    for(int mode=0;mode<3;++mode){Reset();Verify(mode);Check(observedReturn==ret,"restored return PC");}
    std::cout<<"Actual compiler-generated GS/FH4 local catch, outer propagation, RAII destruction and exact restoration PASS\n";
}
