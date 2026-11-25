#include "OrderedProducerRead.hpp"
#include <array>
#include <cstdlib>
#include <iostream>
#include <vector>
using namespace gcet::producer;
static void Require(bool b,const char* why){if(!b){std::cerr<<why<<"\n";std::exit(1);}}
int main(){
    // Exact same helper invoked by the plugin's actual pose and PSM
    // handlers. Each failure position must stop at that read, never publish
    // partial output or invoke a reader on admission refusal.
    using Values=std::array<int,4>;
    for(int fail=-1;fail<=4;++fail){
        Values value{9,9,9,9};std::vector<int> trace;
        auto read=[&](int i){return [&,i](Values& target){
            trace.push_back(i);target[i]=i+1;return fail!=i;
        };};
        auto r=ReadOrdered(value,[&]{return fail!=-1;},[]{return true;},read(0),read(1),read(2),read(3));
        if(fail==-1){
            Require(r==ReadResult::Unsupported&&trace.empty(),"preflight executed a getter");
        }else if(fail<4){
            Require(r==ReadResult::Failed&&trace.size()==static_cast<size_t>(fail+1),"failed prefix replay or continuation");
        }else{
            Require(r==ReadResult::Complete&&value==Values{1,2,3,4},"complete publication incorrect");
        }
        if(fail<4)Require(value==Values{9,9,9,9},"partial output published");
        for(size_t i=0;i<trace.size();++i)Require(trace[i]==static_cast<int>(i),"read order changed");
    }
    // Model invalidation from a callback inside any native getter,
    // including the final read: never replay or publish an invalid epoch.
    for(int invalidate=-1;invalidate<=4;++invalidate){
        Values value{9,9,9,9};std::vector<int> trace;
        unsigned generation=invalidate==-1?2u:1u;
        auto read=[&](int i){return [&,i](Values& target){
            trace.push_back(i);target[i]=i+1;
            if(invalidate==i)++generation;
            return true;
        };};
        auto r=ReadOrdered(value,[]{return true;},[&]{return generation==1;},
                           read(0),read(1),read(2),read(3));
        if(invalidate==-1)
            Require(r==ReadResult::Unsupported&&trace.empty(),"invalid entry epoch admitted");
        else if(invalidate<4)
            Require(r==ReadResult::Failed&&trace.size()==static_cast<size_t>(invalidate+1),
                    "invocation after reentrant invalidation or replayed prefix");
        else
            Require(r==ReadResult::Complete&&value==Values{1,2,3,4},"valid epoch publication");
        if(invalidate<4)Require(value==Values{9,9,9,9},"invalid epoch output published");
    }
    std::cout<<"Native producer read: preflight, exact order, each failure boundary, reentrant invalidation and atomic publication PASS\n";
}
