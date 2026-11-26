#include "NativeDiagnostics.hpp"
#include <cstdlib>
#include <string>
#include <vector>
using namespace gcet::diagnostics;
namespace {
std::vector<std::string> messages;
void Capture(const char* line){messages.emplace_back(line);}
void Require(bool ok){if(!ok)std::abort();}
}
int main(){
    Counters c;
    c.SetSink(Capture);
    for(int i=0;i<10000;++i){c.Record(Lane::Pose,Code::Attempt);c.Record(Lane::Pose,Code::CallerFullName);c.Record(Lane::Pose,Code::Unsupported);}
    Require(messages.size()==3);
    Require(c.Get(Lane::Pose,Code::Unsupported)==10000);
    c.Record(Lane::SharedPose,Code::Attempt);
    c.Record(Lane::SharedPose,Code::Complete);
    Require(messages.size()==5);
    Require(c.ClaimDetail(Lane::Pose,0));
    for(int i=0;i<10000;++i)Require(!c.ClaimDetail(Lane::Pose,0));
    Require(c.ClaimDetail(Lane::Pose,1)&&c.ClaimDetail(Lane::SharedPose,0));
    Require(!c.ClaimDetail(Lane::Pose,4));
    c.Detail("ABI test");
    Require(messages.back()=="ABI test");
    c.Summary("test");
    bool refused=false,zero=false,success=false;
    for(const auto& m:messages){
        refused|=m.find("lane=Pose code=CallerFullName count=10000")!=std::string::npos;
        zero|=m.find("lane=PositionEuler attempts=0")!=std::string::npos;
        success|=m.find("lane=SharedPose code=Complete count=1")!=std::string::npos;
    }
    Require(refused&&zero&&success);
    auto n=messages.size();c.SetSink(nullptr);c.Record(Lane::Board4Int,Code::Attempt);c.Summary("disabled");
    Require(messages.size()==n&&c.Get(Lane::Board4Int,Code::Attempt)==1);
}
