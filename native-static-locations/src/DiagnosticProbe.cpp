#include "DiagnosticProbe.hpp"
namespace gcet::static_locations {
int32_t DiagnosticProbe::Create(float radius){
    float x=0,y=0,z=0;
    if(next_>kLastId || !coordinator_.CurrentPosition(x,y,z))
        return -1;
    const int32_t id=next_;
    if(!index_.AddAnchor({id,x,y,z,radius,false,static_cast<uint32_t>(kOwner)}))
        return -1;
    ++next_; // even a failed arm consumes an ID; native tombstones are stable
    if(!index_.SetArmed(id,true)){
        index_.RemoveAnchor(id);
        return -1;
    }
    // Build and arm the new entry BEFORE removing the prior probe. No
    // transient no-probe window and no reuse of tombstoned locations.
    if(last_>0)index_.RemoveAnchor(last_);
    last_=id;
    return id;
}
bool DiagnosticProbe::Remove(){
    if(last_<=0)return false;
    const int32_t id=last_;
    last_=0;
    return index_.RemoveAnchor(id);
}
} // namespace gcet::static_locations
