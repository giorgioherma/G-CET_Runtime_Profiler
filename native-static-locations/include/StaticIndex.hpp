#pragma once
#include <cstdint>
#include "TransitionJournal.hpp"
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
    bool inside;
};

// All public methods must be called on the game's main thread. No background thread,
// no per-mod native hook, no per-tick allocations after the index is built.
class StaticIndex final {
public:
    bool Load(std::span<const Anchor> anchors);
    // Runtime source-proven registration. One-time indexing is allowed to
    // allocate; Update/Poll still do not after Add has reserved scratch space.
    // IDs are unique for the lifetime of this loaded manifest, including
    // tombstones; callers must allocate a new ID on source-geometry change.
    bool AddAnchor(const Anchor& anchor);
    void Clear();
    void ResetSession();
    bool SetArmed(int32_t id, bool armed);
    // Runtime removal is a tombstone, not a spatial reindex. The next Resolver
    // manifest physically omits the removed static locations.
    bool RemoveAnchor(int32_t id);
    bool RemoveOwner(uint32_t owner);
    size_t OwnerCount(uint32_t owner) const;
    int32_t OwnerAnchor(uint32_t owner, size_t index) const;
    int32_t AnchorAt(size_t index) const;
    bool Subscribe(int32_t consumer, uint32_t owner);
    bool Unsubscribe(int32_t consumer) { return journal_.Unsubscribe(consumer); }
    bool NeedsSnapshot(int32_t consumer) { return journal_.NeedsSnapshot(consumer); }
    bool AckSnapshot(int32_t consumer) { return journal_.AckSnapshot(consumer); }
    int32_t LastPolledState(int32_t consumer) const { return journal_.LastState(consumer); }
    int32_t LastPolledOwner(int32_t consumer) const { return journal_.LastOwner(consumer); }
    TransitionJournal::Result Poll(int32_t consumer, TransitionJournal::Event& out) {
        return journal_.Poll(consumer,out);
    }
    // Numeric diagnostics are exposed over RTTI to the separate Profiler.
    // Values represent service work, not eliminated Lua/CET native dispatch.
    int64_t Metric(int32_t metric) const;
    // Returns false for invalid player position; state then fails OPEN to Unknown.
    bool Update(float x, float y, float z);
    // -1 unknown/unavailable, 0 reliably far, 1 near.
    int State(int32_t id) const;
    // Transient edges for the latest Update only, including empty on a
    // stationary skip. Persistent consumers use the independent journal.
    std::span<const Transition> Changes() const { return changes_; }
    uint32_t Generation() const { return generation_; }
    size_t Count() const { return entries_.size(); }

private:
    struct Entry {
        Anchor anchor;
        bool armed = false;
        bool removed = false;
        bool inside = false;
        // A newly armed anchor is UNKNOWN until the next complete native
        // position evaluation, including when the player is stationary.
        uint64_t firstValidEvaluation = 0;
        uint32_t seen = 0;
    };
    static constexpr int kCell = 128;
    static constexpr float kMaxRadius = 1024.0f;
    static constexpr size_t kMaxAnchors = 100000;
    static constexpr size_t kMaxCellsPerAnchor = 400;
    static uint64_t CellKey(int32_t x, int32_t y);
    static bool Valid(const Anchor& anchor);
    void CollectCell(int32_t x, int32_t y);
    void Emit(const Entry& entry, int32_t state);
    bool RemoveEntry(uint32_t index);
    std::vector<Entry> entries_;
    std::unordered_map<int32_t, uint32_t> ids_;
    std::unordered_map<uint32_t, std::vector<uint32_t>> owners_;
    std::unordered_map<uint64_t, std::vector<uint32_t>> cells_;
    std::vector<uint32_t> candidates_;
    std::vector<uint32_t> previousNear_;
    std::vector<Transition> changes_;
    TransitionJournal journal_;
    uint64_t playerUpdates_ = 0;
    uint64_t stationarySkips_ = 0;
    uint64_t distanceTests_ = 0;
    uint64_t emittedTransitions_ = 0;
    uint64_t invalidPlayerSamples_ = 0;
    uint32_t lastCandidates_ = 0;
    uint32_t lastDistanceTests_ = 0;
    uint32_t generation_ = 0;
    uint64_t evaluationCount_ = 0;
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
