#include "PlayerSnapshot.hpp"
#include <cstdlib>
#include <iostream>
#include <limits>
using namespace gcet::static_locations;
#define CHECK(c) do{if(!(c)){std::cerr<<"FAIL "<<#c<<" line "<<__LINE__<<"\n";std::abort();}}while(0)
int main(){
 PlayerSnapshot s;
 CHECK(s.Status()==0&&!s.HasDemand());
 CHECK(!s.Subscribe(0,1)&&!s.Subscribe(1,999));
 CHECK(s.Subscribe(100,PlayerSnapshot::Fields));
 CHECK(s.Subscribe(200,PlayerSnapshot::Availability));
 CHECK(s.Subscribe(100,PlayerSnapshot::Fields));
 CHECK(s.SubscriberCount()==2);
 CHECK(s.Poll(100)==PlayerSnapshot::Snapshot);
 CHECK(s.Poll(200)==PlayerSnapshot::Snapshot);
 CHECK(s.Poll(100)==0);
 s.Enter();
 auto first=s.Epoch();
 CHECK(s.Status()==1&&first>0);
 CHECK((s.Poll(100)&PlayerSnapshot::Session)!=0);
 CHECK((s.Poll(200)&PlayerSnapshot::Session)!=0);
 s.Tick(false,0,0,0);
 CHECK(s.Status()==1&&s.Poll(100)==0);
 s.Tick(true,0,0,0);
 CHECK(s.Status()==2&&s.X()==0&&s.W()==1);
 float px=0,py=0,pz=0,pw=0;
 CHECK(s.ReadVector4(px,py,pz,pw)&&px==0&&py==0&&pz==0&&pw==1);
 CHECK(s.Poll(100)==PlayerSnapshot::Fields);
 CHECK(s.Poll(200)==PlayerSnapshot::Availability);
 auto rev=s.Revision();
 s.Tick(true,0,0,0);
 CHECK(s.Poll(100)==0&&s.Revision()==rev);
 s.Tick(true,2,3,4);
 CHECK(s.Poll(100)==PlayerSnapshot::Position&&s.Poll(200)==0);
 CHECK(s.X()==2&&s.Z()==4);
 // Only W changing must also invalidate the snapshot revision. No XYZ loss
 // or separate per-component samples are permitted.
 CHECK(s.ReadVector4(px,py,pz,pw)&&px==2&&py==3&&pz==4&&pw==1);
 auto beforeW=s.Revision();
 s.Tick(true,2,3,4,0.25f);
 CHECK(s.W()==0.25f&&s.Revision()==beforeW+1);
 CHECK(s.ReadVector4(px,py,pz,pw)&&pw==0.25f);
 CHECK(s.Poll(100)==PlayerSnapshot::Position);
 s.Tick(true,2,3,4,std::numeric_limits<float>::quiet_NaN());
 CHECK(s.Status()==1&&!s.ReadVector4(px,py,pz,pw));
 CHECK(s.Poll(100)==PlayerSnapshot::Availability);
 s.Tick(true,2,3,4,1);
 CHECK(s.Status()==2&&s.Poll(100)==PlayerSnapshot::Fields);
 CHECK(s.Poll(200)==PlayerSnapshot::Availability);
 s.Tick(true,3,3,4);s.Tick(true,4,3,4);
 CHECK((s.Poll(100)&PlayerSnapshot::Coalesced)!=0);
 CHECK(s.X()==4);
 s.Tick(false,0,0,0);
 CHECK(s.Status()==1&&s.Poll(100)==PlayerSnapshot::Availability);
 CHECK(s.Poll(200)==PlayerSnapshot::Availability);
 s.Tick(true,std::numeric_limits<float>::quiet_NaN(),0,0);
 CHECK(s.Status()==1&&s.Poll(100)==0);
 s.Tick(true,8,9,10);
 CHECK(s.Status()==2&&s.Poll(100)==PlayerSnapshot::Fields);
 s.Exit();
 CHECK(s.Status()==0&&s.Epoch()!=first);
 CHECK(!s.ReadVector4(px,py,pz,pw));
 CHECK((s.Poll(100)&PlayerSnapshot::Session)!=0);
 s.Enter();
 CHECK(s.Status()==1&&s.Poll(100)!=0);
 CHECK(s.Unsubscribe(200)&&!s.Unsubscribe(200));
 CHECK(s.Unsubscribe(100)&&!s.HasDemand());
 CHECK(s.Subscribe(300,PlayerSnapshot::Position));
 CHECK(s.Poll(300)==PlayerSnapshot::Snapshot);
 CHECK(s.Status()==1);
 s.Tick(true,20,30,40);
 CHECK((s.Poll(300)&PlayerSnapshot::Position)!=0);
 CHECK(s.Unsubscribe(300)&&!s.HasDemand());
 auto count=s.Samples();s.Tick(true,21,31,41);
 CHECK(s.Samples()==count);
 for(int i=1;i<=64;++i)CHECK(s.Subscribe(i,PlayerSnapshot::Position));
 CHECK(!s.Subscribe(65,PlayerSnapshot::Position));
 for(int i=1;i<=64;++i)CHECK(s.Unsubscribe(i));
 std::cout<<"PlayerSnapshot tests PASS\n";
}
