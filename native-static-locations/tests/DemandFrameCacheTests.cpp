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
 // Independent display consumers use the SAME existing native producer.
 // DAV needs three coordinates, DriveBus needs the complete vector.
 // A single panel reads once; no panel or anchor means no engine read.
 DemandFrameCache shared;
 unsigned physicalReads=0;
 auto produce=[&](Position4& value){++physicalReads;value={12,34,56,0.75f};return true;};
 CHECK(shared.Reads()==0&&physicalReads==0);
 Position4 dav{},bus{};
 CHECK(shared.Read(200,true,produce,dav));
 CHECK(shared.Read(200,true,produce,bus));
 CHECK(physicalReads==1&&shared.Reads()==1&&shared.Hits()==1&&dav.x==bus.x&&dav.w==bus.w);
 // The existing RunningUpdate producer can seed its own sampled position.
 ++physicalReads;
 shared.Seed(201,true,{13,35,57,0.5f});
 CHECK(shared.Read(201,true,produce,bus)&&shared.Read(201,true,produce,dav));
 CHECK(physicalReads==2&&shared.Reads()==1&&bus.w==0.5f&&dav.y==35);
 // Distinct engine frames and explicit exact/live requests must still read.
 CHECK(shared.Read(202,true,produce,dav)&&physicalReads==3);
 CHECK(shared.Read(202,true,produce,bus,true)&&physicalReads==4);
 CHECK(shared.Read(202,false,produce,dav)&&physicalReads==5);
 CHECK(shared.Read(202,false,produce,bus)&&physicalReads==6);
 shared.Reset();
 CHECK(shared.Read(202,true,produce,dav)&&physicalReads==7);
 std::cout<<"Shared frame position production: two panel consumers 2 -> 1; existing native seed 2 -> 0 extra acquisitions; exact and unknown-frame reads retained PASS\n";
 // Actual shared spatial cache admission: a successful strict framework
 // pose remains exact; two independent registered spatial users consume
 // its sample through one existing native position owner.
 DemandFrameCache cohort;
 const PositionReceiver hero{0x1234,77},replacement{0x1234,78},other{0x4321,77};
 Position4 coarse{};
 const auto epoch=cohort.Generation();
 unsigned hostReads=1,spatialReads=0;
 CHECK(cohort.SeedSharedSpatial(300,true,hero,epoch,{4,5,6,1}));
 auto spatial=[&](uint64_t frame,bool known,PositionReceiver who){
   if(cohort.ReadSharedSpatial(frame,known,who,coarse))return;
   ++spatialReads;coarse={9,10,11,1};
 };
 spatial(300,true,hero);
 CHECK(hostReads+spatialReads==1&&coarse.x==4&&cohort.SharedSpatialHits()==1);
 spatial(300,true,replacement);
 spatial(300,true,other);
 spatial(300,false,hero);
 spatial(301,true,hero);
 CHECK(spatialReads==4&&cohort.SharedSpatialHits()==1);
 cohort.Reset();
 CHECK(!cohort.SeedSharedSpatial(300,true,hero,epoch,{4,5,6,1}));
 spatial(300,true,hero);
 CHECK(spatialReads==5);
 CHECK(cohort.SeedSharedSpatial(301,true,hero,cohort.Generation(),{4,5,6,1}));
 CHECK(cohort.Read(301,true,produce,coarse,true));
 CHECK(!cohort.HasSharedSpatialSample());
 // The reverse phase order still reads both. No assertion that frame
 // identity establishes exact gameplay equivalence or savings in game.
 std::cout<<"Coarse spatial cohort: exact host pose + registered location phase 2 -> 1 acquisition when admitted; identity/epoch/frame/strict mismatch retained PASS\n";
 // Different gameplay callers still read at their own exact phase.
 // Only their compatible coarse spatial consumer gets the donated sample.
 DemandFrameCache liveDonor;
 int exactReads=0,checks=0;
 auto exact=[&](Position4& v,PositionReceiver& id){
    ++exactReads;id=hero;v={float(exactReads),2,3,1};return true;
 };
 auto same=[&]{++checks;return true;};
 CHECK(liveDonor.ReadLiveAndShareSpatial(400,true,exact,same,coarse)&&coarse.x==1);
 CHECK(liveDonor.ReadLiveAndShareSpatial(400,true,exact,same,coarse)&&coarse.x==2);
 CHECK(exactReads==2&&checks==2);
 CHECK(liveDonor.ReadSharedSpatial(400,true,hero,coarse)&&coarse.x==2);
 CHECK(!liveDonor.ReadSharedSpatial(400,true,other,coarse));
 CHECK(liveDonor.ReadLiveAndShareSpatial(401,true,exact,[]{return false;},coarse)&&coarse.x==3);
 CHECK(!liveDonor.HasSharedSpatialSample());
 CHECK(liveDonor.ReadLiveAndShareSpatial(401,false,exact,same,coarse)&&coarse.x==4);
 CHECK(checks==2&&!liveDonor.HasSharedSpatialSample());
 CHECK(!liveDonor.ReadLiveAndShareSpatial(402,true,exact,[&]{liveDonor.Reset();return true;},coarse));
 CHECK(!liveDonor.HasSharedSpatialSample());
 std::cout<<"Strict gameplay donors: two exact acquisitions retained, compatible spatial acquisition eliminated, latest sample/unknown frame/epoch safeguards PASS\n";
 // Actual strict entry-point policy: frame lookup is a dependency only
 // when a compatible coarse spatial consumer exists. No hypothetical
 // display cache prefetch, no stale sample and no additional exact read.
 DemandFrameCache liveDemand;
 bool active=false,frameKnown=true;
 unsigned frames=0,strict=0,donated=0;
 auto demand=[&]{return active;};
 auto frame=[&](uint64_t& f){++frames;f=500;return frameKnown;};
 auto strictRead=[&](Position4& v){++strict;v={float(strict),2,3,1};return true;};
 auto donorRead=[&](Position4& v,PositionReceiver& id){++donated;id=hero;v={99,2,3,1};return true;};
 liveDemand.Seed(500,true,{50,2,3,1});
 for(int i=0;i<100;++i)
    CHECK(liveDemand.ReadLiveForDemand(demand,frame,strictRead,donorRead,coarse));
 CHECK(frames==0&&strict==100&&donated==0&&!liveDemand.Valid());
 CHECK(liveDemand.Read(500,true,strictRead,coarse)&&strict==101&&coarse.x==101);
 active=true;
 CHECK(liveDemand.ReadLiveForDemand(demand,frame,strictRead,donorRead,coarse));
 CHECK(frames==2&&strict==101&&donated==1);
 CHECK(liveDemand.ReadSharedSpatial(500,true,hero,coarse)&&coarse.x==99);
 frameKnown=false;
 CHECK(liveDemand.ReadLiveForDemand(demand,frame,strictRead,donorRead,coarse));
 CHECK(frames==3&&strict==102&&donated==1&&!liveDemand.Valid());
 frameKnown=true;
 auto removes=[&](uint64_t& f){++frames;f=500;active=false;return true;};
 CHECK(liveDemand.ReadLiveForDemand(demand,removes,strictRead,donorRead,coarse));
 CHECK(frames==4&&strict==103&&donated==1&&!liveDemand.Valid());
 active=true;
 auto invalidate=[&](uint64_t& f){++frames;f=500;liveDemand.Reset();return true;};
 CHECK(!liveDemand.ReadLiveForDemand(demand,invalidate,strictRead,donorRead,coarse));
 CHECK(frames==5&&strict==103&&donated==1);
 std::cout<<"Demand-controlled strict position: 100 exact reads, 100 -> 0 frame-token acquisitions without coarse demand; active/unknown/removal/epoch paths PASS\n";
 std::cout<<"DemandFrameCache PASS\n";
}
