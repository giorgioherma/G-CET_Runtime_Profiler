#pragma once

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>
#include <windows.h>

// ABI shared with the embedded, statically linked LuaJIT collector probe.
extern "C" {
struct GCETGCStats
{
    uint64_t stepCalls, stepTicks, maxStepTicks, completedCycles;
    uint64_t fullCalls, fullTicks, maxFullTicks, frequency;
};
void gcet_gc_stats_set(int enable, int reset);
void gcet_gc_stats_read(GCETGCStats* out);
struct GCETGCSlowEvent {
    uint64_t startNs, endNs, threadId, registrationId;
    uint64_t heapBefore, heapAfter, threshold, debt;
    uint32_t phaseBefore, phaseAfter, pause, stepmul;
};
struct GCETGCPhaseStats { uint64_t calls[6], ticks[6]; };
void gcet_gc_trace_segment(uint64_t captureOffsetNs);
uint64_t gcet_gc_scope_set(uint64_t registration);
uint64_t gcet_gc_alloc_bytes();
struct GGCTraceOwner {
 uint64_t bytes, allocations, objects, strings;
 uint64_t samples, collisions, freedSamples, freedBytes;
 uint64_t lifetimeTicks, finalizedSamples;
 uint64_t freedTables, freedStrings, freedUserdata, freedCdata, freedOther;
};
uint64_t gcet_gc_alloc_ops();
void ggc_trace_owner_read(uint64_t, GGCTraceOwner*);
void ggc_trace_focus_set(uint64_t, int);
int ggc_trace_focused(uint64_t);
uint64_t ggc_trace_finalizer_calls();
int gcet_gc_slow_read(GCETGCSlowEvent* out, int capacity, int* dropped);
void gcet_gc_phase_read(GCETGCPhaseStats* out);
struct GGCUnscopedContext { uint64_t bytes, operations; };
struct GGCUnscopedSecond { GGCUnscopedContext states[8]; };
void ggc_trace_unscoped_context_read(GGCUnscopedContext*, int capacity);
int ggc_trace_unscoped_seconds_read(GGCUnscopedSecond*, int capacity);
struct GGCUnscopedDimension { uint64_t bytes, operations; };
struct GGCUnscopedDetail {
    GGCUnscopedDimension morphology[2][6];
    GGCUnscopedDimension reallocDeltas[2][6];
    GGCUnscopedDimension reallocNewSizes[2][6];
    GGCUnscopedDimension bridges[6];
};
struct GGCUnscopedPathSecond {
    GGCUnscopedDimension paths[2];
    GGCUnscopedDimension reallocOrigins[2];
    GGCUnscopedDimension bridges[6];
};
struct GGCUnscopedNativeSample {
    uint64_t captureMs, positiveBytes, operationSequence;
    uint32_t vmState, allocatorPath, bridgeContext, depth, reallocOrigin;
    uint64_t oldSize, newSize;
    void* frames[6];
};
uint32_t ggc_trace_bridge_set(uint32_t context);
void ggc_trace_unscoped_detail_read(GGCUnscopedDetail*);
int ggc_trace_unscoped_path_seconds_read(GGCUnscopedPathSecond*, int);
int ggc_trace_unscoped_samples_read(GGCUnscopedNativeSample*, int, uint64_t*);

}

class CETRuntimeProfiler
{
public:
    struct TimelineMod;
    struct TimelineCallback;

    struct Counter
    {
        uint64_t RegistrationId{};
        std::string Mod;
        std::string Kind;
        std::string Target;
        std::string SourceFile;
        int SourceLineStart{};
        int SourceLineEnd{};
        std::atomic<uint64_t> LuaFunctionIdentity{0};

        std::atomic<uint64_t> Calls{0};
        std::atomic<uint64_t> InclusiveNs{0};
        std::atomic<uint64_t> ExclusiveNs{0};
        std::atomic<uint64_t> MaxInclusiveNs{0};
        std::atomic<uint64_t> MaxExclusiveNs{0};
        std::atomic<uint64_t> GrossAllocatedBytes{0};
        std::atomic<bool> GcTracerFocused{false};
        TimelineMod* TimelineOwner{};

        // Exact rendered-frame multiplicity, rotated lazily per callback.
        std::atomic<uint64_t> FrameCurrent{0};
        std::atomic<uint64_t> FrameCurrentCalls{0};
        std::atomic<uint64_t> FrameRecordedCalls{0};
        std::atomic<uint64_t> FrameActiveFrames{0};
        std::atomic<uint64_t> FrameOneCallFrames{0};
        std::atomic<uint64_t> FrameTwoCallFrames{0};
        std::atomic<uint64_t> FrameThreeCallFrames{0};
        std::atomic<uint64_t> FrameFourCallFrames{0};
        std::atomic<uint64_t> FrameFivePlusCallFrames{0};
        std::atomic<uint64_t> FrameMaxCalls{0};
        std::atomic_flag FrameRotateLock = ATOMIC_FLAG_INIT;

        // Adaptive deep-profiler state. Broad timing remains authoritative;
        // these fields only control sampled call/return dissection.
        std::atomic<bool> DeepSelected{false};
        std::atomic<bool> DeepComplete{false};
        std::atomic<uint32_t> DeepProfileEpoch{1};
        std::atomic<uint64_t> DeepSamples{0};
        std::atomic<uint64_t> DeepTargetSamples{0};
        std::atomic<uint64_t> DeepSampleStride{1};
        std::atomic<uint64_t> DeepSampleTicker{0};
        std::atomic<uint64_t> DeepHookConflicts{0};
        std::atomic<uint64_t> DeepReuseFromRegistrationId{0};
        std::atomic<uint64_t> DeepEpochStartCalls{0};
        std::atomic<uint64_t> DeepEpochStartExclusiveNs{0};
        std::atomic<uint64_t> DeepBaselineAvgExclusiveNs{0};
        std::atomic<uint64_t> DeepLastWindowCalls{0};
        std::atomic<uint64_t> DeepLastWindowExclusiveNs{0};

        // Completed hot paths stay cheaply spike-armed. Behavior epochs only
        // reopen after sustained broad-runtime drift, not a single noisy window.
        std::atomic<bool> DeepSpikeArmed{false};
        std::atomic<uint64_t> DeepSpikeProbeStride{251};
        std::atomic<uint64_t> DeepSpikeProbeTicker{0};
        std::atomic<uint64_t> DeepSpikeProbeSamples{0};
        std::atomic<uint64_t> DeepSpikeCaptures{0};
        std::atomic<uint32_t> DeepDriftWindows{0};
        std::atomic<int32_t> DeepDriftDirection{0};

        // Heap consequence telemetry is sampled only when adaptive deep
        // profiling already selected this callback. It never changes GC state.
        std::atomic<uint64_t> DeepHeapSamples{0};
        std::atomic<uint64_t> DeepHeapPositiveDeltaBytes{0};
        std::atomic<uint64_t> DeepHeapReclaimedDeltaBytes{0};
        std::atomic<int64_t> DeepHeapNetDeltaBytes{0};
        std::atomic<uint64_t> DeepHeapMaxPositiveDeltaBytes{0};
        std::atomic<uint64_t> DeepHeapMaxReclaimedDeltaBytes{0};
    };

    struct TimelineMod
    {
        std::string Mod;
        std::atomic<uint64_t> CurrentBucket{UINT64_MAX};
        std::atomic<uint64_t> Calls{0};
        std::atomic<uint64_t> ExclusiveNs{0};
        std::atomic<uint64_t> MaxExclusiveNs{0};
        std::atomic_flag RotateLock = ATOMIC_FLAG_INIT;
    };

    // FunctionOverride::Context is deliberately left byte-for-byte upstream.
    // Callback metadata is keyed externally by the Context address instead.
    struct CallbackBinding
    {
        std::string Kind;
        std::string Target;
        Counter* ResolvedCounter{};
    };

    struct SpikeEvent
    {
        Counter* CounterPtr{};
        uint64_t Sequence{};
        uint64_t Frame{};
        uint64_t CaptureStartNs{};
        uint64_t CaptureEndNs{};
        uint64_t InclusiveNs{};
        uint64_t ExclusiveNs{};
        uint64_t ChildNs{};
        uint64_t ThreadId{};
    };

    struct TimelineEvent
    {
        TimelineMod* ModPtr{};
        uint64_t Bucket{};
        uint64_t Calls{};
        uint64_t ExclusiveNs{};
        uint64_t MaxExclusiveNs{};
    };

    struct GcCollectorFrame
    {
        uint64_t Frame{}, StartNs{}, CaptureNs{}, IncrementalCalls{}, CompletedCycles{};
        uint64_t FullCalls{};
        double IncrementalMs{}, FullMs{}, MaxIncrementalMs{}, MaxFullMs{};
    };

    struct GcExplicitEvent
    {
        uint64_t Sequence{}, Frame{}, StartNs{}, EndNs{}, DurationNs{};
        uint64_t BeforeBytes{}, AfterBytes{};
        std::string Action, SourceFile;
        int SourceLine{};
    };

    struct MarkerEvent
    {
        uint64_t Sequence{};
        uint64_t CaptureNs{};
        int64_t UnixEpochMs{};
        std::string Label;
    };

    struct SchedulerJobCounter
    {
        std::string Owner;
        std::string JobType;
        std::string Job;
        std::string IntervalUnit;
        double IntervalValue{};

        std::atomic<uint64_t> Calls{0};
        std::atomic<uint64_t> TotalNs{0};
        std::atomic<uint64_t> MaxNs{0};
    };

    struct SchedulerJobSample
    {
        SchedulerJobCounter* CounterPtr{};
        uint64_t DurationNs{};
    };

    struct SchedulerSpikeEvent
    {
        SchedulerJobCounter* CounterPtr{};
        uint64_t Sequence{};
        uint64_t Frame{};
        uint64_t CaptureStartNs{};
        uint64_t CaptureEndNs{};
        uint64_t DurationNs{};
    };

    struct SchedulerFrameBurstEvent
    {
        uint64_t Sequence{};
        uint64_t Frame{};
        uint64_t CaptureStartNs{};
        uint64_t CaptureEndNs{};
        uint64_t SchedulerWallNs{};
        uint64_t TotalJobNs{};
        std::vector<SchedulerJobSample> Jobs;
    };

    struct SchedulerFrameState
    {
        bool Active{false};
        uint64_t Frame{};
        std::chrono::steady_clock::time_point Start{};
        uint64_t TotalJobNs{};
        std::vector<SchedulerJobSample> Jobs;
    };

    enum class DeepSampleMode : uint8_t
    {
        Hotset,
        SpikeProbe
    };

    struct DeepFunctionAggregate
    {
        uint64_t RegistrationId{};
        uint32_t ProfileEpoch{};
        uint64_t FunctionIdentity{};
        std::string FunctionKey;
        std::string FunctionName;
        std::string What;
        std::string SourceFile;
        int SourceLineStart{};
        int SourceLineEnd{};
        uint32_t MinDepth{UINT32_MAX};
        uint64_t Calls{};
        uint64_t InclusiveNs{};
        uint64_t ExclusiveNs{};
        uint64_t MaxInclusiveNs{};
        uint64_t SampledExclusiveAllocBytes{};
        uint64_t SampledExclusiveAllocations{};
    };

    struct DeepEdgeAggregate
    {
        uint64_t RegistrationId{};
        uint32_t ProfileEpoch{};
        std::string ParentFunctionKey;
        std::string ChildFunctionKey;
        uint64_t Calls{};
        uint64_t ChildInclusiveNs{};
    };

    struct DeepCallsiteAggregate
    {
        uint64_t SampleSequence{};
        uint64_t RegistrationId{};
        uint32_t ProfileEpoch{};
        uint64_t Frame{};
        uint64_t FrameInvocationOrdinal{};
        std::string CallerSourceFile;
        int CallerLine{};
        std::string ParentFunctionKey;
        std::string ChildFunctionKey;
        uint64_t Calls{};
        uint64_t ChildInclusiveNs{};
    };

    // Experimental: scalar values are supplied by explicitly instrumented
    // existing getter callsites. Never dereference native handles or tables.
    // Only labels, not observed values, are exported from a capture.
    struct ScalarObservation
    {
        uint64_t Observations{};
        uint64_t Unchanged{};
        uint64_t Changed{};
        std::string LastValue;
    };

    struct DeepFrame
    {
        uint64_t FunctionIdentity{};
        uint64_t SourceHash{};
        std::string FunctionKey;
        std::string FunctionName;
        std::string What;
        std::string SourceFile;
        int SourceLineStart{};
        int SourceLineEnd{};
        uint32_t Depth{};
        std::string ParentFunctionKey;
        std::string CallsiteSourceFile;
        int CallsiteLine{};
        std::chrono::steady_clock::time_point Start{};
        uint64_t ChildRawNs{};
        uint64_t ProfilerNs{};
        uint64_t AllocStartBytes{};
        uint64_t AllocStartOps{};
        uint64_t ChildAllocBytes{};
        uint64_t ChildAllocOps{};
    };

    struct DeepLineHit
    {
        std::string SourceFile;
        int Line{};
        uint64_t Hits{};
    };

    struct DeepSampleEvent
    {
        uint64_t Sequence{};
        uint64_t RegistrationId{};
        uint32_t ProfileEpoch{};
        uint64_t Frame{};
        uint64_t FrameInvocationOrdinal{};
        uint64_t CaptureStartNs{};
        uint64_t CaptureEndNs{};
        std::string Mode;
        uint64_t ApproxOwnWallNs{};
        uint64_t HookEvents{};
        uint64_t LineEvents{};
        uint64_t UnresolvedLineEvents{};
        uint64_t UniqueLines{};
        uint64_t PathTransitions{};
        uint64_t PathFingerprint{};
        uint64_t NestedRegistrationCount{};
        uint64_t NestedRegistrationNs{};
        uint64_t LuaHeapBeforeBytes{};
        uint64_t LuaHeapAfterBytes{};
        int64_t LuaHeapDeltaBytes{};
        bool LineRowsTruncated{};
    };

    struct DeepLineEvent
    {
        uint64_t SampleSequence{};
        uint64_t RegistrationId{};
        uint32_t ProfileEpoch{};
        std::string SourceFile;
        int Line{};
        uint64_t Hits{};
    };

    struct DeepThreadState
    {
        bool Active{false};
        Counter* CounterPtr{};
        uint32_t ProfileEpoch{};
        DeepSampleMode Mode{DeepSampleMode::Hotset};
        uint64_t SampleSequence{};
        uint64_t Frame{};
        uint64_t FrameInvocationOrdinal{};
        uint64_t SampleStartCaptureNs{};
        std::chrono::steady_clock::time_point SampleStartWall{};
        lua_State* State{};
        uint64_t LuaHeapBeforeBytes{};
        bool LuaHeapSampleValid{false};
        lua_Hook PreviousHook{};
        int PreviousMask{};
        int PreviousCount{};
        uint64_t RootNetNs{};
        uint64_t HookEvents{};
        uint64_t LineEvents{};
        uint64_t UnresolvedLineEvents{};
        uint64_t PathTransitions{};
        uint64_t PathFingerprint{1469598103934665603ULL};
        uint64_t LastPathToken{UINT64_MAX};
        bool LineRowsTruncated{false};
        uint32_t NestedRegistrationDepth{};
        std::chrono::steady_clock::time_point NestedBoundaryStart{};
        uint64_t NestedBoundaryAllocBytes{};
        uint64_t NestedBoundaryAllocOps{};
        uint64_t NestedRegistrationCount{};
        uint64_t NestedRegistrationNs{};
        std::vector<DeepFrame> Frames;
        std::unordered_map<std::string, DeepFunctionAggregate> Functions;
        std::unordered_map<std::string, DeepEdgeAggregate> Edges;
        std::unordered_map<std::string, DeepCallsiteAggregate> Callsites;
        std::unordered_map<std::string, DeepLineHit> Lines;
    };

    using Clock = std::chrono::steady_clock;

    using Clock = std::chrono::steady_clock;

    static constexpr uint64_t DefaultSpikeThresholdNs = 5'000'000;
    static constexpr uint64_t DefaultTimelineBucketNs = 50'000'000;
    static constexpr uint64_t DefaultSchedulerJobSpikeThresholdNs = 2'000'000;
    static constexpr uint64_t DefaultSchedulerFrameBurstThresholdNs = 5'000'000;
    static constexpr size_t MaxSpikeEvents = 20'000;
    static constexpr size_t MaxTimelineEvents = 1'000'000;
    static constexpr size_t MaxMarkerEvents = 1'000;
    static constexpr size_t MaxGcExplicitEvents = 4'096;
    static constexpr size_t MaxGcCollectorFrames = 40'000;
    static constexpr size_t MaxSchedulerSpikeEvents = 20'000;
    static constexpr size_t MaxSchedulerFrameBurstEvents = 20'000;

    // Adaptive deep profiling intentionally stays small. Broad profiling
    // chooses the targets; deep tracing samples only enough future invocations
    // to explain the hot callback without turning the whole Lua VM into a trace.
    static constexpr uint64_t DeepWarmupNs = 2'000'000'000ULL;
    static constexpr uint64_t GcTracerWarmupNs = 5'000'000'000ULL;
    static constexpr uint64_t GcTracerRebalanceNs = 5'000'000'000ULL;
    static constexpr uint64_t DeepRebalanceNs = 1'000'000'000ULL;
    static constexpr size_t MaxDeepHotRegistrations = 6;
    static constexpr uint64_t DefaultDeepTargetSamples = 24;
    static constexpr uint64_t ReusedDeepTargetSamples = 6;
    static constexpr uint32_t DeepDriftWindowsRequired = 3;
    static constexpr size_t MaxDeepSampleEvents = 20'000;
    static constexpr size_t MaxDeepLineEvents = 500'000;
    static constexpr size_t MaxDeepCallsiteEvents = 250'000;
    static constexpr size_t MaxDeepUniqueLinesPerSample = 2'048;
    static constexpr uint64_t MaxDeepPathTransitions = 4'096;

    enum class CaptureState : uint8_t
    {
        Paused,
        Running
    };

    // Exact native CET entrypoint context (not source ownership).
    // Only active captures change TLS; no call into Lua, no new getters.
    class BridgeScope {
    public:
        BridgeScope(uint32_t context, bool active) : m_active(active) {
            if (active) m_old=ggc_trace_bridge_set(context);
        }
        ~BridgeScope() { if (m_active) ggc_trace_bridge_set(m_old); }
        BridgeScope(const BridgeScope&)=delete;
        BridgeScope& operator=(const BridgeScope&)=delete;
    private:
        bool m_active{};
        uint32_t m_old{};
    };

    // Scope tracks both inclusive and exclusive callback time.
    //
    // If callback A invokes callback B synchronously on the same thread,
    // B's elapsed time is subtracted from A's exclusive time. This prevents
    // nested Observe/Override/event callbacks from being counted twice in
    // the per-mod CPU-pressure ranking.
    class Scope
    {
    public:
        explicit Scope(Counter* aCounter)
            : m_counter(aCounter)
            , m_active(CETRuntimeProfiler::Get().IsCapturing())
        {
            if (!m_active)
                return;

            m_parent = s_current;
            m_allocStart = gcet_gc_alloc_bytes();
            m_priorRegistration = gcet_gc_scope_set(m_counter ? m_counter->RegistrationId : 0);
            m_start = Clock::now();
            s_current = this;
        }

        Scope(const Scope&) = delete;
        Scope& operator=(const Scope&) = delete;

        ~Scope()
        {
            if (!m_active)
                return;

            const auto end = Clock::now();
            const auto elapsed = static_cast<uint64_t>(
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    end - m_start).count());

            const uint64_t exclusive =
                elapsed > m_childNs ? elapsed - m_childNs : 0;
            const uint64_t inclusiveAlloc = gcet_gc_alloc_bytes() - m_allocStart;
            const uint64_t ownAlloc = inclusiveAlloc > m_childAllocated
                ? inclusiveAlloc - m_childAllocated : 0;
            if (m_parent) m_parent->m_childAllocated += inclusiveAlloc;
            gcet_gc_scope_set(m_priorRegistration);
            if (m_counter) m_counter->GrossAllocatedBytes.fetch_add(
                ownAlloc, std::memory_order_relaxed);

            if (m_counter)
            {
                auto& profiler = CETRuntimeProfiler::Get();
                CETRuntimeProfiler::Record(m_counter, elapsed, exclusive);

                const auto frameBookkeepingStart = Clock::now();
                profiler.RecordFrameMultiplicity(m_counter);
                const uint64_t frameBookkeepingNs = static_cast<uint64_t>(
                    std::max<int64_t>(
                        0,
                        std::chrono::duration_cast<std::chrono::nanoseconds>(
                            Clock::now() - frameBookkeepingStart).count()));

                // Continuous per-mod timeline accumulation. The common path
                // is only relaxed atomics. A vector/mutex flush occurs at most
                // once per active mod per timeline bucket. Any rollover
                // bookkeeping is excluded from a synchronous parent callback.
                const uint64_t timelineBookkeepingNs =
                    profiler.RecordTimeline(m_counter, end, elapsed, exclusive);

                // The threshold test is just one relaxed atomic load on the
                // hot path. Mutex/string work happens only for exceptional
                // callbacks that cross the configured spike threshold.
                uint64_t spikeBookkeepingNs = 0;
                if (profiler.ShouldRecordSpike(exclusive))
                {
                    const auto bookkeepingStart = Clock::now();
                    profiler.RecordSpike(
                        m_counter, end, elapsed, exclusive, m_childNs);
                    spikeBookkeepingNs = static_cast<uint64_t>(
                        std::chrono::duration_cast<std::chrono::nanoseconds>(
                            Clock::now() - bookkeepingStart).count());
                }

                // If this is a nested callback, exclude the profiler's own
                // exceptional-event bookkeeping from the parent's exclusive
                // attribution. Otherwise a spike-row mutex/store could create
                // a false parent spike.
                if (m_parent)
                    m_parent->m_childNs +=
                        elapsed + frameBookkeepingNs +
                        timelineBookkeepingNs + spikeBookkeepingNs;
            }
            else if (m_parent)
            {
                m_parent->m_childNs += elapsed;
            }

            s_current = m_parent;
        }

    private:
        Counter* m_counter{};
        bool m_active{false};
        Scope* m_parent{};
        Clock::time_point m_start{};
        uint64_t m_childNs{0};
        uint64_t m_childAllocated{0};
        uint64_t m_allocStart{0};
        uint64_t m_priorRegistration{0};

        inline static thread_local Scope* s_current = nullptr;

