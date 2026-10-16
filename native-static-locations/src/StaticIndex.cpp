#include "StaticIndex.hpp"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <fstream>
#include <limits>
#include <array>

namespace gcet::static_locations {
namespace {
    constexpr float kCellF = 128.0f;
    // Coordinate bounds keep floor/cell conversion and the cell-key math safe.
    constexpr float kMaxCoordinate = 1000000.0f;
    bool Finite(float n) { return std::isfinite(n); }
    bool ValidPlayer(float x, float y, float z) {
        return Finite(x) && Finite(y) && Finite(z) &&
            std::abs(x) <= kMaxCoordinate && std::abs(y) <= kMaxCoordinate &&
            std::abs(z) <= kMaxCoordinate;
    }
    int32_t Cell(float n) { return static_cast<int32_t>(std::floor(n / kCellF)); }
    uint32_t ReadU32(const unsigned char* p) {
        return uint32_t(p[0]) | (uint32_t(p[1]) << 8) |
            (uint32_t(p[2]) << 16) | (uint32_t(p[3]) << 24);
    }
    float ReadF32(const unsigned char* p) {
        uint32_t v = ReadU32(p); float f;
        std::memcpy(&f, &v, sizeof(f)); return f;
    }
}

uint64_t StaticIndex::CellKey(int32_t x, int32_t y) {
    return (uint64_t(uint32_t(x)) << 32) | uint32_t(y);
}
bool StaticIndex::Valid(const Anchor& a) {
    return a.id > 0 && a.owner > 0 && ValidPlayer(a.x, a.y, a.z) &&
        Finite(a.radius) && a.radius > 0 && a.radius <= kMaxRadius;
}

bool StaticIndex::Load(std::span<const Anchor> anchors) {
    if (anchors.size() > kMaxAnchors) return false;
    // Build in a temporary to preserve the existing registry on bad input.
    StaticIndex next;
    next.entries_.reserve(anchors.size());
    next.ids_.reserve(anchors.size());
    next.candidates_.reserve(anchors.size());
    next.previousNear_.reserve(anchors.size());
    next.changes_.reserve(anchors.size());
    for (const Anchor& anchor : anchors) {
        if (!Valid(anchor) || next.ids_.contains(anchor.id)) return false;
        const int32_t xl = Cell(anchor.x - anchor.radius);
        const int32_t xh = Cell(anchor.x + anchor.radius);
        const int32_t yl = Cell(anchor.y - anchor.radius);
        const int32_t yh = Cell(anchor.y + anchor.radius);
        const size_t count = size_t(xh-xl+1) * size_t(yh-yl+1);
        if (count > kMaxCellsPerAnchor) return false;
        uint32_t i = static_cast<uint32_t>(next.entries_.size());
        next.entries_.push_back({anchor, false, false, 0});
        next.ids_[anchor.id] = i;
        next.owners_[anchor.owner].push_back(i);
        for (int32_t x = xl; x <= xh; ++x)
            for (int32_t y = yl; y <= yh; ++y)
                next.cells_[CellKey(x,y)].push_back(i);
    }
    *this = std::move(next);
    return true;
}
void StaticIndex::Clear() {
    *this = StaticIndex{};
}
void StaticIndex::ResetSession() {
    positioned_ = false;
    dirty_ = true;
    journal_.InvalidateAll();
    ++generation_;
    changes_.clear();
    previousNear_.clear();
    for (auto& entry : entries_) { entry.near = false; entry.seen = 0; }
    stamp_ = 0;
}
bool StaticIndex::SetArmed(int32_t id, bool armed) {
    const auto it = ids_.find(id);
    if (it == ids_.end()) return false;
    auto& e = entries_[it->second];
    if (e.removed) return false;
    if (e.armed != armed) {
        if (!armed && positioned_) Emit(e,-1); // disarm becomes UNKNOWN, never FAR
        e.armed = armed;
        if (armed) e.near=false; // guarantee a new near wake after rearming
        dirty_ = true;
    }
    return true;
}
void StaticIndex::Emit(const Entry& e, int32_t state) {
    journal_.Append(e.anchor.id,e.anchor.owner,state);
    ++emittedTransitions_;
}
bool StaticIndex::RemoveEntry(uint32_t i) {
    auto& e=entries_[i];
    if (e.removed) return false;
    if (positioned_) Emit(e,-1);
    e.removed=true; e.armed=false; e.near=false;
    dirty_=true;
    return true;
}
bool StaticIndex::RemoveAnchor(int32_t id) {
    auto it=ids_.find(id);
    return it!=ids_.end() && RemoveEntry(it->second);
}
bool StaticIndex::RemoveOwner(uint32_t owner) {
    auto it=owners_.find(owner);
    if (it==owners_.end()) return false;
    bool changed=false;
    for (auto i:it->second) changed=RemoveEntry(i)||changed;
    return changed;
}
size_t StaticIndex::OwnerCount(uint32_t owner) const {
    auto it=owners_.find(owner);
    return it==owners_.end()?0:it->second.size();
}
int32_t StaticIndex::OwnerAnchor(uint32_t owner, size_t index) const {
    auto it=owners_.find(owner);
    if (it==owners_.end()||index>=it->second.size()) return -1;
    const auto& e=entries_[it->second[index]];
    return e.removed?-1:e.anchor.id;
}
int32_t StaticIndex::AnchorAt(size_t index) const {
    if (index>=entries_.size()) return -1;
    const auto& e=entries_[index];
    return e.removed?-1:e.anchor.id;
}
bool StaticIndex::Subscribe(int32_t consumer,uint32_t owner) {
    if (owner!=0 && owners_.find(owner)==owners_.end()) return false;
    return journal_.Subscribe(consumer,owner);
}
int64_t StaticIndex::Metric(int32_t metric) const {
    switch(metric) {
        case 0: return static_cast<int64_t>(playerUpdates_);
        case 1: return static_cast<int64_t>(stationarySkips_);
        case 2: return static_cast<int64_t>(distanceTests_);
        case 3: return static_cast<int64_t>(emittedTransitions_);
        case 4: return static_cast<int64_t>(journal_.Delivered());
        case 5: return static_cast<int64_t>(journal_.Overflows());
        case 6: return static_cast<int64_t>(invalidPlayerSamples_);
        case 7: return lastCandidates_;
        case 8: return lastDistanceTests_;
        case 9: return static_cast<int64_t>(journal_.ConsumerCount());
        case 10: return static_cast<int64_t>(journal_.Appended());
        default: return -1;
    }
}
void StaticIndex::CollectCell(int32_t x, int32_t y) {
    auto it = cells_.find(CellKey(x,y));
    if (it == cells_.end()) return;
    for (auto index : it->second) {
        auto& e = entries_[index];
        if (!e.armed || e.removed || e.seen == stamp_) continue;
        e.seen = stamp_;
        candidates_.push_back(index);
    }
}
bool StaticIndex::Update(float x, float y, float z) {
    if (!ValidPlayer(x,y,z)) {
        ++invalidPlayerSamples_;
        ResetSession();
        return false;
    }
    // Exact equality here is safe: if position is identical AND there was no
    // arm/session/source change, no distance can have crossed a boundary.
    if (positioned_ && !dirty_ && x == x_ && y == y_ && z == z_) {
        ++stationarySkips_;
        return true;
    }
    ++playerUpdates_;
    x_ = x; y_ = y; z_ = z; positioned_ = true; dirty_ = false;
    ++generation_;
    ++stamp_;
    if (!stamp_) { stamp_ = 1; for (auto& e : entries_) e.seen = 0; }
    candidates_.clear();
    changes_.clear();
    lastDistanceTests_=0;
    // Anchors are preassigned to EVERY cell overlapping their wake circle.
    // Querying the player's cell alone is sufficient even when wake radii vary.
    CollectCell(Cell(x), Cell(y));
    lastCandidates_=static_cast<uint32_t>(candidates_.size());
    for (uint32_t index : candidates_) {
        auto& e = entries_[index];
        ++distanceTests_;
        ++lastDistanceTests_;
        const float dx = x-e.anchor.x, dy=y-e.anchor.y;
        const float distSq = dx*dx+dy*dy + (e.anchor.xyOnly ? 0.0f : (z-e.anchor.z)*(z-e.anchor.z));
        const bool near = distSq <= e.anchor.radius*e.anchor.radius;
        if (near != e.near) {
            e.near = near;
            changes_.push_back({e.anchor.id, near});
            Emit(e,near?1:0);
        }
    }
    // Previous near anchors can leave the current cell. No global N-anchor scan.
    for (uint32_t index : previousNear_) {
        auto& e = entries_[index];
        if (e.near && (!e.armed || e.seen != stamp_)) {
            e.near = false;
            changes_.push_back({e.anchor.id, false});
            if (e.armed && !e.removed) Emit(e,0);
        }
    }
    previousNear_.clear();
    for (uint32_t index : candidates_) if (entries_[index].near) previousNear_.push_back(index);
    return true;
}
int StaticIndex::State(int32_t id) const {
    auto it=ids_.find(id);
    if (it == ids_.end() || !positioned_) return -1;
    const auto& e=entries_[it->second];
    return e.armed && !e.removed ? (e.near ? 1 : 0) : -1;
}

bool ReadCompiledFile(const std::filesystem::path& path, std::vector<Anchor>& out) {
    std::ifstream input(path,std::ios::binary | std::ios::ate);
    if (!input) return false;
    auto length=input.tellg();
    if (length < 16 || length > std::streamoff(16+28*100000)) return false;
    input.seekg(0);
    std::vector<unsigned char> bytes(static_cast<size_t>(length));
    if (!input.read(reinterpret_cast<char*>(bytes.data()),length)) return false;
    if (std::memcmp(bytes.data(),"GCAS",4) != 0 || ReadU32(bytes.data()+4) != 1 || ReadU32(bytes.data()+12) != 0) return false;
    const uint32_t count=ReadU32(bytes.data()+8);
    if (count > 100000 || bytes.size()!=16u+28u*count) return false;
    std::vector<Anchor> parsed;
    parsed.reserve(count);
    for (uint32_t i=0;i<count;++i) {
        const auto* p=bytes.data()+16u+28u*i;
        const uint32_t flags=ReadU32(p+20);
        if (flags & ~uint32_t(1)) return false;
        parsed.push_back({static_cast<int32_t>(ReadU32(p)),ReadF32(p+4),ReadF32(p+8),ReadF32(p+12),ReadF32(p+16),(flags&1)!=0,ReadU32(p+24)});
    }
    StaticIndex guard;
    if (!guard.Load(parsed)) return false;
    out=std::move(parsed);
    return true;
}
}
