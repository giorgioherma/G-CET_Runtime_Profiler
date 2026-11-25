#include "MethodSlot.hpp"
#include <array>
#include <cstdlib>
#include <iostream>
#include <vector>
using namespace gcet::producer;
struct Method{int signature=1;int shortName=1;int fullName=1;};
struct Class{Class* parent=nullptr;std::vector<Method*> members,statics;};
struct Adapter{
    static Class* Parent(Class* c){return c->parent;}
    static uint32_t Size(Class* c,bool s){return static_cast<uint32_t>((s?c->statics:c->members).size());}
    static const void* Storage(Class* c,bool s){auto& v=s?c->statics:c->members;return v.empty()?nullptr:v.data();}
    static Method* At(Class* c,bool s,uint32_t i){return (s?c->statics:c->members)[i];}
};
static void Check(bool b,const char* m){if(!b){std::cerr<<m<<"\n";std::exit(1);}}
int main(){
    Method original,foreign{1,2,2},staticMethod;
    Class base{nullptr,{&original},{&staticMethod}},derived{&base,{&foreign},{}},other{&base,{},{}};
    MethodSlot<Class,Method,Adapter,4> cache;
    auto check=[](Method* m){return m->signature==1;};
    auto matches=[](Method* m){return m->shortName==1;};
    auto same=[](Method* a,Method* b){return a->fullName==b->fullName;};
    auto get=[&](Class* c,bool isStatic){return cache.Get(c,isStatic,check,matches,same);};
    Check(cache.Bind(&derived,false,&base,0,&original),"initial bind");
    for(int i=0;i<1000;++i)Check(get(&derived,false)==&original,"valid hit");
    Check(!get(&other,false),"different receiver class");
    Check(!get(&derived,true),"static mismatch");
    original.signature=2;Check(!get(&derived,false),"signature mutation");
    original.signature=1;
    base.members[0]=&foreign;Check(!get(&derived,false),"same slot replacement");
    base.members[0]=&original;
    // Same storage, same size, unchanged selected slot: a different live
    // method becomes an overload or a more-derived same-full-name shadow.
    foreign.shortName=1;
    Check(!get(&derived,false),"in-place nonselected overload mutation");
    foreign.fullName=1;
    Check(!get(&derived,false),"in-place more-derived same-name shadow");
    foreign.shortName=2;foreign.fullName=2;
    Method replacement{1,1,3};
    derived.members[0]=&replacement;
    Check(!get(&derived,false),"same-size nonselected pointer replacement");
    derived.members[0]=&foreign;
    Check(get(&derived,false)==&original,"restored original resolution");
    derived.members.push_back(&original);
    Check(!get(&derived,false),"new class overload");
    Check(cache.Bind(&derived,false,&base,0,&original),"rebind");
    derived.parent=nullptr;Check(!get(&derived,false),"lineage mutation");
    derived.parent=&base;cache.Reset();Check(!get(&derived,false),"session reset");
    Check(cache.Bind(&derived,true,&base,0,&staticMethod)&&get(&derived,true)==&staticMethod,"static owner");
    Class a,b,c,d,e;a.parent=&b;b.parent=&c;c.parent=&d;d.parent=&e;e.members={&original};
    Check(!cache.Bind(&a,false,&e,0,&original)&&!get(&a,false),"unbounded lineage refused");
    std::cout<<"Producer method lease: live name set, same-size overload/shadow mutation, class/signature/slot/lineage/epoch invalidation PASS\n";
}
