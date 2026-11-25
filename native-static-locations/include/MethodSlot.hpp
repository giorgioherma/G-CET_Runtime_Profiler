#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace gcet::producer {
// A bounded metadata lease, not a game-object handle or a sample cache.
// Every hit checks the live owner lineage and the exact currently occupied
// method slot before allowing a descriptor dereference. Session/RTTI reset
// destroys the lease; an incompatible shape is resolved again or refused.
template<class Class,class Method,class Adapter,size_t MaxDepth=32>
class MethodSlot final {
    struct Level {Class* cls=nullptr;const void* storage=nullptr;uint32_t size=0;};
public:
    void Reset(){count_=0;method_=nullptr;owner_=0;slot_=0;}
    template<class Check>
    Method* Get(Class* root,bool isStatic,Check&& check) const {
        if(!method_||!root||isStatic!=static_)return nullptr;
        auto* cur=root;
        for(size_t i=0;i<count_;++i){
            const auto& l=levels_[i];
            if(!cur||cur!=l.cls||Adapter::Size(cur,isStatic)!=l.size||
               Adapter::Storage(cur,isStatic)!=l.storage)return nullptr;
            cur=Adapter::Parent(cur);
        }
        if(cur||owner_>=count_)return nullptr;
        auto* cls=levels_[owner_].cls;
        if(slot_>=levels_[owner_].size||Adapter::At(cls,isStatic,slot_)!=method_)return nullptr;
        return check(method_)?method_:nullptr;
    }
    bool Bind(Class* root,bool isStatic,Class* owner,uint32_t slot,Method* method){
        Reset();
        if(!root||!owner||!method)return false;
        for(auto* cur=root;cur;cur=Adapter::Parent(cur)){
            if(count_==MaxDepth){Reset();return false;}
            const auto size=Adapter::Size(cur,isStatic);
            if(cur==owner){
                if(slot>=size||Adapter::At(cur,isStatic,slot)!=method){Reset();return false;}
                owner_=count_;slot_=slot;method_=method;
            }
            levels_[count_++]={cur,Adapter::Storage(cur,isStatic),size};
        }
        if(!method_){Reset();return false;}
        static_=isStatic;return true;
    }
private:
    std::array<Level,MaxDepth> levels_{};
    size_t count_=0,owner_=0;
    uint32_t slot_=0;
    Method* method_=nullptr;
    bool static_=false;
};
}
