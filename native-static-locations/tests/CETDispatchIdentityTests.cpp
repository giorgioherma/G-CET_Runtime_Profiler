// Actual release mapped without loading CET or executing DllMain.
#define NOMINMAX
#include "CETDispatchIdentity.hpp"
#include "CETLiveObserver.hpp"
#include <windows.h>
#include <filesystem>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>
using gcet::cet_dispatch::Identity;
using gcet::cet_dispatch::CheckImage;
using gcet::cet_dispatch::CheckLoadedCET;
static void Check(bool value,const char* reason){if(!value)throw std::runtime_error(reason);}
struct File {
    HANDLE value=INVALID_HANDLE_VALUE;
    ~File(){if(value!=INVALID_HANDLE_VALUE)CloseHandle(value);}
};
struct Mapping {
    HANDLE value=nullptr;
    ~Mapping(){if(value)CloseHandle(value);}
};
struct View {
    void* value=nullptr;
    ~View(){if(value)UnmapViewOfFile(value);}
};
static void CheckOwnerContract(){
    using namespace gcet::cet_dispatch;
    struct Allocation {
        void* p=nullptr;
        ~Allocation(){if(p)VirtualFree(p,0,MEM_RELEASE);}
    } image{VirtualAlloc(nullptr,0x6de000,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE)},
      owner{VirtualAlloc(nullptr,OwnerLayout::CETSize,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE)};
    Check(image.p&&owner.p,"owner snapshot fixture allocation");
    auto* bytes=static_cast<uint8_t*>(image.p);
    auto* data=static_cast<uint8_t*>(owner.p);
    const auto root=reinterpret_cast<uintptr_t>(data);
    auto word=[](uint8_t* at,uintptr_t v){std::memcpy(at,&v,sizeof(v));};
    Check(InspectOwnerSnapshot(nullptr)==OwnerStatus::ReadFailure,"null owner image");
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::NotInitialized,"empty owner slots");
    word(bytes+OwnerLayout::RootSlot,root);
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::NotInitialized,"partially published owner");
    word(bytes+OwnerLayout::OverrideSlot,root+OwnerLayout::OverrideOffset);
    word(data+OwnerLayout::OverrideOffset+OwnerLayout::ScriptingBackPointer,root+OwnerLayout::ScriptingOffset);
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::NotInitialized,"startup incomplete");
    data[OwnerLayout::StartupMarker]=1;
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::ReadySnapshot,"coherent compiled layout");
    data[OwnerLayout::StartupMarker]=2;
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::Inconsistent,"invalid startup bool");
    data[OwnerLayout::StartupMarker]=1;
    word(bytes+OwnerLayout::OverrideSlot,root+OwnerLayout::OverrideOffset+8);
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::Inconsistent,"foreign override owner");
    word(bytes+OwnerLayout::OverrideSlot,root+OwnerLayout::OverrideOffset);
    word(data+OwnerLayout::OverrideOffset+OwnerLayout::ScriptingBackPointer,root);
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::Inconsistent,"foreign scripting back pointer");
    word(data+OwnerLayout::OverrideOffset+OwnerLayout::ScriptingBackPointer,root+OwnerLayout::ScriptingOffset);
    DWORD old=0,unused=0;
    Check(VirtualProtect(data,OwnerLayout::CETSize,PAGE_NOACCESS,&old)!=FALSE,"inaccessible owner fixture");
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::ReadFailure,"inaccessible owner accepted");
    Check(VirtualProtect(data,OwnerLayout::CETSize,old,&unused)!=FALSE,"restore owner fixture");
    Check(InspectOwnerSnapshot(bytes)==OwnerStatus::ReadySnapshot,"restored owner rejected");
    std::cout<<"Owner snapshot absent/partial/startup/inconsistent/unreadable/coherent PASS\n";
}
int wmain(int argc,wchar_t** argv){
    try {
        Check(CheckLoadedCET()==Identity::Unavailable,"probe must not load missing CET");
        Check(!gcet::cet_dispatch::PrepareObserver(0x1000,0x1234)&&
              gcet::cet_dispatch::BindObserver()==0&&
              !gcet::cet_dispatch::SetObserverDemand(1,false)&&
              !gcet::cet_dispatch::ReleaseObserver(1),
              "absent CET must not publish native attachment");
        Check(gcet::cet_dispatch::CheckLoadedOwner()==gcet::cet_dispatch::OwnerStatus::AbsentModule,"owner probe must not load CET");
        CheckOwnerContract();
        if(argc==1){std::cout<<"Absent-module probe PASS\n";return 0;}
        Check(argc==2,"expected exact official ASI path");
        std::filesystem::path path(argv[1]);
        File file{CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr)};
        Check(file.value!=INVALID_HANDLE_VALUE,"open release ASI");
        Mapping mapping{CreateFileMappingW(file.value,nullptr,PAGE_READONLY|SEC_IMAGE_NO_EXECUTE,0,0,nullptr)};
        Check(mapping.value!=nullptr,"create non-executable PE mapping");
        View view{MapViewOfFile(mapping.value,FILE_MAP_READ,0,0,0)};
        Check(view.value!=nullptr,"map release ASI");
        gcet::cet_dispatch::ProbeDetail detail;
        auto official=CheckImage(path,view.value,&detail);
        if(official!=Identity::ExactImage){
            std::cerr<<"Identity "<<static_cast<int>(official)<<" stage="<<detail.stage
                <<" rva=0x"<<std::hex<<detail.rva<<" offset=0x"<<detail.offset
                <<" expected=0x"<<unsigned(detail.expected)<<" actual=0x"<<unsigned(detail.actual)
                <<" error="<<std::dec<<detail.error<<"\n";
        }
        std::cout<<"PE ImageBase field offset=0x"<<std::hex<<detail.imageBaseOffset
            <<" disk=0x"<<detail.diskImageBase<<" mapped=0x"<<detail.mappedImageBase
            <<" view=0x"<<reinterpret_cast<uintptr_t>(view.value)<<std::dec<<"\n";
        Check(official==Identity::ExactImage,"official mapped CET identity rejected");
        Check(gcet::cet_dispatch::InspectOwnerSnapshot(view.value)==gcet::cet_dispatch::OwnerStatus::NotInitialized,
              "uninitialized official image falsely ready");
        Check(CheckImage(path,nullptr)==Identity::LoadedCodeMismatch,"invalid loaded image accepted");
        // Independent scratch copy of the image header and all inspected code.
        // Deliberately do not write to the release mapping or a loaded module.
        std::vector<unsigned char> scratch(7536640);
        auto* image=static_cast<const unsigned char*>(view.value);
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(image+dos->e_lfanew);
        std::memcpy(scratch.data(),image,nt->OptionalHeader.SizeOfHeaders);
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){
            const auto& s=sections[i];
            if(s.SizeOfRawData)
                std::memcpy(scratch.data()+s.VirtualAddress,image+s.VirtualAddress,s.SizeOfRawData);
        }
        Check(CheckImage(path,scratch.data())==Identity::ExactImage,"scratch identity baseline");
        scratch[0]^=1;
        Check(CheckImage(path,scratch.data())==Identity::LoadedCodeMismatch,"unrelated header change accepted");
        scratch[0]^=1;
        scratch[detail.imageBaseOffset]^=1;
        Check(CheckImage(path,scratch.data())==Identity::LoadedCodeMismatch,"invalid unaligned ImageBase accepted");
        scratch[detail.imageBaseOffset]^=1;
        for(size_t guard:{size_t{0x10834f},size_t{0x108b41}}){
            scratch[guard]^=1;
            Check(CheckImage(path,scratch.data())==Identity::LoadedCodeMismatch,"changed dispatcher accepted");
            scratch[guard]^=1;
        }
        auto foreign=path.parent_path()/L"gcet-dispatch-test-foreign.bin";
        {std::ofstream out(foreign,std::ios::binary);out<<"not official CET";}
        auto result=CheckImage(foreign,view.value);
        std::filesystem::remove(foreign);
        Check(result==Identity::UnsupportedFile,"foreign binary accepted");
        std::cout<<"Official CET mapped identity, both changed guards, foreign file and absent module PASS\n";
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
