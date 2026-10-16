#include "TransitionJournal.hpp"

namespace gcet::static_locations {
bool TransitionJournal::Subscribe(int32_t consumerId, uint32_t ownerId) {
    if (consumerId <= 0) return false;
    auto it=consumers_.find(consumerId);
    if (it != consumers_.end()) return it->second.owner == ownerId;
    if (consumers_.size()>=kMaxConsumers) return false;
    return consumers_.emplace(consumerId, Consumer{ownerId,nextSequence_,true,false,-1,-1}).second;
}
bool TransitionJournal::Unsubscribe(int32_t consumerId) { return consumers_.erase(consumerId)!=0; }

bool TransitionJournal::NeedsSnapshot(int32_t consumerId) {
    auto it=consumers_.find(consumerId);
    if (it==consumers_.end()) return true;
    auto& c=it->second;
    if (!c.snapshot && nextSequence_>kCapacity && c.next < nextSequence_-kCapacity) {
        c.snapshot=true;
        if (!c.reportedOverflow) { ++overflows_; c.reportedOverflow=true; }
    }
    return c.snapshot;
}
bool TransitionJournal::AckSnapshot(int32_t consumerId) {
    auto it=consumers_.find(consumerId);
    if (it==consumers_.end() || !it->second.snapshot) return false;
    // The API is main-thread only: caller must take a complete native state
    // snapshot immediately before acknowledgement, without another Update.
    it->second.next=nextSequence_;
    it->second.snapshot=false;
    it->second.reportedOverflow=false;
    it->second.lastState=-1;
    it->second.lastOwner=-1;
    return true;
}
TransitionJournal::Result TransitionJournal::Poll(int32_t consumerId, Event& out) {
    auto it=consumers_.find(consumerId);
    if (it==consumers_.end() || NeedsSnapshot(consumerId)) return Result::NeedsSnapshot;
    auto& c=it->second;
    while (c.next<nextSequence_) {
        const Event& e=events_[(c.next-1)%kCapacity];
        // Guard against rollover/corruption and fail open rather than dispatch
        // an event from an unrelated registry epoch.
        if (e.sequence!=c.next) {
            c.snapshot=true;
            if (!c.reportedOverflow) { ++overflows_; c.reportedOverflow=true; }
            return Result::NeedsSnapshot;
        }
        ++c.next;
        if (c.owner && c.owner != e.owner) continue;
        out=e;
        c.lastState=e.state;
        c.lastOwner=static_cast<int32_t>(e.owner);
        ++delivered_;
        return Result::EventReady;
    }
    return Result::Empty;
}
int32_t TransitionJournal::LastState(int32_t consumerId) const {
    auto it=consumers_.find(consumerId);
    return it==consumers_.end()?-1:it->second.lastState;
}
int32_t TransitionJournal::LastOwner(int32_t consumerId) const {
    auto it=consumers_.find(consumerId);
    return it==consumers_.end()?-1:it->second.lastOwner;
}
void TransitionJournal::Append(int32_t id,uint32_t owner,int32_t state) {
    events_[(nextSequence_-1)%kCapacity] = Event{nextSequence_,id,owner,state};
    ++nextSequence_;
}
void TransitionJournal::InvalidateAll() {
    for (auto& [id,c]:consumers_) {
        (void)id;
        c.snapshot=true;
        c.reportedOverflow=false;
        c.lastState=-1;
        c.lastOwner=-1;
    }
}
}
