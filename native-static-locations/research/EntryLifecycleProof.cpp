// Developer-only executable: CET's actual code is never executed.
// All code modifications affect a disposable private copy or our own test
// function. No game, CET loader, live process or installed files are touched.
#define NOMINMAX
#include <windows.h>
#include <detours.h>
#include "CETDispatchIdentity.hpp"
#include "GuardRedirectProof.hpp"
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace {
using gcet::cet_dispatch::CheckImage;
using gcet::cet_dispatch::Identity;
void Check(bool ok,const char* why){if(!ok)throw std::runtime_error(why);}
void DetourResult(LONG status,const char* what){
    if(status!=NO_ERROR)throw std::runtime_error(std::string(what)+": "+std::to_string(status));
}
struct File {
    HANDLE h=INVALID_HANDLE_VALUE;
    ~File(){if(h!=INVALID_HANDLE_VALUE)CloseHandle(h);}
};
struct Mapping {
    HANDLE h=nullptr;
    ~Mapping(){if(h)CloseHandle(h);}
};
struct View {
    void* p=nullptr;
    ~View(){if(p)UnmapViewOfFile(p);}
};
struct Image {
    void* p=nullptr;
    ~Image(){if(p)VirtualFree(p,0,MEM_RELEASE);}
};
// A single-threaded research process. Production attach/detach must separately
// prove publication and in-flight quiescence for all actual game threads.
struct Transaction {
    bool active=false;
    Transaction(){
        DetourResult(DetourTransactionBegin(),"begin");
        active=true;
        if(auto result=DetourUpdateThread(GetCurrentThread());result!=NO_ERROR){
            DetourTransactionAbort();active=false;DetourResult(result,"update thread");
        }
    }
    ~Transaction(){if(active)DetourTransactionAbort();}
    void Commit(){auto result=DetourTransactionCommit();active=false;DetourResult(result,"commit");}
    void Abort(){auto result=DetourTransactionAbort();active=false;DetourResult(result,"abort");}
};

using ExecuteFn=bool (*)(const void*,void*,void*,void*,void*,void*,void*,void*,char*,uint8_t);
using ClearFn=void (*)(void*);
ExecuteFn executeOriginal=nullptr;
ClearFn clearOriginal=nullptr;
// These detours are never invoked for the copied CET image.
bool ExecutePassthrough(const void* chain,void* lock,void* context,void* args,void* result,
                        void* out,void* stack,void* frame,char* code,uint8_t parameter){
    return executeOriginal(chain,lock,context,args,result,out,stack,frame,code,parameter);
}
void ClearPassthrough(void* self){clearOriginal(self);}

template<class Fn>
void* Address(Fn fn){return reinterpret_cast<void*>(fn);}
template<class Fn>
void Attach(Fn& original,Fn replacement){
    DetourResult(DetourAttach(reinterpret_cast<PVOID*>(&original),Address(replacement)),"attach");
}
template<class Fn>
void Detach(Fn& original,Fn replacement){
    DetourResult(DetourDetach(reinterpret_cast<PVOID*>(&original),Address(replacement)),"detach");
}

void CheckOfficialEntries(const std::filesystem::path& path){
    File file{CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr)};
    Check(file.h!=INVALID_HANDLE_VALUE,"open official ASI");
    Mapping mapping{CreateFileMappingW(file.h,nullptr,PAGE_READONLY|SEC_IMAGE_NO_EXECUTE,0,0,nullptr)};
    Check(mapping.h!=nullptr,"map official ASI");
    View view{MapViewOfFile(mapping.h,FILE_MAP_READ,0,0,0)};
    Check(view.p!=nullptr,"read official ASI");
    Check(CheckImage(path,view.p)==Identity::ExactImage,"official ASI identity");
    const auto* bytes=static_cast<const unsigned char*>(view.p);
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(bytes);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(bytes+dos->e_lfanew);
    const size_t size=nt->OptionalHeader.SizeOfImage;
    // Extra private pages adjacent to the copied image: never a live CET allocation.
    Image copy{VirtualAlloc(nullptr,size+8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE)};
    Check(copy.p!=nullptr,"allocate private test image");
    auto* image=static_cast<unsigned char*>(copy.p);
    // Preserve the full image, including .pdata/.xdata for read-only unwind proof.
    for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){
        const auto& section=IMAGE_FIRST_SECTION(nt)[i];
        Check(section.VirtualAddress<=size&&section.SizeOfRawData<=size-section.VirtualAddress,
              "section exceeds private image");
        if(section.SizeOfRawData)std::memcpy(image+section.VirtualAddress,bytes+section.VirtualAddress,section.SizeOfRawData);
    }
    std::memcpy(image,bytes,nt->OptionalHeader.SizeOfHeaders);
    const auto* sections=IMAGE_FIRST_SECTION(nt);
    for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){
        const auto& section=sections[i];
        if(!(section.Characteristics&IMAGE_SCN_MEM_EXECUTE))continue;
        Check(section.VirtualAddress<=size&&section.SizeOfRawData<=size-section.VirtualAddress,
              "code section exceeds test image");
        std::memcpy(image+section.VirtualAddress,bytes+section.VirtualAddress,section.SizeOfRawData);
        DWORD old=0;
        Check(VirtualProtect(image+section.VirtualAddress,section.SizeOfRawData,PAGE_EXECUTE_READ,&old)!=0,
              "protect private test code");
    }
    Check(CheckImage(path,image)==Identity::ExactImage,"private image baseline");
    const auto execute=reinterpret_cast<ExecuteFn>(image+0x1092d0);
    const auto clear=reinterpret_cast<ClearFn>(image+0x107ee0);
    executeOriginal=execute;clearOriginal=clear;
    std::array<unsigned char,64> executeBytes{},clearBytes{};
    std::memcpy(executeBytes.data(),Address(execute),executeBytes.size());
    std::memcpy(clearBytes.data(),Address(clear),clearBytes.size());
    auto checkRestored=[&]{
        Check(executeOriginal==execute&&clearOriginal==clear,"original target pointer not restored");
        Check(std::memcmp(executeBytes.data(),Address(execute),executeBytes.size())==0&&
              std::memcmp(clearBytes.data(),Address(clear),clearBytes.size())==0,"entry bytes not restored");
        Check(CheckImage(path,image)==Identity::ExactImage,"restored image identity");
    };
    {
        Transaction tx;
        Attach(executeOriginal,&ExecutePassthrough);
        Attach(clearOriginal,&ClearPassthrough);
        tx.Abort();
    }
    checkRestored();
    for(int iteration=0;iteration<3;++iteration){
        {
            Transaction tx;
            Attach(executeOriginal,&ExecutePassthrough);
            Attach(clearOriginal,&ClearPassthrough);
            tx.Commit();
        }
        Check(executeOriginal&&executeOriginal!=execute&&clearOriginal&&clearOriginal!=clear,
              "committed trampoline pointers not published");
        Check(std::memcmp(executeBytes.data(),Address(execute),executeBytes.size())!=0&&
              std::memcmp(clearBytes.data(),Address(clear),clearBytes.size())!=0,"expected entry detours absent");
        Check(CheckImage(path,image)==Identity::LoadedCodeMismatch,"changed image falsely accepted");
        // Detours trampoline unwind is a separate requirement. Report only;
        // never execute these CET trampolines in the test process.
        DWORD64 base=0;
        std::cout<<"ExecuteChain trampoline runtime-function entry: "
                 <<(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(executeOriginal),&base,nullptr)!=nullptr)
                 <<"\n";
        {
            Transaction tx;
            Detach(clearOriginal,&ClearPassthrough);
            Detach(executeOriginal,&ExecutePassthrough);
            tx.Commit();
        }
        checkRestored();
    }
    gcet::research::CheckGuardRedirect(path,image,size);
    executeOriginal=nullptr;clearOriginal=nullptr;
    std::cout<<"Official CET ExecuteChain/Clear entry transaction abort, attach and exact restoration PASS\n";
}

