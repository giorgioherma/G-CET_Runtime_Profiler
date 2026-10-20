#pragma once
#include <cstdint>
#include <cmath>
namespace gcet::static_locations {
// Independent pure C++ contract for a coherent 4-component sample.
// Frame identity is authoritative only when the game's RTTI frame read succeeds.
// If that read is unavailable, every request samples the engine directly.
struct Position4 final {float x=0,y=0,z=0,w=0;};
class DemandFrameCache final {
public:
    void Reset(){valid_=false;frameValid_=false;++generation_;}
    uint64_t Generation() const{return generation_;}
    uint64_t Hits() const{return hits_;}
    uint64_t Reads() const{return reads_;}
    bool Valid() const{return valid_;}
    static bool Finite(Position4 p) {
        return std::isfinite(p.x)&&std::isfinite(p.y)&&
               std::isfinite(p.z)&&std::isfinite(p.w)&&
               std::abs(p.x)<=1000000.f&&std::abs(p.y)<=1000000.f&&
               std::abs(p.z)<=1000000.f;
    }
    void Seed(uint64_t frame,bool frameValid,Position4 p) {
        if(!frameValid||!Finite(p)) { Reset(); return; }
        value_=p;frame_=frame;frameValid_=true;valid_=true;
    }
    template<class Sampler>
    bool Read(uint64_t frame,bool frameValid,Sampler&& sample,Position4& out,
              bool forceLive=false) {
        if(!forceLive&&frameValid&&frameValid_&&valid_&&frame_==frame){
            out=value_;++hits_;return true;
        }
        ++reads_;
        Position4 current{};
        if(!sample(current)||!Finite(current)){
            // Never return the last known vector after a failed read.
            Reset();out={};return false;
        }
        out=current;
        if(frameValid){value_=current;frame_=frame;frameValid_=true;valid_=true;}
        else{valid_=false;frameValid_=false;}
        return true;
    }
private:
    Position4 value_{};
    uint64_t frame_=0,generation_=0,hits_=0,reads_=0;
    bool frameValid_=false,valid_=false;
};
}
