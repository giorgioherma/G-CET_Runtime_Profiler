#pragma once
#include <windows.h>
#include <cstdint>

namespace gcet::cet_dispatch {
// Install/restore only. The caller must hold a proven lifetime boundary
// for the original owner and must NOT already own its shared/exclusive lock.
// Resolve from the same loaded MSVCP140 runtime as the verified CET import.
// A busy lock fails closed; never block the game thread or add hot-path work.
struct ExistingMutexApi {
    using Try=int(__cdecl*)(void*) noexcept;
    using Unlock=void(__cdecl*)(void*) noexcept;
    Try tryExclusive=nullptr;
    Unlock unlockExclusive=nullptr;
    static ExistingMutexApi Resolve(HMODULE runtime) noexcept {
        if(!runtime)return {};
        return {
            reinterpret_cast<Try>(GetProcAddress(runtime,"_Smtx_try_lock_exclusive")),
            reinterpret_cast<Unlock>(GetProcAddress(runtime,"_Smtx_unlock_exclusive"))
        };
    }
    explicit operator bool()const noexcept{return tryExclusive&&unlockExclusive;}
};
class ExistingMutexLease {
    ExistingMutexApi api_{};
    void* handle_=nullptr;
public:
    ExistingMutexLease(ExistingMutexApi api,void* handle)noexcept:api_(api){
        if(api&&handle&&reinterpret_cast<uintptr_t>(handle)%alignof(void*)==0&&api.tryExclusive(handle))
            handle_=handle;
    }
    ExistingMutexLease(const ExistingMutexLease&)=delete;
    ExistingMutexLease& operator=(const ExistingMutexLease&)=delete;
    ~ExistingMutexLease(){if(handle_)api_.unlockExclusive(handle_);}
    explicit operator bool()const noexcept{return handle_!=nullptr;}
};
}
