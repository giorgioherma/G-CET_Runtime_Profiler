#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include <unordered_map>

namespace gcet::static_locations {

// Main-thread-only, allocation-free on Append/Poll after subscriptions exist.
// One bounded event history serves any number of independent consumers. Consumers
// must acknowledge an initial/current-state snapshot before they receive events.
// A slow consumer gets NeedsSnapshot rather than silently losing a wake.
class TransitionJournal final {
public:
    static constexpr size_t kCapacity = 8192;
    static constexpr size_t kMaxConsumers = 1024;
    struct Event { uint64_t sequence=0; int32_t id=0; uint32_t owner=0; int32_t state=-1; };
    enum class Result : int32_t { NeedsSnapshot=-1, Empty=0, EventReady=1 };

    bool Subscribe(int32_t consumerId, uint32_t ownerId);
    bool Unsubscribe(int32_t consumerId);
    bool NeedsSnapshot(int32_t consumerId);
    bool AckSnapshot(int32_t consumerId);
    // Atomically advances only this consumer and copies its own event. No consumer
    // may drain another consumer's history. 'ownerId==0' subscribes to all owners.
    Result Poll(int32_t consumerId, Event& out);
    int32_t LastState(int32_t consumerId) const;
    int32_t LastOwner(int32_t consumerId) const;
    void Append(int32_t id, uint32_t owner, int32_t state);
    void InvalidateAll();
    uint64_t Appended() const { return nextSequence_ - 1; }
    uint64_t Delivered() const { return delivered_; }
    uint64_t Overflows() const { return overflows_; }
    size_t ConsumerCount() const { return consumers_.size(); }

private:
    struct Consumer {
        uint32_t owner = 0;
        uint64_t next = 1;
        bool snapshot = true;
        bool reportedOverflow = false;
        int32_t lastState = -1;
        int32_t lastOwner = -1;
    };
    std::array<Event,kCapacity> events_{};
    std::unordered_map<int32_t,Consumer> consumers_;
    uint64_t nextSequence_=1;
    uint64_t delivered_=0;
    uint64_t overflows_=0;
};
}
