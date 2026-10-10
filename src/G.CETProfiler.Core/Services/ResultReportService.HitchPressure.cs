namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private const double HitchMinimumAbsoluteThresholdMs = 25.0;
    private const double HitchBaselineMultiplier = 1.50;
    private const double HitchEpisodeMergeGapMs = 250.0;

    private sealed class HitchPressureAnalysis
    {
        public string VocabularyVersion { get; init; } = "1.0";
        public double BaselineFrameMs { get; init; }
        public double ToleranceFrameMs { get; init; }
        public double BaselineMultiplier { get; init; }
        public double MinimumAbsoluteThresholdMs { get; init; }
        public double EpisodeMergeGapMs { get; init; }
        public int HitchFrameCount { get; init; }
        public int EpisodeCount { get; init; }
        public int SevereEpisodeCount { get; init; }
        public int CatastrophicEpisodeCount { get; init; }
        public double EpisodesPerMinute { get; init; }
        public double MedianEpisodeStartGapMs { get; init; }
        public double P90EpisodeStartGapMs { get; init; }
        public double LongestQuietMs { get; init; }
        public double HitchTollMs { get; init; }
        public int RuntimeSignalEpisodes { get; init; }
        public int NoRecordedRuntimeSignalEpisodes { get; init; }
        public bool ExactRuntimeAttribution { get; init; }
        public List<HitchOwnerPressureMetric> Owners { get; init; } = [];
        public List<HitchEpisodeMetric> Episodes { get; init; } = [];
    }

    private sealed class HitchOwnerPressureMetric
    {
        public string Owner { get; init; } = "";
        public int EpisodeCount { get; init; }
        public int HitchFrameCount { get; init; }
        public int SpikeCount { get; init; }
        public int SoleSignalEpisodes { get; init; }
        public double EpisodeSharePct { get; init; }
        public double RecordedExclusiveMs { get; init; }
        public double MaxRecordedExclusiveMs { get; init; }
    }

    private sealed class HitchEpisodeMetric
    {
        public int Index { get; init; }
        public double StartMs { get; init; }
        public double EndMs { get; init; }
        public int FrameCount { get; init; }
        public double PeakFrameMs { get; init; }
        public double TollMs { get; init; }
        public string Severity { get; init; } = "";
        public string[] RuntimeOwners { get; init; } = Array.Empty<string>();
        public string TopRuntimeOwner { get; init; } = "";
        public double TopRuntimeExclusiveMs { get; init; }
    }

    private sealed class MutableHitchEpisode
    {
        public double StartMs { get; set; }
        public double EndMs { get; set; }
        public List<CapFrameMetric> Frames { get; } = [];
    }

    private static HitchPressureAnalysis BuildHitchPressure(
        IReadOnlyList<CapFrameMetric> frames,
        IReadOnlyList<SpikeMetric> spikes,
        bool exactAlignment)
    {
        var validFrames = frames
            .Where(x =>
                double.IsFinite(x.RelativeStartMs) &&
                double.IsFinite(x.FrameMs) &&
                x.FrameMs > 0)
            .OrderBy(x => x.RelativeStartMs)
            .ToList();

        if (validFrames.Count == 0)
            return new HitchPressureAnalysis
            {
                BaselineMultiplier = HitchBaselineMultiplier,
                MinimumAbsoluteThresholdMs = HitchMinimumAbsoluteThresholdMs,
                EpisodeMergeGapMs = HitchEpisodeMergeGapMs,
                ExactRuntimeAttribution = false
            };

        var baseline = Percentile(
            validFrames.Select(x => x.FrameMs),
            0.50);
        var tolerance = Math.Max(
            HitchMinimumAbsoluteThresholdMs,
            baseline * HitchBaselineMultiplier);

        var hitchFrames = validFrames
            .Where(x => x.FrameMs >= tolerance)
            .ToList();
        var episodeBuilders = BuildHitchEpisodes(
            hitchFrames,
            HitchEpisodeMergeGapMs);

        var episodeStartGaps = episodeBuilders
            .Zip(
                episodeBuilders.Skip(1),
                (a, b) => b.StartMs - a.StartMs)
            .Where(x => x > 0 && double.IsFinite(x))
            .ToArray();

        var quietGaps = episodeBuilders
            .Zip(
                episodeBuilders.Skip(1),
                (a, b) => Math.Max(0.0, b.StartMs - a.EndMs))
            .Where(double.IsFinite)
            .ToArray();

        var durationMs =
            validFrames.Max(x => x.RelativeStartMs + x.FrameMs) -
            validFrames.Min(x => x.RelativeStartMs);
        var hitchTollMs = hitchFrames.Sum(x =>
            Math.Max(0.0, x.FrameMs - tolerance));

        var episodeMetrics = new List<HitchEpisodeMetric>();
        var ownerEpisodes = new Dictionary<string, HashSet<int>>(
            StringComparer.OrdinalIgnoreCase);
        var ownerFrames = new Dictionary<string, HashSet<int>>(
            StringComparer.OrdinalIgnoreCase);
        var ownerSpikes = new Dictionary<string, Dictionary<long, SpikeMetric>>(
            StringComparer.OrdinalIgnoreCase);
        var ownerSoleEpisodes = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        var runtimeSignalEpisodes = 0;

        for (var episodeIndex = 0; episodeIndex < episodeBuilders.Count; episodeIndex++)
        {
            var episode = episodeBuilders[episodeIndex];
            var overlappingSpikes = exactAlignment
                ? spikes
                    .Where(spike => episode.Frames.Any(frame =>
                        Overlaps(
                            frame.RelativeStartMs,
                            frame.RelativeStartMs + frame.FrameMs,
                            spike.CaptureStartMs,
                            spike.CaptureEndMs)))
                    .GroupBy(x => x.Sequence)
                    .Select(g => g.OrderByDescending(x => x.ExclusiveMs).First())
                    .ToList()
                : [];

            var owners = overlappingSpikes
                .Select(x => x.Mod)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (owners.Length > 0)
                runtimeSignalEpisodes++;

            var topSpike = overlappingSpikes
                .OrderByDescending(x => x.ExclusiveMs)
                .FirstOrDefault();

            foreach (var owner in owners)
            {
                if (!ownerEpisodes.TryGetValue(owner, out var episodesForOwner))
                {
                    episodesForOwner = new HashSet<int>();
                    ownerEpisodes[owner] = episodesForOwner;
                }
                episodesForOwner.Add(episodeIndex);

                if (!ownerFrames.TryGetValue(owner, out var framesForOwner))
                {
                    framesForOwner = new HashSet<int>();
                    ownerFrames[owner] = framesForOwner;
                }

                foreach (var frame in episode.Frames)
                {
                    if (overlappingSpikes.Any(spike =>
                        string.Equals(spike.Mod, owner, StringComparison.OrdinalIgnoreCase) &&
                        Overlaps(
                            frame.RelativeStartMs,
                            frame.RelativeStartMs + frame.FrameMs,
                            spike.CaptureStartMs,
                            spike.CaptureEndMs)))
                    {
                        framesForOwner.Add(frame.Index);
                    }
                }

                if (!ownerSpikes.TryGetValue(owner, out var spikesForOwner))
                {
                    spikesForOwner = new Dictionary<long, SpikeMetric>();
                    ownerSpikes[owner] = spikesForOwner;
                }
                foreach (var spike in overlappingSpikes.Where(x =>
                    string.Equals(x.Mod, owner, StringComparison.OrdinalIgnoreCase)))
                {
                    spikesForOwner[spike.Sequence] = spike;
                }
            }

            if (owners.Length == 1)
            {
                ownerSoleEpisodes[owners[0]] =
                    ownerSoleEpisodes.TryGetValue(owners[0], out var count)
                        ? count + 1
                        : 1;
            }

            var peak = episode.Frames.Max(x => x.FrameMs);
            episodeMetrics.Add(new HitchEpisodeMetric
            {
                Index = episodeIndex + 1,
                StartMs = episode.StartMs,
                EndMs = episode.EndMs,
                FrameCount = episode.Frames.Count,
                PeakFrameMs = peak,
                TollMs = episode.Frames.Sum(x =>
                    Math.Max(0.0, x.FrameMs - tolerance)),
                Severity =
                    peak >= 100.0 ? "CATASTROPHIC" :
                    peak >= 50.0 ? "SEVERE" :
                    peak >= 33.3 ? "MAJOR" :
                    "NOTICEABLE",
                RuntimeOwners = owners,
                TopRuntimeOwner = topSpike?.Mod ?? "",
                TopRuntimeExclusiveMs = topSpike?.ExclusiveMs ?? 0
            });
        }

        var ownerMetrics = ownerEpisodes
            .Select(pair =>
            {
                ownerSpikes.TryGetValue(pair.Key, out var spikeMap);
                ownerFrames.TryGetValue(pair.Key, out var frameSet);
                var samples = spikeMap?.Values.ToArray() ?? Array.Empty<SpikeMetric>();
                return new HitchOwnerPressureMetric
                {
                    Owner = pair.Key,
                    EpisodeCount = pair.Value.Count,
                    HitchFrameCount = frameSet?.Count ?? 0,
                    SpikeCount = samples.Length,
                    SoleSignalEpisodes = ownerSoleEpisodes.TryGetValue(pair.Key, out var sole)
                        ? sole
                        : 0,
                    EpisodeSharePct = episodeBuilders.Count > 0
                        ? pair.Value.Count * 100.0 / episodeBuilders.Count
                        : 0,
                    RecordedExclusiveMs = samples.Sum(x => x.ExclusiveMs),
                    MaxRecordedExclusiveMs = samples
                        .Select(x => x.ExclusiveMs)
                        .DefaultIfEmpty(0)
                        .Max()
                };
            })
            .OrderByDescending(x => x.SoleSignalEpisodes)
            .ThenByDescending(x => x.EpisodeCount)
            .ThenByDescending(x => x.RecordedExclusiveMs)
            .ThenBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HitchPressureAnalysis
        {
            BaselineFrameMs = baseline,
            ToleranceFrameMs = tolerance,
            BaselineMultiplier = HitchBaselineMultiplier,
            MinimumAbsoluteThresholdMs = HitchMinimumAbsoluteThresholdMs,
            EpisodeMergeGapMs = HitchEpisodeMergeGapMs,
            HitchFrameCount = hitchFrames.Count,
            EpisodeCount = episodeBuilders.Count,
            SevereEpisodeCount = BuildHitchEpisodes(
                validFrames.Where(x => x.FrameMs >= 33.3).ToList(),
                HitchEpisodeMergeGapMs).Count,
            CatastrophicEpisodeCount = BuildHitchEpisodes(
                validFrames.Where(x => x.FrameMs >= 100.0).ToList(),
                HitchEpisodeMergeGapMs).Count,
            EpisodesPerMinute = durationMs > 0
                ? episodeBuilders.Count * 60_000.0 / durationMs
                : 0,
            MedianEpisodeStartGapMs = Percentile(episodeStartGaps, 0.50),
            P90EpisodeStartGapMs = Percentile(episodeStartGaps, 0.90),
            LongestQuietMs = quietGaps.Length == 0 ? 0 : quietGaps.Max(),
            HitchTollMs = hitchTollMs,
            RuntimeSignalEpisodes = runtimeSignalEpisodes,
            NoRecordedRuntimeSignalEpisodes =
                Math.Max(0, episodeBuilders.Count - runtimeSignalEpisodes),
            ExactRuntimeAttribution = exactAlignment,
            Owners = ownerMetrics,
            Episodes = episodeMetrics
        };
    }

    private static List<MutableHitchEpisode> BuildHitchEpisodes(
        IReadOnlyList<CapFrameMetric> frames,
        double mergeGapMs)
    {
        var output = new List<MutableHitchEpisode>();

        foreach (var frame in frames.OrderBy(x => x.RelativeStartMs))
        {
            var start = frame.RelativeStartMs;
            var end = frame.RelativeStartMs + frame.FrameMs;
            var current = output.LastOrDefault();

            if (current is null || start - current.EndMs > mergeGapMs)
            {
                current = new MutableHitchEpisode
                {
                    StartMs = start,
                    EndMs = end
                };
                output.Add(current);
            }
            else
            {
                current.EndMs = Math.Max(current.EndMs, end);
            }

            current.Frames.Add(frame);
        }

        return output;
    }
}
