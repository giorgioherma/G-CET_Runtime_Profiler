#pragma once
#include <array>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstddef>

namespace gcet::diagnostics {
// Fixed-size process-lifetime counters. No heap, polling, locks or log writes
// on repeat observations. Only the first occurrence per lane/reason is logged.
// Summaries are cumulative (not per capture); session boundaries label them.
enum class Lane : size_t { Pose,SharedPose,PositionEuler,Board4Int,Board3IntBool,VectorFrame,VectorLive,VectorValid,Running,PlayerSubscribe,PlayerUnsubscribe,StaticArm,StaticArmCoarse,StaticSubscribe,StaticUnsubscribe,StaticAddAnchor,StaticRemoveAnchor,StaticRemoveOwner,StaticReady,StaticCoordinator,StaticPoll,StaticSnapshot,StaticAck,FrameNumber,PlayerSample, Count };
enum class Code : size_t { Attempt,Complete,Unsupported,Failed,NoResult,NoReceiver,NoContext,NoCaller,NoOutput,NoBindings,NoClass,WrongReceiverClass,CallerFullName,CallerShortName,CallerParams,CallerContextType,PositionStorage,OrientationStorage,EulerStorage,IntStorage,IntKeyStorage,BoolStorage,BoolKeyStorage,PositionOutput,OrientationOutput,YawOutput,EulerOutput,IntOutput,BoolOutput,OutputOverlap,PositionMethod,OrientationMethod,EulerMethod,IntMethod,BoolMethod,EpochChanged,PositionRead,OrientationRead,EulerRead,IntRead1,IntRead2,IntRead3,IntRead4,BoolRead,NoDemand,LocationDemand,SnapshotDemand,BothDemand,Unknown,Resync,Empty,Event,NoRtti,NoFrameClass,NoFrameMethod,InvokeFailed,NoPlayer,InvalidSample,NotReady,Registered,RegisterTypeMissing,RegisterCreateFailed,RegisterReturnFailed,RegisterParamFailed, Count };
inline constexpr const char* LaneNames[]={"Pose","SharedPose","PositionEuler","Board4Int","Board3IntBool","VectorFrame","VectorLive","VectorValid","Running","PlayerSubscribe","PlayerUnsubscribe","StaticArm","StaticArmCoarse","StaticSubscribe","StaticUnsubscribe","StaticAddAnchor","StaticRemoveAnchor","StaticRemoveOwner","StaticReady","StaticCoordinator","StaticPoll","StaticSnapshot","StaticAck","FrameNumber","PlayerSample"};
inline constexpr const char* CodeNames[]={"Attempt","Complete","Unsupported","Failed","NoResult","NoReceiver","NoContext","NoCaller","NoOutput","NoBindings","NoClass","WrongReceiverClass","CallerFullName","CallerShortName","CallerParams","CallerContextType","PositionStorage","OrientationStorage","EulerStorage","IntStorage","IntKeyStorage","BoolStorage","BoolKeyStorage","PositionOutput","OrientationOutput","YawOutput","EulerOutput","IntOutput","BoolOutput","OutputOverlap","PositionMethod","OrientationMethod","EulerMethod","IntMethod","BoolMethod","EpochChanged","PositionRead","OrientationRead","EulerRead","IntRead1","IntRead2","IntRead3","IntRead4","BoolRead","NoDemand","LocationDemand","SnapshotDemand","BothDemand","Unknown","Resync","Empty","Event","NoRtti","NoFrameClass","NoFrameMethod","InvokeFailed","NoPlayer","InvalidSample","NotReady","Registered","RegisterTypeMissing","RegisterCreateFailed","RegisterReturnFailed","RegisterParamFailed"};
static_assert(std::size(LaneNames)==static_cast<size_t>(Lane::Count));
static_assert(std::size(CodeNames)==static_cast<size_t>(Code::Count));
using Sink=void(*)(const char*);
class Counters {
    static constexpr size_t N=static_cast<size_t>(Lane::Count);
    static constexpr size_t M=static_cast<size_t>(Code::Count);
    std::array<std::array<std::atomic<uint64_t>,M>,N> values_{};
    std::atomic<Sink> sink_{nullptr};
public:
    void SetSink(Sink sink) noexcept {sink_.store(sink,std::memory_order_release);}
    uint64_t Get(Lane lane,Code code) const noexcept {
        return values_[static_cast<size_t>(lane)][static_cast<size_t>(code)].load(std::memory_order_relaxed);
    }
    void Record(Lane lane,Code code) noexcept {
        auto& cell=values_[static_cast<size_t>(lane)][static_cast<size_t>(code)];
        if(cell.fetch_add(1,std::memory_order_relaxed)!=0)return;
        auto sink=sink_.load(std::memory_order_acquire);
        if(!sink)return;
        char line[192];
        std::snprintf(line,sizeof(line),"G-NativeRuntime DIAG first lane=%s code=%s",
            LaneNames[static_cast<size_t>(lane)],CodeNames[static_cast<size_t>(code)]);
        sink(line);
    }
    void Summary(const char* boundary) const noexcept {
        auto sink=sink_.load(std::memory_order_acquire);
        if(!sink)return;
        char line[256];
        std::snprintf(line,sizeof(line),"G-NativeRuntime DIAG cumulative summary boundary=%s",boundary);
        sink(line);
        for(size_t i=0;i<N;++i){
            // Emit zero attempts explicitly for every family to distinguish
            // a never-entered native path from a path rejecting requests.
            std::snprintf(line,sizeof(line),"G-NativeRuntime DIAG lane=%s attempts=%llu",
                LaneNames[i],static_cast<unsigned long long>(Get(static_cast<Lane>(i),Code::Attempt)));
            sink(line);
            for(size_t j=1;j<M;++j){
                auto count=values_[i][j].load(std::memory_order_relaxed);
                if(!count)continue;
                std::snprintf(line,sizeof(line),"G-NativeRuntime DIAG lane=%s code=%s count=%llu",
                    LaneNames[i],CodeNames[j],static_cast<unsigned long long>(count));
                sink(line);
            }
        }
    }
};
inline Counters counters;
inline void Record(Lane l,Code c) noexcept {counters.Record(l,c);}
inline void Outcome(Lane l,bool ok) noexcept {Record(l,ok?Code::Complete:Code::Failed);}
}