volatile unsigned fixtureCalls=0;
const void* expected[9]{};
bool __declspec(noinline) Fixture(const void* a,void* b,void* c,void* d,void* e,
        void* f,void* g,void* h,char* i,uint8_t j){
    fixtureCalls=fixtureCalls+1;
    return a==expected[0]&&b==expected[1]&&c==expected[2]&&d==expected[3]&&e==expected[4]&&
        f==expected[5]&&g==expected[6]&&h==expected[7]&&i==expected[8]&&j==197;
}
ExecuteFn fixtureOriginal=&Fixture;
unsigned fixtureDetours=0;
bool FixtureDetour(const void* a,void* b,void* c,void* d,void* e,
        void* f,void* g,void* h,char* i,uint8_t j){
    ++fixtureDetours;
    return fixtureOriginal(a,b,c,d,e,f,g,h,i,j);
}
void CheckCallableABI(){
    char objects[9]{};
    for(size_t i=0;i<9;++i)expected[i]=objects+i;
    // Volatile function pointer prevents the compiler from folding the direct call.
    ExecuteFn volatile call=&Fixture;
    auto invoke=[&](uint8_t p){return call(objects,objects+1,objects+2,objects+3,
        objects+4,objects+5,objects+6,objects+7,objects+8,p);};
    Check(invoke(197)&&!invoke(196),"stock callable fixture");
    {
        Transaction tx;Attach(fixtureOriginal,&FixtureDetour);tx.Commit();
    }
    Check(invoke(197)&&!invoke(196),"ten-argument/return ABI through trampoline");
    Check(fixtureDetours==2&&fixtureCalls==4,"call count through trampoline");
    {
        Transaction tx;Detach(fixtureOriginal,&FixtureDetour);tx.Commit();
    }
    Check(fixtureOriginal==&Fixture&&invoke(197)&&fixtureDetours==2&&fixtureCalls==5,
          "callable restore");
    std::cout<<"Callable ExecuteChain-shape argument and return ABI PASS\n";
}
}

int wmain(int argc,wchar_t** argv){
    try {
        Check(argc==2,"official CET ASI argument required");
        CheckCallableABI();
        CheckOfficialEntries(std::filesystem::path(argv[1]));
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
