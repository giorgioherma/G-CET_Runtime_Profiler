#pragma once
// Research only. This contract accepts a DISPOSABLE private image, never a
// loaded module. It does not install a game hook or execute copied CET code.
#include "CETDispatchIdentity.hpp"
#include "CETStacklessGate.hpp"
#include <windows.h>
#include <array>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <new>
#include <stdexcept>

namespace gcet::research {
inline void Require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
struct GuardSite {
    uint32_t guard,active,empty;
    bool r15;
    std::array<uint8_t,11> stock;
};
inline constexpr GuardSite Sites[]={
    {0x10834f,0x10835a,0x1087e9,true,{0x41,0x80,0x7f,0x70,0,0x0f,0x85,0x8f,0x04,0,0}},
    {0x108b41,0x108b4c,0x109200,false,{0x41,0x80,0x7e,0x70,0,0x0f,0x85,0xb4,0x06,0,0}}
};
using PatchBytes=std::array<uint8_t,11>;
inline PatchBytes Redirect(uintptr_t from,uintptr_t to){
    const auto next=from+5;
    const auto distance=to>=next?to-next:next-to;
    Require(distance<=uint64_t(to>=next?INT32_MAX:2147483648ull),"branch outside rel32 range");
    int64_t delta=to>=next?int64_t(distance):-int64_t(distance);
    PatchBytes result{0xe9,0,0,0,0,0x90,0x90,0x90,0x90,0x90,0x90};
    int32_t rel=static_cast<int32_t>(delta);
    std::memcpy(result.data()+1,&rel,4);
    return result;
}
inline uintptr_t Destination(uintptr_t source,const PatchBytes& bytes){
    int32_t rel=0;std::memcpy(&rel,bytes.data()+1,4);
    Require(bytes[0]==0xe9,"not rel32 jump");
    return static_cast<uintptr_t>(static_cast<int64_t>(source+5)+rel);
}
inline bool Equal(const uint8_t* p,const PatchBytes& bytes){
    return std::memcmp(p,bytes.data(),bytes.size())==0;
}
// On a quiescent private image only. Do not generalize this into a live
// patch API: installation, error recovery and thread ownership are separate.
class PrivateGuards {
public:
    PrivateGuards(uint8_t* image,std::array<uintptr_t,2> islands):image_(image){
        MEMORY_BASIC_INFORMATION mbi{};
        Require(VirtualQuery(image,&mbi,sizeof(mbi))==sizeof(mbi)&&
                mbi.AllocationBase==image&&mbi.Type==MEM_PRIVATE,"test requires private allocation");
        for(size_t i=0;i<2;++i){
            Require(Equal(image+Sites[i].guard,Sites[i].stock),"guard bytes differ");
            patches_[i]=Redirect(reinterpret_cast<uintptr_t>(image+Sites[i].guard),islands[i]);
        }
    }
    bool Apply(int failBefore=-1){
        // Preflight the whole pair before touching the first guard.
        for(size_t i=0;i<2;++i)if(!Equal(image_+Sites[i].guard,Sites[i].stock))return false;
        for(size_t i=0;i<2;++i){
            if(static_cast<int>(i)==failBefore||!Write(i,patches_[i])){
                Restore();return false;
            }
        }
        return true;
    }
    void Restore(){
        for(size_t i=0;i<2;++i){
            auto* p=image_+Sites[i].guard;
            if(Equal(p,Sites[i].stock))continue;
            Require(Equal(p,patches_[i]),"unexpected code drift during private restore");
            Require(Write(i,Sites[i].stock),"private guard restoration failed");
        }
    }
    const PatchBytes& Patch(size_t i)const{return patches_[i];}
private:
    bool Write(size_t i,const PatchBytes& bytes){
        auto* p=image_+Sites[i].guard;
        DWORD old=0,discard=0;
        if(!VirtualProtect(p,bytes.size(),PAGE_READWRITE,&old))return false;
        std::memcpy(p,bytes.data(),bytes.size());
        Require(VirtualProtect(p,bytes.size(),old,&discard)!=0,"restore original page protection");
        Require(FlushInstructionCache(GetCurrentProcess(),p,bytes.size())!=0,"flush private guard");
        return Equal(p,bytes);
    }
    uint8_t* image_;
    std::array<PatchBytes,2> patches_{};
};
struct GuardData {
    cet_dispatch::DispatchEpoch epoch;
    cet_dispatch::ObserverBinding binding{epoch,GetCurrentThreadId()};
};

inline void CheckGuardRedirect(const std::filesystem::path& path,uint8_t* image,size_t imageSize){
    using namespace cet_dispatch;
    Require(CheckImage(path,image)==Identity::ExactImage,"private guard baseline");
    auto* code=image+imageSize;
    auto* data=new(code+4096) GuardData;
    std::array<uintptr_t,2> islands{};
    for(size_t i=0;i<2;++i){
        auto site=Sites[i];
        islands[i]=reinterpret_cast<uintptr_t>(code+i*1024);
        StacklessGate gate(islands[i],site.r15,data->binding.Guard(),data->epoch,
            reinterpret_cast<uintptr_t>(image+site.active),reinterpret_cast<uintptr_t>(image+site.empty));
        Require(gate.Code().size()<1024,"island capacity");
        std::memcpy(code+i*1024,gate.Code().data(),gate.Code().size());
    }
    DWORD old=0;
    Require(VirtualProtect(code,4096,PAGE_EXECUTE_READ,&old)!=0,"private island W^X");
    Require(FlushInstructionCache(GetCurrentProcess(),code,4096)!=0,"flush private islands");
    // The address-range contract must refuse rather than truncate.
    bool refused=false;
    try{(void)Redirect(0x10000,0x80010006ull);}catch(const std::runtime_error&){refused=true;}
    Require(refused,"out-of-range target admitted");
    PrivateGuards transaction(image,islands);
    Require(!transaction.Apply(1),"injected second guard failure accepted");
    Require(CheckImage(path,image)==Identity::ExactImage,"partial application not rolled back");
    for(int iteration=0;iteration<3;++iteration){
        Require(transaction.Apply(),"apply both private guards");
        for(size_t i=0;i<2;++i){
            const auto at=reinterpret_cast<uintptr_t>(image+Sites[i].guard);
            Require(Equal(image+Sites[i].guard,transaction.Patch(i)),"redirect bytes");
            Require(Destination(at,transaction.Patch(i))==islands[i],"redirect destination");
            MEMORY_BASIC_INFORMATION mbi{};
            Require(VirtualQuery(image+Sites[i].guard,&mbi,sizeof(mbi))==sizeof(mbi)&&
                    mbi.Protect==PAGE_EXECUTE_READ,"guard protection not restored");
        }
        Require(CheckImage(path,image)==Identity::LoadedCodeMismatch,"patched image not rejected");
        Require(!transaction.Apply(),"duplicate apply accepted");
        transaction.Restore();transaction.Restore();
        Require(CheckImage(path,image)==Identity::ExactImage,"complete original identity not restored");
    }
    // Verify a foreign alteration in the SECOND guard refuses before touching
    // the first. Only the isolated scratch copy is modified here.
    auto* second=image+Sites[1].guard;
    Require(VirtualProtect(second,11,PAGE_READWRITE,&old)!=0,"scratch writable");
    second[0]^=1;
    DWORD unused=0;
    Require(VirtualProtect(second,11,old,&unused)!=0,"scratch protection");
    Require(!transaction.Apply()&&Equal(image+Sites[0].guard,Sites[0].stock),"partial install on foreign code");
    Require(VirtualProtect(second,11,PAGE_READWRITE,&old)!=0,"scratch restore writable");
    second[0]^=1;
    Require(VirtualProtect(second,11,old,&unused)!=0,"scratch restore protection");
    Require(CheckImage(path,image)==Identity::ExactImage,"foreign test baseline restore");
    data->~GuardData();
    std::cout<<"Both actual CET guard redirections, partial rollback, foreign-code refusal, W^X and exact restoration PASS\n";
}
}
