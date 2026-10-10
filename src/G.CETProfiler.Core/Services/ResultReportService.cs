using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record ResultReportBuildResult(
    string ReportPath,
    string SummaryPath,
    int FindingsCount,
    bool HasCoreData,
    bool HasSchedulerData,
    bool HasFrameTimeData);

/// <summary>
/// Human-first post-capture interpretation for the standalone CET profiler.
/// Native profiler measurement remains unchanged; this service only organizes and
/// interprets the verified CSV output after collection.
/// </summary>
public static partial class ResultReportService
{
    public const string ReportFileName = "CET_Report.html";
    public const string SummaryFileName = "CET_Summary.json";
    public const string ResolverInputFileName = "CET_Resolver_Input.json";
    public const string ResolverResolutionFileName = "G-CET_Resolver.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ResolverJsonOptions = new()
    {
        // CET_Resolver_Input.json is a machine handoff and can exceed tens of MB
        // on deep/heavy stacks. Pretty-printing only adds I/O and allocation.
        WriteIndented = false
    };

    private static readonly HashSet<string> SchedulerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "CET_Runtime_Profile_Scheduler_ByJob.csv",
        "CET_Runtime_Profile_Scheduler_Spikes.csv",
        "CET_Runtime_Profile_Scheduler_FrameBursts.csv"
    };

    private static readonly HashSet<string> RuntimeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "CET_Runtime_Profile_ByMod.csv",
        "CET_Runtime_Profile_ByModKind.csv",
        "CET_Runtime_Profile_Detail.csv",
        "CET_Runtime_Profile_Spikes.csv",
        "CET_Runtime_Profile_Timeline.csv",
        "CET_Runtime_Profile_FrameMultiplicity.csv",
        "CET_Runtime_Profile_Markers.csv",
        "CET_Runtime_Profile_GC_Explicit.csv",
        "CET_Runtime_Profile_GC_Collector.csv",
        "CET_Runtime_Profile_Deep_Registrations.csv",
        "CET_Runtime_Profile_Deep_Functions.csv",
        "CET_Runtime_Profile_Deep_Edges.csv",
        "CET_Runtime_Profile_Deep_Samples.csv",
        "CET_Runtime_Profile_Deep_Lines.csv",
        "CET_Runtime_Profile_Deep_Callsites.csv"
    };

    public static string GetArchiveRelativePath(string fileName)
    {
        if (SchedulerFiles.Contains(fileName))
            return Path.Combine("Data", "Scheduler", fileName);

        if (RuntimeFiles.Contains(fileName))
            return Path.Combine("Data", "Runtime", fileName);

        return Path.Combine("Data", "Metadata", fileName);
    }

    public static ResultReportBuildResult Generate(string captureRoot)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            throw new ArgumentException("Capture folder is empty.", nameof(captureRoot));

        captureRoot = Path.GetFullPath(captureRoot);
        Directory.CreateDirectory(captureRoot);

        var a = Analyze(captureRoot);
        var summaryPath = Path.Combine(captureRoot, SummaryFileName);
        var resolverInputPath = Path.Combine(captureRoot, ResolverInputFileName);
        var reportPath = Path.Combine(captureRoot, ReportFileName);

        var summary = new
        {
            schemaVersion = "1.5",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                contractVersion = "1.1",
                producer = "G-CET-Runtime-Profiler",
                domain = "cet"
            },
            scope = a.FrameTime is null
                ? "CET-side runtime workload only"
                : "CET-side runtime workload with optional CapFrameX frametime correlation",
            capture = new
            {
                title = ReadCaptureTitle(captureRoot),
                durationSeconds = Round(a.CaptureSeconds, 3),
                elapsedSeconds = Round(a.CaptureSeconds, 3),
                activeOwners = a.Owners.Count,
                measuredCalls = a.TotalCalls,
                callsPerSecond = Round(a.TotalCallsPerSecond, 3),
                measuredCallsPerSecond = Round(a.TotalCallsPerSecond, 3),
                exclusiveMsPerSecond = Round(a.TotalMsPerSecond, 6),
                measuredExclusiveMsPerSecond = Round(a.TotalMsPerSecond, 6),
                measuredOneCorePct = Round(a.TotalOneCorePct, 6)
            },
            topOwners = a.TopOwners.Select(x => new
            {
                owner = x.Name,
                infrastructure = IsInfrastructureOwner(x.Name),
                calls = x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                measuredOneCorePct = Round(x.MeasuredOneCorePct, 6),
                measuredSharePct = Round(EffectiveShare(x, a.TotalMsPerSecond), 3),
                maxExclusiveMs = Round(x.MaxExclusiveMs, 6)
            }),
            workloadServices = a.WorkloadServices is null || !a.WorkloadServices.HasEvidence ? null : new
            {
                measurement = "NESTED_0_ENGINE_CLIENT_WORK_AND_15_SECOND_HEALTH_CHECKPOINTS",
                additiveToSchedulerOrCallbackTime = false,
                instrumentedClients = a.WorkloadServices.MeasuredClients.Count,
                measuredCalls = a.WorkloadServices.MeasuredCalls,
                measuredNestedMsPerSecond = Round(a.WorkloadServices.MeasuredNestedMsPerSecond, 3),
                healthCheckpoints = a.WorkloadServices.Statuses.Count,
                peakObservedQueue = a.WorkloadServices.PeakQueue,
                peakObservedPhasePending = a.WorkloadServices.PeakPhasePending,
                latestHealth = a.WorkloadServices.Latest,
                clients = a.WorkloadServices.MeasuredClients
                    .Take(50).Select(x => new {
                        x.Owner, x.JobType, x.Job, x.Calls,
                        msPerSecond = Round(x.MsPerSecond, 3),
                        maxMs = Round(x.MaxMs, 3)
                    })
            },
            collectorGc = a.CollectorGc is null ? null : new
            {
                measurement = "EXACT_INTERNAL_LUAJIT_GC_EXECUTION_FRAME_BUCKETED",
                automaticVsExplicitOriginSeparatelyIdentified = false,
                collectionPolicyChanged = false,
                measuredFrames = a.CollectorGc.Frames.Count,
                totalMeasuredMs = Round(a.CollectorGc.TotalMs, 3),
                incrementalCalls = a.CollectorGc.StepCalls,
                incrementalMs = Round(a.CollectorGc.StepMs, 3),
                completedCycles = a.CollectorGc.CompletedCycles,
                fullCollections = a.CollectorGc.FullCalls,
                fullCollectionMs = Round(a.CollectorGc.FullMs, 3),
                maxIncrementalStepMs = Round(a.CollectorGc.MaxStepToDateMs, 3),
                maxFullCollectionMs = Round(a.CollectorGc.MaxFullToDateMs, 3),
                maximumFrameBucketMs = Round(a.CollectorGc.MaxObservedFrameMs, 3),
                callbackSpikeOverlapFrames = a.CollectorGc.SpikeOverlapFrames,
                alignedHitchEpisodeOverlapFrames = a.CollectorGc.AlignedHitchEpisodeOverlap,
                droppedFrameBuckets = a.CollectorGc.DroppedFramesAtDump,
                slowestFrameBuckets = a.CollectorGc.Frames
                    .OrderByDescending(f => f.TotalMs).Take(30)
                    .Select(f => new {
                        f.Frame, f.StartMs, f.EndMs,
                        collectorMs = Round(f.TotalMs, 3),
                        f.IncrementalCalls, f.FullCalls,
                        f.CallbackSpikeOverlap
                    })
            },
            explicitGc = a.ExplicitGc is null ? null : new
            {
                measurement = "EXACT_EXPLICIT_COLLECTGARBAGE_CALLS_ONLY",
                automaticLuaJitGcTimingAvailable = false,
                count = a.ExplicitGc.Events.Count,
                totalDurationMs = Round(a.ExplicitGc.TotalDurationMs, 3),
                maximumDurationMs = Round(a.ExplicitGc.MaximumDurationMs, 3),
                callbacksNearRecordedSpikes = a.ExplicitGc.SpikeOverlapCount,
                alignedHitchEpisodeOverlaps = a.FrameTime?.Correlated == true && a.FrameTime.HitchPressure is not null
                    ? a.ExplicitGc.Events.Count(x => a.FrameTime.HitchPressure.Episodes.Any(e =>
                        e.StartMs <= x.EndMs && e.EndMs >= x.StartMs))
                    : (int?)null,
                fullCollections = a.ExplicitGc.Events.Count(x => x.Action == "collect"),
                steps = a.ExplicitGc.Events.Count(x => x.Action == "step"),
                slowest = a.ExplicitGc.Events
                    .OrderByDescending(x => x.DurationMs).Take(30)
                    .Select(x => new {
                        captureStartMs = Round(x.StartMs, 3),
                        durationMs = Round(x.DurationMs, 3),
                        x.Action, x.SourceFile, x.SourceLine,
                        x.NearRecordedSpike
                    })
            },
            luaHeap = a.LuaHeap is null ? null : new
            {
                mode = "PASSIVE_HEAP_COUNT_NO_COLLECTION_CONTROL",
                cadenceSeconds = 0.5,
                checkpointSeconds = 10,
                significantShrinkMiB = 8,
                // Only measured heap-size changes; GC activity is not proven.
                gcPauseAttribution = "NOT_MEASURED",
                recordedCheckpoints = a.LuaHeap.SampleCount,
                firstHeapMiB = Round(a.LuaHeap.FirstMiB, 3),
                lastHeapMiB = Round(a.LuaHeap.LastMiB, 3),
                minHeapMiB = Round(a.LuaHeap.MinMiB, 3),
                maxHeapMiB = Round(a.LuaHeap.MaxMiB, 3),
                observedShrinkEvents = a.LuaHeap.DropCount,
                maxSingleSampleShrinkMiB = Round(a.LuaHeap.MaxDropMiB, 3),
                shrinkEventsNearRecordedSpikes = a.LuaHeap.DropsNearSpikes,
                maxMarkerBudget = 480,
                markerBudgetExhausted = a.LuaHeap.SampleCount >= 480,
                significantShrinks = a.LuaHeap.Samples
                    .Where(x => x.IsDrop)
                    .Take(80)
                    .Select(x => new
                    {
                        captureMs = Round(x.CaptureMs, 3),
                        heapMiB = Round(x.HeapMiB, 3),
                        shrinkMiB = Round(x.DropMiB, 3),
                        x.NearRecordedSpike
                    })
            },
            callVolume = a.CallVolume.Select(x => new
            {
                owner = x.Name,
                calls = x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                callSharePct = Round(Percent(x.Calls, a.TotalCalls), 3),
                exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6)
            }),
            callbackHotspots = a.TopCallbacks.Select(x => new
            {
                kind = x.Kind,
                target = x.Target,
                owners = x.OwnerCount,
                calls = x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                maxExclusiveMs = Round(x.MaxExclusiveMs, 6),
                topOwner = x.TopOwner
            }),
            burstAnalysis = new
            {
                vocabularyVersion = "1.0",
                spikeThresholdSemantics = "RECORDED_SPIKES_ONLY; DEFAULT_NATIVE_THRESHOLD_5_MS",
                thresholds = new
                {
                    sustainedHotMsPerSecond = BurstAnalysisService.SustainedHotThresholdMsPerSecond,
                    burstHotP95Ms = BurstAnalysisService.BurstHotP95ThresholdMs,
                    catastrophicBurstMs = BurstAnalysisService.CatastrophicBurstThresholdMs,
                    periodicMinimumSpikes = BurstAnalysisService.PeriodicMinimumSpikes,
                    periodicMinimumIntervalMs = BurstAnalysisService.PeriodicMinimumIntervalMs,
                    periodicMaximumIntervalMs = BurstAnalysisService.PeriodicMaximumIntervalMs,
                    periodicMaximumJitterPct = BurstAnalysisService.PeriodicMaximumJitterPct,
                    periodicIntervalTolerancePct = BurstAnalysisService.PeriodicIntervalTolerancePct,
                    periodicMinimumSupportPct = BurstAnalysisService.PeriodicMinimumSupportPct
                },
                stutterMaterialCandidates = a.BurstCallbacks.Count,
                candidates = a.BurstCallbacks.Take(30).Select(x => new
                {
                    registrationId = x.RegistrationId > 0 ? x.RegistrationId : (long?)null,
                    owner = x.Owner,
                    kind = x.Kind,
                    target = x.Target,
                    exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                    primaryClass = x.Profile.PrimaryClass,
                    classes = x.Profile.Classes,
                    spikeCount = x.Profile.SpikeCount,
                    spikeRatePct = Round(x.Profile.SpikeRatePct, 6),
                    medianExclusiveMs = Round(x.Profile.MedianExclusiveMs, 6),
                    p95ExclusiveMs = Round(x.Profile.P95ExclusiveMs, 6),
                    maxExclusiveMs = Round(x.Profile.MaxExclusiveMs, 6),
                    medianIntervalMs = Round(x.Profile.MedianIntervalMs, 6),
                    intervalMadMs = Round(x.Profile.IntervalMadMs, 6),
                    intervalJitterPct = Round(x.Profile.IntervalJitterPct, 6),
                    periodicSupportPct = Round(x.Profile.PeriodicSupportPct, 6),
                    stutterMaterial = x.Profile.StutterMaterial
                })
            },
            heavyCetWindows = a.TopWindows.Take(10).Select(x => new
            {
                bucketIndex = x.BucketIndex,
                startMs = Round(x.StartMs, 3),
                endMs = Round(x.EndMs, 3),
                exclusiveMs = Round(x.ExclusiveMs, 6),
                calls = x.Calls,
                topOwner = x.TopOwner,
                topOwnerExclusiveMs = Round(x.TopOwnerExclusiveMs, 6)
            }),
            spikes = a.Spikes.Select(x => new
            {
                x.Sequence,
                startMs = Round(x.CaptureStartMs, 3),
                endMs = Round(x.CaptureEndMs, 3),
                exclusiveMs = Round(x.ExclusiveMs, 6),
                inclusiveMs = Round(x.InclusiveMs, 6),
                owner = x.Mod,
                kind = x.Kind,
                target = x.Target
            }),
            scheduler = new
            {
                present = a.SchedulerJobs.Count > 0 || a.WorstSchedulerBurst is not null,
                zeroEngineBoundaryMsPerSecond = a.ZeroEngine is null
                    ? (double?)null
                    : Round(a.ZeroEngine.ExclusiveMsPerSecond, 6),
                scheduledJobMsPerSecond = Round(a.SchedulerTotalMsPerSecond, 6),
                jobs = a.SchedulerJobs.Count,
                topJobs = a.SchedulerJobs.Take(12).Select(x => new
                {
                    owner = x.Owner,
                    jobType = x.JobType,
                    job = x.Job,
                    cadence = FormatCadence(x.IntervalValue, x.IntervalUnit),
                    callsPerSecond = Round(x.CallsPerSecond, 3),
                    msPerSecond = Round(x.MsPerSecond, 6),
                    maxMs = Round(x.MaxMs, 6)
                }),
                worstBurst = a.WorstSchedulerBurst is null
                    ? null
                    : new
                    {
                        burstSequence = a.WorstSchedulerBurst.Sequence,
                        frame = a.WorstSchedulerBurst.Frame,
                        totalJobMs = Round(a.WorstSchedulerBurst.TotalJobMs, 6),
                        schedulerWallMs = Round(a.WorstSchedulerBurst.SchedulerWallMs, 6),
                        jobCount = a.WorstSchedulerBurst.JobCount,
                        dominantCadence = a.WorstSchedulerBurst.DominantCadence,
                        dominantCadenceJobs = a.WorstSchedulerBurst.DominantCadenceJobs
                    },
                topJobSpikes = a.SchedulerSpikes.Select(x => new
                {
                    x.Sequence,
                    x.Frame,
                    durationMs = Round(x.DurationMs, 6),
                    owner = x.Owner,
                    jobType = x.JobType,
                    job = x.Job,
                    cadence = FormatCadence(x.IntervalValue, x.IntervalUnit)
                })
            },
            frameTime = a.FrameTime is null
                ? null
                : new
                {
                    source = a.FrameTime.SourceFile,
                    appVersion = a.FrameTime.AppVersion,
                    game = a.FrameTime.GameName,
                    gpu = a.FrameTime.GPU,
                    processor = a.FrameTime.Processor,
                    sync = new
                    {
                        quality = a.FrameTime.SyncQuality,
                        correlated = a.FrameTime.Correlated,
                        exactAlignment = a.FrameTime.ExactAlignment,
                        alignmentMethod = a.FrameTime.AlignmentMethod,
                        capFrameXRecordUtc = a.FrameTime.CapFrameXRecordUtc?.ToString("O"),
                        cetStartUtc = a.FrameTime.CetStartUtc?.ToString("O"),
                        durationDeltaMs = double.IsFinite(a.FrameTime.DurationDeltaMs) ? Round(a.FrameTime.DurationDeltaMs, 3) : (double?)null,
                        recordLagMs = double.IsFinite(a.FrameTime.RecordLagMs) ? Round(a.FrameTime.RecordLagMs, 3) : (double?)null
                    },
                    frames = new
                    {
                        count = a.FrameTime.FrameCount,
                        durationSeconds = Round(a.FrameTime.DurationSeconds, 3),
                        averageFps = Round(a.FrameTime.AverageFps, 3),
                        meanMs = Round(a.FrameTime.MeanFrameMs, 6),
                        medianMs = Round(a.FrameTime.MedianFrameMs, 6),
                        p95Ms = Round(a.FrameTime.P95FrameMs, 6),
                        p99Ms = Round(a.FrameTime.P99FrameMs, 6),
                        maxMs = Round(a.FrameTime.MaxFrameMs, 6),
                        over25Ms = a.FrameTime.FramesOver25Ms,
                        over33_3Ms = a.FrameTime.FramesOver33Ms,
                        over50Ms = a.FrameTime.FramesOver50Ms,
                        over100Ms = a.FrameTime.FramesOver100Ms,
                        meanCpuActiveMs = Round(a.FrameTime.MeanCpuActiveMs, 6),
                        p95CpuActiveMs = Round(a.FrameTime.P95CpuActiveMs, 6),
                        meanGpuActiveMs = Round(a.FrameTime.MeanGpuActiveMs, 6),
                        p95GpuActiveMs = Round(a.FrameTime.P95GpuActiveMs, 6)
                    },
                    hitchPressure = a.FrameTime.HitchPressure is null
                        ? null
                        : new
                        {
                            vocabularyVersion = a.FrameTime.HitchPressure.VocabularyVersion,
                            semantics = "FRAME_EXCESS_OVER_ADAPTIVE_TOLERANCE; RUNTIME_OVERLAP_IS_CORRELATION_NOT_CAUSATION",
                            tolerance = new
                            {
                                baselineFrameMs = Round(a.FrameTime.HitchPressure.BaselineFrameMs, 6),
                                thresholdFrameMs = Round(a.FrameTime.HitchPressure.ToleranceFrameMs, 6),
                                baselineMultiplier = a.FrameTime.HitchPressure.BaselineMultiplier,
                                minimumAbsoluteThresholdMs = a.FrameTime.HitchPressure.MinimumAbsoluteThresholdMs,
                                episodeMergeGapMs = a.FrameTime.HitchPressure.EpisodeMergeGapMs
                            },
                            frequency = new
                            {
                                hitchFrames = a.FrameTime.HitchPressure.HitchFrameCount,
                                episodes = a.FrameTime.HitchPressure.EpisodeCount,
                                severeEpisodes = a.FrameTime.HitchPressure.SevereEpisodeCount,
                                catastrophicEpisodes = a.FrameTime.HitchPressure.CatastrophicEpisodeCount,
                                episodesPerMinute = Round(a.FrameTime.HitchPressure.EpisodesPerMinute, 6),
                                medianEpisodeStartGapMs = Round(a.FrameTime.HitchPressure.MedianEpisodeStartGapMs, 6),
                                p90EpisodeStartGapMs = Round(a.FrameTime.HitchPressure.P90EpisodeStartGapMs, 6),
                                longestQuietMs = Round(a.FrameTime.HitchPressure.LongestQuietMs, 6),
                                hitchTollMs = Round(a.FrameTime.HitchPressure.HitchTollMs, 6)
                            },
                            attribution = new
                            {
                                exact = a.FrameTime.HitchPressure.ExactRuntimeAttribution,
                                runtimeSignalEpisodes = a.FrameTime.HitchPressure.RuntimeSignalEpisodes,
                                noRecordedRuntimeSignalEpisodes = a.FrameTime.HitchPressure.NoRecordedRuntimeSignalEpisodes,
                                authorizesTransform = false,
                                owners = a.FrameTime.HitchPressure.Owners.Take(20).Select(x => new
                                {
                                    owner = x.Owner,
                                    episodes = x.EpisodeCount,
                                    hitchFrames = x.HitchFrameCount,
                                    recordedSpikes = x.SpikeCount,
                                    soleSignalEpisodes = x.SoleSignalEpisodes,
                                    episodeSharePct = Round(x.EpisodeSharePct, 6),
                                    recordedExclusiveMs = Round(x.RecordedExclusiveMs, 6),
                                    maxRecordedExclusiveMs = Round(x.MaxRecordedExclusiveMs, 6)
                                })
                            },
                            episodes = a.FrameTime.HitchPressure.Episodes.Take(250).Select(x => new
                            {
                                x.Index,
                                startMs = Round(x.StartMs, 3),
                                endMs = Round(x.EndMs, 3),
                                frames = x.FrameCount,
                                peakFrameMs = Round(x.PeakFrameMs, 6),
                                tollMs = Round(x.TollMs, 6),
                                severity = x.Severity,
                                runtimeOwners = x.RuntimeOwners,
                                topRuntimeOwner = x.TopRuntimeOwner,
                                topRuntimeExclusiveMs = Round(x.TopRuntimeExclusiveMs, 6)
                            })
                        },
                    correlation = new
                    {
                        highCetThresholdMsPer50msWindow = Round(a.FrameTime.HighCetThresholdMs, 6),
                        slowFramesHighCet = a.FrameTime.SlowFramesHighCet,
                        slowFramesExactCallback = a.FrameTime.SlowFramesExactCallback,
                        slowFramesSchedulerBurst = a.FrameTime.SlowFramesSchedulerBurst,
                        slowFramesCetNormal = a.FrameTime.SlowFramesCetNormal,
                        topCetWindowsWithSlowFrame = a.FrameTime.TopCetWindowsWithSlowFrame,
                        topCetWindowCount = a.FrameTime.TopCetWindowCount,
                        highCetWindowSlowRatePct = Round(a.FrameTime.HighCetWindowSlowRatePct, 3),
                        normalCetWindowSlowRatePct = Round(a.FrameTime.NormalCetWindowSlowRatePct, 3),
                        pearson50ms = Round(a.FrameTime.PearsonWindowCorrelation, 6),
                        spearman50ms = Round(a.FrameTime.SpearmanWindowCorrelation, 6)
                    },
                    worstFrames = a.FrameTime.WorstFrames.Select(x => new
                    {
                        frameIndex = x.FrameIndex,
                        startMs = Round(x.StartMs, 3),
                        frameMs = Round(x.FrameMs, 6),
                        cpuActiveMs = Round(x.CpuActiveMs, 6),
                        gpuActiveMs = Round(x.GpuActiveMs, 6),
                        pcLatencyMs = Round(x.PcLatencyMs, 6),
                        frameType = x.FrameType,
                        cetWindowMs = Round(x.CetWindowMs, 6),
                        topCetOwner = x.TopCetOwner,
                        topCetOwnerMs = Round(x.TopCetOwnerMs, 6),
                        highCet = x.HighCet,
                        evidence = x.Evidence
                    })
                },
            findings = a.Findings.Select(x => new
            {
                x.Title,
                x.Evidence,
                x.Explanation
            }),
            dataIndex = BuildDataIndex(captureRoot),
            data = new
            {
                runtime = RuntimeFiles
                    .Where(x => File.Exists(Path.Combine(captureRoot, "Data", "Runtime", x)))
                    .OrderBy(x => x)
                    .Select(x => "Data/Runtime/" + x),
                scheduler = SchedulerFiles
                    .Where(x => File.Exists(Path.Combine(captureRoot, "Data", "Scheduler", x)))
                    .OrderBy(x => x)
                    .Select(x => "Data/Scheduler/" + x)
            }
        };

        File.WriteAllText(
            summaryPath,
            JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        // Stream the large machine handoff directly to disk. This avoids building
        // another giant formatted JSON string in memory and cuts needless write volume.
        using (var resolverStream = File.Create(resolverInputPath))
        {
            JsonSerializer.Serialize(
                resolverStream,
                BuildResolverInput(captureRoot, a),
                ResolverJsonOptions);
            resolverStream.WriteByte((byte)'\n');
        }

        File.WriteAllText(
            reportPath,
            BuildHtml(a, captureRoot),
            new UTF8Encoding(false));

        return new ResultReportBuildResult(
            reportPath,
            summaryPath,
            a.Findings.Count,
            a.Owners.Count > 0 || a.TopCallbacks.Count > 0,
            a.SchedulerJobs.Count > 0 || a.WorstSchedulerBurst is not null,
            a.FrameTime is not null);
    }

    private static string ReadCaptureTitle(string captureRoot)
    {
        var path = Path.Combine(captureRoot, "CaptureTitle.txt");
        if (!File.Exists(path))
            return "WORLD";

        try
        {
            return File.ReadLines(path)
                .Select(x => x.Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
                ?? "WORLD";
        }
        catch
        {
            return "WORLD";
        }
    }

    private static bool IsInfrastructureOwner(string owner) =>
        string.Equals(owner, "0-Engine", StringComparison.OrdinalIgnoreCase);

    private static object BuildDataIndex(string captureRoot)
    {
        var files = Directory.EnumerateFiles(captureRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(captureRoot, path).Replace('\\', '/'))
            .Where(x => !x.Equals(ReportFileName, StringComparison.OrdinalIgnoreCase) &&
                        !x.Equals(SummaryFileName, StringComparison.OrdinalIgnoreCase) &&
                        !x.Equals(ResolverInputFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        static bool Starts(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        var runtime = files.Where(x => Starts(x, "Data/Runtime/")).ToArray();
        var scheduler = files.Where(x => Starts(x, "Data/Scheduler/")).ToArray();
        var developer = files.Where(x => Starts(x, "Data/Developer/")).ToArray();
        var metadata = files.Where(x =>
                Starts(x, "Data/Metadata/") ||
                x.Equals("CaptureTitle.txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var frameTime = files.Where(x => Starts(x, "FrameTime/")).ToArray();

        var indexed = new HashSet<string>(
            runtime.Concat(scheduler).Concat(developer).Concat(metadata).Concat(frameTime),
            StringComparer.OrdinalIgnoreCase);

        return new
        {
            runtime,
            scheduler,
            developer,
            metadata,
            frameTime,
            other = files.Where(x => !indexed.Contains(x)).ToArray()
        };
    }
}
