#pragma once
#include <cstdint>
#include <filesystem>
#include <span>
#include <unordered_map>
#include <vector>

namespace gcet::static_locations {

struct Anchor {
    int32_t id;
    float x, y, z;
    float radius;
    bool xyOnly;
    uint32_t owner;
};

struct Transition {
    int32_t id;
    bool near;
};

// All public methods must be called on the game's main thread. No background thread,
// no per-mod native hook, no per-tick allocations after the index is built.
class StaticIndex final {
public:
    bool Load(std::span<const Anchor> anchors);
    void Clear();
    void ResetSession();
    bool SetArmed(int32_t id, bool armed);
    // Returns false for invalid player position; state then fails OPEN to Unknown.
    bool Update(float x, float y, float z);
    // -1 unknown/unavailable, 0 reliably far, 1 near.
    int State(int32_t id) const;
    std::span<const Transition> Changes() const { return changes_; }
    uint32_t Generation() const { return generation_; }
    size_t Count() const { return entries_.size(); }

private:
    struct Entry {
        Anchor anchor;
        bool armed = false;
        bool near = false;
        uint32_t seen = 0;
    };
    static constexpr int kCell = 128;
    static constexpr float kMaxRadius = 1024.0f;
    static constexpr size_t kMaxAnchors = 100000;
    static constexpr size_t kMaxCellsPerAnchor = 400;
    static uint64_t CellKey(int32_t x, int32_t y);
    static bool Valid(const Anchor& anchor);
    void CollectCell(int32_t x, int32_t y);
    std::vector<Entry> entries_;
    std::unordered_map<int32_t, uint32_t> ids_;
    std::unordered_map<uint64_t, std::vector<uint32_t>> cells_;
    std::vector<uint32_t> candidates_;
    std::vector<uint32_t> previousNear_;
    std::vector<Transition> changes_;
    uint32_t generation_ = 0;
    uint32_t stamp_ = 0;
    float x_ = 0, y_ = 0, z_ = 0;
    bool positioned_ = false;
    bool dirty_ = true;
};

// Frozen little-endian disk format: header GCAS (4), version(4), count(4), reserved(4).
// Every 28-byte record: int32 id; float x/y/z/radius; uint32 flags (bit 0 XY);
// uint32 owner. Zero internal pointers or mod-owned userdata are persisted.
bool ReadCompiledFile(const std::filesystem::path& path, std::vector<Anchor>& out);
}