    public:
        static void ExcludeProfilerOverhead(uint64_t aNs)
        {
            if (s_current)
                s_current->m_childNs += aNs;
        }
    };

    class DeepTraceScope
    {
    public:
        DeepTraceScope(Counter* aCounter, lua_State* aState)
            : m_state(aState)
        {
            auto& profiler = CETRuntimeProfiler::Get();

            // A nested profiled registration is a structural boundary, not part
            // of the parent's Lua body. Temporarily silence the parent's hook
            // and subtract that nested registration from the parent's deep tree.
            m_boundary = profiler.EnterDeepRegistrationBoundary(aCounter, aState);
            if (m_boundary)
                return;

            auto& probe = DeepProbeCounterForThread();
            ++probe;
            if (probe <= 8 || (probe & 0x1FFu) == 0) {
                profiler.MaybeRebalanceDeepHotset();
                profiler.MaybeRebalanceGcTracer();
            }

            m_active = profiler.BeginDeepSample(aCounter, aState);
        }

        DeepTraceScope(const DeepTraceScope&) = delete;
        DeepTraceScope& operator=(const DeepTraceScope&) = delete;

        ~DeepTraceScope()
        {
            if (m_active)
                CETRuntimeProfiler::Get().EndDeepSample(m_state);
            if (m_boundary)
                CETRuntimeProfiler::Get().ExitDeepRegistrationBoundary(m_state);
        }

    private:
        static uint32_t& DeepProbeCounterForThread()
        {
            static thread_local uint32_t value = 0;
            return value;
        }

        lua_State* m_state{};
        bool m_active{false};
        bool m_boundary{false};
    };

    static CETRuntimeProfiler& Get()
    {
        static CETRuntimeProfiler instance;
        return instance;
    }

    void Configure(const std::filesystem::path& aCETRoot)
    {
        std::lock_guard lock(m_mutex);
        m_outputRoot = aCETRoot;
    }

    Counter* Register(const std::string& aMod,
                      const std::string& aKind,
                      const std::string& aTarget)
    {
        std::lock_guard lock(m_mutex);
        return RegisterLocked(aMod, aKind, aTarget);
    }

    void AttachSource(Counter* aCounter,
                      const std::string& aSourceFile,
                      int aSourceLineStart,
                      int aSourceLineEnd,
                      uint64_t aFunctionIdentity = 0)
    {
        if (!aCounter)
            return;

        std::lock_guard lock(m_mutex);
        if (!aSourceFile.empty())
            aCounter->SourceFile = aSourceFile;
        if (aSourceLineStart > 0)
            aCounter->SourceLineStart = aSourceLineStart;
        if (aSourceLineEnd > 0)
            aCounter->SourceLineEnd = aSourceLineEnd;
        if (aFunctionIdentity)
            aCounter->LuaFunctionIdentity.store(
                aFunctionIdentity, std::memory_order_relaxed);
    }

    bool EnterDeepRegistrationBoundary(Counter* aCounter, lua_State* aState)
    {
        auto& state = DeepStateForThread();
        if (!state.Active || !aCounter || !aState || state.State != aState ||
            state.CounterPtr == aCounter)
        {
            return false;
        }

        const auto bookkeepingStart = Clock::now();
        if (state.NestedRegistrationDepth++ == 0)
        {
            state.NestedBoundaryStart = bookkeepingStart;
            state.NestedBoundaryAllocBytes = gcet_gc_alloc_bytes();
            state.NestedBoundaryAllocOps = gcet_gc_alloc_ops();
            lua_sethook(aState, nullptr, 0, 0);
        }
        ++state.NestedRegistrationCount;

        Scope::ExcludeProfilerOverhead(static_cast<uint64_t>(
            std::max<int64_t>(
                0,
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    Clock::now() - bookkeepingStart).count())));
        return true;
    }

    void ExitDeepRegistrationBoundary(lua_State* aState)
    {
        auto& state = DeepStateForThread();
        if (!state.Active || !aState || state.State != aState ||
            state.NestedRegistrationDepth == 0)
        {
            return;
        }

        const auto boundaryEnd = Clock::now();
        if (--state.NestedRegistrationDepth != 0)
            return;

        const uint64_t nestedNs = static_cast<uint64_t>(
            std::max<int64_t>(
                0,
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    boundaryEnd - state.NestedBoundaryStart).count()));
        state.NestedRegistrationNs += nestedNs;
        if (!state.Frames.empty()) {
            state.Frames.back().ChildRawNs += nestedNs;
            state.Frames.back().ChildAllocBytes +=
                gcet_gc_alloc_bytes() - state.NestedBoundaryAllocBytes;
            state.Frames.back().ChildAllocOps +=
                gcet_gc_alloc_ops() - state.NestedBoundaryAllocOps;
        }

        const auto bookkeepingStart = Clock::now();
        lua_sethook(
            aState,
            &CETRuntimeProfiler::DeepLuaHook,
            LUA_MASKCALL | LUA_MASKRET | LUA_MASKLINE,
            0);
        Scope::ExcludeProfilerOverhead(static_cast<uint64_t>(
            std::max<int64_t>(
                0,
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    Clock::now() - bookkeepingStart).count())));
    }

    static uint64_t ReadLuaHeapBytes(lua_State* aState)
    {
        if (!aState)
            return 0;

        // Lua 5.1/LuaJIT exposes heap usage as whole KiB plus a byte
        // remainder. These are observation-only queries; they do not request
        // or advance collection.
        const int kib = lua_gc(aState, LUA_GCCOUNT, 0);
        const int rem = lua_gc(aState, LUA_GCCOUNTB, 0);
        if (kib < 0 || rem < 0)
            return 0;

        return static_cast<uint64_t>(kib) * 1024ULL +
               static_cast<uint64_t>(rem);
    }

    bool BeginDeepSample(Counter* aCounter, lua_State* aState)
    {
        if (!aCounter || !aState || !IsCapturing())
            return false;

        const bool complete =
            aCounter->DeepComplete.load(std::memory_order_acquire);
        const bool hotset = !complete &&
           (aCounter->DeepSelected.load(std::memory_order_acquire) ||
            aCounter->GcTracerFocused.load(std::memory_order_acquire));
        const bool spikeProbe =
            complete && aCounter->DeepSpikeArmed.load(std::memory_order_acquire);

        if (!hotset && !spikeProbe)
            return false;

        auto& threadState = DeepStateForThread();
        if (threadState.Active)
            return false;

        // Never steal a debugger/third-party Lua hook. Broad profiling remains
        // active even when deep evidence cannot be collected.
        if (lua_gethook(aState) != nullptr)
        {
            aCounter->DeepHookConflicts.fetch_add(1, std::memory_order_relaxed);
            return false;
        }

        uint64_t stride = 1;
        uint64_t sampleTick = 0;

        if (spikeProbe)
        {
            stride = std::max<uint64_t>(
                1, aCounter->DeepSpikeProbeStride.load(std::memory_order_relaxed));
            sampleTick =
                aCounter->DeepSpikeProbeTicker.fetch_add(1, std::memory_order_relaxed);
        }
        else
        {
            stride = std::max<uint64_t>(
                1, aCounter->DeepSampleStride.load(std::memory_order_relaxed));
            sampleTick =
                aCounter->DeepSampleTicker.fetch_add(1, std::memory_order_relaxed);

            const uint64_t target = std::max<uint64_t>(
                1, aCounter->DeepTargetSamples.load(std::memory_order_relaxed));
            if (aCounter->DeepSamples.load(std::memory_order_relaxed) >= target)
                return false;
        }

        if (((sampleTick + aCounter->RegistrationId) % stride) != 0)
            return false;

        if (hotset &&
            aCounter->DeepSamples.load(std::memory_order_relaxed) == 0 &&
            aCounter->DeepEpochStartCalls.load(std::memory_order_relaxed) == 0)
        {
            aCounter->DeepEpochStartCalls.store(
                aCounter->Calls.load(std::memory_order_relaxed),
                std::memory_order_relaxed);
            aCounter->DeepEpochStartExclusiveNs.store(
                aCounter->ExclusiveNs.load(std::memory_order_relaxed),
                std::memory_order_relaxed);
        }

        const auto now = Clock::now();
        threadState = {};
        threadState.Active = true;
        threadState.CounterPtr = aCounter;
        threadState.ProfileEpoch =
            aCounter->DeepProfileEpoch.load(std::memory_order_relaxed);
        threadState.Mode =
            spikeProbe ? DeepSampleMode::SpikeProbe : DeepSampleMode::Hotset;
        threadState.SampleSequence =
            m_nextDeepSampleSequence.fetch_add(1, std::memory_order_relaxed) + 1;
        threadState.Frame =
            m_currentGameFrame.load(std::memory_order_acquire);
        if (threadState.Frame > 0 && aCounter)
        {
            const uint64_t current =
                aCounter->FrameCurrent.load(std::memory_order_relaxed);
            const uint64_t completedInFrame =
                current == threadState.Frame
                    ? aCounter->FrameCurrentCalls.load(std::memory_order_relaxed)
                    : 0;
            threadState.FrameInvocationOrdinal = completedInFrame + 1;
        }
        threadState.SampleStartWall = now;
        threadState.SampleStartCaptureNs = FastCapturedNanoseconds(now);
        threadState.State = aState;
        threadState.LuaHeapBeforeBytes = ReadLuaHeapBytes(aState);
        threadState.LuaHeapSampleValid = threadState.LuaHeapBeforeBytes > 0;
        threadState.PreviousHook = lua_gethook(aState);
        threadState.PreviousMask = lua_gethookmask(aState);
        threadState.PreviousCount = lua_gethookcount(aState);
        threadState.Frames.reserve(32);
        threadState.Functions.reserve(64);
        threadState.Edges.reserve(96);
        threadState.Callsites.reserve(128);
        threadState.Lines.reserve(128);

        lua_sethook(
            aState,
            &CETRuntimeProfiler::DeepLuaHook,
            LUA_MASKCALL | LUA_MASKRET | LUA_MASKLINE,
            0);
        return true;
    }

    void EndDeepSample(lua_State* aState)
    {
        auto& state = DeepStateForThread();
        if (!state.Active)
            return;

        lua_State* statePtr = state.State ? state.State : aState;
        if (statePtr)
        {
            lua_sethook(
                statePtr,
                state.PreviousHook,
                state.PreviousMask,
                state.PreviousCount);
        }

        const uint64_t luaHeapAfterBytes = ReadLuaHeapBytes(statePtr);
        const bool luaHeapSampleValid =
            state.LuaHeapSampleValid && luaHeapAfterBytes > 0;
        const int64_t luaHeapDeltaBytes =
            luaHeapSampleValid
                ? static_cast<int64_t>(luaHeapAfterBytes) -
                      static_cast<int64_t>(state.LuaHeapBeforeBytes)
                : 0;

        const auto end = Clock::now();
        while (!state.Frames.empty())
            CompleteDeepFrame(state, end);

        Counter* counter = state.CounterPtr;
        const uint64_t captureEndNs = FastCapturedNanoseconds(end);
        const uint64_t sampleWallNs = static_cast<uint64_t>(
            std::max<int64_t>(
                0,
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    end - state.SampleStartWall).count()));
        const uint64_t approxOwnWallNs =
            sampleWallNs > state.NestedRegistrationNs
                ? sampleWallNs - state.NestedRegistrationNs
                : 0;

        bool keepSample = state.Mode == DeepSampleMode::Hotset;
        if (state.Mode == DeepSampleMode::SpikeProbe && counter)
        {
            counter->DeepSpikeProbeSamples.fetch_add(1, std::memory_order_relaxed);
            const uint64_t baseline =
                counter->DeepBaselineAvgExclusiveNs.load(std::memory_order_relaxed);
            const uint64_t threshold =
                std::max<uint64_t>(
                    DefaultSpikeThresholdNs,
                    baseline > 0 ? baseline * 4 : 0);
            keepSample = approxOwnWallNs >= threshold;
            if (keepSample)
            {
                counter->DeepSpikeCaptures.fetch_add(1, std::memory_order_relaxed);
            }
        }

        if (keepSample && counter && luaHeapSampleValid)
        {
            counter->DeepHeapSamples.fetch_add(1, std::memory_order_relaxed);
            counter->DeepHeapNetDeltaBytes.fetch_add(
                luaHeapDeltaBytes, std::memory_order_relaxed);

            if (luaHeapDeltaBytes > 0)
            {
                const uint64_t positive =
                    static_cast<uint64_t>(luaHeapDeltaBytes);
                counter->DeepHeapPositiveDeltaBytes.fetch_add(
                    positive, std::memory_order_relaxed);
                UpdateMax(counter->DeepHeapMaxPositiveDeltaBytes, positive);
            }
            else if (luaHeapDeltaBytes < 0)
            {
                const uint64_t reclaimed =
                    static_cast<uint64_t>(-luaHeapDeltaBytes);
                counter->DeepHeapReclaimedDeltaBytes.fetch_add(
                    reclaimed, std::memory_order_relaxed);
                UpdateMax(counter->DeepHeapMaxReclaimedDeltaBytes, reclaimed);
            }
        }

        if (keepSample)
        {
            bool lineRowsTruncated = state.LineRowsTruncated;
            std::lock_guard lock(m_mutex);

            for (auto& [functionKey, local] : state.Functions)
            {
                const std::string aggregateKey =
                    std::to_string(local.RegistrationId) + "\x1f" +
                    std::to_string(local.ProfileEpoch) + "\x1f" +
                    functionKey;

                auto& aggregate = m_deepFunctions[aggregateKey];
                if (aggregate.FunctionKey.empty())
                {
                    aggregate = local;
                }
                else
                {
                    aggregate.Calls += local.Calls;
                    aggregate.InclusiveNs += local.InclusiveNs;
                    aggregate.ExclusiveNs += local.ExclusiveNs;
                    aggregate.SampledExclusiveAllocBytes += local.SampledExclusiveAllocBytes;
                    aggregate.SampledExclusiveAllocations += local.SampledExclusiveAllocations;
                    aggregate.MaxInclusiveNs =
                        std::max(aggregate.MaxInclusiveNs, local.MaxInclusiveNs);
                    aggregate.MinDepth =
                        std::min(aggregate.MinDepth, local.MinDepth);
                }
            }

            for (auto& [edgeKey, local] : state.Edges)
            {
                const std::string aggregateKey =
                    std::to_string(local.RegistrationId) + "\x1f" +
                    std::to_string(local.ProfileEpoch) + "\x1f" +
                    edgeKey;

                auto& aggregate = m_deepEdges[aggregateKey];
                if (aggregate.ParentFunctionKey.empty())
                {
                    aggregate = local;
                }
                else
                {
                    aggregate.Calls += local.Calls;
                    aggregate.ChildInclusiveNs += local.ChildInclusiveNs;
                }
            }

            const bool canStoreSample = m_deepSamples.size() < MaxDeepSampleEvents;
            if (!canStoreSample)
            {
                ++m_droppedDeepSamples;
            }
            else
            {
                for (const auto& [_, callsite] : state.Callsites)
                {
                    if (m_deepCallsites.size() >= MaxDeepCallsiteEvents)
                    {
                        ++m_droppedDeepCallsites;
                        continue;
                    }
                    m_deepCallsites.push_back(callsite);
                }

                for (const auto& [_, line] : state.Lines)
                {
                    if (m_deepLines.size() >= MaxDeepLineEvents)
                    {
                        ++m_droppedDeepLines;
                        lineRowsTruncated = true;
                        continue;
                    }

                    m_deepLines.push_back({
                        state.SampleSequence,
                        counter ? counter->RegistrationId : 0,
                        state.ProfileEpoch,
                        line.SourceFile,
                        line.Line,
                        line.Hits
                    });
                }

                m_deepSamples.push_back({
                    state.SampleSequence,
                    counter ? counter->RegistrationId : 0,
                    state.ProfileEpoch,
                    state.Frame,
                    state.FrameInvocationOrdinal,
                    state.SampleStartCaptureNs,
                    captureEndNs,
                    state.Mode == DeepSampleMode::Hotset
                        ? "HOTSET"
                        : "SPIKE_CAPTURE",
                    approxOwnWallNs,
                    state.HookEvents,
                    state.LineEvents,
                    state.UnresolvedLineEvents,
                    static_cast<uint64_t>(state.Lines.size()),
                    state.PathTransitions,
                    state.PathFingerprint,
                    state.NestedRegistrationCount,
                    state.NestedRegistrationNs,
                    luaHeapSampleValid ? state.LuaHeapBeforeBytes : 0,
                    luaHeapSampleValid ? luaHeapAfterBytes : 0,
                    luaHeapSampleValid ? luaHeapDeltaBytes : 0,
                    lineRowsTruncated
                });
            }
        }

        if (counter && state.Mode == DeepSampleMode::Hotset)
        {
            const uint64_t samples =
                counter->DeepSamples.fetch_add(1, std::memory_order_relaxed) + 1;
            const uint64_t target = std::max<uint64_t>(
                1, counter->DeepTargetSamples.load(std::memory_order_relaxed));

            if (samples >= target)
            {
                const uint64_t nowCalls =
                    counter->Calls.load(std::memory_order_relaxed);
                const uint64_t nowExclusive =
                    counter->ExclusiveNs.load(std::memory_order_relaxed);
                const uint64_t startCalls =
                    counter->DeepEpochStartCalls.load(std::memory_order_relaxed);
                const uint64_t startExclusive =
                    counter->DeepEpochStartExclusiveNs.load(std::memory_order_relaxed);

                if (nowCalls > startCalls && nowExclusive >= startExclusive)
                {
                    counter->DeepBaselineAvgExclusiveNs.store(
                        (nowExclusive - startExclusive) / (nowCalls - startCalls),
                        std::memory_order_relaxed);
                }

                counter->DeepComplete.store(true, std::memory_order_release);
                counter->DeepSelected.store(false, std::memory_order_release);
                counter->DeepSpikeArmed.store(true, std::memory_order_release);
                counter->DeepDriftWindows.store(0, std::memory_order_relaxed);
                counter->DeepDriftDirection.store(0, std::memory_order_relaxed);
            }
        }

        state = {};
    }

    // Registration-time binding: native data only. No sol::environment access,
    // no lua_State access, and no modification of CET's Context layout.
    void BindCallback(const void* aContext,
                      const std::string& aKind,
                      const std::string& aTarget)
    {
        if (!aContext)
            return;

        std::lock_guard lock(m_mutex);
        auto& binding = m_callbackBindings[aContext];
        binding.Kind = aKind;
        binding.Target = aTarget;
        binding.ResolvedCounter = nullptr;
    }

    // Called lazily only while CET is already executing a callback under a
    // valid locked Lua state. The caller resolves the mod name; this class
    // itself never touches Lua/Sol.
    Counter* ResolveCallback(const void* aContext,
                             const std::string& aMod)
    {
        if (!aContext)
            return nullptr;

        std::lock_guard lock(m_mutex);

        const auto it = m_callbackBindings.find(aContext);
        if (it == m_callbackBindings.end())
            return nullptr;

        auto& binding = it->second;
        if (!binding.ResolvedCounter)
        {
            std::ostringstream instanceKey;
            instanceKey << "context@"
                        << reinterpret_cast<uintptr_t>(aContext);
            binding.ResolvedCounter = RegisterLocked(
                aMod.empty() ? "<unknown>" : aMod,
                binding.Kind,
                binding.Target,
                instanceKey.str());
        }

        return binding.ResolvedCounter;
    }

    void ClearCallbackBindings()
    {
        std::lock_guard lock(m_mutex);
        m_callbackBindings.clear();
    }

    void Reset()
    {
        std::lock_guard lock(m_mutex);
        ResetCountersLocked();
        ResetSpikesLocked();
        ResetTimelineLocked();
        ResetMarkersLocked();
        ResetGcExplicitLocked();
        ResetGcCollectorLocked();
        ResetSchedulerLocked();
        ResetDeepLocked();
        m_scalarObservations.clear();
        m_droppedScalarObservations = 0;
        m_accumulatedCapture = std::chrono::nanoseconds::zero();
        gcet_gc_trace_segment(0);
        gcet_gc_stats_set(
            m_state.load(std::memory_order_relaxed) == CaptureState::Running ? 1 : 0,
            1);
        m_gcCollectorPrevious = {};
        m_gcCollectorLastBoundaryNs = 0;
        if (m_state.load(std::memory_order_relaxed) == CaptureState::Running)
        {
            m_segmentStarted = Clock::now();
            PublishFastSegmentClockLocked(0);
            AddMarkerLocked("RESET", m_segmentStarted);
        }
    }

    // No automatic getter interception: callers opt in at their existing read.
    // Exact scalar string equality is observed only within this capture;
    // neither this probe nor its hash implies safe cache invalidation.
    bool ObserveScalar(const std::string& aLabel, const std::string& aValue)
    {
        if (!IsCapturing() || aLabel.empty() || aLabel.size() > 128 ||
            aValue.size() > 256)
            return false;
        std::lock_guard lock(m_mutex);
        if (!IsCapturing())
            return false;
        auto it = m_scalarObservations.find(aLabel);
        if (it == m_scalarObservations.end())
        {
            if (m_scalarObservations.size() >= 128)
            {
                ++m_droppedScalarObservations;
                return false;
            }
            it = m_scalarObservations.emplace(aLabel, ScalarObservation{}).first;
        }
        auto& row = it->second;
        if (row.Observations > 0)
        {
            if (row.LastValue == aValue) ++row.Unchanged;
            else ++row.Changed;
        }
        row.LastValue = aValue;
        ++row.Observations;
        return true;
    }

    void StartCapture()
    {
        std::lock_guard lock(m_mutex);
        ResetCountersLocked();
        ResetSpikesLocked();
        ResetTimelineLocked();
        ResetMarkersLocked();
        ResetGcExplicitLocked();
        ResetGcCollectorLocked();
        ResetSchedulerLocked();
        ResetDeepLocked();
        m_scalarObservations.clear();
        m_droppedScalarObservations = 0;
        m_accumulatedCapture = std::chrono::nanoseconds::zero();
        // Disabled during all prior work. Activate atomics only for capture.
        gcet_gc_trace_segment(0);
        gcet_gc_stats_set(1, 1);
        m_gcCollectorPrevious = {};
        m_gcCollectorLastBoundaryNs = 0;
        m_segmentStarted = Clock::now();
        PublishFastSegmentClockLocked(0);
        ++m_captureGeneration;
        m_state.store(CaptureState::Running, std::memory_order_release);
        AddMarkerLocked("CAPTURE_START", m_segmentStarted);
    }

    void PauseCapture()
    {
        std::lock_guard lock(m_mutex);
        PauseLocked(Clock::now());
    }

    void ResumeCapture()
    {
        std::lock_guard lock(m_mutex);
        if (m_state.load(std::memory_order_relaxed) == CaptureState::Running)
            return;

        m_segmentStarted = Clock::now();
        PublishFastSegmentClockLocked(
            static_cast<uint64_t>(std::max<int64_t>(0, m_accumulatedCapture.count())));
        gcet_gc_trace_segment(static_cast<uint64_t>(
            std::max<int64_t>(0, m_accumulatedCapture.count())));
        gcet_gc_stats_set(1, 0);
        m_state.store(CaptureState::Running, std::memory_order_release);
        AddMarkerLocked("RESUME", m_segmentStarted);
    }

    void StopCapture(bool aDump = true)
    {
        {
            std::lock_guard lock(m_mutex);
            PauseLocked(Clock::now());
        }

        if (aDump)
            Dump();
    }

    bool IsCapturing() const
    {
        return m_state.load(std::memory_order_acquire) == CaptureState::Running;
    }

    std::string Status()
    {
        std::lock_guard lock(m_mutex);
        const auto now = Clock::now();
        const double seconds = CapturedSecondsLocked(now);

        std::ostringstream oss;
        oss << (m_state.load(std::memory_order_relaxed) == CaptureState::Running
                    ? "RUNNING"
                    : "PAUSED")
            << " | captured=" << std::fixed << std::setprecision(3)
            << seconds << "s"
            << " | counters=" << m_counters.size()
            << " | spikeThreshold=" << std::setprecision(3)
            << (static_cast<double>(m_spikeThresholdNs.load(std::memory_order_relaxed)) / 1'000'000.0)
            << "ms"
            << " | spikes=" << m_spikeEvents.size()
            << " | spikeDropped=" << m_droppedSpikeEvents
            << " | timelineBucket=" << std::setprecision(1)
            << (static_cast<double>(m_timelineBucketNs.load(std::memory_order_relaxed)) / 1'000'000.0)
            << "ms"
            << " | timelineRows=" << m_timelineEvents.size()
            << " | timelineDropped=" << m_droppedTimelineEvents
            << " | markers=" << m_markers.size()
            << " | schedulerJobs=" << m_schedulerJobs.size()
            << " | schedulerSpikes=" << m_schedulerSpikeEvents.size()
            << " | schedulerBursts=" << m_schedulerFrameBursts.size()
            << " | captureFrames=" << m_captureFrameCount.load(std::memory_order_relaxed)
            << " | deepFunctions=" << m_deepFunctions.size()
            << " | deepEdges=" << m_deepEdges.size()
            << " | deepSamples=" << m_deepSamples.size()
            << " | deepCallsites=" << m_deepCallsites.size()
            << " | deepLines=" << m_deepLines.size();
        return oss.str();
    }

    // Frame-bucketed exact internal collector time, read from LuaJIT's
    // own instrumentation. Includes allocator and explicit API GC paths.
    // A row is emitted only if GC ran in the preceding observed frame.
    void SnapshotCollectorLocked(Clock::time_point now, uint64_t frame)
    {
        GCETGCStats current{};
        gcet_gc_stats_read(&current);
        if (!current.frequency) return;
        const auto previous = m_gcCollectorPrevious;
        m_gcCollectorPrevious = current;
        const uint64_t atNs = CapturedNanosecondsLocked(now);
        const uint64_t startNs = m_gcCollectorLastBoundaryNs;
        m_gcCollectorLastBoundaryNs = atNs;
        if (current.stepCalls < previous.stepCalls ||
            current.fullCalls < previous.fullCalls ||
            current.stepTicks < previous.stepTicks ||
            current.fullTicks < previous.fullTicks)
            return;

        const auto steps = current.stepCalls - previous.stepCalls;
        const auto fulls = current.fullCalls - previous.fullCalls;
        if (!steps && !fulls) return;
        if (m_gcCollectorFrames.size() >= MaxGcCollectorFrames)
        {
            ++m_droppedGcCollectorFrames;
            return;
        }

        const double msPerTick = 1'000.0 /
            static_cast<double>(current.frequency);
        m_gcCollectorFrames.push_back({
            frame,
            startNs,
            atNs,
            steps,
            current.completedCycles >= previous.completedCycles
                ? current.completedCycles - previous.completedCycles : 0,
            fulls,
            (current.stepTicks - previous.stepTicks) * msPerTick,
            (current.fullTicks - previous.fullTicks) * msPerTick,
            current.maxStepTicks * msPerTick,
            current.maxFullTicks * msPerTick
        });
    }

    uint64_t BeginGameFrame()
    {
        if (!IsCapturing())
        {
            m_currentGameFrame.store(0, std::memory_order_release);
            return 0;
        }

        const uint64_t frame =
            m_captureFrameCount.fetch_add(1, std::memory_order_relaxed) + 1;
        {
            std::lock_guard lock(m_mutex);
            SnapshotCollectorLocked(Clock::now(), frame - 1);
        }
        m_currentGameFrame.store(frame, std::memory_order_release);
        return frame;
    }

    uint64_t CurrentGameFrame() const
    {
        return m_currentGameFrame.load(std::memory_order_acquire);
    }

    void RecordFrameMultiplicity(Counter* aCounter)
    {
        if (!aCounter)
            return;

        const uint64_t frame =
            m_currentGameFrame.load(std::memory_order_acquire);
        if (frame == 0 ||
            m_state.load(std::memory_order_relaxed) != CaptureState::Running)
            return;

        uint64_t current =
            aCounter->FrameCurrent.load(std::memory_order_relaxed);
        if (current != frame)
        {
            while (aCounter->FrameRotateLock.test_and_set(
                       std::memory_order_acquire))
                std::this_thread::yield();

            current = aCounter->FrameCurrent.load(std::memory_order_relaxed);
            if (current != frame)
            {
                const uint64_t previousCalls =
                    aCounter->FrameCurrentCalls.exchange(
                        0, std::memory_order_relaxed);
                if (current != 0 && previousCalls > 0)
                    CommitFrameMultiplicity(aCounter, previousCalls);

                aCounter->FrameCurrent.store(frame, std::memory_order_relaxed);
            }

            aCounter->FrameRotateLock.clear(std::memory_order_release);
        }

        aCounter->FrameCurrentCalls.fetch_add(1, std::memory_order_relaxed);
    }

    double SetSpikeThresholdMs(double aThresholdMs)
    {
        // Keep the mode useful as a spike detector and prevent an accidental
        // zero-threshold setting from turning it into an every-callback trace.
        if (!std::isfinite(aThresholdMs))
            aThresholdMs = 5.0;

        const double clamped = std::clamp(aThresholdMs, 0.1, 10'000.0);
        const auto ns = static_cast<uint64_t>(clamped * 1'000'000.0);
        m_spikeThresholdNs.store(ns, std::memory_order_release);
        return static_cast<double>(ns) / 1'000'000.0;
    }

    double GetSpikeThresholdMs() const
    {
        return static_cast<double>(
                   m_spikeThresholdNs.load(std::memory_order_acquire)) /
               1'000'000.0;
    }

    bool ShouldRecordSpike(uint64_t aExclusiveNs) const
    {
        return aExclusiveNs >=
               m_spikeThresholdNs.load(std::memory_order_relaxed);
    }

    double SetTimelineBucketMs(double aBucketMs)
    {
        if (!std::isfinite(aBucketMs))
            aBucketMs = 50.0;

        const double clamped = std::clamp(aBucketMs, 10.0, 1000.0);
        const auto ns = static_cast<uint64_t>(clamped * 1'000'000.0);

        std::lock_guard lock(m_mutex);
        // A capture uses one bucket width from start to finish so its CSV has
        // a single unambiguous time scale. Configure while idle/before Start.
        if (m_state.load(std::memory_order_relaxed) == CaptureState::Running ||
            !m_timelineEvents.empty())
        {
            return static_cast<double>(
                       m_timelineBucketNs.load(std::memory_order_relaxed)) /
                   1'000'000.0;
        }

        m_timelineBucketNs.store(ns, std::memory_order_release);
        return static_cast<double>(ns) / 1'000'000.0;
    }

    double GetTimelineBucketMs() const
    {
        return static_cast<double>(
                   m_timelineBucketNs.load(std::memory_order_acquire)) /
               1'000'000.0;
    }

    // Exact explicit Lua collect/step call timing only; automatic incremental
    // LuaJIT GC requires internal instrumentation and is not claimed here.
    void RecordGcExplicit(const char* action, Clock::time_point start,
                          Clock::time_point end, uint64_t beforeBytes,
                          uint64_t afterBytes, const char* sourceFile,
                          int sourceLine)
    {
        if (!IsCapturing()) return;
        std::lock_guard lock(m_mutex);
        if (m_state.load(std::memory_order_relaxed) != CaptureState::Running)
            return;
        if (m_gcExplicitEvents.size() >= MaxGcExplicitEvents)
        {
            ++m_droppedGcExplicitEvents;
            return;
        }
        const auto duration = std::chrono::duration_cast<std::chrono::nanoseconds>(
            end - start).count();
        m_gcExplicitEvents.push_back({
            ++m_nextGcExplicitSequence,
            m_currentGameFrame.load(std::memory_order_relaxed),
            CapturedNanosecondsLocked(start),
            CapturedNanosecondsLocked(end),
            static_cast<uint64_t>(std::max<int64_t>(0, duration)),
            beforeBytes, afterBytes,
            action ? action : "unknown",
            sourceFile ? sourceFile : "",
            sourceLine
        });
    }

    std::string Mark(const std::string& aLabel)
    {
        std::lock_guard lock(m_mutex);
        if (m_markers.size() >= MaxMarkerEvents)
        {
            ++m_droppedMarkerEvents;
            return "marker buffer full";
        }

        const auto now = Clock::now();
        AddMarkerLocked(aLabel.empty() ? "MARK" : aLabel, now);

        std::ostringstream oss;
        oss << "MARK " << (aLabel.empty() ? "MARK" : aLabel)
            << " @ " << std::fixed << std::setprecision(3)
            << (static_cast<double>(CapturedNanosecondsLocked(now)) / 1'000'000.0)
            << " ms";
        return oss.str();
    }

    uint64_t RegisterSchedulerJob(const std::string& aOwner,
                                  const std::string& aJobType,
                                  const std::string& aJob,
                                  double aIntervalValue,
                                  const std::string& aIntervalUnit)
    {
        std::lock_guard lock(m_mutex);

        const std::string owner = aOwner.empty() ? "<unknown>" : aOwner;
        const std::string jobType = aJobType.empty() ? "unknown" : aJobType;
        const std::string job = aJob.empty() ? "<unnamed>" : aJob;
        const std::string key =
            owner + "\x1f" + jobType + "\x1f" + job;

        const auto found = m_schedulerJobs.find(key);
        if (found != m_schedulerJobs.end())
        {
            found->second->IntervalValue = aIntervalValue;
            found->second->IntervalUnit = aIntervalUnit;
            return static_cast<uint64_t>(
                reinterpret_cast<uintptr_t>(found->second.get()));
        }

        auto counter = std::make_unique<SchedulerJobCounter>();
        counter->Owner = owner;
        counter->JobType = jobType;
        counter->Job = job;
        counter->IntervalValue = aIntervalValue;
        counter->IntervalUnit = aIntervalUnit;

        auto* raw = counter.get();
        m_schedulerJobs.emplace(key, std::move(counter));
        return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(raw));
    }

    void SchedulerFrameBegin(uint64_t aFrame)
    {
        auto& state = SchedulerFrameStateForThread();
        state.Active = IsCapturing();
        state.Frame = aFrame;
        state.TotalJobNs = 0;
        state.Jobs.clear();

        if (!state.Active)
            return;

        if (state.Jobs.capacity() < 64)
            state.Jobs.reserve(64);
        state.Start = Clock::now();
    }

    void SchedulerFrameEnd(uint64_t aFrame)
    {
        auto& state = SchedulerFrameStateForThread();
        if (!state.Active || state.Frame != aFrame)
        {
            state.Active = false;
            state.Jobs.clear();
            return;
        }

        const auto end = Clock::now();
        const uint64_t wallNs = static_cast<uint64_t>(
            std::chrono::duration_cast<std::chrono::nanoseconds>(
                end - state.Start).count());

        if (IsCapturing() &&
            state.TotalJobNs >=
                m_schedulerFrameBurstThresholdNs.load(std::memory_order_relaxed))
        {
            const uint64_t captureEndNs = FastCapturedNanoseconds(end);
            const uint64_t captureStartNs =
                captureEndNs > wallNs ? captureEndNs - wallNs : 0;

            std::lock_guard lock(m_mutex);
            if (m_schedulerFrameBursts.size() < MaxSchedulerFrameBurstEvents)
            {
                SchedulerFrameBurstEvent event;
                event.Sequence = ++m_nextSchedulerBurstSequence;
                event.Frame = aFrame;
                event.CaptureStartNs = captureStartNs;
                event.CaptureEndNs = captureEndNs;
                event.SchedulerWallNs = wallNs;
                event.TotalJobNs = state.TotalJobNs;
                event.Jobs = state.Jobs;
                m_schedulerFrameBursts.push_back(std::move(event));
            }
            else
            {
                ++m_droppedSchedulerFrameBursts;
            }
        }

        state.Active = false;
        state.Jobs.clear();
    }

    uint64_t SchedulerJobBegin(uint64_t aHandle) const
    {
        if (!aHandle || !IsCapturing())
            return 0;

        return static_cast<uint64_t>(ClockTicksNs(Clock::now()));
    }

    void SchedulerJobEnd(uint64_t aHandle,
                         uint64_t aStartTicksNs,
                         uint64_t aFrame)
    {
        if (!aHandle || !aStartTicksNs || !IsCapturing())
            return;

        auto* counter = reinterpret_cast<SchedulerJobCounter*>(
            static_cast<uintptr_t>(aHandle));
        if (!counter)
            return;

        const auto end = Clock::now();
        const int64_t endTicks = ClockTicksNs(end);
        if (endTicks <= 0 ||
            static_cast<uint64_t>(endTicks) <= aStartTicksNs)
            return;

        const uint64_t elapsed =
            static_cast<uint64_t>(endTicks) - aStartTicksNs;

        counter->Calls.fetch_add(1, std::memory_order_relaxed);
        counter->TotalNs.fetch_add(elapsed, std::memory_order_relaxed);
        UpdateMax(counter->MaxNs, elapsed);

        // GCET WorkQueue/PhasePlanner/StateSignals/FrameListeners are
        // nested client measurements, not additional Scheduler wall time.
        // Preserve per-client counters and spikes, but never inflate the
        // stock 0-Engine frame-burst accumulator.
        auto& state = SchedulerFrameStateForThread();
        const bool nestedWorkload =
            counter->JobType.rfind("gcet-", 0) == 0;
        if (!nestedWorkload && state.Active && state.Frame == aFrame)
        {
            state.TotalJobNs += elapsed;
            state.Jobs.push_back({counter, elapsed});
        }

        if (elapsed >=
            m_schedulerJobSpikeThresholdNs.load(std::memory_order_relaxed))
        {
            const uint64_t captureEndNs = FastCapturedNanoseconds(end);
            const uint64_t captureStartNs =
                captureEndNs > elapsed ? captureEndNs - elapsed : 0;

            std::lock_guard lock(m_mutex);
            if (m_schedulerSpikeEvents.size() < MaxSchedulerSpikeEvents)
            {
                m_schedulerSpikeEvents.push_back({
                    counter,
                    ++m_nextSchedulerSpikeSequence,
                    aFrame,
                    captureStartNs,
                    captureEndNs,
                    elapsed
                });
            }
            else
            {
                ++m_droppedSchedulerSpikeEvents;
            }
        }
    }

    double SetSchedulerJobSpikeThresholdMs(double aThresholdMs)
    {
        if (!std::isfinite(aThresholdMs))
            aThresholdMs = 2.0;

        const double clamped = std::clamp(aThresholdMs, 0.05, 10'000.0);
        const auto ns = static_cast<uint64_t>(clamped * 1'000'000.0);
        m_schedulerJobSpikeThresholdNs.store(ns, std::memory_order_release);
        return static_cast<double>(ns) / 1'000'000.0;
    }

    double GetSchedulerJobSpikeThresholdMs() const
    {
        return static_cast<double>(
                   m_schedulerJobSpikeThresholdNs.load(std::memory_order_acquire)) /
               1'000'000.0;
    }

    double SetSchedulerFrameBurstThresholdMs(double aThresholdMs)
    {
        if (!std::isfinite(aThresholdMs))
            aThresholdMs = 5.0;

        const double clamped = std::clamp(aThresholdMs, 0.05, 10'000.0);
        const auto ns = static_cast<uint64_t>(clamped * 1'000'000.0);
        m_schedulerFrameBurstThresholdNs.store(ns, std::memory_order_release);
        return static_cast<double>(ns) / 1'000'000.0;
    }

    double GetSchedulerFrameBurstThresholdMs() const
    {
        return static_cast<double>(
                   m_schedulerFrameBurstThresholdNs.load(std::memory_order_acquire)) /
               1'000'000.0;
    }

    static void Record(Counter* aCounter,
                       uint64_t aInclusiveNs,
                       uint64_t aExclusiveNs)
    {
        if (!aCounter)
            return;

        aCounter->Calls.fetch_add(1, std::memory_order_relaxed);
        aCounter->InclusiveNs.fetch_add(
            aInclusiveNs, std::memory_order_relaxed);
        aCounter->ExclusiveNs.fetch_add(
            aExclusiveNs, std::memory_order_relaxed);

        UpdateMax(aCounter->MaxInclusiveNs, aInclusiveNs);
        UpdateMax(aCounter->MaxExclusiveNs, aExclusiveNs);
    }

    uint64_t RecordTimeline(Counter* aCounter,
                            Clock::time_point aEnd,
                            uint64_t /*aInclusiveNs*/,
                            uint64_t aExclusiveNs)
    {
        if (!aCounter || !aCounter->TimelineOwner)
            return 0;

        const uint64_t bucketWidth =
            m_timelineBucketNs.load(std::memory_order_relaxed);
        if (!bucketWidth)
            return 0;

        const uint64_t captureNs = FastCapturedNanoseconds(aEnd);
        const uint64_t bucket = captureNs / bucketWidth;
        uint64_t rolloverBookkeepingNs = 0;

        // Existing per-mod timeline remains the broad correlation stream.
        auto* owner = aCounter->TimelineOwner;
        uint64_t current = owner->CurrentBucket.load(std::memory_order_relaxed);
        if (current != bucket)
        {
            const auto bookkeepingStart = Clock::now();
            while (owner->RotateLock.test_and_set(std::memory_order_acquire))
                std::this_thread::yield();

            current = owner->CurrentBucket.load(std::memory_order_relaxed);
            if (current != bucket)
            {
                if (current != UINT64_MAX)
                {
                    TimelineEvent event{
                        owner,
                        current,
                        owner->Calls.exchange(0, std::memory_order_relaxed),
                        owner->ExclusiveNs.exchange(0, std::memory_order_relaxed),
                        owner->MaxExclusiveNs.exchange(0, std::memory_order_relaxed)
                    };

                    if (event.Calls)
                    {
                        std::lock_guard lock(m_mutex);
                        if (m_timelineEvents.size() < MaxTimelineEvents)
                            m_timelineEvents.push_back(event);
                        else
                            ++m_droppedTimelineEvents;
                    }
                }

                owner->CurrentBucket.store(bucket, std::memory_order_relaxed);
            }

            owner->RotateLock.clear(std::memory_order_release);
            rolloverBookkeepingNs += static_cast<uint64_t>(
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    Clock::now() - bookkeepingStart).count());
        }

        owner->Calls.fetch_add(1, std::memory_order_relaxed);
        owner->ExclusiveNs.fetch_add(aExclusiveNs, std::memory_order_relaxed);
        UpdateMax(owner->MaxExclusiveNs, aExclusiveNs);

        return rolloverBookkeepingNs;
    }

    void RecordSpike(Counter* aCounter,
                     Clock::time_point aEnd,
                     uint64_t aInclusiveNs,
                     uint64_t aExclusiveNs,
                     uint64_t aChildNs)
    {
        if (!aCounter)
            return;

        std::lock_guard lock(m_mutex);

        if (m_spikeEvents.size() >= MaxSpikeEvents)
        {
            ++m_droppedSpikeEvents;
            return;
        }

        const uint64_t captureEndNs = CapturedNanosecondsLocked(aEnd);
        const uint64_t captureStartNs =
            captureEndNs > aInclusiveNs ? captureEndNs - aInclusiveNs : 0;

        m_spikeEvents.push_back({
            aCounter,
            ++m_nextSpikeSequence,
            m_currentGameFrame.load(std::memory_order_relaxed),
            captureStartNs,
            captureEndNs,
            aInclusiveNs,
            aExclusiveNs,
            aChildNs,
            static_cast<uint64_t>(
                std::hash<std::thread::id>{}(std::this_thread::get_id()))
        });
    }

    void Dump()
    {
        struct Row
        {
            uint64_t RegistrationId{};
            std::string Mod;
            std::string Kind;
            std::string Target;
            std::string SourceFile;
            int SourceLineStart{};
            int SourceLineEnd{};
            uint64_t Calls{};
            uint64_t InclusiveNs{};
            uint64_t ExclusiveNs{};
            uint64_t MaxInclusiveNs{};
            uint64_t MaxExclusiveNs{};
            uint64_t GrossAllocatedBytes{};
        };

        struct SpikeRow
        {
            uint64_t Sequence{};
            uint64_t Frame{};
            uint64_t CaptureStartNs{};
            uint64_t CaptureEndNs{};
            uint64_t InclusiveNs{};
            uint64_t ExclusiveNs{};
            uint64_t ChildNs{};
            uint64_t ThreadId{};
            uint64_t RegistrationId{};
            std::string Mod;
            std::string Kind;
            std::string Target;
            std::string SourceFile;
            int SourceLineStart{};
            int SourceLineEnd{};
        };

        struct TimelineRow
        {
            uint64_t Bucket{};
            uint64_t Calls{};
            uint64_t ExclusiveNs{};
            uint64_t MaxExclusiveNs{};
            std::string Mod;
        };

        struct FrameMultiplicityRow
        {
            uint64_t RegistrationId{};
            std::string Mod;
            std::string Kind;
            std::string Target;
            std::string SourceFile;
            int SourceLineStart{};
            int SourceLineEnd{};
            uint64_t TotalFrames{};
            uint64_t FramesWithCalls{};
            uint64_t RecordedCalls{};
            uint64_t OneCallFrames{};
            uint64_t TwoCallFrames{};
            uint64_t ThreeCallFrames{};
            uint64_t FourCallFrames{};
            uint64_t FivePlusCallFrames{};
            uint64_t MaxCallsInFrame{};
        };

        struct DeepRegistrationRow
        {
            uint64_t RegistrationId{};
            std::string Mod;
            std::string Kind;
            std::string Target;
            std::string SourceFile;
            int SourceLineStart{};
            int SourceLineEnd{};
            uint64_t LuaFunctionIdentity{};
            uint32_t ProfileEpoch{};
            bool Selected{};
            bool Complete{};
            uint64_t ReusedFromRegistrationId{};
            uint64_t Samples{};
            uint64_t TargetSamples{};
            uint64_t SampleStride{};
            uint64_t HookConflicts{};
            uint64_t BaselineAvgExclusiveNs{};
            bool SpikeArmed{};
            uint64_t SpikeProbeStride{};
            uint64_t SpikeProbeSamples{};
            uint64_t SpikeCaptures{};
            uint32_t DriftWindows{};
            int32_t DriftDirection{};
            uint64_t HeapSamples{};
            uint64_t HeapPositiveDeltaBytes{};
            uint64_t HeapReclaimedDeltaBytes{};
            int64_t HeapNetDeltaBytes{};
            uint64_t HeapMaxPositiveDeltaBytes{};
            uint64_t HeapMaxReclaimedDeltaBytes{};
        };

        struct SchedulerJobRow
        {
            std::string Owner;
            std::string JobType;
            std::string Job;
            std::string IntervalUnit;
            double IntervalValue{};
            uint64_t Calls{};
            uint64_t TotalNs{};
            uint64_t MaxNs{};
        };

        struct SchedulerSpikeRow
        {
            uint64_t Sequence{};
            uint64_t Frame{};
            uint64_t CaptureStartNs{};
            uint64_t CaptureEndNs{};
            uint64_t DurationNs{};
            std::string Owner;
            std::string JobType;
            std::string Job;
            std::string IntervalUnit;
            double IntervalValue{};
        };

        struct SchedulerBurstRow
        {
            uint64_t Sequence{};
            uint64_t Frame{};
            uint64_t CaptureStartNs{};
            uint64_t CaptureEndNs{};
            uint64_t SchedulerWallNs{};
            uint64_t TotalJobNs{};
            uint64_t JobCount{};
            uint64_t SampleIndex{};
            uint64_t JobDurationNs{};
            std::string Owner;
            std::string JobType;
            std::string Job;
            std::string IntervalUnit;
            double IntervalValue{};
        };

        std::vector<Row> rows;
        std::vector<SpikeRow> spikeRows;
        std::vector<TimelineRow> timelineRows;
        std::vector<FrameMultiplicityRow> frameMultiplicityRows;
        std::vector<DeepRegistrationRow> deepRegistrationRows;
        std::vector<DeepFunctionAggregate> deepFunctionRows;
        std::vector<DeepEdgeAggregate> deepEdgeRows;
        std::vector<DeepCallsiteAggregate> deepCallsiteRows;
        std::vector<std::pair<std::string, ScalarObservation>> scalarObservationRows;
        uint64_t droppedScalarObservations{};
        std::vector<DeepSampleEvent> deepSampleRows;
        std::vector<DeepLineEvent> deepLineRows;
        std::vector<MarkerEvent> markerRows;
        std::vector<GcExplicitEvent> gcExplicitRows;
        std::vector<GcCollectorFrame> gcCollectorRows;
        std::vector<SchedulerJobRow> schedulerJobRows;
        std::vector<SchedulerSpikeRow> schedulerSpikeRows;
        std::vector<SchedulerBurstRow> schedulerBurstRows;
        std::filesystem::path outputRoot;
        double elapsedSec{};
        uint64_t droppedSpikeEvents{};
        double spikeThresholdMs{};
        double timelineBucketMs{};
        uint64_t timelineBucketNs{};
        uint64_t droppedTimelineEvents{};
        uint64_t droppedMarkerEvents{};
        uint64_t droppedGcExplicitEvents{};
        uint64_t droppedGcCollectorFrames{};
        uint64_t droppedSchedulerSpikeEvents{};
        uint64_t droppedSchedulerFrameBursts{};
        uint64_t droppedDeepSamples{};
        uint64_t droppedDeepLines{};
        uint64_t droppedDeepCallsites{};
        uint64_t captureFrames{};
        double schedulerJobSpikeThresholdMs{};
        double schedulerFrameBurstThresholdMs{};
        uint64_t dumpGeneration{};

        {
            std::lock_guard lock(m_mutex);

            // A pre-capture or duplicate Dump must be a true no-op. Older builds
            // could emit header-only CSV shells when Dump was reached without a
            // real START. Those files confused the manager and could block install.
            // Output is now strictly lazy: no capture generation, no files.
            dumpGeneration = m_captureGeneration;
            if (dumpGeneration == 0 || dumpGeneration == m_dumpedGeneration)
                return;

            outputRoot = m_outputRoot;
            elapsedSec = CapturedSecondsLocked(Clock::now());
            rows.reserve(m_counters.size());

            for (const auto& [_, counter] : m_counters)
            {
                rows.push_back({
                    counter->RegistrationId,
                    counter->Mod,
                    counter->Kind,
                    counter->Target,
                    counter->SourceFile,
                    counter->SourceLineStart,
                    counter->SourceLineEnd,
                    counter->Calls.load(std::memory_order_relaxed),
                    counter->InclusiveNs.load(std::memory_order_relaxed),
                    counter->ExclusiveNs.load(std::memory_order_relaxed),
                    counter->MaxInclusiveNs.load(std::memory_order_relaxed),
                    counter->MaxExclusiveNs.load(std::memory_order_relaxed),
                    counter->GrossAllocatedBytes.load(std::memory_order_relaxed)
                });
            }

            captureFrames =
                m_captureFrameCount.load(std::memory_order_relaxed);

            frameMultiplicityRows.reserve(m_counters.size());
            for (const auto& [_, counter] : m_counters)
            {
                uint64_t activeFrames =
                    counter->FrameActiveFrames.load(std::memory_order_relaxed);
                uint64_t recordedCalls =
                    counter->FrameRecordedCalls.load(std::memory_order_relaxed);
                uint64_t one =
                    counter->FrameOneCallFrames.load(std::memory_order_relaxed);
                uint64_t two =
                    counter->FrameTwoCallFrames.load(std::memory_order_relaxed);
                uint64_t three =
                    counter->FrameThreeCallFrames.load(std::memory_order_relaxed);
                uint64_t four =
                    counter->FrameFourCallFrames.load(std::memory_order_relaxed);
                uint64_t fivePlus =
                    counter->FrameFivePlusCallFrames.load(std::memory_order_relaxed);
                uint64_t maxCalls =
                    counter->FrameMaxCalls.load(std::memory_order_relaxed);

                const uint64_t openCalls =
                    counter->FrameCurrentCalls.load(std::memory_order_relaxed);
                if (openCalls > 0)
                {
                    ++activeFrames;
                    recordedCalls += openCalls;
                    if (openCalls == 1) ++one;
                    else if (openCalls == 2) ++two;
                    else if (openCalls == 3) ++three;
                    else if (openCalls == 4) ++four;
                    else ++fivePlus;
                    maxCalls = std::max(maxCalls, openCalls);
                }

                frameMultiplicityRows.push_back({
                    counter->RegistrationId,
                    counter->Mod,
                    counter->Kind,
                    counter->Target,
                    counter->SourceFile,
                    counter->SourceLineStart,
                    counter->SourceLineEnd,
                    captureFrames,
                    activeFrames,
                    recordedCalls,
                    one,
                    two,
                    three,
                    four,
                    fivePlus,
                    maxCalls
                });
            }

            spikeRows.reserve(m_spikeEvents.size());
            for (const auto& spike : m_spikeEvents)
            {
                if (!spike.CounterPtr)
                    continue;

                spikeRows.push_back({
                    spike.Sequence,
                    spike.Frame,
                    spike.CaptureStartNs,
                    spike.CaptureEndNs,
                    spike.InclusiveNs,
                    spike.ExclusiveNs,
                    spike.ChildNs,
                    spike.ThreadId,
                    spike.CounterPtr->RegistrationId,
                    spike.CounterPtr->Mod,
                    spike.CounterPtr->Kind,
                    spike.CounterPtr->Target,
                    spike.CounterPtr->SourceFile,
                    spike.CounterPtr->SourceLineStart,
                    spike.CounterPtr->SourceLineEnd
                });
            }

            timelineRows.reserve(m_timelineEvents.size() + m_timelineMods.size());
            for (const auto& event : m_timelineEvents)
            {
                if (!event.ModPtr || !event.Calls)
                    continue;
                timelineRows.push_back({
                    event.Bucket, event.Calls, event.ExclusiveNs,
                    event.MaxExclusiveNs, event.ModPtr->Mod
                });
            }

            // Include the currently open bucket for each mod without rotating
            // the live accumulator. STOP pauses capture before Dump, so this
            // is a stable final snapshot in normal use.
            for (const auto& [_, ownerPtr] : m_timelineMods)
            {
                const auto* owner = ownerPtr.get();
                const uint64_t calls = owner->Calls.load(std::memory_order_relaxed);
                const uint64_t bucket = owner->CurrentBucket.load(std::memory_order_relaxed);
                if (!calls || bucket == UINT64_MAX)
                    continue;
                timelineRows.push_back({
                    bucket, calls,
                    owner->ExclusiveNs.load(std::memory_order_relaxed),
                    owner->MaxExclusiveNs.load(std::memory_order_relaxed),
                    owner->Mod
                });
            }

            deepRegistrationRows.reserve(m_counters.size());
            for (const auto& [_, counter] : m_counters)
            {
                deepRegistrationRows.push_back({
                    counter->RegistrationId,
                    counter->Mod,
                    counter->Kind,
                    counter->Target,
                    counter->SourceFile,
                    counter->SourceLineStart,
                    counter->SourceLineEnd,
                    counter->LuaFunctionIdentity.load(std::memory_order_relaxed),
                    counter->DeepProfileEpoch.load(std::memory_order_relaxed),
                    counter->DeepSelected.load(std::memory_order_relaxed),
                    counter->DeepComplete.load(std::memory_order_relaxed),
                    counter->DeepReuseFromRegistrationId.load(std::memory_order_relaxed),
                    counter->DeepSamples.load(std::memory_order_relaxed),
                    counter->DeepTargetSamples.load(std::memory_order_relaxed),
                    counter->DeepSampleStride.load(std::memory_order_relaxed),
                    counter->DeepHookConflicts.load(std::memory_order_relaxed),
                    counter->DeepBaselineAvgExclusiveNs.load(std::memory_order_relaxed),
                    counter->DeepSpikeArmed.load(std::memory_order_relaxed),
                    counter->DeepSpikeProbeStride.load(std::memory_order_relaxed),
                    counter->DeepSpikeProbeSamples.load(std::memory_order_relaxed),
                    counter->DeepSpikeCaptures.load(std::memory_order_relaxed),
                    counter->DeepDriftWindows.load(std::memory_order_relaxed),
                    counter->DeepDriftDirection.load(std::memory_order_relaxed),
                    counter->DeepHeapSamples.load(std::memory_order_relaxed),
                    counter->DeepHeapPositiveDeltaBytes.load(std::memory_order_relaxed),
                    counter->DeepHeapReclaimedDeltaBytes.load(std::memory_order_relaxed),
                    counter->DeepHeapNetDeltaBytes.load(std::memory_order_relaxed),
                    counter->DeepHeapMaxPositiveDeltaBytes.load(std::memory_order_relaxed),
                    counter->DeepHeapMaxReclaimedDeltaBytes.load(std::memory_order_relaxed)
                });
            }

            deepFunctionRows.reserve(m_deepFunctions.size());
            for (const auto& [_, row] : m_deepFunctions)
                deepFunctionRows.push_back(row);

            deepEdgeRows.reserve(m_deepEdges.size());
            for (const auto& [_, row] : m_deepEdges)
                deepEdgeRows.push_back(row);

            scalarObservationRows.reserve(m_scalarObservations.size());
            for (const auto& pair : m_scalarObservations)
                scalarObservationRows.push_back(pair);
            droppedScalarObservations = m_droppedScalarObservations;
            deepCallsiteRows = m_deepCallsites;
            deepSampleRows = m_deepSamples;
            deepLineRows = m_deepLines;
            droppedDeepSamples = m_droppedDeepSamples;
            droppedDeepLines = m_droppedDeepLines;
            droppedDeepCallsites = m_droppedDeepCallsites;

            markerRows = m_markers;
            gcExplicitRows = m_gcExplicitEvents;
            droppedGcExplicitEvents = m_droppedGcExplicitEvents;
            gcCollectorRows = m_gcCollectorFrames;
            droppedGcCollectorFrames = m_droppedGcCollectorFrames;

            schedulerJobRows.reserve(m_schedulerJobs.size());
            for (const auto& [_, counter] : m_schedulerJobs)
            {
                schedulerJobRows.push_back({
                    counter->Owner,
                    counter->JobType,
                    counter->Job,
                    counter->IntervalUnit,
                    counter->IntervalValue,
                    counter->Calls.load(std::memory_order_relaxed),
                    counter->TotalNs.load(std::memory_order_relaxed),
                    counter->MaxNs.load(std::memory_order_relaxed)
                });
            }

            schedulerSpikeRows.reserve(m_schedulerSpikeEvents.size());
            for (const auto& event : m_schedulerSpikeEvents)
            {
                if (!event.CounterPtr)
                    continue;

                schedulerSpikeRows.push_back({
                    event.Sequence,
                    event.Frame,
                    event.CaptureStartNs,
                    event.CaptureEndNs,
                    event.DurationNs,
                    event.CounterPtr->Owner,
                    event.CounterPtr->JobType,
                    event.CounterPtr->Job,
                    event.CounterPtr->IntervalUnit,
                    event.CounterPtr->IntervalValue
                });
            }

            for (const auto& event : m_schedulerFrameBursts)
            {
                const uint64_t jobCount =
                    static_cast<uint64_t>(event.Jobs.size());
                uint64_t sampleIndex = 0;

                for (const auto& sample : event.Jobs)
                {
                    ++sampleIndex;
                    if (!sample.CounterPtr)
                        continue;

                    schedulerBurstRows.push_back({
                        event.Sequence,
                        event.Frame,
                        event.CaptureStartNs,
                        event.CaptureEndNs,
                        event.SchedulerWallNs,
                        event.TotalJobNs,
                        jobCount,
                        sampleIndex,
                        sample.DurationNs,
                        sample.CounterPtr->Owner,
                        sample.CounterPtr->JobType,
                        sample.CounterPtr->Job,
                        sample.CounterPtr->IntervalUnit,
                        sample.CounterPtr->IntervalValue
                    });
                }
            }

            droppedSchedulerSpikeEvents = m_droppedSchedulerSpikeEvents;
            droppedSchedulerFrameBursts = m_droppedSchedulerFrameBursts;
            schedulerJobSpikeThresholdMs =
                static_cast<double>(
                    m_schedulerJobSpikeThresholdNs.load(std::memory_order_relaxed)) /
                1'000'000.0;
            schedulerFrameBurstThresholdMs =
                static_cast<double>(
                    m_schedulerFrameBurstThresholdNs.load(std::memory_order_relaxed)) /
                1'000'000.0;

            droppedTimelineEvents = m_droppedTimelineEvents;
            droppedMarkerEvents = m_droppedMarkerEvents;
            timelineBucketNs = m_timelineBucketNs.load(std::memory_order_relaxed);
            timelineBucketMs = static_cast<double>(timelineBucketNs) / 1'000'000.0;

            droppedSpikeEvents = m_droppedSpikeEvents;
            spikeThresholdMs =
                static_cast<double>(m_spikeThresholdNs.load(std::memory_order_relaxed)) /
                1'000'000.0;
        }

        if (outputRoot.empty())
            outputRoot = std::filesystem::current_path();

        elapsedSec = std::max(0.001, elapsedSec);

        uint64_t grandExclusiveNs = 0;
        for (const auto& row : rows)
            grandExclusiveNs += row.ExclusiveNs;

        std::sort(rows.begin(), rows.end(),
                  [](const Row& a, const Row& b)
                  {
                      if (a.ExclusiveNs != b.ExclusiveNs)
                          return a.ExclusiveNs > b.ExclusiveNs;
                      return a.Calls > b.Calls;
                  });

        // -----------------------------------------------------------------
        // TIMELINE: continuous per-mod workload buckets. This is designed to
        // correlate with CapFrameX and expose cumulative sub-threshold work.
        // -----------------------------------------------------------------
        {
            std::sort(timelineRows.begin(), timelineRows.end(),
                      [](const TimelineRow& a, const TimelineRow& b)
                      {
                          if (a.Bucket != b.Bucket)
                              return a.Bucket < b.Bucket;
                          return a.Mod < b.Mod;
                      });

            const auto path = outputRoot / "CET_Runtime_Profile_Timeline.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "BucketIndex,BucketStartMs,BucketEndMs,Mod,Calls,"
                     "ExclusiveMs,MaxExclusiveMs,"
                     "BucketWidthMs,DroppedTimelineRowsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : timelineRows)
                {
                    const uint64_t startNs = row.Bucket * timelineBucketNs;
                    const uint64_t endNs = startNs + timelineBucketNs;
                    f << row.Bucket << ','
                      << (static_cast<double>(startNs) / 1'000'000.0) << ','
                      << (static_cast<double>(endNs) / 1'000'000.0) << ','
                      << Csv(row.Mod) << ','
                      << row.Calls << ','
                      << (static_cast<double>(row.ExclusiveNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.MaxExclusiveNs) / 1'000'000.0) << ','
                      << timelineBucketMs << ','
                      << droppedTimelineEvents << ','
                      << "continuous-per-mod-correlation"
                      << '\n';
                }
            }
        }

        // -----------------------------------------------------------------
        // ADAPTIVE DEEP PROFILER: sampled Lua call/return dissection for the
        // registrations broad profiling promoted during this capture.
        // These rows explain composition only; absolute callback timing remains
        // CET_Runtime_Profile_Detail.csv.
        // -----------------------------------------------------------------
        {
            std::sort(
                frameMultiplicityRows.begin(),
                frameMultiplicityRows.end(),
                [](const FrameMultiplicityRow& a, const FrameMultiplicityRow& b)
                {
                    if (a.MaxCallsInFrame != b.MaxCallsInFrame)
                        return a.MaxCallsInFrame > b.MaxCallsInFrame;
                    if (a.FramesWithCalls != b.FramesWithCalls)
                        return a.FramesWithCalls > b.FramesWithCalls;
                    return a.RegistrationId < b.RegistrationId;
                });

            const auto path =
                outputRoot / "CET_Runtime_Profile_FrameMultiplicity.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "RegistrationId,Mod,Kind,Target,SourceFile,"
                     "SourceLineStart,SourceLineEnd,TotalFrames,FramesWithCalls,"
                     "ZeroCallFrames,RecordedCalls,OneCallFrames,TwoCallFrames,"
                     "ThreeCallFrames,FourCallFrames,FivePlusCallFrames,"
                     "MaxCallsInFrame,MeanCallsPerActiveFrame,MultiCallFramePct,"
                     "Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : frameMultiplicityRows)
                {
                    const uint64_t zeroFrames =
                        row.TotalFrames > row.FramesWithCalls
                            ? row.TotalFrames - row.FramesWithCalls
                            : 0;
                    const uint64_t multiFrames =
                        row.TwoCallFrames + row.ThreeCallFrames +
                        row.FourCallFrames + row.FivePlusCallFrames;
                    const double meanCalls =
                        row.FramesWithCalls
                            ? static_cast<double>(row.RecordedCalls) /
                                  static_cast<double>(row.FramesWithCalls)
                            : 0.0;
                    const double multiPct =
                        row.FramesWithCalls
                            ? static_cast<double>(multiFrames) /
                                  static_cast<double>(row.FramesWithCalls) * 100.0
                            : 0.0;

                    f << row.RegistrationId << ','
                      << Csv(row.Mod) << ','
                      << Csv(row.Kind) << ','
                      << Csv(row.Target) << ','
                      << Csv(row.SourceFile) << ','
                      << row.SourceLineStart << ','
                      << row.SourceLineEnd << ','
                      << row.TotalFrames << ','
                      << row.FramesWithCalls << ','
                      << zeroFrames << ','
                      << row.RecordedCalls << ','
                      << row.OneCallFrames << ','
                      << row.TwoCallFrames << ','
                      << row.ThreeCallFrames << ','
                      << row.FourCallFrames << ','
                      << row.FivePlusCallFrames << ','
                      << row.MaxCallsInFrame << ','
                      << meanCalls << ','
                      << multiPct << ','
                      << "exact-native-frame-callback-multiplicity"
                      << '\n';
                }
            }
        }

        // A separate optional CSV: only present if a probe was actually called.
        // Existing deep files and schema stay unchanged for legacy readers.
        if (!scalarObservationRows.empty() || droppedScalarObservations > 0)
        {
            std::sort(scalarObservationRows.begin(), scalarObservationRows.end(),
                [](const auto& a, const auto& b) { return a.first < b.first; });
            std::ofstream f(outputRoot / "CET_Runtime_Profile_Scalar_Observations.csv",
                            std::ios::trunc);
            if (f)
            {
                f << "Label,Observations,Unchanged,Changed,DroppedLabels,Interpretation\n";
                for (const auto& item : scalarObservationRows)
                    f << Csv(item.first) << ',' << item.second.Observations << ','
                      << item.second.Unchanged << ',' << item.second.Changed << ','
                      << droppedScalarObservations << ','
                      << "OPT_IN_EXACT_SCALAR_EQUALITY_NOT_CACHE_PROOF" << '\n';
            }
        }

        {
            std::sort(deepRegistrationRows.begin(), deepRegistrationRows.end(),
                      [](const DeepRegistrationRow& a, const DeepRegistrationRow& b)
                      {
                          return a.RegistrationId < b.RegistrationId;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Deep_Registrations.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "RegistrationId,Mod,Kind,Target,SourceFile,SourceLineStart,"
                     "SourceLineEnd,LuaFunctionIdentity,ProfileEpoch,Selected,"
                     "Complete,ReusedFromRegistrationId,Samples,TargetSamples,"
                     "SampleStride,HookConflicts,BaselineAvgExclusiveUs,SpikeArmed,"
                     "SpikeProbeStride,SpikeProbeSamples,SpikeCaptures,DriftWindows,"
                     "DriftDirection,HeapSamples,SampledHeapPositiveDeltaKB,"
                     "SampledHeapReclaimedDeltaKB,SampledHeapNetDeltaKB,"
                     "MaxPositiveHeapDeltaKB,MaxReclaimedHeapDeltaKB,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : deepRegistrationRows)
                {
                    f << row.RegistrationId << ','
                      << Csv(row.Mod) << ','
                      << Csv(row.Kind) << ','
                      << Csv(row.Target) << ','
                      << Csv(row.SourceFile) << ','
                      << row.SourceLineStart << ','
                      << row.SourceLineEnd << ','
                      << row.LuaFunctionIdentity << ','
                      << row.ProfileEpoch << ','
                      << (row.Selected ? 1 : 0) << ','
                      << (row.Complete ? 1 : 0) << ','
                      << row.ReusedFromRegistrationId << ','
                      << row.Samples << ','
                      << row.TargetSamples << ','
                      << row.SampleStride << ','
                      << row.HookConflicts << ','
                      << (static_cast<double>(row.BaselineAvgExclusiveNs) / 1'000.0) << ','
                      << (row.SpikeArmed ? 1 : 0) << ','
                      << row.SpikeProbeStride << ','
                      << row.SpikeProbeSamples << ','
                      << row.SpikeCaptures << ','
                      << row.DriftWindows << ','
                      << row.DriftDirection << ','
                      << row.HeapSamples << ','
                      << (static_cast<double>(row.HeapPositiveDeltaBytes) / 1024.0) << ','
                      << (static_cast<double>(row.HeapReclaimedDeltaBytes) / 1024.0) << ','
                      << (static_cast<double>(row.HeapNetDeltaBytes) / 1024.0) << ','
                      << (static_cast<double>(row.HeapMaxPositiveDeltaBytes) / 1024.0) << ','
                      << (static_cast<double>(row.HeapMaxReclaimedDeltaBytes) / 1024.0) << ','
                      << "adaptive-hotset-sampled-path-plus-readonly-lua-heap-delta"
                      << '\n';
                }
            }
        }

        {
            std::sort(deepSampleRows.begin(), deepSampleRows.end(),
                      [](const DeepSampleEvent& a, const DeepSampleEvent& b)
                      {
                          return a.Sequence < b.Sequence;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Deep_Samples.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "SampleSequence,RegistrationId,ProfileEpoch,Frame,FrameInvocationOrdinal,CaptureStartMs,"
                     "CaptureEndMs,Mode,ApproxOwnWallMs,HookEvents,LineEvents,"
                     "UnresolvedLineEvents,UniqueLines,PathTransitions,PathFingerprint,"
                     "NestedRegistrationCount,NestedRegistrationMs,LuaHeapBeforeMB,"
                     "LuaHeapAfterMB,LuaHeapDeltaKB,LineRowsTruncated,"
                     "DroppedSamplesAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : deepSampleRows)
                {
                    f << row.Sequence << ','
                      << row.RegistrationId << ','
                      << row.ProfileEpoch << ','
                      << row.Frame << ','
                      << row.FrameInvocationOrdinal << ','
                      << (static_cast<double>(row.CaptureStartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.CaptureEndNs) / 1'000'000.0) << ','
                      << Csv(row.Mode) << ','
                      << (static_cast<double>(row.ApproxOwnWallNs) / 1'000'000.0) << ','
                      << row.HookEvents << ','
                      << row.LineEvents << ','
                      << row.UnresolvedLineEvents << ','
                      << row.UniqueLines << ','
                      << row.PathTransitions << ','
                      << row.PathFingerprint << ','
                      << row.NestedRegistrationCount << ','
                      << (static_cast<double>(row.NestedRegistrationNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.LuaHeapBeforeBytes) / (1024.0 * 1024.0)) << ','
                      << (static_cast<double>(row.LuaHeapAfterBytes) / (1024.0 * 1024.0)) << ','
                      << (static_cast<double>(row.LuaHeapDeltaBytes) / 1024.0) << ','
                      << (row.LineRowsTruncated ? 1 : 0) << ','
                      << droppedDeepSamples << ','
                      << "timestamped-hotpath-path-fingerprint-plus-readonly-lua-heap-delta"
                      << '\n';
                }
            }
        }

        {
            std::sort(deepLineRows.begin(), deepLineRows.end(),
                      [](const DeepLineEvent& a, const DeepLineEvent& b)
                      {
                          if (a.SampleSequence != b.SampleSequence)
                              return a.SampleSequence < b.SampleSequence;
                          if (a.SourceFile != b.SourceFile)
                              return a.SourceFile < b.SourceFile;
                          return a.Line < b.Line;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Deep_Lines.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "SampleSequence,RegistrationId,ProfileEpoch,SourceFile,Line,"
                     "Hits,DroppedLineRowsAtDump,Interpretation\n";

                for (const auto& row : deepLineRows)
                {
                    f << row.SampleSequence << ','
                      << row.RegistrationId << ','
                      << row.ProfileEpoch << ','
                      << Csv(row.SourceFile) << ','
                      << row.Line << ','
                      << row.Hits << ','
                      << droppedDeepLines << ','
                      << "sampled-line-hit-path-evidence"
                      << '\n';
                }
            }
        }

        {
            std::sort(deepFunctionRows.begin(), deepFunctionRows.end(),
                      [](const DeepFunctionAggregate& a, const DeepFunctionAggregate& b)
                      {
                          if (a.RegistrationId != b.RegistrationId)
                              return a.RegistrationId < b.RegistrationId;
                          if (a.ProfileEpoch != b.ProfileEpoch)
                              return a.ProfileEpoch < b.ProfileEpoch;
                          if (a.ExclusiveNs != b.ExclusiveNs)
                              return a.ExclusiveNs > b.ExclusiveNs;
                          return a.Calls > b.Calls;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Deep_Functions.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "RegistrationId,ProfileEpoch,FunctionIdentity,FunctionKey,"
                     "FunctionName,What,SourceFile,SourceLineStart,SourceLineEnd,"
                     "MinDepth,Calls,InclusiveMs,ExclusiveMs,AvgExclusiveUs,"
                     "MaxInclusiveMs,GGCTracerSampledOwnAllocatedBytes,"
                     "GGCTracerSampledOwnAllocations,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : deepFunctionRows)
                {
                    const double avgExclusiveUs =
                        row.Calls
                            ? static_cast<double>(row.ExclusiveNs) /
                                  1'000.0 / static_cast<double>(row.Calls)
                            : 0.0;
                    f << row.RegistrationId << ','
                      << row.ProfileEpoch << ','
                      << row.FunctionIdentity << ','
                      << Csv(row.FunctionKey) << ','
                      << Csv(row.FunctionName) << ','
                      << Csv(row.What) << ','
                      << Csv(row.SourceFile) << ','
                      << row.SourceLineStart << ','
                      << row.SourceLineEnd << ','
                      << (row.MinDepth == UINT32_MAX ? 0 : row.MinDepth) << ','
                      << row.Calls << ','
                      << (static_cast<double>(row.InclusiveNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.ExclusiveNs) / 1'000'000.0) << ','
                      << avgExclusiveUs << ','
                      << (static_cast<double>(row.MaxInclusiveNs) / 1'000'000.0) << ','
                      << row.SampledExclusiveAllocBytes << ','
                      << row.SampledExclusiveAllocations << ','
                      << "sampled-relative-composition-not-absolute-runtime"
                      << '\n';
                }
            }
        }

        {
            std::sort(deepEdgeRows.begin(), deepEdgeRows.end(),
                      [](const DeepEdgeAggregate& a, const DeepEdgeAggregate& b)
                      {
                          if (a.RegistrationId != b.RegistrationId)
                              return a.RegistrationId < b.RegistrationId;
                          if (a.ProfileEpoch != b.ProfileEpoch)
                              return a.ProfileEpoch < b.ProfileEpoch;
                          return a.ChildInclusiveNs > b.ChildInclusiveNs;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Deep_Edges.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "RegistrationId,ProfileEpoch,ParentFunctionKey,"
                     "ChildFunctionKey,Calls,ChildInclusiveMs,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : deepEdgeRows)
                {
                    f << row.RegistrationId << ','
                      << row.ProfileEpoch << ','
                      << Csv(row.ParentFunctionKey) << ','
                      << Csv(row.ChildFunctionKey) << ','
                      << row.Calls << ','
                      << (static_cast<double>(row.ChildInclusiveNs) / 1'000'000.0) << ','
                      << "sampled-call-edge"
                      << '\n';
                }
            }
        }

        {
            std::sort(
                deepCallsiteRows.begin(),
                deepCallsiteRows.end(),
                [](const DeepCallsiteAggregate& a, const DeepCallsiteAggregate& b)
                {
                    if (a.SampleSequence != b.SampleSequence)
                        return a.SampleSequence < b.SampleSequence;
                    if (a.ChildInclusiveNs != b.ChildInclusiveNs)
                        return a.ChildInclusiveNs > b.ChildInclusiveNs;
                    return a.CallerLine < b.CallerLine;
                });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Deep_Callsites.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "SampleSequence,RegistrationId,ProfileEpoch,Frame,"
                     "FrameInvocationOrdinal,CallerSourceFile,CallerLine,ParentFunctionKey,"
                     "ChildFunctionKey,Calls,ChildInclusiveMs,"
                     "DroppedCallsiteRowsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : deepCallsiteRows)
                {
                    f << row.SampleSequence << ','
                      << row.RegistrationId << ','
                      << row.ProfileEpoch << ','
                      << row.Frame << ','
                      << row.FrameInvocationOrdinal << ','
                      << Csv(row.CallerSourceFile) << ','
                      << row.CallerLine << ','
                      << Csv(row.ParentFunctionKey) << ','
                      << Csv(row.ChildFunctionKey) << ','
                      << row.Calls << ','
                      << (static_cast<double>(row.ChildInclusiveNs) / 1'000'000.0) << ','
                      << droppedDeepCallsites << ','
                      << "sampled-per-invocation-callsite-callee-evidence"
                      << '\n';
                }
            }
        }

        // Exact explicit GC calls are included inside their containing CET
        // callback durations. Do not sum these durations with callback totals.
        {
            const auto path = outputRoot / "CET_Runtime_Profile_GC_Explicit.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "Sequence,Frame,CaptureStartMs,CaptureEndMs,DurationMs,"
                     "Action,BeforeMiB,AfterMiB,ReleasedMiB,SourceFile,"
                     "SourceLine,DroppedEventsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);
                for (const auto& gc : gcExplicitRows)
                {
                    const double before = static_cast<double>(gc.BeforeBytes) /
                        (1024.0 * 1024.0);
                    const double after = static_cast<double>(gc.AfterBytes) /
                        (1024.0 * 1024.0);
                    f << gc.Sequence << ',' << gc.Frame << ','
                      << (static_cast<double>(gc.StartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(gc.EndNs) / 1'000'000.0) << ','
                      << (static_cast<double>(gc.DurationNs) / 1'000'000.0) << ','
                      << Csv(gc.Action) << ',' << before << ',' << after << ','
                      << std::max(0.0, before - after) << ','
                      << Csv(gc.SourceFile) << ',' << gc.SourceLine << ','
                      << droppedGcExplicitEvents << ','
                      << "exact-explicit-call-wall-time-nested-within-callback\n";
                }
            }
        }

        // -----------------------------------------------------------------
        // Exact internal GC execution time bucketed by native rendered frame.
        // Includes both automatically triggered and explicitly requested steps.
        // Child time may already be inside mod callback and Scheduler totals.
        {
            std::ofstream f(
                outputRoot / "CET_Runtime_Profile_GC_Collector.csv",
                std::ios::trunc);
            if (f)
            {
                f << "Frame,CaptureStartMs,CaptureEndMs,IncrementalCalls,IncrementalMs,"
                     "CompletedCycles,FullCalls,FullMs,CollectorTotalMs,"
                     "MaxStepToDateMs,MaxFullToDateMs,DroppedFramesAtDump,"
                     "Interpretation\n";
                f << std::fixed << std::setprecision(6);
                for (const auto& gc : gcCollectorRows)
                {
                    f << gc.Frame << ','
                      << (static_cast<double>(gc.StartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(gc.CaptureNs) / 1'000'000.0) << ','
                      << gc.IncrementalCalls << ',' << gc.IncrementalMs << ','
                      << gc.CompletedCycles << ',' << gc.FullCalls << ','
                      << gc.FullMs << ','
                      << (gc.IncrementalMs + gc.FullMs) << ','
                      << gc.MaxIncrementalMs << ',' << gc.MaxFullMs << ','
                      << droppedGcCollectorFrames << ','
                      << "exact-internal-collector-time-frame-bucketed-nested\n";
                }
            }
        }


        // Positive growth at LuaJIT allocation sites, attributed to innermost
        // measured CET registration (not native allocations or pause ownership).
        {
            auto allocations = rows;
            std::sort(allocations.begin(), allocations.end(),
                [](const Row& a, const Row& b) {
                    return a.GrossAllocatedBytes > b.GrossAllocatedBytes;
                });
            std::ofstream f(outputRoot / "CET_Runtime_Profile_Lua_Allocations.csv", std::ios::trunc);
            if (f) {
                f << "RegistrationId,Mod,Kind,Target,SourceFile,Calls,GrossAllocatedMiB,"
                     "GrossAllocatedMiBPerSecond,Interpretation\n";
                f << std::fixed << std::setprecision(6);
                for (const auto& row : allocations) {
                    const double mib = static_cast<double>(row.GrossAllocatedBytes) / 1048576.0;
                    f << row.RegistrationId << ',' << Csv(row.Mod) << ',' << Csv(row.Kind)
                      << ',' << Csv(row.Target) << ',' << Csv(row.SourceFile)
                      << ',' << row.Calls << ',' << mib << ',' << mib / elapsedSec
                      << ",capture-gated-positive-LuaJIT-allocation-growth-own-callback\n";
                }
            }
        }


        // G-GC Tracer. Allocation requests and GC-object births are distinct:
        // LuaJIT realloc growth is not the same as a new collectable object.
        // Samples are NOT extrapolated; allocation owner is the creator scope,
        // never the callback that happened to trigger garbage collection.
        {
            struct Volume {
                uint64_t bytes{}, allocations{}, objects{}, strings{};
                uint64_t samples{}, collisions{}, reclaimed{}, reclaimedBytes{};
                uint64_t lifeTicks{}, finalized{};
                uint64_t tables{}, freedStrings{}, userdata{}, cdata{}, other{};
                bool focused{};
            };
            std::unordered_map<std::string, Volume> modTotals;
            std::ofstream registrations(
                outputRoot / "CET_Runtime_Profile_GGC_Registrations.csv", std::ios::trunc);
            if (registrations)
                registrations << "RegistrationId,Mod,Kind,Target,AllocatedBytes,"
                    "AllocationOperations,NewGCObjects,NewStrings,SampledObjects,"
                    "SampleCollisions,SampledReclaimedObjects,SampledReclaimedBytes,"
                    "SampledLifetimeTicks,SampledFinalizers,FocusedAtDump\n";
            for (const auto& row : rows) {
                GGCTraceOwner native{};
                ggc_trace_owner_read(row.RegistrationId, &native);
                const bool focused = ggc_trace_focused(row.RegistrationId) != 0;
                if (registrations)
                    registrations << row.RegistrationId << ',' << Csv(row.Mod) << ','
                        << Csv(row.Kind) << ',' << Csv(row.Target) << ','
                        << native.bytes << ',' << native.allocations << ','
                        << native.objects << ',' << native.strings << ','
                        << native.samples << ',' << native.collisions << ','
                        << native.freedSamples << ',' << native.freedBytes << ','
                        << native.lifetimeTicks << ',' << native.finalizedSamples
                        << ',' << (focused ? 1 : 0) << '\n';
                auto& t = modTotals[row.Mod];
                t.bytes += native.bytes; t.allocations += native.allocations;
                t.objects += native.objects; t.strings += native.strings;
                t.samples += native.samples; t.collisions += native.collisions;
                t.reclaimed += native.freedSamples; t.reclaimedBytes += native.freedBytes;
                t.lifeTicks += native.lifetimeTicks; t.finalized += native.finalizedSamples;
                t.tables += native.freedTables; t.freedStrings += native.freedStrings;
                t.userdata += native.freedUserdata; t.cdata += native.freedCdata;
                t.other += native.freedOther;
                t.focused = t.focused || focused;
            }
            for (uint64_t unknown : {0ULL, 32767ULL}) {
                GGCTraceOwner native{};
                ggc_trace_owner_read(unknown, &native);
                const std::string label = unknown == 0
                    ? "<unattributed>" : "<registration-overflow>";
                auto& t = modTotals[label];
                t.bytes = native.bytes; t.allocations = native.allocations;
                t.objects = native.objects; t.strings = native.strings;
                t.samples = native.samples; t.collisions = native.collisions;
                t.reclaimed = native.freedSamples; t.reclaimedBytes = native.freedBytes;
                t.lifeTicks = native.lifetimeTicks; t.finalized = native.finalizedSamples;
                t.tables = native.freedTables; t.freedStrings = native.freedStrings;
                t.userdata = native.freedUserdata; t.cdata = native.freedCdata;
                t.other = native.freedOther;
            }
            std::vector<std::string> rankedBytes, rankedOps;
            for (const auto& [name, t] : modTotals)
                if (t.bytes || t.allocations) {
                    rankedBytes.push_back(name); rankedOps.push_back(name);
                }
            std::sort(rankedBytes.begin(), rankedBytes.end(),
                [&](const auto& a, const auto& b) {
                    return modTotals[a].bytes == modTotals[b].bytes
                        ? a < b : modTotals[a].bytes > modTotals[b].bytes;
                });
            std::sort(rankedOps.begin(), rankedOps.end(),
                [&](const auto& a, const auto& b) {
                    return modTotals[a].allocations == modTotals[b].allocations
                        ? a < b : modTotals[a].allocations > modTotals[b].allocations;
                });
            std::unordered_map<std::string, size_t> byteRank, opRank;
            std::unordered_map<std::string, bool> topTen;
            for (size_t i = 0; i < rankedBytes.size(); ++i) {
                const auto& name = rankedBytes[i];
                byteRank[name] = i + 1;
                if (topTen.size() < 5 && name != "<unattributed>" &&
                    name != "<registration-overflow>")
                    topTen[name] = true;
            }
            for (size_t i = 0; i < rankedOps.size(); ++i)
                opRank[rankedOps[i]] = i + 1;
            for (const auto& name : rankedOps) {
                if (topTen.size() >= 10) break;
                if (name != "<unattributed>" && name != "<registration-overflow>")
                    topTen[name] = true;
            }
            GCETGCStats gc{};
            gcet_gc_stats_read(&gc);
            const double lifetimeScale = gc.frequency
                ? 1000.0 / static_cast<double>(gc.frequency) : 0.0;
            std::ofstream ranks(
                outputRoot / "CET_Runtime_Profile_GGC_Rankings.csv", std::ios::trunc);
            if (ranks) {
                ranks << "Mod,BytesRank,OperationsRank,SelectedTopTenAtDump,"
                    "FocusedAtDump,AllocatedMiB,AllocatedMiBPerSecond,"
                    "AllocationOperations,AllocationsPerSecond,NewGCObjects,NewStrings,"
                    "SampledObjects,SampleCollisions,SampledReclaimedObjects,"
                    "SampledReclaimedMiB,AverageReclaimedLifetimeMs,"
                    "SampledFinalizers,FreedTablesSample,FreedStringsSample,"
                    "FreedUserdataSample,FreedCdataSample,FreedOtherSample,"
                    "ActualGlobalFinalizers,Interpretation\n";
                ranks << std::fixed << std::setprecision(6);
                for (const auto& name : rankedBytes) {
                    const auto& t = modTotals[name];
                    ranks << Csv(name) << ',' << byteRank[name] << ',' << opRank[name]
                        << ',' << (topTen.count(name) ? 1 : 0)
                        << ',' << (t.focused ? 1 : 0) << ','
                        << t.bytes / 1048576.0 << ',' << t.bytes / 1048576.0 / elapsedSec << ','
                        << t.allocations << ',' << t.allocations / elapsedSec << ','
                        << t.objects << ',' << t.strings << ','
                        << t.samples << ',' << t.collisions << ',' << t.reclaimed << ','
                        << t.reclaimedBytes / 1048576.0 << ','
                        << (t.reclaimed ? t.lifeTicks * lifetimeScale / t.reclaimed : 0.0)
                        << ',' << t.finalized << ',' << t.tables << ','
                        << t.freedStrings << ',' << t.userdata << ',' << t.cdata << ','
                        << t.other << ',' << ggc_trace_finalizer_calls()
                        << ",G_GC_TRACER_NATIVE_2_15_FINALIZER_CALL_NESTED_SCOPE_V1\n";
                }
            }
        }
        // Unscoped VM-state context is not mod identity or a GC-trigger cause.
        {
            static const char* states[8] = {
                "VM_INTERP", "VM_C", "VM_GC", "VM_EXIT",
                "VM_RECORD", "VM_OPT", "VM_ASM", "VM_UNKNOWN"
            };
            GGCUnscopedContext contexts[8]{};
            ggc_trace_unscoped_context_read(contexts, 8);
            std::ofstream out(outputRoot / "CET_Runtime_Profile_GGC_UnscopedContexts.csv", std::ios::trunc);
            if (out) {
                out << "VmExecutionState,PositiveBytes,AllocationOperations,Interpretation\n";
                for (size_t i = 0; i < 8; ++i)
                    out << states[i] << ',' << contexts[i].bytes << ','
                        << contexts[i].operations
                        << ",owner-zero-execution-context-not-producer-identity\n";
            }
            std::vector<GGCUnscopedSecond> seconds(1201);
            const int count = ggc_trace_unscoped_seconds_read(seconds.data(),
                static_cast<int>(seconds.size()));
            std::ofstream timeline(outputRoot / "CET_Runtime_Profile_GGC_UnscopedTimeline.csv",
                                   std::ios::trunc);
            if (timeline) {
                timeline << "CaptureSecond,VmExecutionState,PositiveBytes,AllocationOperations,"
                            "Window,Interpretation\n";
                for (int i = 0; i < count; ++i)
                    for (size_t state = 0; state < 8; ++state) {
                        const auto& v = seconds[static_cast<size_t>(i)].states[state];
                        if (!v.bytes && !v.operations) continue;
                        timeline << i << ',' << states[state] << ','
                                 << v.bytes << ',' << v.operations << ','
                                 << (i == 1200 ? "OVERFLOW_GE_1200_SECONDS" : "ONE_SECOND")
                                 << ",owner-zero-allocation-not-GC-cause\n";
                    }
            }
        }


        // Four orthogonal owner-zero diagnostics: allocation path/size,
        // CET execution boundary, one-second morphology, native stack samples.
        // Native samples are sampled *events*, not all bytes or Lua callsites.
        {
            static const char* origins[2]={"RESIZE_GROWTH","NEW_GCO"};
            static const char* tiers[6]={"LT_64B","64_255B","256_1023B",
                "1_4KIB","4_16KIB","GE_16KIB"};
            static const char* bridges[6]={"OUTSIDE_CET_INSTRUMENTED_BOUNDARY",
                "CET_FRAME_UPDATE","CET_OVERLAY_DUMP","CET_DRAW_DISPATCH",
                "CET_LIFECYCLE_DISPATCH","CET_OVERLAY_EVENT_DISPATCH"};
            static const char* states[8]={"VM_INTERP","VM_C","VM_GC","VM_EXIT",
                "VM_RECORD","VM_OPT","VM_ASM","VM_UNKNOWN"};
            GGCUnscopedDetail detail{};
            ggc_trace_unscoped_detail_read(&detail);
            std::ofstream shapes(outputRoot / "CET_Runtime_Profile_GGC_UnscopedShapes.csv",
                                 std::ios::trunc);
            if(shapes) {
                shapes << "AllocatorPath,PositiveGrowthTier,PositiveBytes,"
                          "AllocationOperations,Interpretation\n";
                for(int path=0;path<2;path++)
                    for(int tier=0;tier<6;tier++) {
                        const auto& x=detail.morphology[path][tier];
                        shapes << origins[path] << ',' << tiers[tier] << ','
                               << x.bytes << ',' << x.operations
                               << ",exact-unscoped-positive-growth-not-live-heap\n";
                    }
            }
            // Positive delta and absolute-new-size each reconcile independently
            // to the original RESIZE_GROWTH path, not all owner-zero bytes.
            {
                static const char* kinds[2]={"OLD_SIZE_ZERO","OLD_SIZE_NONZERO"};
                std::ofstream reallocs(outputRoot / "CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv",
                                      std::ios::trunc);
                if(reallocs) {
                    reallocs << "Dimension,ReallocOrigin,SizeTier,PositiveBytes,"
                                "AllocationOperations,Interpretation\n";
                    for(int origin=0;origin<2;origin++)
                        for(int tier=0;tier<6;tier++) {
                            const auto& delta=detail.reallocDeltas[origin][tier];
                            const auto& absolute=detail.reallocNewSizes[origin][tier];
                            reallocs << "POSITIVE_DELTA," << kinds[origin] << ','
                                     << tiers[tier] << ',' << delta.bytes << ','
                                     << delta.operations << ",realloc-accounting-not-retained-heap\n";
                            reallocs << "ABSOLUTE_NEW_SIZE," << kinds[origin] << ','
                                     << tiers[tier] << ',' << absolute.bytes << ','
                                     << absolute.operations << ",new-size-tier-positive-growth-bytes\n";
                        }
                }
            }
            std::ofstream scopes(outputRoot / "CET_Runtime_Profile_GGC_UnscopedBoundaries.csv",
                                 std::ios::trunc);
            if(scopes) {
                scopes << "NativeExecutionBoundary,PositiveBytes,AllocationOperations,"
                          "Interpretation\n";
                for(int bridge=0;bridge<6;bridge++) {
                    const auto& x=detail.bridges[bridge];
                    scopes << bridges[bridge] << ',' << x.bytes << ','
                           << x.operations
                           << ",CET-outer-boundary-not-mod-or-function-producer\n";
                }
            }
            std::vector<GGCUnscopedPathSecond> bins(1201);
            const int count=ggc_trace_unscoped_path_seconds_read(
                bins.data(),static_cast<int>(bins.size()));
            std::ofstream timeline(outputRoot / "CET_Runtime_Profile_GGC_UnscopedPathTimeline.csv",
                                   std::ios::trunc);
            if(timeline) {
                timeline << "CaptureSecond,Dimension,Category,PositiveBytes,"
                            "AllocationOperations,Window,Interpretation\n";
                for(int i=0;i<count;i++) {
                    for(int path=0;path<2;path++) {
                        const auto& x=bins[static_cast<size_t>(i)].paths[path];
                        if(x.operations)
                            timeline << i << ",ALLOCATOR_PATH," << origins[path] << ','
                                     << x.bytes << ',' << x.operations << ','
                                     << (i==1200?"OVERFLOW":"ONE_SECOND")
                                     << ",positive-growth-not-GC-cause\n";
                    }
                    for(int origin=0;origin<2;origin++) {
                        const auto& x=bins[static_cast<size_t>(i)].reallocOrigins[origin];
                        if(x.operations)
                            timeline << i << ",REALLOC_ORIGIN,"
                                     << (origin==0?"OLD_SIZE_ZERO":"OLD_SIZE_NONZERO")
                                     << ',' << x.bytes << ',' << x.operations << ','
                                     << (i==1200?"OVERFLOW":"ONE_SECOND")
                                     << ",subset-of-RESIZE_GROWTH-not-additive\n";
                    }
                    for(int bridge=0;bridge<6;bridge++) {
                        const auto& x=bins[static_cast<size_t>(i)].bridges[bridge];
                        if(x.operations)
                            timeline << i << ",CET_BOUNDARY," << bridges[bridge] << ','
                                     << x.bytes << ',' << x.operations << ','
                                     << (i==1200?"OVERFLOW":"ONE_SECOND")
                                     << ",CET-boundary-not-source-ownership\n";
                    }
                }
            }
            std::vector<GGCUnscopedNativeSample> samples(2048);
            uint64_t dropped=0;
            const int observed=ggc_trace_unscoped_samples_read(samples.data(),
                static_cast<int>(samples.size()),&dropped);
            std::ofstream stack(outputRoot / "CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv",
                                std::ios::trunc);
            if(stack) {
                stack << "CaptureMs,OperationSequence,PositiveBytes,AllocatorPath,"
                         "VmExecutionState,NativeExecutionBoundary,StackDepth,"
                         "Frame1,Frame2,Frame3,Frame4,Frame5,Frame6,DroppedSamples,"
                         "Interpretation,ReallocOrigin,OldSize,NewSize\n";
                for(int i=0;i<observed;i++) {
                    const auto& s=samples[static_cast<size_t>(i)];
                    stack << s.captureMs << ',' << s.operationSequence << ','
                          << s.positiveBytes << ',' << origins[s.allocatorPath < 2?s.allocatorPath:0]
                          << ',' << states[s.vmState < 8?s.vmState:7] << ','
                          << bridges[s.bridgeContext < 6?s.bridgeContext:0] << ','
                          << s.depth;
                    for(int frame=0;frame<6;frame++) {
                        std::string symbol="NO_FRAME";
                        if(static_cast<uint32_t>(frame)<s.depth && s.frames[frame]) {
                            HMODULE module=nullptr;
                            if(GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                reinterpret_cast<LPCSTR>(s.frames[frame]),&module) && module) {
                                char name[MAX_PATH]{};
                                GetModuleFileNameA(module,name,MAX_PATH);
                                std::string path(name);
                                const size_t slash=path.find_last_of("\\/");
                                if(slash!=std::string::npos) path=path.substr(slash+1);
                                std::ostringstream address;
                                address << path << "+0x" << std::hex
                                        << (reinterpret_cast<uintptr_t>(s.frames[frame]) -
                                            reinterpret_cast<uintptr_t>(module));
                                symbol=address.str();
                            } else symbol="UNKNOWN_OR_JIT_CODE";
                        }
                        stack << ',' << Csv(symbol);
                    }
                    stack << ',' << dropped
                          << ",bounded-native-unwind-sample-not-exact-producer,"
                          << (s.reallocOrigin==0?"OLD_SIZE_ZERO":
                              s.reallocOrigin==1?"OLD_SIZE_NONZERO":"NOT_REALLOC")
                          << ',' << s.oldSize << ',' << s.newSize << '\n';
                }
            }
        }

        // Function attribution reuses already-sampled G-CET Lua call/return
        // hooks. Reports sampled inclusive-call windows, NOT total allocations.
        {
            std::ofstream f(outputRoot / "CET_Runtime_Profile_GGC_Functions.csv", std::ios::trunc);
            if (f) {
                f << "RegistrationId,ProfileEpoch,FunctionName,SourceFile,SourceLineStart,"
                     "SourceLineEnd,SampledCalls,SampledExclusiveAllocatedBytes,"
                     "SampledExclusiveAllocationOperations,Interpretation\n";
                for (const auto& row : deepFunctionRows) {
                    if (!row.SampledExclusiveAllocBytes &&
                        !row.SampledExclusiveAllocations) continue;
                    f << row.RegistrationId << ',' << row.ProfileEpoch << ','
                      << Csv(row.FunctionName) << ',' << Csv(row.SourceFile) << ','
                      << row.SourceLineStart << ',' << row.SourceLineEnd << ','
                      << row.Calls << ',' << row.SampledExclusiveAllocBytes << ','
                      << row.SampledExclusiveAllocations << ','
                      << "sampled-callback-debug-hooks-not-global-function-census\n";
                }
            }
        }

        // Phase state-machine timing and exceptional >2 ms individual operations.
        {
            static const char* names[] = {
                "pause", "propagate", "atomic", "sweepstring", "sweep", "finalize"
            };
            GCETGCStats collector{};
            GCETGCPhaseStats phases{};
            gcet_gc_stats_read(&collector);
            gcet_gc_phase_read(&phases);
            const double ticksToMs = collector.frequency
                ? 1000.0 / static_cast<double>(collector.frequency) : 0.0;
            std::ofstream f(outputRoot / "CET_Runtime_Profile_GC_Phases.csv", std::ios::trunc);
            if (f) {
                f << "Phase,Operations,WallMs,Interpretation\n";
                f << std::fixed << std::setprecision(6);
                for (size_t i = 0; i < 6; ++i)
                    f << names[i] << ',' << phases.calls[i] << ','
                      << phases.ticks[i] * ticksToMs
                      << ",internal-state-operation-wall-time-nested-in-collector\n";
            }
            std::vector<GCETGCSlowEvent> events(2048);
            int dropped = 0;
            const int found = gcet_gc_slow_read(events.data(),
                static_cast<int>(events.size()), &dropped);
            std::ofstream slow(outputRoot / "CET_Runtime_Profile_GC_SlowSteps.csv", std::ios::trunc);
            if (slow) {
                slow << "CaptureStartMs,CaptureEndMs,DurationMs,ThreadId,RegistrationId,"
                        "PhaseBefore,PhaseAfter,HeapBeforeMiB,HeapAfterMiB,ThresholdMiB,"
                        "DebtKiB,Pause,StepMul,DroppedEventsAtDump,Interpretation\n";
                slow << std::fixed << std::setprecision(6);
                for (int i = 0; i < found; ++i) {
                    const auto& e = events[static_cast<size_t>(i)];
                    const char* before = e.phaseBefore < 6 ? names[e.phaseBefore] : "unknown";
                    const char* after = e.phaseAfter < 6 ? names[e.phaseAfter] : "unknown";
                    slow << e.startNs / 1'000'000.0 << ',' << e.endNs / 1'000'000.0
                         << ',' << (e.endNs - e.startNs) / 1'000'000.0 << ','
                         << e.threadId << ',' << e.registrationId << ','
                         << before << ',' << after << ','
                         << e.heapBefore / 1048576.0 << ',' << e.heapAfter / 1048576.0
                         << ',' << e.threshold / 1048576.0 << ','
                         << e.debt / 1024.0 << ',' << e.pause << ',' << e.stepmul
                         << ',' << dropped
                         << ",exact-internal-phase-wall-time-trigger-site-not-allocator-owner\n";
                }
            }
        }

        // MARKERS: capture-relative + wall-clock anchors for phase boundaries
        // and easier external timeline alignment.
        // -----------------------------------------------------------------
        {
            std::sort(markerRows.begin(), markerRows.end(),
                      [](const MarkerEvent& a, const MarkerEvent& b)
                      {
                          return a.Sequence < b.Sequence;
                      });
            const auto path = outputRoot / "CET_Runtime_Profile_Markers.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "Sequence,CaptureMs,UnixEpochMs,Label,DroppedMarkersAtDump\n";
                f << std::fixed << std::setprecision(6);
                for (const auto& marker : markerRows)
                {
                    f << marker.Sequence << ','
                      << (static_cast<double>(marker.CaptureNs) / 1'000'000.0) << ','
                      << marker.UnixEpochMs << ','
                      << Csv(marker.Label) << ','
                      << droppedMarkerEvents << '\n';
                }
            }
        }

        // -----------------------------------------------------------------
        // SPIKES: chronological exceptional callbacks only.
        //
        // CaptureStartMs/CaptureEndMs use the profiler's active-capture
        // timeline (paused time excluded). A row means a callback overlapped
        // this interval and accumulated the shown exclusive elapsed time; it
        // does NOT prove that the callback itself caused an external stall.
        // -----------------------------------------------------------------
        {
            std::sort(spikeRows.begin(), spikeRows.end(),
                      [](const SpikeRow& a, const SpikeRow& b)
                      {
                          if (a.CaptureStartNs != b.CaptureStartNs)
                              return a.CaptureStartNs < b.CaptureStartNs;
                          return a.Sequence < b.Sequence;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Spikes.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                f << "Sequence,Frame,CaptureStartMs,CaptureEndMs,InclusiveMs,"
                     "ExclusiveMs,ChildMs,RegistrationId,Mod,Kind,Target,"
                     "SourceFile,SourceLineStart,SourceLineEnd,ThreadId,"
                     "ThresholdMs,DroppedEventsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& spike : spikeRows)
                {
                    f << spike.Sequence << ','
                      << spike.Frame << ','
                      << (static_cast<double>(spike.CaptureStartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(spike.CaptureEndNs) / 1'000'000.0) << ','
                      << (static_cast<double>(spike.InclusiveNs) / 1'000'000.0) << ','
                      << (static_cast<double>(spike.ExclusiveNs) / 1'000'000.0) << ','
                      << (static_cast<double>(spike.ChildNs) / 1'000'000.0) << ','
                      << spike.RegistrationId << ','
                      << Csv(spike.Mod) << ','
                      << Csv(spike.Kind) << ','
                      << Csv(spike.Target) << ','
                      << Csv(spike.SourceFile) << ','
                      << spike.SourceLineStart << ','
                      << spike.SourceLineEnd << ','
                      << spike.ThreadId << ','
                      << spikeThresholdMs << ','
                      << droppedSpikeEvents << ','
                      << "correlation-only-not-causation"
                      << '\n';
                }
            }
        }

        // -----------------------------------------------------------------
        // SCHEDULER BY JOB: cooperative 0-Engine child attribution.
        //
        // These rows are supplemental inclusive job wall-time measurements
        // inside 0-Engine::onUpdate. Do NOT add them to normal per-mod totals.
        // -----------------------------------------------------------------
        {
            std::sort(schedulerJobRows.begin(), schedulerJobRows.end(),
                      [](const SchedulerJobRow& a, const SchedulerJobRow& b)
                      {
                          if (a.TotalNs != b.TotalNs)
                              return a.TotalNs > b.TotalNs;
                          return a.Calls > b.Calls;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Scheduler_ByJob.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                f << "Owner,JobType,Job,IntervalValue,IntervalUnit,Calls,"
                     "CallsPerSecond,TotalMs,MsPerSecond,MeasuredOneCorePct,"
                     "AvgUs,MaxMs,ElapsedSeconds,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : schedulerJobRows)
                {
                    const double totalMs =
                        static_cast<double>(row.TotalNs) / 1'000'000.0;
                    const double msPerSec = totalMs / elapsedSec;
                    const double callsPerSec =
                        static_cast<double>(row.Calls) / elapsedSec;
                    const double avgUs =
                        row.Calls
                            ? static_cast<double>(row.TotalNs) /
                                  1'000.0 /
                                  static_cast<double>(row.Calls)
                            : 0.0;
                    const double maxMs =
                        static_cast<double>(row.MaxNs) / 1'000'000.0;

                    f << Csv(row.Owner) << ','
                      << Csv(row.JobType) << ','
                      << Csv(row.Job) << ','
                      << row.IntervalValue << ','
                      << Csv(row.IntervalUnit) << ','
                      << row.Calls << ','
                      << callsPerSec << ','
                      << totalMs << ','
                      << msPerSec << ','
                      << (msPerSec / 10.0) << ','
                      << avgUs << ','
                      << maxMs << ','
                      << elapsedSec << ','
                      << "inclusive-inside-0-engine-do-not-add-to-mod-totals"
                      << '\n';
                }
            }
        }

        // -----------------------------------------------------------------
        // SCHEDULER SPIKES: individual jobs over the dedicated threshold.
        // -----------------------------------------------------------------
        {
            std::sort(schedulerSpikeRows.begin(), schedulerSpikeRows.end(),
                      [](const SchedulerSpikeRow& a, const SchedulerSpikeRow& b)
                      {
                          if (a.CaptureStartNs != b.CaptureStartNs)
                              return a.CaptureStartNs < b.CaptureStartNs;
                          return a.Sequence < b.Sequence;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Scheduler_Spikes.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                f << "Sequence,Frame,CaptureStartMs,CaptureEndMs,DurationMs,"
                     "Owner,JobType,Job,IntervalValue,IntervalUnit,"
                     "ThresholdMs,DroppedEventsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : schedulerSpikeRows)
                {
                    f << row.Sequence << ','
                      << row.Frame << ','
                      << (static_cast<double>(row.CaptureStartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.CaptureEndNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.DurationNs) / 1'000'000.0) << ','
                      << Csv(row.Owner) << ','
                      << Csv(row.JobType) << ','
                      << Csv(row.Job) << ','
                      << row.IntervalValue << ','
                      << Csv(row.IntervalUnit) << ','
                      << schedulerJobSpikeThresholdMs << ','
                      << droppedSchedulerSpikeEvents << ','
                      << "scheduler-job-inclusive-wall-time-inside-0-engine"
                      << '\n';
                }
            }
        }

        // -----------------------------------------------------------------
        // SCHEDULER FRAME BURSTS: all jobs from frames whose combined measured
        // scheduler job time crosses the burst threshold.
        // -----------------------------------------------------------------
        {
            std::sort(schedulerBurstRows.begin(), schedulerBurstRows.end(),
                      [](const SchedulerBurstRow& a, const SchedulerBurstRow& b)
                      {
                          if (a.Sequence != b.Sequence)
                              return a.Sequence < b.Sequence;
                          return a.SampleIndex < b.SampleIndex;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_Scheduler_FrameBursts.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                f << "BurstSequence,Frame,CaptureStartMs,CaptureEndMs,"
                     "SchedulerWallMs,TotalJobMs,JobCount,SampleIndex,"
                     "Owner,JobType,Job,IntervalValue,IntervalUnit,JobMs,"
                     "JobSharePct,ThresholdMs,DroppedBurstsAtDump,"
                     "Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : schedulerBurstRows)
                {
                    const double share =
                        row.TotalJobNs
                            ? static_cast<double>(row.JobDurationNs) /
                                  static_cast<double>(row.TotalJobNs) * 100.0
                            : 0.0;

                    f << row.Sequence << ','
                      << row.Frame << ','
                      << (static_cast<double>(row.CaptureStartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.CaptureEndNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.SchedulerWallNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.TotalJobNs) / 1'000'000.0) << ','
                      << row.JobCount << ','
                      << row.SampleIndex << ','
                      << Csv(row.Owner) << ','
                      << Csv(row.JobType) << ','
                      << Csv(row.Job) << ','
                      << row.IntervalValue << ','
                      << Csv(row.IntervalUnit) << ','
                      << (static_cast<double>(row.JobDurationNs) / 1'000'000.0) << ','
                      << share << ','
                      << schedulerFrameBurstThresholdMs << ','
                      << droppedSchedulerFrameBursts << ','
                      << "all-jobs-in-combined-scheduler-burst-frame"
                      << '\n';
                }
            }
        }

        // -----------------------------------------------------------------
        // DETAIL: every mod/kind/target registration.
        // -----------------------------------------------------------------
        {
            const auto path =
                outputRoot / "CET_Runtime_Profile_Detail.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                f << "RegistrationId,Mod,Kind,Target,SourceFile,SourceLineStart,SourceLineEnd,"
                     "Calls,CallsPerSecond,"
                     "InclusiveTotalMs,ExclusiveTotalMs,"
                     "InclusiveMsPerSecond,ExclusiveMsPerSecond,"
                     "MeasuredOneCorePct,AvgExclusiveUs,"
                     "MaxInclusiveMs,MaxExclusiveMs,"
                     "MeasuredExclusiveSharePct,ElapsedSeconds,Coverage\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : rows)
                    WriteRow(f, row, elapsedSec, grandExclusiveNs);
            }
        }

        struct Summary
        {
            uint64_t Calls{};
            uint64_t InclusiveNs{};
            uint64_t ExclusiveNs{};
            uint64_t MaxInclusiveNs{};
            uint64_t MaxExclusiveNs{};
        };

        auto add = [](Summary& s, const Row& r)
        {
            s.Calls += r.Calls;
            s.InclusiveNs += r.InclusiveNs;
            s.ExclusiveNs += r.ExclusiveNs;
            s.MaxInclusiveNs =
                std::max(s.MaxInclusiveNs, r.MaxInclusiveNs);
            s.MaxExclusiveNs =
                std::max(s.MaxExclusiveNs, r.MaxExclusiveNs);
        };

        // -----------------------------------------------------------------
        // BY MOD + KIND: separates onUpdate/onDraw/Observe/Override etc.
        // -----------------------------------------------------------------
        {
            std::unordered_map<std::string, Summary> grouped;
            std::unordered_map<std::string, std::pair<std::string,std::string>>
                labels;

            for (const auto& row : rows)
            {
                const std::string key = row.Mod + "\x1f" + row.Kind;
                add(grouped[key], row);
                labels[key] = {row.Mod, row.Kind};
            }

            struct GroupRow
            {
                std::string Mod;
                std::string Kind;
                Summary S;
            };

            std::vector<GroupRow> groups;
            groups.reserve(grouped.size());

            for (const auto& [key, s] : grouped)
            {
                const auto& label = labels[key];
                groups.push_back({label.first, label.second, s});
            }

            std::sort(groups.begin(), groups.end(),
                      [](const GroupRow& a, const GroupRow& b)
                      {
                          return a.S.ExclusiveNs > b.S.ExclusiveNs;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_ByModKind.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                WriteSummaryHeader(f, "Mod,Kind");
                f << std::fixed << std::setprecision(6);

                for (const auto& g : groups)
                {
                    f << Csv(g.Mod) << ',' << Csv(g.Kind) << ',';
                    WriteSummaryMetrics(
                        f, g.S, elapsedSec, grandExclusiveNs);
                }
            }
        }

        // -----------------------------------------------------------------
        // BY MOD: primary ranking.
        // -----------------------------------------------------------------
        {
            std::unordered_map<std::string, Summary> grouped;

            for (const auto& row : rows)
                add(grouped[row.Mod], row);

            struct ModRow
            {
                std::string Mod;
                Summary S;
            };

            std::vector<ModRow> mods;
            mods.reserve(grouped.size());

            for (const auto& [mod, s] : grouped)
                mods.push_back({mod, s});

            std::sort(mods.begin(), mods.end(),
                      [](const ModRow& a, const ModRow& b)
                      {
                          return a.S.ExclusiveNs > b.S.ExclusiveNs;
                      });

            const auto path =
                outputRoot / "CET_Runtime_Profile_ByMod.csv";
            std::ofstream f(path, std::ios::trunc);

            if (f)
            {
                WriteSummaryHeader(f, "Mod");
                f << std::fixed << std::setprecision(6);

                for (const auto& m : mods)
                {
                    f << Csv(m.Mod) << ',';
                    WriteSummaryMetrics(
                        f, m.S, elapsedSec, grandExclusiveNs);
                }
            }
        }

        // Normal controls pause before exporting. Mark that paused capture
        // generation as exported so shutdown/duplicate Dump calls cannot recreate
        // empty/header-only files. If a console caller dumps while still RUNNING,
        // leave it eligible for the final STOP export.
        {
            std::lock_guard lock(m_mutex);
            if (m_state.load(std::memory_order_relaxed) == CaptureState::Paused &&
                m_captureGeneration == dumpGeneration)
            {
                m_dumpedGeneration = dumpGeneration;
            }
        }
    }

private:
    static DeepThreadState& DeepStateForThread()
    {
        static thread_local DeepThreadState state;
        return state;
    }

    static uint64_t HashString64(const std::string& aValue)
    {
        uint64_t hash = 1469598103934665603ULL;
        for (const unsigned char c : aValue)
        {
            hash ^= static_cast<uint64_t>(c);
            hash *= 1099511628211ULL;
        }
        return hash;
    }

    static uint64_t MixPathHash(uint64_t aHash, uint64_t aToken)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            aHash ^= (aToken >> shift) & 0xFFULL;
            aHash *= 1099511628211ULL;
        }
        return aHash;
    }

    static std::string DeepFunctionKey(
        uint64_t aIdentity,
        const std::string& aSource,
        int aStart,
        int aEnd,
        const std::string& aName,
        const std::string& aWhat)
    {
        std::ostringstream oss;
        oss << std::hex << aIdentity << std::dec
            << '|' << aSource
            << '|' << aStart
            << '|' << aEnd
            << '|' << aName
            << '|' << aWhat;
        return oss.str();
    }

    static DeepFrame DescribeDeepFrame(
        lua_State* aState,
        lua_Debug* aDebug,
        uint32_t aDepth,
        const std::string& aParentKey)
    {
        DeepFrame frame;
        frame.Depth = aDepth;
        frame.ParentFunctionKey = aParentKey;

        if (!aState || !aDebug || lua_getinfo(aState, "nSf", aDebug) == 0)
        {
            frame.FunctionKey = "<unresolved>";
            frame.FunctionName = "<unresolved>";
            frame.What = "unknown";
            return frame;
        }

        const void* identity = lua_topointer(aState, -1);
        lua_pop(aState, 1);
        frame.FunctionIdentity =
            static_cast<uint64_t>(reinterpret_cast<uintptr_t>(identity));

        const char* source = aDebug->source ? aDebug->source : "";
        if (source[0] == '@')
            ++source;

        frame.SourceFile = source;
        frame.SourceHash = HashString64(frame.SourceFile);
        frame.SourceLineStart = aDebug->linedefined;
        frame.SourceLineEnd = aDebug->lastlinedefined;
        frame.FunctionName =
            aDebug->name && aDebug->name[0] ? aDebug->name : "<anonymous>";
        frame.What =
            aDebug->what && aDebug->what[0] ? aDebug->what : "unknown";
        frame.FunctionKey = DeepFunctionKey(
            frame.FunctionIdentity,
            frame.SourceFile,
            frame.SourceLineStart,
            frame.SourceLineEnd,
            frame.FunctionName,
            frame.What);

        lua_Debug caller{};
        if (lua_getstack(aState, 1, &caller) != 0 &&
            lua_getinfo(aState, "Sl", &caller) != 0)
        {
            const char* callerSource = caller.source ? caller.source : "";
            if (callerSource[0] == '@')
                ++callerSource;
            frame.CallsiteSourceFile = callerSource;
            frame.CallsiteLine = caller.currentline;
        }

        return frame;
    }

    static void RecordDeepLine(
        DeepThreadState& aState,
        lua_State* aLuaState,
        lua_Debug* aDebug)
    {
        if (!aLuaState || !aDebug || aDebug->currentline <= 0)
            return;

        ++aState.LineEvents;

        // LINE hooks identify the currently executing Lua activation. Do not
        // infer its source from our CALL/RETURN stack: LuaJIT can interleave C
        // frames/metamethods in ways that leave the top tracked frame as =[C].
        // Query the line event's own activation record instead.
        std::string sourceFile;
        if (lua_getinfo(aLuaState, "S", aDebug) != 0)
        {
            const char* source = aDebug->source ? aDebug->source : "";
            if (source[0] == '@')
                ++source;
            sourceFile = source;
        }

        if (sourceFile.empty() || sourceFile == "=[C]")
        {
            ++aState.UnresolvedLineEvents;
            if (!aState.Frames.empty())
                sourceFile = aState.Frames.back().SourceFile;
        }

        if (sourceFile.empty())
            sourceFile = "<unresolved>";

        const int line = aDebug->currentline;
        const uint64_t sourceHash = HashString64(sourceFile);
        const uint64_t token =
            sourceHash ^
            (static_cast<uint64_t>(static_cast<uint32_t>(line)) *
             0x9E3779B185EBCA87ULL);

        if (token != aState.LastPathToken &&
            aState.PathTransitions < MaxDeepPathTransitions)
        {
            aState.PathFingerprint =
                MixPathHash(aState.PathFingerprint, token);
            aState.LastPathToken = token;
            ++aState.PathTransitions;
        }

        const std::string key =
            sourceFile + "\x1f" + std::to_string(line);
        auto found = aState.Lines.find(key);
        if (found == aState.Lines.end())
        {
            if (aState.Lines.size() >= MaxDeepUniqueLinesPerSample)
            {
                aState.LineRowsTruncated = true;
                return;
            }

            DeepLineHit hit;
            hit.SourceFile = sourceFile;
            hit.Line = line;
            hit.Hits = 1;
            aState.Lines.emplace(key, std::move(hit));
        }
        else
        {
            ++found->second.Hits;
        }
    }

    static void CompleteDeepFrame(
        DeepThreadState& aState,
        Clock::time_point aEnd)
    {
        if (aState.Frames.empty())
            return;

        DeepFrame frame = std::move(aState.Frames.back());
        aState.Frames.pop_back();

        const uint64_t rawNs = static_cast<uint64_t>(
            std::max<int64_t>(
                0,
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    aEnd - frame.Start).count()));

        const uint64_t profilerNs =
            std::min(rawNs, frame.ProfilerNs);
        const uint64_t inclusiveNs = rawNs - profilerNs;

        const uint64_t excludedNs =
            std::min(rawNs, frame.ChildRawNs + profilerNs);
        const uint64_t exclusiveNs = rawNs - excludedNs;

        const uint64_t allocBytes = gcet_gc_alloc_bytes() - frame.AllocStartBytes;
        const uint64_t allocOps = gcet_gc_alloc_ops() - frame.AllocStartOps;
        const uint64_t ownBytes = allocBytes > frame.ChildAllocBytes
            ? allocBytes - frame.ChildAllocBytes : 0;
        const uint64_t ownOps = allocOps > frame.ChildAllocOps
            ? allocOps - frame.ChildAllocOps : 0;
        const std::string localKey = frame.FunctionKey;
        auto& aggregate = aState.Functions[localKey];
        if (aggregate.FunctionKey.empty())
        {
            aggregate.RegistrationId =
                aState.CounterPtr ? aState.CounterPtr->RegistrationId : 0;
            aggregate.ProfileEpoch = aState.ProfileEpoch;
            aggregate.FunctionIdentity = frame.FunctionIdentity;
            aggregate.FunctionKey = frame.FunctionKey;
            aggregate.FunctionName = frame.FunctionName;
            aggregate.What = frame.What;
            aggregate.SourceFile = frame.SourceFile;
            aggregate.SourceLineStart = frame.SourceLineStart;
            aggregate.SourceLineEnd = frame.SourceLineEnd;
            aggregate.MinDepth = frame.Depth;
        }

        ++aggregate.Calls;
        aggregate.InclusiveNs += inclusiveNs;
        aggregate.ExclusiveNs += exclusiveNs;
        aggregate.SampledExclusiveAllocBytes += ownBytes;
        aggregate.SampledExclusiveAllocations += ownOps;
        aggregate.MaxInclusiveNs =
            std::max(aggregate.MaxInclusiveNs, inclusiveNs);
        aggregate.MinDepth =
            std::min(aggregate.MinDepth, frame.Depth);

        if (frame.Depth == 0)
            aState.RootNetNs += inclusiveNs;

        if (!frame.ParentFunctionKey.empty())
        {
            const std::string edgeKey =
                frame.ParentFunctionKey + "\x1f" + frame.FunctionKey;
            auto& edge = aState.Edges[edgeKey];
            if (edge.ParentFunctionKey.empty())
            {
                edge.RegistrationId =
                    aState.CounterPtr ? aState.CounterPtr->RegistrationId : 0;
                edge.ProfileEpoch = aState.ProfileEpoch;
                edge.ParentFunctionKey = frame.ParentFunctionKey;
                edge.ChildFunctionKey = frame.FunctionKey;
            }
            ++edge.Calls;
            edge.ChildInclusiveNs += inclusiveNs;

            if (frame.CallsiteLine > 0 &&
                !frame.CallsiteSourceFile.empty() &&
                frame.CallsiteSourceFile != "=[C]")
            {
                const std::string callsiteKey =
                    frame.ParentFunctionKey + "\x1f" +
                    frame.FunctionKey + "\x1f" +
                    frame.CallsiteSourceFile + "\x1f" +
                    std::to_string(frame.CallsiteLine);
                auto& callsite = aState.Callsites[callsiteKey];
                if (callsite.ChildFunctionKey.empty())
                {
                    callsite.SampleSequence = aState.SampleSequence;
                    callsite.RegistrationId =
                        aState.CounterPtr ? aState.CounterPtr->RegistrationId : 0;
                    callsite.ProfileEpoch = aState.ProfileEpoch;
                    callsite.Frame = aState.Frame;
                    callsite.FrameInvocationOrdinal = aState.FrameInvocationOrdinal;
                    callsite.CallerSourceFile = frame.CallsiteSourceFile;
                    callsite.CallerLine = frame.CallsiteLine;
                    callsite.ParentFunctionKey = frame.ParentFunctionKey;
                    callsite.ChildFunctionKey = frame.FunctionKey;
                }
                ++callsite.Calls;
                callsite.ChildInclusiveNs += inclusiveNs;
            }
        }

        if (!aState.Frames.empty()) {
            aState.Frames.back().ChildRawNs += rawNs;
            aState.Frames.back().ChildAllocBytes += allocBytes;
            aState.Frames.back().ChildAllocOps += allocOps;
        }
    }

    static void DeepLuaHook(lua_State* aState, lua_Debug* aDebug)
    {
        auto& deep = DeepStateForThread();
        if (!deep.Active || deep.State != aState || !aDebug)
            return;

        const auto hookStart = Clock::now();
        const int event = aDebug->event;

        if (event == LUA_HOOKLINE)
        {
            RecordDeepLine(deep, aState, aDebug);
        }
        else if (event == LUA_HOOKRET)
        {
            CompleteDeepFrame(deep, hookStart);
        }
#ifdef LUA_HOOKTAILCALL
        else if (event == LUA_HOOKTAILCALL)
        {
            CompleteDeepFrame(deep, hookStart);

            const std::string parentKey =
                deep.Frames.empty()
                    ? std::string()
                    : deep.Frames.back().FunctionKey;
            DeepFrame frame = DescribeDeepFrame(
                aState,
                aDebug,
                static_cast<uint32_t>(deep.Frames.size()),
                parentKey);
            frame.AllocStartBytes = gcet_gc_alloc_bytes();
            frame.AllocStartOps = gcet_gc_alloc_ops();
            deep.Frames.push_back(std::move(frame));
        }
#endif
        else if (event == LUA_HOOKCALL)
        {
            const std::string parentKey =
                deep.Frames.empty()
                    ? std::string()
                    : deep.Frames.back().FunctionKey;
            DeepFrame frame = DescribeDeepFrame(
                aState,
                aDebug,
                static_cast<uint32_t>(deep.Frames.size()),
                parentKey);
            frame.AllocStartBytes = gcet_gc_alloc_bytes();
            frame.AllocStartOps = gcet_gc_alloc_ops();
            deep.Frames.push_back(std::move(frame));
        }

        const auto hookEnd = Clock::now();
        const uint64_t hookNs = static_cast<uint64_t>(
            std::max<int64_t>(
                0,
                std::chrono::duration_cast<std::chrono::nanoseconds>(
                    hookEnd - hookStart).count()));

        if (event == LUA_HOOKCALL)
        {
            if (deep.Frames.size() >= 2)
                deep.Frames[deep.Frames.size() - 2].ProfilerNs += hookNs;
            if (!deep.Frames.empty())
                deep.Frames.back().Start = hookEnd;
        }
#ifdef LUA_HOOKTAILCALL
        else if (event == LUA_HOOKTAILCALL)
        {
            if (deep.Frames.size() >= 2)
                deep.Frames[deep.Frames.size() - 2].ProfilerNs += hookNs;
            if (!deep.Frames.empty())
                deep.Frames.back().Start = hookEnd;
        }
#endif
        else
        {
            if (!deep.Frames.empty())
                deep.Frames.back().ProfilerNs += hookNs;
        }

        ++deep.HookEvents;
        Scope::ExcludeProfilerOverhead(hookNs);
    }


    // Dynamic top-five allocation volume + next five distinct by operation count.
    // Selection runs only from existing infrequent profiling management callbacks.
    void MaybeRebalanceGcTracer()
    {
        if (!IsCapturing()) return;
        const auto now = Clock::now();
        const int64_t ticks = ClockTicksNs(now);
        auto prior = m_nextGcTracerRebalanceTicksNs.load(std::memory_order_relaxed);
        if (ticks < prior || !m_nextGcTracerRebalanceTicksNs.compare_exchange_strong(
            prior, ticks + static_cast<int64_t>(GcTracerRebalanceNs),
            std::memory_order_acq_rel, std::memory_order_relaxed)) return;
        if (FastCapturedNanoseconds(now) < GcTracerWarmupNs) return;
        std::lock_guard lock(m_mutex);
        struct Volume { std::string mod; uint64_t bytes{}, ops{}; };
        std::unordered_map<std::string, Volume> totals;
        for (const auto& [_, counter] : m_counters) {
            GGCTraceOwner stats{};
            ggc_trace_owner_read(counter->RegistrationId, &stats);
            auto& v = totals[counter->Mod];
            v.mod = counter->Mod;
            v.bytes += stats.bytes; v.ops += stats.allocations;
        }
        std::vector<Volume> bytes, ops;
        for (const auto& [_, v] : totals)
            if (v.bytes || v.ops) { bytes.push_back(v); ops.push_back(v); }
        std::sort(bytes.begin(), bytes.end(), [](const auto& a, const auto& b) {
            return a.bytes == b.bytes ? a.mod < b.mod : a.bytes > b.bytes;
        });
        std::sort(ops.begin(), ops.end(), [](const auto& a, const auto& b) {
            return a.ops == b.ops ? a.mod < b.mod : a.ops > b.ops;
        });
        std::unordered_map<std::string, bool> selected;
        for (size_t i = 0; i < std::min<size_t>(5, bytes.size()); ++i)
            selected[bytes[i].mod] = true;
        for (const auto& v : ops) {
            if (selected.size() >= 10) break;
            selected[v.mod] = true;
        }
        for (const auto& [_, counter] : m_counters) {
            const bool chosen = selected.count(counter->Mod) != 0;
            const bool wasFocused = counter->GcTracerFocused.exchange(
                chosen, std::memory_order_acq_rel);
            ggc_trace_focus_set(counter->RegistrationId, chosen ? 1 : 0);
            // A callback may have completed the CPU hotset before this GC
            // promotion. Give newly promoted owners a fresh bounded sampling
            // epoch instead of silently producing no function attribution.
            if (chosen && !wasFocused) {
                counter->DeepComplete.store(false, std::memory_order_relaxed);
                counter->DeepSpikeArmed.store(false, std::memory_order_relaxed);
                counter->DeepSamples.store(0, std::memory_order_relaxed);
                counter->DeepSampleTicker.store(0, std::memory_order_relaxed);
                counter->DeepTargetSamples.store(
                    DefaultDeepTargetSamples, std::memory_order_relaxed);
                counter->DeepProfileEpoch.fetch_add(1, std::memory_order_relaxed);
            }
            if (chosen && !counter->DeepSelected.load(std::memory_order_relaxed))
                counter->DeepSampleStride.store(32, std::memory_order_relaxed);
        }
    }

    void MaybeRebalanceDeepHotset()
    {
        if (!IsCapturing())
            return;

        const auto now = Clock::now();
        const int64_t nowTicks = ClockTicksNs(now);
        auto nextTicks =
            m_nextDeepRebalanceTicksNs.load(std::memory_order_relaxed);
        if (nowTicks < nextTicks)
            return;

        if (!m_nextDeepRebalanceTicksNs.compare_exchange_strong(
                nextTicks,
                nowTicks + static_cast<int64_t>(DeepRebalanceNs),
                std::memory_order_acq_rel,
                std::memory_order_relaxed))
        {
            return;
        }

        const uint64_t captureNs = FastCapturedNanoseconds(now);
        if (captureNs < DeepWarmupNs)
            return;

        struct Candidate
        {
            Counter* CounterPtr{};
            double Score{};
            double WindowCallsPerSecond{};
            double WindowMsPerSecond{};
        };

        std::vector<Candidate> candidates;
        std::lock_guard lock(m_mutex);

        const uint64_t previousCaptureNs = m_lastDeepRebalanceCaptureNs;
        const uint64_t windowNs =
            previousCaptureNs > 0 && captureNs > previousCaptureNs
                ? captureNs - previousCaptureNs
                : captureNs;
        m_lastDeepRebalanceCaptureNs = captureNs;

        const double windowSeconds =
            std::max(0.001, static_cast<double>(windowNs) / 1'000'000'000.0);

        for (auto& [_, counterPtr] : m_counters)
        {
            auto* counter = counterPtr.get();
            counter->DeepSelected.store(false, std::memory_order_relaxed);

            const uint64_t calls =
                counter->Calls.load(std::memory_order_relaxed);
            const uint64_t exclusive =
                counter->ExclusiveNs.load(std::memory_order_relaxed);
            const uint64_t previousCalls =
                counter->DeepLastWindowCalls.exchange(
                    calls, std::memory_order_relaxed);
            const uint64_t previousExclusive =
                counter->DeepLastWindowExclusiveNs.exchange(
                    exclusive, std::memory_order_relaxed);

            const uint64_t deltaCalls =
                calls >= previousCalls ? calls - previousCalls : calls;
            const uint64_t deltaExclusive =
                exclusive >= previousExclusive
                    ? exclusive - previousExclusive
                    : exclusive;

            const double callsPerSecond =
                static_cast<double>(deltaCalls) / windowSeconds;
            const double msPerSecond =
                static_cast<double>(deltaExclusive) /
                1'000'000.0 / windowSeconds;

            uint64_t spikeStride = 31;
            if (callsPerSecond >= 1000.0)
                spikeStride = 2047;
            else if (callsPerSecond >= 250.0)
                spikeStride = 1021;
            else if (callsPerSecond >= 60.0)
                spikeStride = 251;
            else if (callsPerSecond >= 10.0)
                spikeStride = 127;
            counter->DeepSpikeProbeStride.store(
                spikeStride, std::memory_order_relaxed);

            if (counter->DeepComplete.load(std::memory_order_relaxed))
            {
                const uint64_t baseline =
                    counter->DeepBaselineAvgExclusiveNs.load(
                        std::memory_order_relaxed);

                int32_t driftDirection = 0;
                if (baseline > 0 && deltaCalls >= 10 && msPerSecond >= 0.5)
                {
                    const uint64_t windowAverage =
                        deltaExclusive / std::max<uint64_t>(1, deltaCalls);
                    const double ratio =
                        static_cast<double>(windowAverage) /
                        static_cast<double>(baseline);

                    if (ratio >= 2.5)
                        driftDirection = 1;
                    else if (ratio <= 0.40)
                        driftDirection = -1;
                }

                if (driftDirection == 0)
                {
                    counter->DeepDriftDirection.store(
                        0, std::memory_order_relaxed);
                    counter->DeepDriftWindows.store(
                        0, std::memory_order_relaxed);
                }
                else
                {
                    const int32_t previousDirection =
                        counter->DeepDriftDirection.load(std::memory_order_relaxed);
                    uint32_t windows = 1;
                    if (previousDirection == driftDirection)
                    {
                        windows =
                            counter->DeepDriftWindows.load(std::memory_order_relaxed) + 1;
                    }

                    counter->DeepDriftDirection.store(
                        driftDirection, std::memory_order_relaxed);
                    counter->DeepDriftWindows.store(
                        windows, std::memory_order_relaxed);

                    if (windows >= DeepDriftWindowsRequired)
                    {
                        counter->DeepComplete.store(
                            false, std::memory_order_relaxed);
                        counter->DeepSpikeArmed.store(
                            false, std::memory_order_relaxed);
                        counter->DeepSamples.store(
                            0, std::memory_order_relaxed);
                        counter->DeepSampleTicker.store(
                            0, std::memory_order_relaxed);
                        counter->DeepTargetSamples.store(
                            DefaultDeepTargetSamples,
                            std::memory_order_relaxed);
                        counter->DeepReuseFromRegistrationId.store(
                            0, std::memory_order_relaxed);
                        counter->DeepEpochStartCalls.store(
                            0, std::memory_order_relaxed);
                        counter->DeepEpochStartExclusiveNs.store(
                            0, std::memory_order_relaxed);
                        counter->DeepBaselineAvgExclusiveNs.store(
                            0, std::memory_order_relaxed);
                        counter->DeepDriftDirection.store(
                            0, std::memory_order_relaxed);
                        counter->DeepDriftWindows.store(
                            0, std::memory_order_relaxed);
                        counter->DeepProfileEpoch.fetch_add(
                            1, std::memory_order_relaxed);
                    }
                }
            }

            if (counter->DeepComplete.load(std::memory_order_relaxed))
                continue;

            if (counter->Mod == "0-Engine" ||
                counter->Mod == "CETProfilerControls" ||
                calls < 2)
            {
                continue;
            }

            const double maxMs =
                static_cast<double>(
                    counter->MaxExclusiveNs.load(std::memory_order_relaxed)) /
                1'000'000.0;

            const bool sustained = msPerSecond >= 1.0;
            const bool exceptionalSpike = maxMs >= 5.0;
            const bool highTraffic =
                callsPerSecond >= 500.0 && msPerSecond >= 0.5;

            if (!sustained && !exceptionalSpike && !highTraffic)
                continue;

            double score = msPerSecond + std::min(maxMs, 100.0) * 0.35;
            if (highTraffic)
                score += msPerSecond * 0.20;

            uint64_t stride = 1;
            if (callsPerSecond >= 1000.0)
                stride = 512;
            else if (callsPerSecond >= 250.0)
                stride = 128;
            else if (callsPerSecond >= 60.0)
                stride = 32;
            else if (callsPerSecond >= 10.0)
                stride = 8;

            counter->DeepSampleStride.store(
                stride, std::memory_order_relaxed);

            uint64_t reusedFrom = 0;
            const uint64_t identity =
                counter->LuaFunctionIdentity.load(std::memory_order_relaxed);
            if (identity)
            {
                for (const auto& [__, otherPtr] : m_counters)
                {
                    const auto* other = otherPtr.get();
                    if (other == counter ||
                        other->Mod != counter->Mod ||
                        other->LuaFunctionIdentity.load(std::memory_order_relaxed) != identity ||
                        !other->DeepComplete.load(std::memory_order_relaxed))
                    {
                        continue;
                    }

                    reusedFrom = other->RegistrationId;
                    break;
                }
            }

            if (reusedFrom)
            {
                counter->DeepReuseFromRegistrationId.store(
                    reusedFrom, std::memory_order_relaxed);
                counter->DeepTargetSamples.store(
                    ReusedDeepTargetSamples, std::memory_order_relaxed);
                score *= 0.35;
            }
            else
            {
                counter->DeepReuseFromRegistrationId.store(
                    0, std::memory_order_relaxed);
                counter->DeepTargetSamples.store(
                    DefaultDeepTargetSamples, std::memory_order_relaxed);
            }

            candidates.push_back({
                counter,
                score,
                callsPerSecond,
                msPerSecond
            });
        }

        std::sort(candidates.begin(), candidates.end(),
                  [](const Candidate& a, const Candidate& b)
                  {
                      if (a.Score != b.Score)
                          return a.Score > b.Score;
                      if (a.WindowMsPerSecond != b.WindowMsPerSecond)
                          return a.WindowMsPerSecond > b.WindowMsPerSecond;
                      return a.WindowCallsPerSecond > b.WindowCallsPerSecond;
                  });

        const size_t selected =
            std::min(MaxDeepHotRegistrations, candidates.size());
        for (size_t i = 0; i < selected; ++i)
        {
            candidates[i].CounterPtr->DeepSelected.store(
                true, std::memory_order_release);
        }
    }

    void ResetDeepLocked()
    {
        m_deepFunctions.clear();
        m_deepEdges.clear();
        m_deepSamples.clear();
        m_deepCallsites.clear();
        m_deepLines.clear();
        m_droppedDeepSamples = 0;
        m_droppedDeepLines = 0;
        m_droppedDeepCallsites = 0;
        m_nextDeepSampleSequence.store(0, std::memory_order_relaxed);
        m_lastDeepRebalanceCaptureNs = 0;
        m_nextGcTracerRebalanceTicksNs.store(0, std::memory_order_relaxed);
        m_nextDeepRebalanceTicksNs.store(0, std::memory_order_relaxed);

        for (auto& [_, counter] : m_counters)
        {
            counter->DeepSelected.store(false, std::memory_order_relaxed);
            counter->GcTracerFocused.store(false, std::memory_order_relaxed);
            counter->DeepComplete.store(false, std::memory_order_relaxed);
            counter->DeepProfileEpoch.store(1, std::memory_order_relaxed);
            counter->DeepSamples.store(0, std::memory_order_relaxed);
            counter->DeepTargetSamples.store(
                DefaultDeepTargetSamples, std::memory_order_relaxed);
            counter->DeepSampleStride.store(1, std::memory_order_relaxed);
            counter->DeepSampleTicker.store(0, std::memory_order_relaxed);
            counter->DeepHookConflicts.store(0, std::memory_order_relaxed);
            counter->DeepReuseFromRegistrationId.store(
                0, std::memory_order_relaxed);
            counter->DeepEpochStartCalls.store(0, std::memory_order_relaxed);
            counter->DeepEpochStartExclusiveNs.store(
                0, std::memory_order_relaxed);
            counter->DeepBaselineAvgExclusiveNs.store(
                0, std::memory_order_relaxed);
            counter->DeepLastWindowCalls.store(0, std::memory_order_relaxed);
            counter->DeepLastWindowExclusiveNs.store(
                0, std::memory_order_relaxed);
            counter->DeepSpikeArmed.store(false, std::memory_order_relaxed);
            counter->DeepSpikeProbeStride.store(251, std::memory_order_relaxed);
            counter->DeepSpikeProbeTicker.store(0, std::memory_order_relaxed);
            counter->DeepSpikeProbeSamples.store(0, std::memory_order_relaxed);
            counter->DeepSpikeCaptures.store(0, std::memory_order_relaxed);
            counter->DeepDriftWindows.store(0, std::memory_order_relaxed);
            counter->DeepDriftDirection.store(0, std::memory_order_relaxed);
            counter->DeepHeapSamples.store(0, std::memory_order_relaxed);
            counter->DeepHeapPositiveDeltaBytes.store(0, std::memory_order_relaxed);
            counter->DeepHeapReclaimedDeltaBytes.store(0, std::memory_order_relaxed);
            counter->DeepHeapNetDeltaBytes.store(0, std::memory_order_relaxed);
            counter->DeepHeapMaxPositiveDeltaBytes.store(0, std::memory_order_relaxed);
            counter->DeepHeapMaxReclaimedDeltaBytes.store(0, std::memory_order_relaxed);
        }

        auto& deep = DeepStateForThread();
        if (deep.Active && deep.State)
        {
            lua_sethook(
                deep.State,
                deep.PreviousHook,
                deep.PreviousMask,
                deep.PreviousCount);
        }
        deep = {};
    }

    Counter* RegisterLocked(const std::string& aMod,
                            const std::string& aKind,
                            const std::string& aTarget,
                            const std::string& aInstanceKey = {})
    {
        const std::string normalizedMod =
            aMod.empty() ? "<unknown>" : aMod;
        std::string key =
            normalizedMod + "\x1f" + aKind + "\x1f" + aTarget;
        if (!aInstanceKey.empty())
            key += "\x1f" + aInstanceKey;

        const auto found = m_counters.find(key);
        if (found != m_counters.end())
            return found->second.get();

        auto counter = std::make_unique<Counter>();
        counter->RegistrationId = ++m_nextRegistrationId;
        counter->Mod = normalizedMod;
        counter->Kind = aKind;
        counter->Target = aTarget;
        counter->DeepTargetSamples.store(
            DefaultDeepTargetSamples, std::memory_order_relaxed);
        counter->DeepSpikeProbeStride.store(
            251, std::memory_order_relaxed);

        auto timelineFound = m_timelineMods.find(normalizedMod);
        if (timelineFound == m_timelineMods.end())
        {
            auto owner = std::make_unique<TimelineMod>();
            owner->Mod = normalizedMod;
            timelineFound = m_timelineMods.emplace(
                normalizedMod, std::move(owner)).first;
        }
        counter->TimelineOwner = timelineFound->second.get();

        Counter* raw = counter.get();
        m_counters.emplace(key, std::move(counter));
        return raw;
    }

    CETRuntimeProfiler()
        : m_segmentStarted(Clock::now())
    {
        // Fixed-capacity reserve keeps spike capture allocation-free once a
        // capture is running. The hard cap also prevents accidental growth.
        m_spikeEvents.reserve(MaxSpikeEvents);
        m_timelineEvents.reserve(MaxTimelineEvents);
        m_markers.reserve(MaxMarkerEvents);
        m_gcExplicitEvents.reserve(MaxGcExplicitEvents);
        m_gcCollectorFrames.reserve(MaxGcCollectorFrames);
        m_schedulerSpikeEvents.reserve(MaxSchedulerSpikeEvents);
        m_schedulerFrameBursts.reserve(MaxSchedulerFrameBurstEvents);
        m_deepSamples.reserve(MaxDeepSampleEvents);
        m_deepCallsites.reserve(MaxDeepCallsiteEvents);
        m_deepLines.reserve(MaxDeepLineEvents);
    }

    void ResetCountersLocked()
    {
        for (auto& [_, counter] : m_counters)
        {
            counter->Calls.store(0, std::memory_order_relaxed);
            counter->InclusiveNs.store(0, std::memory_order_relaxed);
            counter->ExclusiveNs.store(0, std::memory_order_relaxed);
            counter->MaxInclusiveNs.store(0, std::memory_order_relaxed);
            counter->MaxExclusiveNs.store(0, std::memory_order_relaxed);
            counter->GrossAllocatedBytes.store(0, std::memory_order_relaxed);
            counter->FrameCurrent.store(0, std::memory_order_relaxed);
            counter->FrameCurrentCalls.store(0, std::memory_order_relaxed);
            counter->FrameRecordedCalls.store(0, std::memory_order_relaxed);
            counter->FrameActiveFrames.store(0, std::memory_order_relaxed);
            counter->FrameOneCallFrames.store(0, std::memory_order_relaxed);
            counter->FrameTwoCallFrames.store(0, std::memory_order_relaxed);
            counter->FrameThreeCallFrames.store(0, std::memory_order_relaxed);
            counter->FrameFourCallFrames.store(0, std::memory_order_relaxed);
            counter->FrameFivePlusCallFrames.store(0, std::memory_order_relaxed);
            counter->FrameMaxCalls.store(0, std::memory_order_relaxed);
            counter->FrameRotateLock.clear(std::memory_order_relaxed);
        }
        m_currentGameFrame.store(0, std::memory_order_relaxed);
        m_captureFrameCount.store(0, std::memory_order_relaxed);
    }

    void ResetSpikesLocked()
    {
        m_spikeEvents.clear();
        m_droppedSpikeEvents = 0;
        m_nextSpikeSequence = 0;
    }

    void ResetTimelineLocked()
    {
        m_timelineEvents.clear();
        m_droppedTimelineEvents = 0;
        for (auto& [_, ownerPtr] : m_timelineMods)
        {
            auto* owner = ownerPtr.get();
            owner->CurrentBucket.store(UINT64_MAX, std::memory_order_relaxed);
            owner->Calls.store(0, std::memory_order_relaxed);
            owner->ExclusiveNs.store(0, std::memory_order_relaxed);
            owner->MaxExclusiveNs.store(0, std::memory_order_relaxed);
            owner->RotateLock.clear(std::memory_order_relaxed);
        }

    }

    void ResetMarkersLocked()
    {
        m_markers.clear();
        m_nextMarkerSequence = 0;
        m_droppedMarkerEvents = 0;
    }

    void ResetGcCollectorLocked()
    {
        m_gcCollectorFrames.clear();
        m_droppedGcCollectorFrames = 0;
        m_gcCollectorPrevious = {};
        m_gcCollectorLastBoundaryNs = 0;
    }

    void ResetGcExplicitLocked()
    {
        m_gcExplicitEvents.clear();
        m_nextGcExplicitSequence = 0;
        m_droppedGcExplicitEvents = 0;
    }

    void ResetSchedulerLocked()
    {
        for (auto& [_, counter] : m_schedulerJobs)
        {
            counter->Calls.store(0, std::memory_order_relaxed);
            counter->TotalNs.store(0, std::memory_order_relaxed);
            counter->MaxNs.store(0, std::memory_order_relaxed);
        }

        m_schedulerSpikeEvents.clear();
        m_schedulerFrameBursts.clear();
        m_nextSchedulerSpikeSequence = 0;
        m_nextSchedulerBurstSequence = 0;
        m_droppedSchedulerSpikeEvents = 0;
        m_droppedSchedulerFrameBursts = 0;

        auto& schedulerFrame = SchedulerFrameStateForThread();
        schedulerFrame.Active = false;
        schedulerFrame.TotalJobNs = 0;
        schedulerFrame.Jobs.clear();
    }

    static SchedulerFrameState& SchedulerFrameStateForThread()
    {
        static thread_local SchedulerFrameState state;
        return state;
    }

    static int64_t ClockTicksNs(Clock::time_point aTime)
    {
        return std::chrono::duration_cast<std::chrono::nanoseconds>(
                   aTime.time_since_epoch()).count();
    }

    void PublishFastSegmentClockLocked(uint64_t aBaseCaptureNs)
    {
        m_fastSegmentBaseCaptureNs.store(aBaseCaptureNs, std::memory_order_release);
        m_fastSegmentStartedTicksNs.store(
            ClockTicksNs(m_segmentStarted), std::memory_order_release);
    }

    uint64_t FastCapturedNanoseconds(Clock::time_point aNow) const
    {
        const int64_t startTicks =
            m_fastSegmentStartedTicksNs.load(std::memory_order_acquire);
        const uint64_t base =
            m_fastSegmentBaseCaptureNs.load(std::memory_order_acquire);
        const int64_t nowTicks = ClockTicksNs(aNow);
        return nowTicks > startTicks
                   ? base + static_cast<uint64_t>(nowTicks - startTicks)
                   : base;
    }

    void AddMarkerLocked(const std::string& aLabel, Clock::time_point aNow)
    {
        if (m_markers.size() >= MaxMarkerEvents)
        {
            ++m_droppedMarkerEvents;
            return;
        }

        const auto unixMs = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::system_clock::now().time_since_epoch()).count();
        m_markers.push_back({
            ++m_nextMarkerSequence,
            CapturedNanosecondsLocked(aNow),
            unixMs,
            aLabel
        });
    }

    static void CommitFrameMultiplicity(Counter* aCounter, uint64_t aCalls)
    {
        if (!aCounter || aCalls == 0)
            return;

        aCounter->FrameRecordedCalls.fetch_add(aCalls, std::memory_order_relaxed);
        aCounter->FrameActiveFrames.fetch_add(1, std::memory_order_relaxed);

        if (aCalls == 1) aCounter->FrameOneCallFrames.fetch_add(1, std::memory_order_relaxed);
        else if (aCalls == 2) aCounter->FrameTwoCallFrames.fetch_add(1, std::memory_order_relaxed);
        else if (aCalls == 3) aCounter->FrameThreeCallFrames.fetch_add(1, std::memory_order_relaxed);
        else if (aCalls == 4) aCounter->FrameFourCallFrames.fetch_add(1, std::memory_order_relaxed);
        else aCounter->FrameFivePlusCallFrames.fetch_add(1, std::memory_order_relaxed);

        UpdateMax(aCounter->FrameMaxCalls, aCalls);
    }

    void PauseLocked(Clock::time_point aNow)
    {
        if (m_state.load(std::memory_order_relaxed) != CaptureState::Running)
            return;

        SnapshotCollectorLocked(aNow, m_currentGameFrame.load(std::memory_order_relaxed));
        gcet_gc_stats_set(0, 0);
        m_accumulatedCapture +=
            std::chrono::duration_cast<std::chrono::nanoseconds>(
                aNow - m_segmentStarted);
        m_state.store(CaptureState::Paused, std::memory_order_release);
        m_currentGameFrame.store(0, std::memory_order_release);
        AddMarkerLocked("PAUSE", aNow);
    }

    uint64_t CapturedNanosecondsLocked(Clock::time_point aNow) const
    {
        auto elapsed = m_accumulatedCapture;
        if (m_state.load(std::memory_order_relaxed) == CaptureState::Running)
        {
            elapsed += std::chrono::duration_cast<std::chrono::nanoseconds>(
                aNow - m_segmentStarted);
        }

        return elapsed.count() > 0
                   ? static_cast<uint64_t>(elapsed.count())
                   : 0;
    }

    double CapturedSecondsLocked(Clock::time_point aNow) const
    {
        return static_cast<double>(CapturedNanosecondsLocked(aNow)) /
               1'000'000'000.0;
    }

    static void UpdateMax(std::atomic<uint64_t>& aTarget, uint64_t aValue)
    {
        auto previous = aTarget.load(std::memory_order_relaxed);
        while (previous < aValue &&
               !aTarget.compare_exchange_weak(
                   previous,
                   aValue,
                   std::memory_order_relaxed,
                   std::memory_order_relaxed))
        {
        }
    }

    static std::string Csv(const std::string& aValue)
    {
        if (aValue.find_first_of(",\"\r\n") == std::string::npos)
            return aValue;

        std::string out;
        out.reserve(aValue.size() + 2);
        out.push_back('"');

        for (const char c : aValue)
        {
            if (c == '"')
                out.push_back('"');
            out.push_back(c);
        }

        out.push_back('"');
        return out;
    }

    template<typename TRow>
    static void WriteRow(std::ofstream& f,
                         const TRow& row,
                         double elapsedSec,
                         uint64_t grandExclusiveNs)
    {
        const double inclusiveMs =
            static_cast<double>(row.InclusiveNs) / 1'000'000.0;
        const double exclusiveMs =
            static_cast<double>(row.ExclusiveNs) / 1'000'000.0;
        const double inclusiveMsPerSec = inclusiveMs / elapsedSec;
        const double exclusiveMsPerSec = exclusiveMs / elapsedSec;
        const double oneCorePct = exclusiveMsPerSec / 10.0;
        const double callsPerSec =
            static_cast<double>(row.Calls) / elapsedSec;
        const double avgExclusiveUs =
            row.Calls
                ? static_cast<double>(row.ExclusiveNs) /
                      1'000.0 /
                      static_cast<double>(row.Calls)
                : 0.0;
        const double maxInclusiveMs =
            static_cast<double>(row.MaxInclusiveNs) / 1'000'000.0;
        const double maxExclusiveMs =
            static_cast<double>(row.MaxExclusiveNs) / 1'000'000.0;
        const double share =
            grandExclusiveNs
                ? static_cast<double>(row.ExclusiveNs) /
                      static_cast<double>(grandExclusiveNs) *
                      100.0
                : 0.0;

        f << row.RegistrationId << ','
          << Csv(row.Mod) << ','
          << Csv(row.Kind) << ','
          << Csv(row.Target) << ','
          << Csv(row.SourceFile) << ','
          << row.SourceLineStart << ','
          << row.SourceLineEnd << ','
          << row.Calls << ','
          << callsPerSec << ','
          << inclusiveMs << ','
          << exclusiveMs << ','
          << inclusiveMsPerSec << ','
          << exclusiveMsPerSec << ','
          << oneCorePct << ','
          << avgExclusiveUs << ','
          << maxInclusiveMs << ','
          << maxExclusiveMs << ','
          << share << ','
          << elapsedSec << ','
          << "events+observe+override-exclusive+capture-gated"
          << '\n';
    }

    static void WriteSummaryHeader(std::ofstream& f,
                                   const char* prefix)
    {
        f << prefix
          << ",Calls,CallsPerSecond,InclusiveTotalMs,ExclusiveTotalMs,"
             "InclusiveMsPerSecond,ExclusiveMsPerSecond,"
             "MeasuredOneCorePct,AvgExclusiveUs,MaxInclusiveMs,"
             "MaxExclusiveMs,MeasuredExclusiveSharePct,"
             "ElapsedSeconds,Coverage\n";
    }

    template<typename TSummary>
    static void WriteSummaryMetrics(std::ofstream& f,
                                    const TSummary& s,
                                    double elapsedSec,
                                    uint64_t grandExclusiveNs)
    {
        const double inclusiveMs =
            static_cast<double>(s.InclusiveNs) / 1'000'000.0;
        const double exclusiveMs =
            static_cast<double>(s.ExclusiveNs) / 1'000'000.0;
        const double inclusiveMsPerSec = inclusiveMs / elapsedSec;
        const double exclusiveMsPerSec = exclusiveMs / elapsedSec;
        const double oneCorePct = exclusiveMsPerSec / 10.0;
        const double callsPerSec =
            static_cast<double>(s.Calls) / elapsedSec;
        const double avgExclusiveUs =
            s.Calls
                ? static_cast<double>(s.ExclusiveNs) /
                      1'000.0 /
                      static_cast<double>(s.Calls)
                : 0.0;
        const double maxInclusiveMs =
            static_cast<double>(s.MaxInclusiveNs) / 1'000'000.0;
        const double maxExclusiveMs =
            static_cast<double>(s.MaxExclusiveNs) / 1'000'000.0;
        const double share =
            grandExclusiveNs
                ? static_cast<double>(s.ExclusiveNs) /
                      static_cast<double>(grandExclusiveNs) *
                      100.0
                : 0.0;

        f << s.Calls << ','
          << callsPerSec << ','
          << inclusiveMs << ','
          << exclusiveMs << ','
          << inclusiveMsPerSec << ','
          << exclusiveMsPerSec << ','
          << oneCorePct << ','
          << avgExclusiveUs << ','
          << maxInclusiveMs << ','
          << maxExclusiveMs << ','
          << share << ','
          << elapsedSec << ','
          << "events+observe+override-exclusive+capture-gated"
          << '\n';
    }

    std::mutex m_mutex;
    std::unordered_map<std::string, std::unique_ptr<Counter>> m_counters;
    std::unordered_map<std::string, std::unique_ptr<TimelineMod>> m_timelineMods;
    std::unordered_map<const void*, CallbackBinding> m_callbackBindings;
    std::vector<SpikeEvent> m_spikeEvents;
    std::vector<TimelineEvent> m_timelineEvents;
    std::vector<MarkerEvent> m_markers;
    std::vector<GcExplicitEvent> m_gcExplicitEvents;
    std::vector<GcCollectorFrame> m_gcCollectorFrames;
    GCETGCStats m_gcCollectorPrevious{};
    uint64_t m_gcCollectorLastBoundaryNs{};
    std::unordered_map<std::string, std::unique_ptr<SchedulerJobCounter>> m_schedulerJobs;
    std::vector<SchedulerSpikeEvent> m_schedulerSpikeEvents;
    std::vector<SchedulerFrameBurstEvent> m_schedulerFrameBursts;
    std::unordered_map<std::string, DeepFunctionAggregate> m_deepFunctions;
    std::unordered_map<std::string, DeepEdgeAggregate> m_deepEdges;
    std::vector<DeepCallsiteAggregate> m_deepCallsites;
    std::vector<DeepSampleEvent> m_deepSamples;
    std::vector<DeepLineEvent> m_deepLines;
    std::unordered_map<std::string, ScalarObservation> m_scalarObservations;
    uint64_t m_droppedScalarObservations{};
    std::filesystem::path m_outputRoot;
    uint64_t m_captureGeneration{0};
    uint64_t m_dumpedGeneration{0};
    uint64_t m_nextRegistrationId{0};
    std::atomic<CaptureState> m_state{CaptureState::Paused};
    std::atomic<uint64_t> m_spikeThresholdNs{DefaultSpikeThresholdNs};
    std::atomic<uint64_t> m_timelineBucketNs{DefaultTimelineBucketNs};
    std::atomic<uint64_t> m_schedulerJobSpikeThresholdNs{DefaultSchedulerJobSpikeThresholdNs};
    std::atomic<uint64_t> m_schedulerFrameBurstThresholdNs{DefaultSchedulerFrameBurstThresholdNs};
    std::atomic<uint64_t> m_currentGameFrame{0};
    std::atomic<uint64_t> m_captureFrameCount{0};
    std::atomic<int64_t> m_fastSegmentStartedTicksNs{0};
    std::atomic<uint64_t> m_fastSegmentBaseCaptureNs{0};
    std::atomic<int64_t> m_nextDeepRebalanceTicksNs{0};
    std::atomic<int64_t> m_nextGcTracerRebalanceTicksNs{0};
    std::atomic<uint64_t> m_nextDeepSampleSequence{0};
    uint64_t m_lastDeepRebalanceCaptureNs{0};
    uint64_t m_droppedDeepSamples{};
    uint64_t m_droppedDeepLines{};
    uint64_t m_droppedDeepCallsites{};
    Clock::time_point m_segmentStarted{};
    std::chrono::nanoseconds m_accumulatedCapture{};
    uint64_t m_nextSpikeSequence{};
    uint64_t m_droppedSpikeEvents{};
    uint64_t m_droppedTimelineEvents{};
    uint64_t m_nextMarkerSequence{};
    uint64_t m_droppedMarkerEvents{};
    uint64_t m_nextGcExplicitSequence{};
    uint64_t m_droppedGcExplicitEvents{};
    uint64_t m_droppedGcCollectorFrames{};
    uint64_t m_nextSchedulerSpikeSequence{};
    uint64_t m_nextSchedulerBurstSequence{};
    uint64_t m_droppedSchedulerSpikeEvents{};
    uint64_t m_droppedSchedulerFrameBursts{};
};
