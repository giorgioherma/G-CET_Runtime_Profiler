#pragma once
#include "StaticIndex.hpp"
#include <cstdint>

namespace gcet::static_locations {
// Single game-state coordinator; no author-mod callbacks feed player positions.
class PositionCoordinator final {
public:
    explicit PositionCoordinator(StaticIndex& index):index_(index){}
    void Enter();
    void Exit();
    bool Tick(bool playerAvailable,float x,float y,float z);
    // 0=not running, 1=unknown/unavailable, 2=current valid position.
    int32_t Status() const { return !running_?0:(known_?2:1); }
    uint64_t Ticks() const {return ticks_;}
    uint64_t UnavailableTicks() const {return unavailable_;}
    uint64_t AvailabilityLosses() const {return losses_;}
private:
    StaticIndex& index_;
    bool running_=false,known_=false;
    uint64_t ticks_=0,unavailable_=0,losses_=0;
};
} // namespace gcet::static_locations
