#include "MethodSlot.hpp"
#include <array>
#include <cstdlib>
#include <iostream>
#include <vector>
using namespace gcet::producer;
struct Method{int signature=1;};
struct Class{Class* parent=nullptr;std::vector<Method*> members,statics;};
struct Adapter{
    static Class* Parent(Class* c){return c->parent;}
    static uint32_t Size(Class* c,bool s){return static_cast<uint32_t>((s?c->statics:c->members).size());}
    static const void* Storage(Class* c,bool s){auto& v=s?c->statics:c->members;return v.empty()?nullptr:v.data();}
    static Method* At(Class* c,bool s,uint32_t i){return (s?c->statics:c->members)[i];}
};
static void Check(bool b,const char* m){if(!b){std::cerr<<m<<"\n";std::exit(1);}}
int main(){
    Method original,foreign,staticMethod;
    Class base{nullptr,{&original},{&staticMethod}},derived{&base,{&foreign},{}},other{&base,{},{}};
    MethodSlot<Class,Method,Adapter,4> cache;
    auto check=[](Method* m){return m->signature==1;};
    Check(cache.Bind(&derived,false,&base,0,&original),"initial bind");
    for(int i=0;i<1000;++i)Check(cache.Get(&derived,false,check)==&original,"valid hit");
    Check(!cache.Get(&other,false,check),"different receiver class");
    Check(!cache.Get(&derived,true,check),"static mismatch");
    original.signature=2;Check(!cache.Get(&derived,false,check),"signature mutation");
    original.signature=1;
    base.members[0]=&foreign;Check(!cache.Get(&derived,false,check),"same slot replacement");
    base.members[0]=&original;derived.members.push_back(&original);
    Check(!cache.Get(&derived,false,check),"new class overload");
    Check(cache.Bind(&derived,false,&base,0,&original),"rebind");
    derived.parent=nullptr;Check(!cache.Get(&derived,false,check),"lineage mutation");
    derived.parent=&base;cache.Reset();Check(!cache.Get(&derived,false,check),"session reset");
    Check(cache.Bind(&derived,true,&base,0,&staticMethod)&&cache.Get(&derived,true,check)==&staticMethod,"static owner");
    Class a,b,c,d,e;a.parent=&b;b.parent=&c;c.parent=&d;d.parent=&e;e.members={&original};
    Check(!cache.Bind(&a,false,&e,0,&original)&&!cache.Get(&a,false,check),"unbounded lineage refused");
    std::cout<<"Producer method lease: 1000 hits, class/signature/slot/lineage/epoch invalidation PASS\n";
}
