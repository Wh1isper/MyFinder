namespace MyFinder.Core.Monitoring;

public static class PlotReduction
{
    // Select original samples, never interpolate. Bucket extrema retain short spikes.
    public static Observation[][] Segments(Observation[] samples, string metric, double maximumGap, int pixelWidth)
    {
        List<List<Observation>> segments = [];
        List<Observation>? current = null;
        Observation? previous = null;
        foreach (var sample in samples)
        {
            var value = sample.Get(metric);
            if (value.Value is null || value.Quality != Quality.Fresh) { current = null; previous = null; continue; }
            if (current is null || previous is null || previous.Segment != sample.Segment || sample.Elapsed - previous.Elapsed > maximumGap)
            { current = []; segments.Add(current); }
            current.Add(sample);
            previous = sample;
        }
        int bucket = Math.Max(1, samples.Length / Math.Max(1, pixelWidth));
        return segments.Select(segment =>
        {
            if (bucket <= 2) return segment.ToArray();
            List<Observation> reduced = [];
            foreach (var group in segment.Chunk(bucket))
            {
                var selected = new[] { group[0], group.MinBy(x => x.Get(metric).Value)!, group.MaxBy(x => x.Get(metric).Value)!, group[^1] };
                reduced.AddRange(selected.Distinct().OrderBy(x => x.Elapsed));
            }
            return reduced.ToArray();
        }).ToArray();
    }
}
