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
        TimelineMod* TimelineOwner{};
        TimelineCallback* OnUpdateTimeline{};

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

    // Exact callback timeline is intentionally restricted to event::onUpdate.
    // Generic callback timeline expansion would multiply capture volume and hot-path
    // bookkeeping without helping the cadence resolver.
    struct TimelineCallback
    {
        Counter* CounterPtr{};
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

    struct OnUpdateTimelineEvent
    {
        TimelineCallback* CallbackPtr{};
        uint64_t Bucket{};
        uint64_t Calls{};
        uint64_t ExclusiveNs{};
        uint64_t MaxExclusiveNs{};
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
        std::chrono::steady_clock::time_point Start{};
        uint64_t ChildRawNs{};
        uint64_t ProfilerNs{};
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
        uint64_t CaptureStartNs{};
        uint64_t CaptureEndNs{};
        std::string Mode;
        uint64_t ApproxOwnWallNs{};
        uint64_t HookEvents{};
        uint64_t LineEvents{};
        uint64_t UniqueLines{};
        uint64_t PathTransitions{};
        uint64_t PathFingerprint{};
        uint64_t NestedRegistrationCount{};
        uint64_t NestedRegistrationNs{};
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
        uint64_t SampleStartCaptureNs{};
        std::chrono::steady_clock::time_point SampleStartWall{};
        lua_State* State{};
        lua_Hook PreviousHook{};
        int PreviousMask{};
        int PreviousCount{};
        uint64_t RootNetNs{};
        uint64_t HookEvents{};
        uint64_t LineEvents{};
        uint64_t PathTransitions{};
        uint64_t PathFingerprint{1469598103934665603ULL};
        uint64_t LastPathToken{UINT64_MAX};
        bool LineRowsTruncated{false};
        uint32_t NestedRegistrationDepth{};
        std::chrono::steady_clock::time_point NestedBoundaryStart{};
        uint64_t NestedRegistrationCount{};
        uint64_t NestedRegistrationNs{};
        std::vector<DeepFrame> Frames;
        std::unordered_map<std::string, DeepFunctionAggregate> Functions;
        std::unordered_map<std::string, DeepEdgeAggregate> Edges;
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
    static constexpr size_t MaxOnUpdateTimelineEvents = 500'000;
    static constexpr size_t MaxMarkerEvents = 1'000;
    static constexpr size_t MaxSchedulerSpikeEvents = 20'000;
    static constexpr size_t MaxSchedulerFrameBurstEvents = 20'000;

    // Adaptive deep profiling intentionally stays small. Broad profiling
    // chooses the targets; deep tracing samples only enough future invocations
    // to explain the hot callback without turning the whole Lua VM into a trace.
    static constexpr uint64_t DeepWarmupNs = 2'000'000'000ULL;
    static constexpr uint64_t DeepRebalanceNs = 1'000'000'000ULL;
    static constexpr size_t MaxDeepHotRegistrations = 6;
    static constexpr uint64_t DefaultDeepTargetSamples = 24;
    static constexpr uint64_t ReusedDeepTargetSamples = 6;
    static constexpr uint32_t DeepDriftWindowsRequired = 3;
    static constexpr size_t MaxDeepSampleEvents = 20'000;
    static constexpr size_t MaxDeepLineEvents = 500'000;
    static constexpr size_t MaxDeepUniqueLinesPerSample = 2'048;
    static constexpr uint64_t MaxDeepPathTransitions = 4'096;

    enum class CaptureState : uint8_t
    {
        Paused,
        Running
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

            if (m_counter)
            {
                auto& profiler = CETRuntimeProfiler::Get();
                CETRuntimeProfiler::Record(m_counter, elapsed, exclusive);

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
                        elapsed + timelineBookkeepingNs + spikeBookkeepingNs;
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
            if (probe <= 8 || (probe & 0x1FFu) == 0)
                profiler.MaybeRebalanceDeepHotset();

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
        if (!state.Frames.empty())
            state.Frames.back().ChildRawNs += nestedNs;

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

    bool BeginDeepSample(Counter* aCounter, lua_State* aState)
    {
        if (!aCounter || !aState || !IsCapturing())
            return false;

        const bool complete =
            aCounter->DeepComplete.load(std::memory_order_acquire);
        const bool hotset =
            !complete && aCounter->DeepSelected.load(std::memory_order_acquire);
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
        threadState.SampleStartWall = now;
        threadState.SampleStartCaptureNs = FastCapturedNanoseconds(now);
        threadState.State = aState;
        threadState.PreviousHook = lua_gethook(aState);
        threadState.PreviousMask = lua_gethookmask(aState);
        threadState.PreviousCount = lua_gethookcount(aState);
        threadState.Frames.reserve(32);
        threadState.Functions.reserve(64);
        threadState.Edges.reserve(96);
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
                    state.SampleStartCaptureNs,
                    captureEndNs,
                    state.Mode == DeepSampleMode::Hotset
                        ? "HOTSET"
                        : "SPIKE_CAPTURE",
                    approxOwnWallNs,
                    state.HookEvents,
                    state.LineEvents,
                    static_cast<uint64_t>(state.Lines.size()),
                    state.PathTransitions,
                    state.PathFingerprint,
                    state.NestedRegistrationCount,
                    state.NestedRegistrationNs,
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
        ResetSchedulerLocked();
        ResetDeepLocked();
        m_accumulatedCapture = std::chrono::nanoseconds::zero();
        if (m_state.load(std::memory_order_relaxed) == CaptureState::Running)
        {
            m_segmentStarted = Clock::now();
            PublishFastSegmentClockLocked(0);
            AddMarkerLocked("RESET", m_segmentStarted);
        }
    }

    void StartCapture()
    {
        std::lock_guard lock(m_mutex);
        ResetCountersLocked();
        ResetSpikesLocked();
        ResetTimelineLocked();
        ResetMarkersLocked();
        ResetSchedulerLocked();
        ResetDeepLocked();
        m_accumulatedCapture = std::chrono::nanoseconds::zero();
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
            << " | onUpdateTimelineRows=" << m_onUpdateTimelineEvents.size()
            << " | onUpdateTimelineDropped=" << m_droppedOnUpdateTimelineEvents
            << " | markers=" << m_markers.size()
            << " | schedulerJobs=" << m_schedulerJobs.size()
            << " | schedulerSpikes=" << m_schedulerSpikeEvents.size()
            << " | schedulerBursts=" << m_schedulerFrameBursts.size()
            << " | deepFunctions=" << m_deepFunctions.size()
            << " | deepEdges=" << m_deepEdges.size()
            << " | deepSamples=" << m_deepSamples.size()
            << " | deepLines=" << m_deepLines.size();
        return oss.str();
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
            !m_timelineEvents.empty() ||
            !m_onUpdateTimelineEvents.empty())
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

        auto& state = SchedulerFrameStateForThread();
        if (state.Active && state.Frame == aFrame)
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

        // Resolver-only exact timeline. Non-onUpdate callbacks pay only this
        // null-pointer branch; onUpdate callbacks use the same 50 ms bucket model.
        if (auto* callback = aCounter->OnUpdateTimeline)
        {
            current = callback->CurrentBucket.load(std::memory_order_relaxed);
            if (current != bucket)
            {
                const auto bookkeepingStart = Clock::now();
                while (callback->RotateLock.test_and_set(std::memory_order_acquire))
                    std::this_thread::yield();

                current = callback->CurrentBucket.load(std::memory_order_relaxed);
                if (current != bucket)
                {
                    if (current != UINT64_MAX)
                    {
                        OnUpdateTimelineEvent event{
                            callback,
                            current,
                            callback->Calls.exchange(0, std::memory_order_relaxed),
                            callback->ExclusiveNs.exchange(0, std::memory_order_relaxed),
                            callback->MaxExclusiveNs.exchange(0, std::memory_order_relaxed)
                        };

                        if (event.Calls)
                        {
                            std::lock_guard lock(m_mutex);
                            if (m_onUpdateTimelineEvents.size() < MaxOnUpdateTimelineEvents)
                                m_onUpdateTimelineEvents.push_back(event);
                            else
                                ++m_droppedOnUpdateTimelineEvents;
                        }
                    }

                    callback->CurrentBucket.store(bucket, std::memory_order_relaxed);
                }

                callback->RotateLock.clear(std::memory_order_release);
                rolloverBookkeepingNs += static_cast<uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(
                        Clock::now() - bookkeepingStart).count());
            }

            callback->Calls.fetch_add(1, std::memory_order_relaxed);
            callback->ExclusiveNs.fetch_add(aExclusiveNs, std::memory_order_relaxed);
            UpdateMax(callback->MaxExclusiveNs, aExclusiveNs);
        }

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
        };

        struct SpikeRow
        {
            uint64_t Sequence{};
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

        struct OnUpdateTimelineRow
        {
            uint64_t Bucket{};
            uint64_t Calls{};
            uint64_t ExclusiveNs{};
            uint64_t MaxExclusiveNs{};
            uint64_t RegistrationId{};
            std::string Mod;
            std::string Kind;
            std::string Target;
            std::string SourceFile;
            int SourceLineStart{};
            int SourceLineEnd{};
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
        std::vector<OnUpdateTimelineRow> onUpdateTimelineRows;
        std::vector<DeepRegistrationRow> deepRegistrationRows;
        std::vector<DeepFunctionAggregate> deepFunctionRows;
        std::vector<DeepEdgeAggregate> deepEdgeRows;
        std::vector<DeepSampleEvent> deepSampleRows;
        std::vector<DeepLineEvent> deepLineRows;
        std::vector<MarkerEvent> markerRows;
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
        uint64_t droppedOnUpdateTimelineEvents{};
        uint64_t droppedMarkerEvents{};
        uint64_t droppedSchedulerSpikeEvents{};
        uint64_t droppedSchedulerFrameBursts{};
        uint64_t droppedDeepSamples{};
        uint64_t droppedDeepLines{};
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
                    counter->MaxExclusiveNs.load(std::memory_order_relaxed)
                });
            }

            spikeRows.reserve(m_spikeEvents.size());
            for (const auto& spike : m_spikeEvents)
            {
                if (!spike.CounterPtr)
                    continue;

                spikeRows.push_back({
                    spike.Sequence,
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

            onUpdateTimelineRows.reserve(
                m_onUpdateTimelineEvents.size() + m_onUpdateTimelineCallbacks.size());
            for (const auto& event : m_onUpdateTimelineEvents)
            {
                if (!event.CallbackPtr || !event.CallbackPtr->CounterPtr || !event.Calls)
                    continue;
                const auto* counter = event.CallbackPtr->CounterPtr;
                onUpdateTimelineRows.push_back({
                    event.Bucket, event.Calls, event.ExclusiveNs,
                    event.MaxExclusiveNs, counter->RegistrationId,
                    counter->Mod, counter->Kind, counter->Target,
                    counter->SourceFile, counter->SourceLineStart, counter->SourceLineEnd
                });
            }

            // Include the currently open bucket for each onUpdate callback.
            for (const auto& [_, callbackPtr] : m_onUpdateTimelineCallbacks)
            {
                const auto* callback = callbackPtr.get();
                const auto* counter = callback->CounterPtr;
                const uint64_t calls = callback->Calls.load(std::memory_order_relaxed);
                const uint64_t bucket = callback->CurrentBucket.load(std::memory_order_relaxed);
                if (!counter || !calls || bucket == UINT64_MAX)
                    continue;
                onUpdateTimelineRows.push_back({
                    bucket, calls,
                    callback->ExclusiveNs.load(std::memory_order_relaxed),
                    callback->MaxExclusiveNs.load(std::memory_order_relaxed),
                    counter->RegistrationId,
                    counter->Mod, counter->Kind, counter->Target,
                    counter->SourceFile, counter->SourceLineStart, counter->SourceLineEnd
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
                    counter->DeepDriftDirection.load(std::memory_order_relaxed)
                });
            }

            deepFunctionRows.reserve(m_deepFunctions.size());
            for (const auto& [_, row] : m_deepFunctions)
                deepFunctionRows.push_back(row);

            deepEdgeRows.reserve(m_deepEdges.size());
            for (const auto& [_, row] : m_deepEdges)
                deepEdgeRows.push_back(row);

            deepSampleRows = m_deepSamples;
            deepLineRows = m_deepLines;
            droppedDeepSamples = m_droppedDeepSamples;
            droppedDeepLines = m_droppedDeepLines;

            markerRows = m_markers;

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
            droppedOnUpdateTimelineEvents = m_droppedOnUpdateTimelineEvents;
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
        // ONUPDATE TIMELINE: exact callback workload buckets for resolver
        // cadence/state analysis. Deliberately excludes all other callbacks.
        // -----------------------------------------------------------------
        {
            std::sort(onUpdateTimelineRows.begin(), onUpdateTimelineRows.end(),
                      [](const OnUpdateTimelineRow& a, const OnUpdateTimelineRow& b)
                      {
                          if (a.Bucket != b.Bucket)
                              return a.Bucket < b.Bucket;
                          if (a.Mod != b.Mod)
                              return a.Mod < b.Mod;
                          if (a.Target != b.Target)
                              return a.Target < b.Target;
                          return a.RegistrationId < b.RegistrationId;
                      });

            const auto path = outputRoot / "CET_Runtime_Profile_OnUpdateTimeline.csv";
            std::ofstream f(path, std::ios::trunc);
            if (f)
            {
                f << "BucketIndex,BucketStartMs,BucketEndMs,RegistrationId,Mod,Kind,Target,"
                     "SourceFile,SourceLineStart,SourceLineEnd,Calls,"
                     "ExclusiveMs,MaxExclusiveMs,"
                     "BucketWidthMs,DroppedTimelineRowsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : onUpdateTimelineRows)
                {
                    const uint64_t startNs = row.Bucket * timelineBucketNs;
                    const uint64_t endNs = startNs + timelineBucketNs;
                    f << row.Bucket << ','
                      << (static_cast<double>(startNs) / 1'000'000.0) << ','
                      << (static_cast<double>(endNs) / 1'000'000.0) << ','
                      << row.RegistrationId << ','
                      << Csv(row.Mod) << ','
                      << Csv(row.Kind) << ','
                      << Csv(row.Target) << ','
                      << Csv(row.SourceFile) << ','
                      << row.SourceLineStart << ','
                      << row.SourceLineEnd << ','
                      << row.Calls << ','
                      << (static_cast<double>(row.ExclusiveNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.MaxExclusiveNs) / 1'000'000.0) << ','
                      << timelineBucketMs << ','
                      << droppedOnUpdateTimelineEvents << ','
                      << "continuous-onupdate-callback-correlation"
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
                     "DriftDirection,Interpretation\n";
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
                      << "adaptive-hotset-sampled-call-return-line-path"
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
                f << "SampleSequence,RegistrationId,ProfileEpoch,CaptureStartMs,"
                     "CaptureEndMs,Mode,ApproxOwnWallMs,HookEvents,LineEvents,"
                     "UniqueLines,PathTransitions,PathFingerprint,"
                     "NestedRegistrationCount,NestedRegistrationMs,LineRowsTruncated,"
                     "DroppedSamplesAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& row : deepSampleRows)
                {
                    f << row.Sequence << ','
                      << row.RegistrationId << ','
                      << row.ProfileEpoch << ','
                      << (static_cast<double>(row.CaptureStartNs) / 1'000'000.0) << ','
                      << (static_cast<double>(row.CaptureEndNs) / 1'000'000.0) << ','
                      << Csv(row.Mode) << ','
                      << (static_cast<double>(row.ApproxOwnWallNs) / 1'000'000.0) << ','
                      << row.HookEvents << ','
                      << row.LineEvents << ','
                      << row.UniqueLines << ','
                      << row.PathTransitions << ','
                      << row.PathFingerprint << ','
                      << row.NestedRegistrationCount << ','
                      << (static_cast<double>(row.NestedRegistrationNs) / 1'000'000.0) << ','
                      << (row.LineRowsTruncated ? 1 : 0) << ','
                      << droppedDeepSamples << ','
                      << "timestamped-hotpath-path-fingerprint"
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
                     "MaxInclusiveMs,Interpretation\n";
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

        // -----------------------------------------------------------------
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
                f << "Sequence,CaptureStartMs,CaptureEndMs,InclusiveMs,"
                     "ExclusiveMs,ChildMs,RegistrationId,Mod,Kind,Target,"
                     "SourceFile,SourceLineStart,SourceLineEnd,ThreadId,"
                     "ThresholdMs,DroppedEventsAtDump,Interpretation\n";
                f << std::fixed << std::setprecision(6);

                for (const auto& spike : spikeRows)
                {
                    f << spike.Sequence << ','
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
        return frame;
    }

    static void RecordDeepLine(
        DeepThreadState& aState,
        int aLine)
    {
        if (aLine <= 0 || aState.Frames.empty())
            return;

        const auto& frame = aState.Frames.back();
        ++aState.LineEvents;

        const uint64_t token =
            frame.SourceHash ^
            (static_cast<uint64_t>(static_cast<uint32_t>(aLine)) *
             0x9E3779B185EBCA87ULL) ^
            frame.FunctionIdentity;

        if (token != aState.LastPathToken &&
            aState.PathTransitions < MaxDeepPathTransitions)
        {
            aState.PathFingerprint =
                MixPathHash(aState.PathFingerprint, token);
            aState.LastPathToken = token;
            ++aState.PathTransitions;
        }

        const std::string key =
            frame.SourceFile + "\x1f" + std::to_string(aLine);
        auto found = aState.Lines.find(key);
        if (found == aState.Lines.end())
        {
            if (aState.Lines.size() >= MaxDeepUniqueLinesPerSample)
            {
                aState.LineRowsTruncated = true;
                return;
            }

            DeepLineHit hit;
            hit.SourceFile = frame.SourceFile;
            hit.Line = aLine;
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
        }

        if (!aState.Frames.empty())
            aState.Frames.back().ChildRawNs += rawNs;
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
            RecordDeepLine(deep, aDebug->currentline);
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
        m_deepLines.clear();
        m_droppedDeepSamples = 0;
        m_droppedDeepLines = 0;
        m_nextDeepSampleSequence.store(0, std::memory_order_relaxed);
        m_lastDeepRebalanceCaptureNs = 0;
        m_nextDeepRebalanceTicksNs.store(0, std::memory_order_relaxed);

        for (auto& [_, counter] : m_counters)
        {
            counter->DeepSelected.store(false, std::memory_order_relaxed);
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
        if (aKind == "event" && aTarget == "onUpdate")
        {
            auto callbackTimeline = std::make_unique<TimelineCallback>();
            callbackTimeline->CounterPtr = raw;
            raw->OnUpdateTimeline = callbackTimeline.get();
            m_onUpdateTimelineCallbacks.emplace(key, std::move(callbackTimeline));
        }

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
        m_onUpdateTimelineEvents.reserve(MaxOnUpdateTimelineEvents);
        m_markers.reserve(MaxMarkerEvents);
        m_schedulerSpikeEvents.reserve(MaxSchedulerSpikeEvents);
        m_schedulerFrameBursts.reserve(MaxSchedulerFrameBurstEvents);
        m_deepSamples.reserve(MaxDeepSampleEvents);
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
        }
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

        m_onUpdateTimelineEvents.clear();
        m_droppedOnUpdateTimelineEvents = 0;
        for (auto& [_, callbackPtr] : m_onUpdateTimelineCallbacks)
        {
            auto* callback = callbackPtr.get();
            callback->CurrentBucket.store(UINT64_MAX, std::memory_order_relaxed);
            callback->Calls.store(0, std::memory_order_relaxed);
            callback->ExclusiveNs.store(0, std::memory_order_relaxed);
            callback->MaxExclusiveNs.store(0, std::memory_order_relaxed);
            callback->RotateLock.clear(std::memory_order_relaxed);
        }
    }

    void ResetMarkersLocked()
    {
        m_markers.clear();
        m_droppedMarkerEvents = 0;
        m_nextMarkerSequence = 0;
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

    void PauseLocked(Clock::time_point aNow)
    {
        if (m_state.load(std::memory_order_relaxed) != CaptureState::Running)
            return;

        m_accumulatedCapture +=
            std::chrono::duration_cast<std::chrono::nanoseconds>(
                aNow - m_segmentStarted);
        m_state.store(CaptureState::Paused, std::memory_order_release);
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
    std::unordered_map<std::string, std::unique_ptr<TimelineCallback>> m_onUpdateTimelineCallbacks;
    std::unordered_map<const void*, CallbackBinding> m_callbackBindings;
    std::vector<SpikeEvent> m_spikeEvents;
    std::vector<TimelineEvent> m_timelineEvents;
    std::vector<OnUpdateTimelineEvent> m_onUpdateTimelineEvents;
    std::vector<MarkerEvent> m_markers;
    std::unordered_map<std::string, std::unique_ptr<SchedulerJobCounter>> m_schedulerJobs;
    std::vector<SchedulerSpikeEvent> m_schedulerSpikeEvents;
    std::vector<SchedulerFrameBurstEvent> m_schedulerFrameBursts;
    std::unordered_map<std::string, DeepFunctionAggregate> m_deepFunctions;
    std::unordered_map<std::string, DeepEdgeAggregate> m_deepEdges;
    std::vector<DeepSampleEvent> m_deepSamples;
    std::vector<DeepLineEvent> m_deepLines;
    std::filesystem::path m_outputRoot;
    uint64_t m_captureGeneration{0};
    uint64_t m_dumpedGeneration{0};
    uint64_t m_nextRegistrationId{0};
    std::atomic<CaptureState> m_state{CaptureState::Paused};
    std::atomic<uint64_t> m_spikeThresholdNs{DefaultSpikeThresholdNs};
    std::atomic<uint64_t> m_timelineBucketNs{DefaultTimelineBucketNs};
    std::atomic<uint64_t> m_schedulerJobSpikeThresholdNs{DefaultSchedulerJobSpikeThresholdNs};
    std::atomic<uint64_t> m_schedulerFrameBurstThresholdNs{DefaultSchedulerFrameBurstThresholdNs};
    std::atomic<int64_t> m_fastSegmentStartedTicksNs{0};
    std::atomic<uint64_t> m_fastSegmentBaseCaptureNs{0};
    std::atomic<int64_t> m_nextDeepRebalanceTicksNs{0};
    std::atomic<uint64_t> m_nextDeepSampleSequence{0};
    uint64_t m_lastDeepRebalanceCaptureNs{0};
    uint64_t m_droppedDeepSamples{};
    uint64_t m_droppedDeepLines{};
    Clock::time_point m_segmentStarted{};
    std::chrono::nanoseconds m_accumulatedCapture{};
    uint64_t m_nextSpikeSequence{};
    uint64_t m_droppedSpikeEvents{};
    uint64_t m_droppedTimelineEvents{};
    uint64_t m_droppedOnUpdateTimelineEvents{};
    uint64_t m_nextMarkerSequence{};
    uint64_t m_droppedMarkerEvents{};
    uint64_t m_nextSchedulerSpikeSequence{};
    uint64_t m_nextSchedulerBurstSequence{};
    uint64_t m_droppedSchedulerSpikeEvents{};
    uint64_t m_droppedSchedulerFrameBursts{};
};
