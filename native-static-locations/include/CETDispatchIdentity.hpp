#pragma once
// Read-only identity gate for official CET 1.37.1. Verified image/PDB in
// run 45589763013. A successful probe is NOT permission to install a hook.
#include <cstdint>
#include <filesystem>

namespace gcet::cet_dispatch {
enum class Identity : int32_t {
    Unavailable = 0,
    ExactImage = 1,
    UnsupportedFile = -1,
    LoadedCodeMismatch = -2,
    ReadFailure = -3
};

// Exact official CET 1.37.1 owner data, independently derived from the
// matched PDB. A coherent snapshot is NOT an owner-lifetime/attachment lease.
struct OwnerLayout {
    static constexpr uint32_t RootSlot = 0x6dd8f0;
    static constexpr uint32_t OverrideSlot = 0x6dd930;
    static constexpr uint32_t CETSize = 13272;
    static constexpr uint32_t ScriptingOffset = 11232;
    static constexpr uint32_t OverrideOffset = 11928;
    static constexpr uint32_t MutexOffset = 12184;
    static constexpr uint32_t ScriptingBackPointer = 248;
    static constexpr uint32_t StartupMarker = 12216;
};
enum class OwnerStatus : int32_t {
    AbsentModule = 0, ReadySnapshot = 1, NotInitialized = 2,
    UnsupportedImage = -1, ReadFailure = -2, Inconsistent = -3
};
// A read-only snapshot under a temporary module reference, never acquire
// the dispatcher lock or save a live pointer across the call.
OwnerStatus CheckLoadedOwner() noexcept;
// Inspect exact-layout owner storage. The caller must separately prove
// image identity; this helper alone neither admits a module nor grants a lock.
OwnerStatus InspectOwnerSnapshot(const void* verifiedImage) noexcept;

// Explicit developer probe. No module load, no writes, no recurring work.
Identity CheckLoadedCET() noexcept;
// Same read-only gate used for official image mapping tests.
struct ProbeDetail {
    const char* stage = "not started";
    uint32_t rva = 0;
    uint32_t offset = 0;
    uint32_t error = 0;
    uint8_t expected = 0, actual = 0;
    uint32_t imageBaseOffset = 0;
    uint64_t diskImageBase = 0, mappedImageBase = 0;
};
Identity CheckImage(const std::filesystem::path& file, const void* mappedImage,
                    ProbeDetail* detail = nullptr) noexcept;
}
