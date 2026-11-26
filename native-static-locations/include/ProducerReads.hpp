#pragma once
#include <RED4ext/Scripting/Functions.hpp>
#include "DemandFrameCache.hpp"
namespace gcet::producer {
// Registers two ordinary synchronous native producers in the existing
// RED4ext service. No RunningUpdate hook, retained receiver or polling.
void RegisterProducerReads();
void ResetProducerReads() noexcept;
// The existing native location/position owner supplies a short-lived stamp.
// Only the explicitly shared framework pose ABI requests it.
struct SharedSpatialStamp {
    uint64_t generation=0,frame=0;
    static_locations::PositionReceiver receiver{};
    bool valid=false;
};
SharedSpatialStamp BeginSharedSpatialPose(RED4ext::IScriptable* receiver);
void CompleteSharedSpatialPose(const SharedSpatialStamp& stamp,
                               static_locations::Position4 value);
}
