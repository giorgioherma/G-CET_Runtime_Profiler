#include "StaticIndex.hpp"
#include <cstdlib>
#define GCET_ASSERT(expr) do { if(!(expr)) { std::cerr << "FAILED: " << #expr << " line " << __LINE__ << "\n"; std::abort(); } } while(false)
#include <cmath>
#include <cstdio>
#include <vector>
#include <limits>
#include <chrono>
#include <iostream>
using namespace gcet::static_locations;
int main(int argc,char** argv) {
    StaticIndex s;
    std::vector<Anchor> input = {
        {1,0,0,5,25,false,11}, {2,0,0,1000,25,true,12}, {3,280,0,0,30,false,13},
        {4,0,0,0,25,false,14}
    };
    GCET_ASSERT(s.Load(input));
    std::vector<Anchor> duplicate = {{1,0,0,0,10,false,1},{1,0,0,0,10,false,1}};
    GCET_ASSERT(!s.Load(duplicate));
    GCET_ASSERT(s.Count()==4); // transactional failed Load
    GCET_ASSERT(s.SetArmed(1,true) && s.SetArmed(2,true) && s.SetArmed(3,true));
    GCET_ASSERT(!s.SetArmed(123,true));
    GCET_ASSERT(s.State(1)==-1); // no player snapshot, must fail open
    GCET_ASSERT(s.Update(0,0,1000));
    GCET_ASSERT(s.State(1)==0 && s.State(2)==1 && s.State(3)==0 && s.State(4)==-1);
    GCET_ASSERT(s.Changes().size()==1 && s.Changes()[0].id==2);
    auto gen=s.Generation();
    GCET_ASSERT(s.Update(0,0,1000) && s.Generation()==gen); // stationary zero work
    GCET_ASSERT(s.Update(0,0,5) && s.State(1)==1 && s.State(2)==1);
    GCET_ASSERT(s.Changes().size()==1 && s.Changes()[0].id==1);
    GCET_ASSERT(s.SetArmed(2,false));
    GCET_ASSERT(s.Update(0,0,5) && s.State(2)==-1);
    GCET_ASSERT(s.Changes().size()==1 && s.Changes()[0].id==2 && !s.Changes()[0].near);
    GCET_ASSERT(s.Update(280,0,0) && s.State(1)==0 && s.State(3)==1);
    GCET_ASSERT(s.Changes().size()==2); // leave first + enter third
    GCET_ASSERT(!s.Update(std::numeric_limits<float>::quiet_NaN(),0,0));
    GCET_ASSERT(s.State(1)==-1 && s.State(3)==-1); // fail open
    GCET_ASSERT(s.Update(280,0,0) && s.State(3)==1);
    s.ResetSession(); GCET_ASSERT(s.State(3)==-1);
    GCET_ASSERT(s.Update(50000,50000,0) && s.State(3)==0);

    // Thousands of locations, only a few near the player. Update must not scan
    // all locations or allocate in the hot path. Independent wake radii work.
    std::vector<Anchor> many;
    many.reserve(10000);
    for(int i=0;i<10000;++i)many.push_back({i+100, float(i%100)*400,float(i/100)*400,0,45,false,1});
    GCET_ASSERT(s.Load(many));
    for(const auto& a: many)GCET_ASSERT(s.SetArmed(a.id,true));
    const auto start=std::chrono::steady_clock::now();
    for(int i=0;i<50000;++i)GCET_ASSERT(s.Update(float(i%1000),float((i/1000)%1000),0));
    const auto end=std::chrono::steady_clock::now();
    std::cout<<"PASS static-index; 10,000 anchors; 50,000 updates in "
        <<std::chrono::duration_cast<std::chrono::milliseconds>(end-start).count()<<" ms\n";
    if(argc>1){std::vector<Anchor> disk;GCET_ASSERT(ReadCompiledFile(argv[1],disk));GCET_ASSERT(disk.size()==2);GCET_ASSERT(s.Load(disk));}
}
