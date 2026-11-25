#define NOMINMAX
#include "CETDispatchIdentity.hpp"
#include <windows.h>
#include <bcrypt.h>
#include <array>
#include <cstring>
#include <fstream>
#include <vector>
#include <stdexcept>
#include <iterator>

namespace gcet::cet_dispatch {
namespace {
// Exact upstream release, not the separately compiled profiler ASI.
// Native target identity is intentionally distinct from Lua source eligibility.
constexpr char kSha[] = "43a7b94698979703f9dfa2b0950607c9a88f53b84bbf09a8e593c7e218d41059";
constexpr uint32_t kImageSize = 7536640;
struct Range { uint32_t begin, end; };
constexpr Range kRanges[] = {
    {0x1080b0, 0x108882}, // complete pure-script dispatch body
    {0x108890, 0x1092c8}, // complete native-stack dispatch body
    {0x1092d0, 0x1092e0}, // ExecuteChain entry
    {0x10adf0, 0x10ae00}, // Override entry
    {0x107ee0, 0x107ef0}, // Clear entry
    {0x15f590, 0x15f5a0}  // ToLua entry
};
struct ModuleRef {
    HMODULE value = nullptr;
    ~ModuleRef(){ if(value) FreeLibrary(value); }
};
struct Algorithm {
    BCRYPT_ALG_HANDLE value = nullptr;
    ~Algorithm(){ if(value) BCryptCloseAlgorithmProvider(value,0); }
};
bool HashMatches(std::vector<unsigned char>& bytes) {
    Algorithm alg;
    if(BCryptOpenAlgorithmProvider(&alg.value,BCRYPT_SHA256_ALGORITHM,nullptr,0)!=0)
        throw std::runtime_error("SHA256 provider unavailable");
    std::array<unsigned char,32> digest{};
    if(BCryptHash(alg.value,nullptr,0,bytes.data(),
        static_cast<ULONG>(bytes.size()),digest.data(),static_cast<ULONG>(digest.size()))!=0)
        throw std::runtime_error("SHA256 failed");
    constexpr char digits[]="0123456789abcdef";
    for(size_t i=0;i<digest.size();++i)
        if(kSha[i*2]!=digits[digest[i]>>4]||kSha[i*2+1]!=digits[digest[i]&15])return false;
    return true;
}
template<class T> bool ReadAt(const std::vector<unsigned char>& bytes,size_t offset,T& value) {
    if(offset>bytes.size()||sizeof(T)>bytes.size()-offset)return false;
    std::memcpy(&value,bytes.data()+offset,sizeof(T));return true;
}
bool EqualMapped(const void* mapped,uint32_t rva,const unsigned char* expected,size_t count,
                 ProbeDetail* detail) {
    if(detail)detail->rva=rva;
    auto address=reinterpret_cast<uintptr_t>(mapped);
    if(!address||rva>kImageSize||count>kImageSize-rva||address>UINTPTR_MAX-rva)return false;
    std::vector<unsigned char> live(count);
    SIZE_T read=0;
    if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<const void*>(address+rva),
        live.data(),count,&read)||read!=count){
        if(detail)detail->error=GetLastError();
        return false;
    }
    for(size_t i=0;i<count;++i){
        if(live[i]==expected[i])continue;
        if(detail){detail->offset=static_cast<uint32_t>(i);detail->expected=expected[i];detail->actual=live[i];}
        return false;
    }
    return true;
}
}

Identity CheckImage(const std::filesystem::path& file,const void* mappedImage,ProbeDetail* detail) noexcept {
    if(detail)*detail={};
    auto stage=[detail](const char* name){if(detail)detail->stage=name;};
    try {
        stage("open");
        std::ifstream in(file,std::ios::binary|std::ios::ate);
        if(!in)return Identity::ReadFailure;
        stage("size");
        auto length=in.tellg();
        if(length<=0||length>32*1024*1024)return Identity::UnsupportedFile;
        std::vector<unsigned char> bytes(static_cast<size_t>(length));
        stage("read");
        in.seekg(0);
        if(!in.read(reinterpret_cast<char*>(bytes.data()),static_cast<std::streamsize>(bytes.size())))
            return Identity::ReadFailure;
        stage("sha256");
        if(!HashMatches(bytes))return Identity::UnsupportedFile;
        stage("PE header");
        IMAGE_DOS_HEADER dos{};
        IMAGE_NT_HEADERS64 nt{};
        if(!ReadAt(bytes,0,dos)||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<0||
            !ReadAt(bytes,static_cast<size_t>(dos.e_lfanew),nt)||
            nt.Signature!=IMAGE_NT_SIGNATURE||nt.FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||
            nt.OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||
            nt.OptionalHeader.SizeOfImage!=kImageSize)
            return Identity::UnsupportedFile;
        stage("mapped headers");
        auto headers=nt.OptionalHeader.SizeOfHeaders;
        if(!headers||headers>bytes.size()||!EqualMapped(mappedImage,0,bytes.data(),headers,detail))
            return Identity::LoadedCodeMismatch;
        size_t sectionStart=static_cast<size_t>(dos.e_lfanew)+sizeof(DWORD)+
            sizeof(IMAGE_FILE_HEADER)+nt.FileHeader.SizeOfOptionalHeader;
        stage("mapped dispatch code");
        for(auto range:kRanges){
            bool found=false;
            for(uint16_t i=0;i<nt.FileHeader.NumberOfSections;++i){
                IMAGE_SECTION_HEADER section{};
                if(!ReadAt(bytes,sectionStart+i*sizeof(section),section))
                    return Identity::UnsupportedFile;
                if(!(section.Characteristics&IMAGE_SCN_MEM_EXECUTE)||range.begin<section.VirtualAddress)
                    continue;
                uint32_t offset=range.begin-section.VirtualAddress;
                size_t count=range.end-range.begin;
                if(offset>section.SizeOfRawData||count>section.SizeOfRawData-offset)continue;
                size_t raw=static_cast<size_t>(section.PointerToRawData)+offset;
                if(raw>bytes.size()||count>bytes.size()-raw)return Identity::UnsupportedFile;
                if(!EqualMapped(mappedImage,range.begin,bytes.data()+raw,count,detail))
                    return Identity::LoadedCodeMismatch;
                found=true;break;
            }
            if(!found)return Identity::UnsupportedFile;
        }
        stage("verified");
        return Identity::ExactImage;
    }catch(...){return Identity::ReadFailure;}
}

Identity CheckLoadedCET() noexcept {
    try {
        // Hold a reference only for the duration of the read. This never loads
        // an absent module or executes its initializer. No cached live pointer.
        ModuleRef module;
        if(!GetModuleHandleExW(0,L"cyber_engine_tweaks.asi",&module.value))
            return Identity::Unavailable;
        wchar_t path[32768];
        DWORD count=GetModuleFileNameW(module.value,path,static_cast<DWORD>(std::size(path)));
        if(!count||count>=std::size(path))return Identity::ReadFailure;
        return CheckImage(std::filesystem::path(std::wstring(path,count)),module.value);
    }catch(...){return Identity::ReadFailure;}
}
}
