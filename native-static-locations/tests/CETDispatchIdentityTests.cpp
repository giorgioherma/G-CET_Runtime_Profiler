// Actual release mapped without loading CET or executing DllMain.
#define NOMINMAX
#include "CETDispatchIdentity.hpp"
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
int wmain(int argc,wchar_t** argv){
    try {
        Check(CheckLoadedCET()==Identity::Unavailable,"probe must not load missing CET");
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
            if(s.Characteristics&IMAGE_SCN_MEM_EXECUTE)
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
