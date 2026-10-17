#pragma once
#include "PositionCoordinator.hpp"
#include "StaticIndex.hpp"
#include <cstdint>

namespace gcet::static_locations {

// Explicit diagnostic only, never a gameplay/Resolver owner. One native-owned
// passive anchor at the coordinator's latest valid player position. It uses a
// reserved high Int32 range to avoid normal source IDs and has no callbacks.
class DiagnosticProbe final {
public:
    static constexpr int32_t kOwner=2100000000;
    static constexpr int32_t kFirstId=2100000000;
    static constexpr int32_t kLastId=2100000099;
    DiagnosticProbe(StaticIndex& index,PositionCoordinator& coordinator)
        :index_(index),coordinator_(coordinator){}
    int32_t Create(float radius);
    bool Remove();
    int32_t Id() const {return last_;}
private:
    StaticIndex& index_;
    PositionCoordinator& coordinator_;
    int32_t next_=kFirstId;
    int32_t last_=0;
};
} // namespace gcet::static_locations
