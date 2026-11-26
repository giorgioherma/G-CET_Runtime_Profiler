#include "DemandFrameCache.hpp"
#include <cstdlib>
#include <iostream>
#include <limits>
using namespace gcet::static_locations;
#define CHECK(x) do {if(!(x)){std::cerr<<"FAIL "<<#x<<" line "<<__LINE__<<"\n";std::abort();}}while(0)
int main(){
 DemandFrameCache cache;
 Position4 p{};
 int reads=0;
 auto engine=[&](Position4& out){++reads;out={10.f,20.f,30.f,0.75f};return true;};
 CHECK(cache.Read(100,true,engine,p)&&reads==1&&p.w==0.75f);
 CHECK(cache.Read(100,true,engine,p)&&reads==1&&cache.Hits()==1);
 CHECK(cache.Read(101,true,engine,p)&&reads==2);
 CHECK(cache.Read(101,false,engine,p)&&reads==3); // Unknown frame: MUST read live
 CHECK(cache.Read(101,false,engine,p)&&reads==4);
 CHECK(cache.Read(101,true,engine,p)&&reads==5); // Old frame cache invalidated
 CHECK(cache.Read(101,true,engine,p,true)&&reads==6); // Forced live
 CHECK(cache.Read(101,true,engine,p)&&reads==6);
 cache.Reset();CHECK(cache.Read(101,true,engine,p)&&reads==7);
 cache.Seed(102,true,{1,2,3,4});
 CHECK(cache.Read(102,true,engine,p)&&reads==7&&p.w==4);
 auto gone=[](Position4&){return false;};
 CHECK(!cache.Read(103,true,gone,p)&&!cache.Valid());
 CHECK(!cache.Read(103,true,gone,p)&&p.w==0);
 auto nan=[](Position4& out){out={1,2,3,std::numeric_limits<float>::quiet_NaN()};return true;};
 CHECK(!cache.Read(103,true,nan,p));
 CHECK(cache.Read(103,true,engine,p)&&reads==8);
 cache.Reset();
 CHECK(!cache.Valid());
 // Reset can occur inside the native engine call, with or without a
 // successful reentrant read in the new session. Never expose old data.
 auto invalidates=[&](Position4& out){cache.Reset();out={1,2,3,4};return true;};
 CHECK(!cache.Read(104,true,invalidates,p)&&!cache.Valid()&&p.w==0);
 auto nested=[&](Position4& out){
    cache.Reset();
    Position4 newer{};
    CHECK(cache.Read(105,true,engine,newer)&&newer.w==0.75f);
    out={1,2,3,4};
    return true;
 };
 CHECK(!cache.Read(104,true,nested,p,true)&&p.w==0);
 auto readsAfterNested=cache.Reads();
 CHECK(cache.Read(105,true,engine,p)&&cache.Reads()==readsAfterNested&&p.w==0.75f);
 auto nestedFailure=[&](Position4& out){
    cache.Reset();cache.Seed(106,true,{6,7,8,9});
    out={1,2,3,4};return false;
 };
 CHECK(!cache.Read(104,true,nestedFailure,p,true)&&p.w==0);
 CHECK(cache.Read(106,true,engine,p)&&p.w==9);
 std::cout<<"DemandFrameCache PASS\n";
}
