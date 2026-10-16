#include "PositionCoordinator.hpp"
#include <cmath>
namespace gcet::static_locations {
void PositionCoordinator::Enter(){
    if(running_)return;
    running_=true; known_=false; index_.ResetSession();
}
void PositionCoordinator::Exit(){
    if(!running_)return;
    running_=false; known_=false; index_.ResetSession();
}
bool PositionCoordinator::Tick(bool available,float x,float y,float z){
    if(!running_)return false;
    ++ticks_;
    const bool valid=available&&std::isfinite(x)&&std::isfinite(y)&&std::isfinite(z)
        &&std::abs(x)<=1000000.f&&std::abs(y)<=1000000.f&&std::abs(z)<=1000000.f;
    if(!valid){
        ++unavailable_;
        if(known_){known_=false; ++losses_; index_.ResetSession();}
        return false;
    }
    if(!index_.Update(x,y,z)){
        ++unavailable_;
        if(known_){known_=false; ++losses_; index_.ResetSession();}
        return false;
    }
    known_=true;
    return true;
}
}
