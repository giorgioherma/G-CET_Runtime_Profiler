#include "StaticIndex.hpp"
#include "PositionCoordinator.hpp"
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
    GCET_ASSERT(s.Changes().size()==1 && s.Changes()[0].id==2 && !s.Changes()[0].inside);
    GCET_ASSERT(s.Update(280,0,0) && s.State(1)==0 && s.State(3)==1);
    GCET_ASSERT(s.Changes().size()==2); // leave first + enter third
    GCET_ASSERT(!s.Update(std::numeric_limits<float>::quiet_NaN(),0,0));
    GCET_ASSERT(s.State(1)==-1 && s.State(3)==-1); // fail open
    GCET_ASSERT(s.Update(280,0,0) && s.State(3)==1);
    s.ResetSession(); GCET_ASSERT(s.State(3)==-1);
    GCET_ASSERT(s.Update(50000,50000,0) && s.State(3)==0);

    // Independent consumers do not steal events from one another.
    // This is required before removing any author's original proximity wake.
    StaticIndex events;
    GCET_ASSERT(events.Load(std::vector<Anchor>{{101,0,0,0,30,false,11},
                                                {102,300,0,0,30,false,12}}));
    GCET_ASSERT(events.Subscribe(5001,0));       // CET aggregator, all owners
    GCET_ASSERT(events.Subscribe(5002,11));      // REDscript mod, owner 11 only
    GCET_ASSERT(events.Subscribe(5001,0));       // idempotent
    GCET_ASSERT(!events.Subscribe(5001,11));     // same consumer, wrong owner
    GCET_ASSERT(!events.Subscribe(5003,999));    // missing owner
    GCET_ASSERT(events.NeedsSnapshot(5001) && events.NeedsSnapshot(5002));
    GCET_ASSERT(events.Update(0,0,0));
    GCET_ASSERT(events.SetArmed(101,true) && events.SetArmed(102,true));
    GCET_ASSERT(events.Update(0,0,0)); // dirty invalidation at same position
    GCET_ASSERT(events.State(101)==1 && events.State(102)==0);
    GCET_ASSERT(events.AckSnapshot(5001) && events.AckSnapshot(5002));
    GCET_ASSERT(events.Update(300,0,0));
    TransitionJournal::Event e1{},e2{};
    GCET_ASSERT(events.Poll(5001,e1)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(e1.id==102 && e1.state==1);
    GCET_ASSERT(events.Poll(5001,e1)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(e1.id==101 && e1.state==0);
    GCET_ASSERT(events.Poll(5001,e1)==TransitionJournal::Result::Empty);
    GCET_ASSERT(events.Poll(5002,e2)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(e2.id==101 && e2.state==0);
    GCET_ASSERT(events.Poll(5002,e2)==TransitionJournal::Result::Empty);
    GCET_ASSERT(events.RemoveOwner(11));
    GCET_ASSERT(events.State(101)==-1);
    GCET_ASSERT(!events.SetArmed(101,true) && !events.RemoveOwner(11));
    GCET_ASSERT(events.Poll(5002,e2)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(e2.id==101 && e2.state==-1); // remove never masquerades as FAR
    GCET_ASSERT(events.OwnerAnchor(11,0)==-1 && events.OwnerAnchor(12,0)==102);
    GCET_ASSERT(events.Metric(3)>=3 && events.Metric(4)>=4);
    GCET_ASSERT(events.Metric(2)>0 && events.Metric(7)>=0);
    events.ResetSession();
    GCET_ASSERT(events.NeedsSnapshot(5001) && events.NeedsSnapshot(5002));
    GCET_ASSERT(events.Poll(5001,e1)==TransitionJournal::Result::NeedsSnapshot);
    GCET_ASSERT(events.AckSnapshot(5001));
    GCET_ASSERT(events.Unsubscribe(5001) && !events.Unsubscribe(5001));

    // A slow reader must get explicit RESYNC_REQUIRED, never a silent jump.
    TransitionJournal journal;
    GCET_ASSERT(journal.Subscribe(1,0) && journal.Subscribe(2,7));
    GCET_ASSERT(journal.AckSnapshot(1) && journal.AckSnapshot(2));
    for(size_t n=0;n<TransitionJournal::kCapacity+10;++n)
        journal.Append(101,7,(n&1)?0:1);
    TransitionJournal::Event out{};
    GCET_ASSERT(journal.Poll(1,out)==TransitionJournal::Result::NeedsSnapshot);
    GCET_ASSERT(journal.NeedsSnapshot(2));
    GCET_ASSERT(journal.Overflows()==2);
    GCET_ASSERT(journal.AckSnapshot(1));
    journal.Append(101,7,1);
    GCET_ASSERT(journal.Poll(1,out)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(out.id==101 && out.state==1);
    GCET_ASSERT(journal.Poll(2,out)==TransitionJournal::Result::NeedsSnapshot);
    GCET_ASSERT(journal.Unsubscribe(1));


    // Single native coordinator; independent consumers; failure becomes UNKNOWN.
    StaticIndex shared;
    GCET_ASSERT(shared.Load(std::vector<Anchor>{{201,0,0,0,20,false,41},
                                                 {202,200,0,0,20,false,42}}));
    GCET_ASSERT(shared.SetArmed(201,true)&&shared.SetArmed(202,true));
    GCET_ASSERT(shared.Subscribe(6001,0)&&shared.Subscribe(6002,41));
    PositionCoordinator driver(shared);
    GCET_ASSERT(driver.Status()==0&&!driver.Tick(true,0,0,0));
    driver.Enter();
    GCET_ASSERT(driver.Status()==1&&shared.State(201)==-1);
    GCET_ASSERT(driver.Tick(true,0,0,0));
    GCET_ASSERT(driver.Status()==2&&shared.State(201)==1&&shared.State(202)==0);
    GCET_ASSERT(shared.AckSnapshot(6001)&&shared.AckSnapshot(6002));
    GCET_ASSERT(driver.Tick(true,200,0,0));
    TransitionJournal::Event delivered{};
    GCET_ASSERT(shared.Poll(6002,delivered)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(delivered.id==201&&delivered.state==0);
    GCET_ASSERT(shared.Poll(6001,delivered)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(delivered.id==202&&delivered.state==1);
    GCET_ASSERT(!driver.Tick(false,0,0,0));
    GCET_ASSERT(driver.Status()==1&&shared.State(201)==-1);
    GCET_ASSERT(shared.NeedsSnapshot(6001)&&shared.NeedsSnapshot(6002));
    auto losses=driver.AvailabilityLosses();
    for(int k=0;k<100;++k)GCET_ASSERT(!driver.Tick(false,0,0,0));
    GCET_ASSERT(driver.AvailabilityLosses()==losses); // no O(N) reset per bad tick
    GCET_ASSERT(driver.Tick(true,0,0,0)&&shared.State(201)==1);
    GCET_ASSERT(shared.AckSnapshot(6001));
    driver.Exit();
    GCET_ASSERT(driver.Status()==0&&shared.State(201)==-1&&shared.NeedsSnapshot(6001));
    driver.Enter();
    GCET_ASSERT(!driver.Tick(true,std::numeric_limits<float>::quiet_NaN(),0,0));
    GCET_ASSERT(driver.Status()==1);
    GCET_ASSERT(driver.Tick(true,200,0,0)&&shared.State(202)==1);
    driver.Exit();


    // Runtime source registration, source reload, tombstones, and dynamic wake.
    StaticIndex dynamic;
    GCET_ASSERT(dynamic.Load(std::vector<Anchor>{}));
    GCET_ASSERT(dynamic.Subscribe(8001,0) && dynamic.AckSnapshot(8001));
    GCET_ASSERT(dynamic.Update(0,0,0));
    GCET_ASSERT(dynamic.AddAnchor({801,0,0,0,20,false,88}));
    GCET_ASSERT(dynamic.Count()==1 && dynamic.State(801)==-1);
    GCET_ASSERT(dynamic.OwnerCount(88)==1 && dynamic.OwnerAnchor(88,0)==801);
    GCET_ASSERT(!dynamic.AddAnchor({801,0,0,0,20,false,88}));
    GCET_ASSERT(!dynamic.AddAnchor({802,0,0,0,-20,false,88}));
    GCET_ASSERT(dynamic.SetArmed(801,true));
    GCET_ASSERT(dynamic.Update(0,0,0) && dynamic.State(801)==1);
    TransitionJournal::Event dynEv{};
    GCET_ASSERT(dynamic.Poll(8001,dynEv)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(dynEv.id==801 && dynEv.state==1);
    GCET_ASSERT(dynamic.RemoveAnchor(801));
    GCET_ASSERT(dynamic.State(801)==-1 && !dynamic.SetArmed(801,true));
    GCET_ASSERT(dynamic.Poll(8001,dynEv)==TransitionJournal::Result::EventReady);
    GCET_ASSERT(dynEv.id==801 && dynEv.state==-1);
    GCET_ASSERT(!dynamic.AddAnchor({801,100,0,0,20,false,88}));
    GCET_ASSERT(dynamic.AddAnchor({803,100,0,0,20,false,88}));
    GCET_ASSERT(dynamic.SetArmed(803,true));
    GCET_ASSERT(dynamic.Update(100,0,0));
    GCET_ASSERT(dynamic.State(803)==1 && dynamic.State(801)==-1);
    GCET_ASSERT(dynamic.OwnerCount(88)==2); // tombstones retained until new payload
    GCET_ASSERT(dynamic.OwnerAnchor(88,0)==-1 && dynamic.OwnerAnchor(88,1)==803);
    GCET_ASSERT(dynamic.RemoveOwner(88) && dynamic.State(803)==-1);
    GCET_ASSERT(!dynamic.RemoveOwner(88));

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
