#pragma once
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
namespace gcet::static_locations {
// Main-thread-only scalar snapshot; never retains REDengine object handles.
class PlayerSnapshot final {
public:
    static constexpr int32_t Position=1, Availability=2, Fields=3;
    static constexpr int32_t Snapshot=4, Session=8, Coalesced=16;
    static constexpr size_t Capacity=64;
    bool Subscribe(int32_t id,int32_t fields) {
        if(id<=0 || fields<=0 || (fields & ~Fields)) return false;
        for(auto& slot:slots_) {
            if(slot.id!=id)continue;
            if(slot.fields!=fields){slot.fields=fields;slot.pending|=Snapshot;}
            return true;
        }
        for(auto& slot:slots_) {
            if(slot.id)continue;
            slot={id,fields,Snapshot};
            ++subscribers_;
            return true;
        }
        return false;
    }
    bool Unsubscribe(int32_t id) {
        if(id<=0)return false;
        for(auto& slot:slots_) {
            if(slot.id!=id)continue;
            slot={};
            --subscribers_;
            if(!subscribers_)known_=false;
            return true;
        }
        return false;
    }
    bool HasDemand() const{return subscribers_!=0;}
    size_t SubscriberCount() const{return subscribers_;}
    void Enter(){
        if(running_)return;
        running_=true;known_=false;++epoch_;
        Publish(Session|Snapshot);
    }
    void Exit(){
        if(!running_)return;
        running_=false;known_=false;++epoch_;
        Publish(Session|Snapshot|Availability);
    }
    void Tick(bool valid,float x,float y,float z){
        if(!running_ || !HasDemand())return;
        ++samples_;
        valid=valid && std::isfinite(x)&&std::isfinite(y)&&std::isfinite(z)
            && std::abs(x)<=1000000.f&&std::abs(y)<=1000000.f&&std::abs(z)<=1000000.f;
        if(!valid){
            if(known_){known_=false;++revision_;Publish(Availability);}
            return;
        }
        const bool becameKnown=!known_;
        const bool moved=becameKnown||x!=x_||y!=y_||z!=z_;
        known_=true;x_=x;y_=y;z_=z;
        if(becameKnown||moved){
            ++revision_;
            Publish((becameKnown?Availability:0)|(moved?Position:0));
        }
    }
    // 0=no change, -1=invalid consumer, otherwise OR-ed change flags.
    int32_t Poll(int32_t id){
        if(id<=0)return -1;
        for(auto& slot:slots_){
            if(slot.id!=id)continue;
            int32_t result=slot.pending;
            slot.pending=0;
            return result;
        }
        return -1;
    }
    // 0 not Running; 1 unavailable; 2 current sample valid.
    int32_t Status() const{return !running_?0:(known_?2:1);}
    uint32_t Epoch() const{return epoch_;}
    uint32_t Revision() const{return revision_;}
    uint64_t Samples() const{return samples_;}
    float X() const{return known_?x_:0;}
    float Y() const{return known_?y_:0;}
    float Z() const{return known_?z_:0;}
private:
    struct Slot{int32_t id=0,fields=0,pending=0;};
    void Publish(int32_t flags){
        for(auto& slot:slots_){
            if(!slot.id)continue;
            int32_t selected=(flags&slot.fields)|(flags&(Snapshot|Session));
            if(!selected)continue;
            // Changes may coalesce: always read the newest snapshot.
            if(slot.pending&Fields)slot.pending|=Coalesced;
            slot.pending|=selected;
        }
    }
    std::array<Slot,Capacity> slots_{};
    size_t subscribers_=0;
    bool running_=false,known_=false;
    uint32_t epoch_=0,revision_=0;
    uint64_t samples_=0;
    float x_=0,y_=0,z_=0;
};
}
