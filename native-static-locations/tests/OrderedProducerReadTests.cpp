#include "OrderedProducerRead.hpp"
#include "TypedProducerResult.hpp"
#include <cstring>
#include <stdexcept>
#include <array>
#include <cstdlib>
#include <iostream>
#include <vector>
using namespace gcet::producer;
static void Require(bool b,const char* why){if(!b){std::cerr<<why<<"\n";std::exit(1);}}
struct FakeType {
    size_t size=16,alignment=16;
    int constructs=0,destructs=0;
    std::vector<int> trace;
    size_t GetSize()const{return size;}
    size_t GetAlignment()const{return alignment;}
    void Construct(void* p){
        Require(reinterpret_cast<uintptr_t>(p)%16==0,"RTTI result alignment");
        ++constructs;trace.push_back(1);
        std::memset(p,0x31,size);
    }
    void Destruct(void*){
        ++destructs;trace.push_back(4);
    }
};
static void CheckTypedResult(){
    FakeType type;
    std::array<uint32_t,4> output{9,9,9,9};
    auto copy=[&](const void* p){
        type.trace.push_back(3);
        std::memcpy(output.data(),p,sizeof(output));return true;
    };
    auto invoke=[&](void* p){
        type.trace.push_back(2);
        Require(*static_cast<unsigned char*>(p)==0x31,"RTTI construct must precede invocation");
        std::array<uint32_t,4> value{1,2,3,0x12345678};
        std::memcpy(p,value.data(),sizeof(value));return true;
    };
    Require(ReadTypedResult<16>(&type,invoke,copy)&&output==std::array<uint32_t,4>{1,2,3,0x12345678},
            "typed result including fourth component");
    Require(type.trace==std::vector<int>{1,2,3,4}&&type.constructs==1&&type.destructs==1,
            "RTTI construction/copy/destruction order");
    type.trace.clear();output={9,9,9,9};
    Require(!ReadTypedResult<16>(&type,[&](void*){type.trace.push_back(2);return false;},copy)&&
            output==std::array<uint32_t,4>{9,9,9,9}&&type.trace==std::vector<int>{1,2,4},
            "failed call must destruct without copying");
    type.trace.clear();
    Require(!ReadTypedResult<16>(&type,invoke,[&](const void*){type.trace.push_back(3);return false;})&&
            type.trace==std::vector<int>{1,2,3,4},"decode failure cleanup");
    type.trace.clear();
    try{
        ReadTypedResult<16>(&type,[&](void*)->bool{type.trace.push_back(2);throw std::runtime_error("callback");},copy);
        Require(false,"expected exception");
    }catch(const std::runtime_error&){}
    Require(type.trace==std::vector<int>{1,2,4}&&type.constructs==type.destructs,"exception cleanup");
    type.trace.clear();type.size=17;
    Require(!ReadTypedResult<16>(&type,invoke,copy)&&type.trace.empty(),"size drift must refuse before construct");
    type.size=16;type.alignment=32;
    Require(!ReadTypedResult<16>(&type,invoke,copy)&&type.trace.empty(),"alignment drift must refuse before construct");
    type.alignment=3;
    Require(!ReadTypedResult<16>(&type,invoke,copy)&&type.trace.empty(),"invalid alignment must refuse");
    type.alignment=16;
    alignas(16) std::array<std::byte,32> storage{};
    FakeType foreign;
    Require(ValidOutStorage(storage.data(),&type,&type,16),"direct valid output");
    Require(!ValidOutStorage(storage.data(),&foreign,&type,16),"foreign same-size output");
    Require(!ValidOutStorage(storage.data()+1,&type,&type,16),"misaligned output");
    Require(!ValidOutStorage(storage.data(),static_cast<FakeType*>(nullptr),&type,16),"missing output type");
    Require(InspectOutStorage(storage.data(),&type,&type,16)==OutStorageStatus::Valid,"valid reason");
    Require(InspectOutStorage(nullptr,&type,&type,16)==OutStorageStatus::Null,"null reason");
    Require(InspectOutStorage(storage.data(),static_cast<FakeType*>(nullptr),&type,16)==OutStorageStatus::TypeMissing,"missing RTTI reason");
    Require(InspectOutStorage(storage.data(),&foreign,&type,16)==OutStorageStatus::TypeMismatch,"foreign RTTI reason");
    Require(InspectOutStorage(storage.data()+8,&type,&type,16)==OutStorageStatus::AddressMisaligned,"8-byte CET slot reason");
    type.size=12;
    Require(InspectOutStorage(storage.data(),&type,&type,16)==OutStorageStatus::SizeMismatch,"size reason");
    type.size=16;type.alignment=3;
    Require(InspectOutStorage(storage.data(),&type,&type,16)==OutStorageStatus::AlignmentInvalid,"alignment metadata reason");
    type.alignment=16;
    Require(InspectOutStorage(reinterpret_cast<void*>(UINTPTR_MAX-15),&type,&type,16)==OutStorageStatus::AddressOverflow,"address range reason");
    Require(DisjointOut(storage.data(),16,storage.data()+16,16),"adjacent output slots");
    Require(!DisjointOut(storage.data(),16,storage.data()+8,16),"overlapping outputs");
    std::cout<<"Typed RTTI result: lifecycle, failure, exception, layout and out-slot validation PASS\n";
}
int main(){
    CheckTypedResult();
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
