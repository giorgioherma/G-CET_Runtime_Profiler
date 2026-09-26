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

    struct Counter
    {
        std::string Mod;
        std::string Kind;
        std::string Target;

        std::atomic<uint64_t> Calls{0};
        std::atomic<uint64_t> InclusiveNs{0};
        std::atomic<uint64_t> ExclusiveNs{0};
        std::atomic<uint64_t> MaxInclusiveNs{0};
        std::atomic<uint64_t> MaxExclusiveNs{0};
        TimelineMod* TimelineOwner{};
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

    using Clock = std::chrono::steady_clock;

    static constexpr uint64_t DefaultSpikeThresholdNs = 5'000'000;
    static constexpr uint64_t DefaultTimelineBucketNs = 50'000'000;
    static constexpr uint64_t DefaultSchedulerJobSpikeThresholdNs = 2'000'000;
    static constexpr uint64_t DefaultSchedulerFrameBurstThresholdNs = 5'000'000;
    static constexpr size_t MaxSpikeEvents = 20'000;
    static constexpr size_t MaxTimelineEvents = 1'000'000;
    static constexpr size_t MaxMarkerEvents = 1'000;
    static constexpr size_t MaxSchedulerSpikeEvents = 20'000;
    static constexpr size_t MaxSchedulerFrameBurstEvents = 20'000;

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
            binding.ResolvedCounter = RegisterLocked(
                aMod.empty() ? "<unknown>" : aMod,
                binding.Kind,
                binding.Target);
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
        m_accumulatedCapture = std::chrono::nanoseconds::zero();
        m_segmentStarted = Clock::now();
        PublishFastSegmentClockLocked(0);
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
            << " | markers=" << m_markers.size()
            << " | schedulerJobs=" << m_schedulerJobs.size()
            << " | schedulerSpikes=" << m_schedulerSpikeEvents.size()
            << " | schedulerBursts=" << m_schedulerFrameBursts.size();
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

        auto* owner = aCounter->TimelineOwner;
        const uint64_t bucketWidth =
            m_timelineBucketNs.load(std::memory_order_relaxed);
        if (!bucketWidth)
            return 0;

        const uint64_t captureNs = FastCapturedNanoseconds(aEnd);
        const uint64_t bucket = captureNs / bucketWidth;
        uint64_t current = owner->CurrentBucket.load(std::memory_order_relaxed);

        uint64_t rolloverBookkeepingNs = 0;
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
            rolloverBookkeepingNs = static_cast<uint64_t>(
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
            std::string Mod;
            std::string Kind;
            std::string Target;
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
            std::string Mod;
            std::string Kind;
            std::string Target;
        };

        struct TimelineRow
        {
            uint64_t Bucket{};
            uint64_t Calls{};
            uint64_t ExclusiveNs{};
            uint64_t MaxExclusiveNs{};
            std::string Mod;
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
        uint64_t droppedMarkerEvents{};
        uint64_t droppedSchedulerSpikeEvents{};
        uint64_t droppedSchedulerFrameBursts{};
        double schedulerJobSpikeThresholdMs{};
        double schedulerFrameBurstThresholdMs{};

        {
            std::lock_guard lock(m_mutex);

            outputRoot = m_outputRoot;
            elapsedSec = CapturedSecondsLocked(Clock::now());
            rows.reserve(m_counters.size());

            for (const auto& [_, counter] : m_counters)
            {
                rows.push_back({
                    counter->Mod,
                    counter->Kind,
                    counter->Target,
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
                    spike.CounterPtr->Mod,
                    spike.CounterPtr->Kind,
                    spike.CounterPtr->Target
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
                     "ExclusiveMs,ChildMs,Mod,Kind,Target,ThreadId,"
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
                      << Csv(spike.Mod) << ','
                      << Csv(spike.Kind) << ','
                      << Csv(spike.Target) << ','
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
                f << "Mod,Kind,Target,Calls,CallsPerSecond,"
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
    }

private:
    Counter* RegisterLocked(const std::string& aMod,
                            const std::string& aKind,
                            const std::string& aTarget)
    {
        const std::string normalizedMod =
            aMod.empty() ? "<unknown>" : aMod;
        const std::string key =
            normalizedMod + "\x1f" + aKind + "\x1f" + aTarget;

        const auto found = m_counters.find(key);
        if (found != m_counters.end())
            return found->second.get();

        auto counter = std::make_unique<Counter>();
        counter->Mod = normalizedMod;
        counter->Kind = aKind;
        counter->Target = aTarget;

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
        m_schedulerSpikeEvents.reserve(MaxSchedulerSpikeEvents);
        m_schedulerFrameBursts.reserve(MaxSchedulerFrameBurstEvents);
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

        f << Csv(row.Mod) << ','
          << Csv(row.Kind) << ','
          << Csv(row.Target) << ','
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
    std::unordered_map<std::string, std::unique_ptr<SchedulerJobCounter>> m_schedulerJobs;
    std::vector<SchedulerSpikeEvent> m_schedulerSpikeEvents;
    std::vector<SchedulerFrameBurstEvent> m_schedulerFrameBursts;
    std::filesystem::path m_outputRoot;
    std::atomic<CaptureState> m_state{CaptureState::Paused};
    std::atomic<uint64_t> m_spikeThresholdNs{DefaultSpikeThresholdNs};
    std::atomic<uint64_t> m_timelineBucketNs{DefaultTimelineBucketNs};
    std::atomic<uint64_t> m_schedulerJobSpikeThresholdNs{DefaultSchedulerJobSpikeThresholdNs};
    std::atomic<uint64_t> m_schedulerFrameBurstThresholdNs{DefaultSchedulerFrameBurstThresholdNs};
    std::atomic<int64_t> m_fastSegmentStartedTicksNs{0};
    std::atomic<uint64_t> m_fastSegmentBaseCaptureNs{0};
    Clock::time_point m_segmentStarted{};
    std::chrono::nanoseconds m_accumulatedCapture{};
    uint64_t m_nextSpikeSequence{};
    uint64_t m_droppedSpikeEvents{};
    uint64_t m_droppedTimelineEvents{};
    uint64_t m_nextMarkerSequence{};
    uint64_t m_droppedMarkerEvents{};
    uint64_t m_nextSchedulerSpikeSequence{};
    uint64_t m_nextSchedulerBurstSequence{};
    uint64_t m_droppedSchedulerSpikeEvents{};
    uint64_t m_droppedSchedulerFrameBursts{};
};
