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
 std::cout<<"DemandFrameCache PASS\n";
}
