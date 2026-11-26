#pragma once
#include <cstdint>
#include <cmath>
namespace gcet::static_locations {
// Independent pure C++ contract for a coherent 4-component sample.
// Frame identity is authoritative only when the game's RTTI frame read succeeds.
// If that read is unavailable, every request samples the engine directly.
struct Position4 final {float x=0,y=0,z=0,w=0;};
// A value-only receiver token; never dereference or retain a cached handle.
struct PositionReceiver final {
    uintptr_t instance=0;
    uint64_t entity=0;
    bool operator==(const PositionReceiver&) const=default;
    bool Valid() const{return instance!=0&&entity!=0;}
};
class DemandFrameCache final {
public:
    void Reset(){valid_=false;frameValid_=false;sharedReceiver_={};++generation_;}
    uint64_t Generation() const{return generation_;}
    uint64_t Hits() const{return hits_;}
    uint64_t Reads() const{return reads_;}
    uint64_t SharedSpatialHits() const{return sharedSpatialHits_;}
    bool HasSharedSpatialSample() const{return valid_&&frameValid_&&sharedReceiver_.Valid();}
    bool Valid() const{return valid_;}
    static bool Finite(Position4 p) {
        return std::isfinite(p.x)&&std::isfinite(p.y)&&
               std::isfinite(p.z)&&std::isfinite(p.w)&&
               std::abs(p.x)<=1000000.f&&std::abs(p.y)<=1000000.f&&
               std::abs(p.z)<=1000000.f;
    }
    void Seed(uint64_t frame,bool frameValid,Position4 p) {
        if(!frameValid||!Finite(p)) { Reset(); return; }
        value_=p;frame_=frame;frameValid_=true;valid_=true;sharedReceiver_={};
    }
    // Exact gameplay/host reads may donate to the existing coarse spatial
    // lane. Explicitly not an exact cross-callback gameplay promise.
    bool SeedSharedSpatial(uint64_t frame,bool frameValid,PositionReceiver receiver,
                           uint64_t generation,Position4 p) {
        if(generation!=generation_||!frameValid||!receiver.Valid()||!Finite(p))return false;
        Seed(frame,true,p);sharedReceiver_=receiver;
        return true;
    }
    bool ReadSharedSpatial(uint64_t frame,bool frameValid,PositionReceiver receiver,Position4& out) {
        if(!frameValid||!HasSharedSpatialSample()||frame_!=frame||receiver!=sharedReceiver_)return false;
        out=value_;++sharedSpatialHits_;return true;
    }
    template<class Sampler>
    bool Read(uint64_t frame,bool frameValid,Sampler&& sample,Position4& out,
              bool forceLive=false) {
        if(!forceLive&&frameValid&&frameValid_&&valid_&&frame_==frame){
            out=value_;++hits_;return true;
        }
        ++reads_;
        const auto generation=generation_;
        Position4 current{};
        const bool sampled=sample(current);
        // An engine call can re-enter the producer or a session transition.
        // Do not republish an old epoch, and do not discard a valid sample
        // already installed by a nested call in the new epoch.
        if(generation_!=generation){out={};return false;}
        if(!sampled||!Finite(current)){
            // Never return the last known vector after a failed read.
            Reset();out={};return false;
        }
        out=current;
        sharedReceiver_={};
        if(frameValid){value_=current;frame_=frame;frameValid_=true;valid_=true;}
        else{valid_=false;frameValid_=false;}
        return true;
    }
    // Strict caller always performs its original acquisition. If the
    // already-admitted spatial lane is active, publish that SAME sample
    // for its later compatible request. No extra position/player read.
    template<class Sampler,class Confirm>
    bool ReadLiveAndShareSpatial(uint64_t frame,bool frameValid,Sampler&& sample,
                                 Confirm&& confirm,Position4& out) {
        const auto generation=generation_;
        PositionReceiver receiver{};
        if(!Read(frame,frameValid,[&](Position4& p){return sample(p,receiver);},out,true))
            return false;
        if(frameValid&&receiver.Valid()){
            const bool same=confirm();
            if(generation_!=generation){out={};return false;}
            if(same)SeedSharedSpatial(frame,true,receiver,generation,out);
            // Unknown frame or a changed phase must never donate.
        }
        return true;
    }
private:
    Position4 value_{};
    PositionReceiver sharedReceiver_{};
    uint64_t frame_=0,generation_=0,hits_=0,reads_=0,sharedSpatialHits_=0;
    bool frameValid_=false,valid_=false;
};
}
