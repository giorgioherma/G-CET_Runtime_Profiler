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

// Explicit developer probe. No module load, no writes, no recurring work.
Identity CheckLoadedCET() noexcept;
// Same read-only gate used for official image mapping tests.
Identity CheckImage(const std::filesystem::path& file, const void* mappedImage) noexcept;
}
