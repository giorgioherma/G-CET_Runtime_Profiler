#pragma once
#include <windows.h>
#include <cstdint>

namespace gcet::cet_dispatch {
// Install/restore only. Official CET's linked _Smtx_* thunks jump directly
// to Kernel32 SRW APIs. Caller must prove original owner lifetime and that
// it does not already own the lock. No waiting or hot-path work here.
class ExistingMutexLease {
    PSRWLOCK handle_=nullptr;
public:
    explicit ExistingMutexLease(void* handle) noexcept {
        static_assert(sizeof(SRWLOCK)==sizeof(void*));
        if(handle&&reinterpret_cast<uintptr_t>(handle)%alignof(SRWLOCK)==0&&
           TryAcquireSRWLockExclusive(static_cast<PSRWLOCK>(handle)))
            handle_=static_cast<PSRWLOCK>(handle);
    }
    ExistingMutexLease(const ExistingMutexLease&)=delete;
    ExistingMutexLease& operator=(const ExistingMutexLease&)=delete;
    ~ExistingMutexLease(){if(handle_)ReleaseSRWLockExclusive(handle_);}
    explicit operator bool()const noexcept{return handle_!=nullptr;}
    bool Owns(void* handle)const noexcept{return handle_&&handle_==handle;}
};
}
