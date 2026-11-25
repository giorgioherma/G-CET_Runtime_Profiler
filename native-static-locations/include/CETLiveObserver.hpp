#pragma once
#include <cstdint>
namespace gcet::cet_dispatch {
// Source-owned, automatically used by a compatible generated shared host.
// No configuration switch and no per-frame native scheduler.
bool PrepareObserver(uintptr_t parent,uint64_t fullName) noexcept;
int32_t BindObserver() noexcept;
bool SetObserverDemand(int32_t handle,bool active) noexcept;
bool ReleaseObserver(int32_t handle) noexcept;
void RetireObserver() noexcept;
}
