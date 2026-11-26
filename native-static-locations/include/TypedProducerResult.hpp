#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include <utility>

namespace gcet::producer {
// Fixed-size, allocation-free storage for the admitted scalar/vector RTTI
// representations only. Match CET's owned return lifetime, rather than
// treating a C++ default constructor as the engine's RTTI constructor.
template<size_t Size, size_t Alignment=16,class Type>
bool FitsTypedStorage(const Type* type) {
    if(!type||type->GetSize()!=Size)return false;
    const auto a=type->GetAlignment();
    return a&&a<=Alignment&&(a&(a-1))==0;
}

template<size_t Size,class Type,class Invoke,class Copy>
bool ReadTypedResult(Type* type,Invoke&& invoke,Copy&& copy) {
    if(!FitsTypedStorage<Size>(type))return false;
    alignas(16) std::array<std::byte,Size> storage;
    type->Construct(storage.data());
    struct Cleanup {
        Type* type;
        void* data;
        ~Cleanup(){type->Destruct(data);}
    } cleanup{type,storage.data()};
    if(!std::forward<Invoke>(invoke)(storage.data()))return false;
    return std::forward<Copy>(copy)(storage.data());
}

// Validate direct typed out-parameter storage before any producer runs.
// The CET opcode supplies the actual live RTTI type and data pointer.
enum class OutStorageStatus { Valid,Null,TypeMissing,TypeMismatch,SizeMismatch,AlignmentInvalid,AddressMisaligned,AddressOverflow };
template<class Type>
OutStorageStatus InspectOutStorage(const void* address,const Type* actual,const Type* expected,size_t size) {
    if(!address)return OutStorageStatus::Null;
    if(!actual)return OutStorageStatus::TypeMissing;
    if(actual!=expected)return OutStorageStatus::TypeMismatch;
    if(actual->GetSize()!=size)return OutStorageStatus::SizeMismatch;
    const auto a=actual->GetAlignment();
    const auto p=reinterpret_cast<uintptr_t>(address);
    if(!a||a>16||(a&(a-1))!=0)return OutStorageStatus::AlignmentInvalid;
    if((p&(a-1))!=0)return OutStorageStatus::AddressMisaligned;
    if(p>UINTPTR_MAX-size)return OutStorageStatus::AddressOverflow;
    return OutStorageStatus::Valid;
}
template<class Type>
bool ValidOutStorage(const void* address,const Type* actual,const Type* expected,size_t size) {
    return InspectOutStorage(address,actual,expected,size)==OutStorageStatus::Valid;
}
inline bool DisjointOut(const void* a,size_t as,const void* b,size_t bs) {
    const auto x=reinterpret_cast<uintptr_t>(a),y=reinterpret_cast<uintptr_t>(b);
    if(!x||!y||x>UINTPTR_MAX-as||y>UINTPTR_MAX-bs)return false;
    return x+as<=y||y+bs<=x;
}
}
