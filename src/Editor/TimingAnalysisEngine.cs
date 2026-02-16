namespace RhythmbulletPrototype.Editor;

public static class TimingAnalysisEngine
{
    private const int DefaultSectionMs = 4000;
    private const int HeatBins = 48;

    public static TimingAnalysisSnapshot Build(LevelDocument level, int timelineEndMs)
    {
        if (level is null || level.Notes.Count < 2)
        {
            return TimingAnalysisSnapshot.Empty;
        }

        var notes = level.Notes
            .Select(n => Math.Max(0, n.TimeMs))
            .OrderBy(t => t)
            .ToList();

        if (notes.Count < 2)
        {
            return TimingAnalysisSnapshot.Empty;
        }

        var intervals = BuildIntervals(notes);
        var medianInterval = Median(intervals);
        if (medianInterval <= 0.0001)
        {
            return TimingAnalysisSnapshot.Empty;
        }

        var estimatedBpm = level.Bpm.GetValueOrDefault(60000d / medianInterval);
        estimatedBpm = Math.Clamp(estimatedBpm, 30d, 360d);
        var beatMs = 60000d / estimatedBpm;
        var cappedTimeline = Math.Max(notes[^1] + DefaultSectionMs, timelineEndMs);

        var sectionDrift = BuildSectionDrift(notes, cappedTimeline, beatMs);
        var instability = BuildTempoInstability(intervals);
        var redlines = BuildRedlineSuggestions(notes, cappedTimeline, beatMs);
        var heat = BuildHumanizationHeatmap(notes, cappedTimeline, beatMs);

        return new TimingAnalysisSnapshot
        {
            Source = "OnsetProxy",
            SectionLengthMs = DefaultSectionMs,
            TempoInstabilityPercent = instability,
            EstimatedBpm = estimatedBpm,
            SectionDrift = sectionDrift,
            RedlineSuggestions = redlines,
            HumanizationHeatmap = heat
        };
    }

    private static List<double> BuildIntervals(IReadOnlyList<int> notes)
    {
        var intervals = new List<double>(Math.Max(0, notes.Count - 1));
        for (var i = 1; i < notes.Count; i++)
        {
            var dt = notes[i] - notes[i - 1];
            if (dt > 8)
            {
                intervals.Add(dt);
            }
        }

        return intervals;
    }

    private static IReadOnlyList<TimingSectionDrift> BuildSectionDrift(
        IReadOnlyList<int> notes,
        int timelineEndMs,
        double beatMs)
    {
        var sections = new List<TimingSectionDrift>();
        for (var start = 0; start < timelineEndMs; start += DefaultSectionMs)
        {
            var end = start + DefaultSectionMs;
            var inSection = notes.Where(t => t >= start && t < end).ToList();
            if (inSection.Count == 0)
            {
                sections.Add(new TimingSectionDrift(start, end, 0d, 0d, 0));
                continue;
            }

            var offsets = new List<double>(inSection.Count);
            foreach (var t in inSection)
            {
                offsets.Add(SignedDistanceToNearestGrid(t, start, beatMs));
            }

            var mean = offsets.Average();
            var spread = Math.Sqrt(offsets.Select(x => (x - mean) * (x - mean)).Average());
            sections.Add(new TimingSectionDrift(start, end, mean, spread, offsets.Count));
        }

        return sections;
    }

    private static double BuildTempoInstability(IReadOnlyList<double> intervals)
    {
        if (intervals.Count < 3)
        {
            return 0d;
        }

        var med = Median(intervals);
        if (med <= 0.0001)
        {
            return 0d;
        }

        var mad = Median(intervals.Select(v => Math.Abs(v - med)).ToList());
        return (mad / med) * 100d;
    }

    private static IReadOnlyList<RedlineSuggestion> BuildRedlineSuggestions(
        IReadOnlyList<int> notes,
        int timelineEndMs,
        double baseBeatMs)
    {
        const int windowMs = 8000;
        var suggestions = new List<RedlineSuggestion>();
        var lastBpm = 60000d / Math.Max(1d, baseBeatMs);

        for (var start = 0; start < timelineEndMs; start += windowMs)
        {
            var end = start + windowMs;
            var local = notes.Where(t => t >= start && t < end).ToList();
            if (local.Count < 3)
            {
                continue;
            }

            var localIntervals = BuildIntervals(local);
            if (localIntervals.Count < 2)
            {
                continue;
            }

            var localBeatMs = Median(localIntervals);
            if (localBeatMs <= 0.0001)
            {
                continue;
            }

            var localBpm = 60000d / localBeatMs;
            var deltaPct = Math.Abs((localBpm - lastBpm) / Math.Max(1d, lastBpm)) * 100d;
            if (deltaPct >= 4d)
            {
                suggestions.Add(new RedlineSuggestion(
                    local[0],
                    Math.Round(localBpm, 2),
                    $"onset cluster shift {deltaPct:0.0}%"));
                lastBpm = localBpm;
            }
        }

        return suggestions;
    }

    private static IReadOnlyList<HumanizationHeatBin> BuildHumanizationHeatmap(
        IReadOnlyList<int> notes,
        int timelineEndMs,
        double beatMs)
    {
        var bins = new List<HumanizationHeatBin>(HeatBins);
        var binMs = Math.Max(250, timelineEndMs / HeatBins);

        for (var i = 0; i < HeatBins; i++)
        {
            var start = i * binMs;
            var end = (i == HeatBins - 1) ? timelineEndMs : start + binMs;
            var local = notes.Where(t => t >= start && t < end).ToList();
            if (local.Count == 0)
            {
                bins.Add(new HumanizationHeatBin(start, end, 0, 0, 0d));
                continue;
            }

            var offsets = new List<double>(local.Count);
            foreach (var t in local)
            {
                offsets.Add(SignedDistanceToNearestGrid(t, 0, beatMs));
            }

            var early = offsets.Count(o => o < -0.25d);
            var late = offsets.Count(o => o > 0.25d);
            var mean = offsets.Average();
            bins.Add(new HumanizationHeatBin(start, end, early, late, mean));
        }

        return bins;
    }

    private static double SignedDistanceToNearestGrid(int timeMs, int gridStartMs, double beatMs)
    {
        var rel = Math.Max(0d, timeMs - gridStartMs);
        var step = Math.Round(rel / beatMs);
        return rel - (step * beatMs);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        if ((sorted.Count & 1) == 1)
        {
            return sorted[mid];
        }

        return (sorted[mid - 1] + sorted[mid]) * 0.5d;
    }
}
