using System.Diagnostics;
using System.Globalization;

namespace MyFinder.Core.Monitoring;

public enum ResourceKind { Cpu, Memory, Network }
public enum Quality { Fresh, WarmingUp, Unavailable, Disconnected, NotMonitored, Stale }
public enum Unit { Percent, Bytes, BytesPerSecond, Megahertz, Count, Milliseconds, Seconds }
public sealed record Metric(double? Value, Unit Unit, Quality Quality = Quality.Fresh, string? Reason = null)
{
    public static Metric Missing(Unit unit, string reason, Quality quality = Quality.Unavailable) => new(null, unit, quality, reason);
}
public sealed record Resource(string Id, ResourceKind Kind, string Name, IReadOnlyDictionary<string, string> Facts);
public sealed record Observation(string ResourceId, DateTimeOffset Time, double Elapsed, long Segment, IReadOnlyDictionary<string, Metric> Values)
{
    public Metric Get(string metric, Unit unit = Unit.Count) => Values.TryGetValue(metric, out var value) ? value : Metric.Missing(unit, "No observation for this field.");
}
public sealed record Reading(Resource Resource, Observation Observation);
public static class ObservationClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();
    public static double Elapsed => Stopwatch.GetElapsedTime(Origin).TotalSeconds;
}

public sealed class CounterRate
{
    private ulong? _previous;
    private double _time;
    public long Segment { get; private set; }
    public double Total { get; private set; }
    public double CoverageSeconds { get; private set; }
    public Metric Observe(ulong value, double time, double maximumGap)
    {
        ulong? previous = _previous;
        double interval = time - _time;
        _previous = value;
        _time = time;
        if (previous is null || interval <= 0 || interval > maximumGap || value < previous)
        {
            Segment++;
            return Metric.Missing(Unit.BytesPerSecond, "Establishing a counter baseline; gaps and resets are excluded from totals.", Quality.WarmingUp);
        }
        double delta = value - previous.Value;
        Total += delta;
        CoverageSeconds += interval;
        return new(delta / interval, Unit.BytesPerSecond);
    }
    public void Break() { _previous = null; }
}

public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<Observation>> _series = [];
    private long _bytes;
    public long ByteLimit { get; }
    public bool BudgetLimited { get; private set; }
    public HistoryStore(long byteLimit = 64 * 1024 * 1024) { ByteLimit = byteLimit; }
    private static long Cost(Observation value) => 256 + value.Values.Count * 192L;
    public void Append(Observation observation, double retentionSeconds)
    {
        lock (_gate)
        {
            if (!_series.TryGetValue(observation.ResourceId, out var queue)) _series[observation.ResourceId] = queue = new();
            if (queue.TryPeek(out _) && queue.Last().Elapsed >= observation.Elapsed) return;
            queue.Enqueue(observation);
            _bytes += Cost(observation);
            PruneCore(observation.Elapsed - retentionSeconds);
            while (_bytes > ByteLimit && _series.Count > 0)
            {
                var oldest = _series.MinBy(x => x.Value.Peek().Elapsed);
                RemoveFirst(oldest.Key, oldest.Value);
                BudgetLimited = true;
            }
        }
    }
    private void RemoveFirst(string id, Queue<Observation> queue)
    {
        _bytes -= Cost(queue.Dequeue());
        if (queue.Count == 0) _series.Remove(id);
    }
    private void PruneCore(double cutoff)
    {
        foreach (var pair in _series.ToArray())
            while (pair.Value.TryPeek(out var sample) && sample.Elapsed < cutoff) RemoveFirst(pair.Key, pair.Value);
    }
    public void Prune(double now, double retentionSeconds) { lock (_gate) PruneCore(now - retentionSeconds); }
    public Observation[] Read(string id, double since)
    { lock (_gate) return _series.TryGetValue(id, out var values) ? values.Where(x => x.Elapsed >= since).ToArray() : []; }
    public (int Resources, int Samples, long EstimatedBytes) Statistics
    { get { lock (_gate) return (_series.Count, _series.Values.Sum(x => x.Count), _bytes); } }
}

public static class ValueFormat
{
    public static string Metric(Metric metric, Preferences preferences, bool storage = false)
    {
        if (metric.Value is not double number || !double.IsFinite(number)) return metric.Quality == Quality.WarmingUp ? "Warming up" : "Unavailable";
        return metric.Unit switch
        {
            Unit.Percent => $"{number:F1}%",
            Unit.Bytes => Size(number, storage ? preferences.StorageBinary : true),
            Unit.BytesPerSecond => preferences.NetworkBits && !storage ? Size(number * 8, false, true) + "/s" : Size(number, storage ? preferences.StorageBinary : false) + "/s",
            Unit.Megahertz => $"{number / 1000:F2} GHz",
            Unit.Milliseconds => $"{number:F2} ms",
            Unit.Seconds => TimeSpan.FromSeconds(Math.Max(0, number)).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture),
            _ => number.ToString("N0", CultureInfo.CurrentCulture)
        };
    }
    public static string Size(double value, bool binary, bool bits = false)
    {
        string[] suffixes = bits ? ["b", "kb", "Mb", "Gb", "Tb"] : binary ? ["B", "KiB", "MiB", "GiB", "TiB"] : ["B", "kB", "MB", "GB", "TB"];
        double divisor = binary ? 1024 : 1000;
        int index = 0;
        while (Math.Abs(value) >= divisor && index < suffixes.Length - 1) { value /= divisor; index++; }
        return $"{value:0.##} {suffixes[index]}";
    }
}
