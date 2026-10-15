namespace MyFinder.Core.Monitoring;

public sealed class MonitoringSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Reading> _latest = [];
    private readonly Dictionary<string, bool> _wasMonitored = [];
    private readonly Dictionary<string, long> _selectionSegments = [];
    private Preferences _preferences;
    private readonly Task _system, _network;
    private readonly NetworkCollector _networkCollector = new();
    public bool NetworkReady { get; private set; }
    public HistoryStore History { get; } = new();
    public MonitoringSession(Preferences preferences)
    {
        _preferences = preferences;
        _networkCollector.Configure(preferences);
        _system = Task.Run(SystemLoop);
        _network = Task.Run(NetworkLoop);
    }
    public void Configure(Preferences preferences)
    {
        _networkCollector.Configure(preferences);
        Volatile.Write(ref _preferences, preferences);
        History.Prune(ObservationClock.Elapsed, preferences.RetentionMinutes * 60);
    }
    public Reading[] Snapshot()
    {
        lock (_gate)
        {
            double now = ObservationClock.Elapsed;
            double freshness = Math.Max(5, Volatile.Read(ref _preferences).IntervalSeconds * 3);
            return _latest.Values.Select(x => now - x.Observation.Elapsed > freshness ? x with
            {
                Observation = x.Observation with { Values = x.Observation.Values.ToDictionary(p => p.Key, p => p.Value with { Value = null, Quality = Quality.Stale, Reason = "No recent observation; the source may be delayed." }) }
            } : x).ToArray();
        }
    }
    private void Publish(IEnumerable<Reading> readings)
    {
        var preferences = Volatile.Read(ref _preferences);
        lock (_gate)
        {
            foreach (var reading in readings)
            {
                string id = reading.Resource.Id;
                bool monitored = preferences.Monitored.Contains(id);
                if (_wasMonitored.TryGetValue(id, out bool old) && old != monitored) _selectionSegments[id] = _selectionSegments.GetValueOrDefault(id) + 1;
                _wasMonitored[id] = monitored;
                var sample = reading.Observation with { Segment = reading.Observation.Segment + _selectionSegments.GetValueOrDefault(id) * 1_000_000 };
                if (monitored) History.Append(sample, preferences.RetentionMinutes * 60);
                else sample = sample with { Values = sample.Values.ToDictionary(x => x.Key, x => Metric.Missing(x.Value.Unit, "Enable monitoring in Settings to collect this resource.", Quality.NotMonitored)) };
                _latest[id] = reading with { Observation = sample };
            }
        }
    }
    private async Task NetworkLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var readings = _networkCollector.Collect();
                lock (_gate)
                {
                    var ids = readings.Select(x => x.Resource.Id).ToHashSet();
                    foreach (string id in _latest.Keys.Where(x => x.StartsWith("network:") && !ids.Contains(x)).ToArray())
                    { _latest.Remove(id); _wasMonitored.Remove(id); _selectionSegments.Remove(id); }
                    Publish(readings);
                    NetworkReady = true;
                }
                await Task.Delay(TimeSpan.FromSeconds(Volatile.Read(ref _preferences).IntervalSeconds), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task SystemLoop()
    {
        using var collector = new SystemCollector();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var preferences = Volatile.Read(ref _preferences);
                Publish(collector.Collect(preferences.IntervalSeconds));
                History.Prune(ObservationClock.Elapsed, preferences.RetentionMinutes * 60);
                await Task.Delay(TimeSpan.FromSeconds(preferences.IntervalSeconds), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_system, _network).ConfigureAwait(false);
        _stop.Dispose();
    }
}
