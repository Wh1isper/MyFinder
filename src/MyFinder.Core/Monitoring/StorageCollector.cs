using System.ComponentModel;

namespace MyFinder.Core.Monitoring;

internal sealed class DeviceActivity : IDisposable
{
    private PerformanceQuery? _query;
    private string _binding = "";
    private double _last;
    private long _generation = -1, _segment;
    public Reading Collect(StorageDevice device, double interval, long generation, bool monitored)
    {
        double now = ObservationClock.Elapsed;
        if (!monitored) Dispose();
        string binding = device.Id + ":" + device.Number;
        bool baseline = _query is null || binding != _binding || generation != _generation || now - _last > Math.Max(5, interval * 3);
        if (baseline)
        {
            Dispose(); _segment++; _binding = binding; _generation = generation;
            if (monitored && device.Durable)
            {
                _query = new();
                foreach (var pair in new Dictionary<string, string> { ["Read"] = "Disk Read Bytes/sec", ["Write"] = "Disk Write Bytes/sec", ["Idle"] = "% Idle Time", ["Response"] = "Avg. Disk sec/Transfer" })
                    _query.Add(pair.Key, $@"\PhysicalDisk({device.Number}*)\{pair.Value}");
            }
        }
        _last = now;
        var values = new Dictionary<string, Metric>();
        if (_query is not null)
        {
            _query.Collect();
            foreach (var pair in new Dictionary<string, Unit> { ["Read"] = Unit.BytesPerSecond, ["Write"] = Unit.BytesPerSecond, ["Idle"] = Unit.Percent, ["Response"] = Unit.Seconds })
            {
                var candidates = _query.ReadArray(pair.Key, pair.Value).Where(x => x.Key == device.Number.ToString() || x.Key.StartsWith(device.Number + " ", StringComparison.Ordinal)).ToArray();
                values[pair.Key] = baseline ? Metric.Missing(pair.Value, "Establishing device counter baseline.", Quality.WarmingUp)
                    : candidates.Length == 1 ? candidates[0].Value : Metric.Missing(pair.Value, "No unambiguous PhysicalDisk instance matches the verified device number.");
            }
            // Reopen the enumerated interface after the PDH snapshot. A recycled number must never relabel another device's samples.
            var after = StorageNative.ReadDevice(device.Path);
            if (after.Id != device.Id || after.Number != device.Number) { Dispose(); throw new IOException("Device identity changed during activity sampling."); }
        }
        else
        {
            foreach (string key in new[] { "Read", "Write", "Idle", "Response" }) values[key] = Metric.Missing(key is "Read" or "Write" ? Unit.BytesPerSecond : key == "Idle" ? Unit.Percent : Unit.Seconds,
                monitored ? "No durable device identifier; reselect a verified device. Activity is not attributed by disk number alone." : "Enable monitoring in Settings.", monitored ? Quality.Unavailable : Quality.NotMonitored);
        }
        var idle = values["Idle"];
        values["Activity"] = idle.Value is double value && value is >= 0 and <= 100 ? new(100 - value, Unit.Percent)
            : Metric.Missing(Unit.Percent, idle.Reason ?? "Idle-time counter is outside its valid range.", idle.Value is null ? idle.Quality : Quality.Unavailable);
        values.Remove("Idle");
        var response = values["Response"];
        values["Response time"] = response with { Value = response.Value * 1000, Unit = Unit.Milliseconds }; values.Remove("Response");
        var facts = new Dictionary<string, string>
        {
            ["Model"] = device.Model.Length == 0 ? "Not reported" : device.Model, ["Bus / reported device"] = device.Bus,
            ["Device number"] = device.Number.ToString(), ["Identity"] = device.Durable ? "Device-associated VPD identifier or reported model/serial identity" : "Unverified; session-only selection",
            ["Activity meaning"] = "100 minus PhysicalDisk idle-time percentage. Shared device activity is not a per-volume percentage and must not be summed.",
            ["Response meaning"] = "PDH average seconds per completed transfer, displayed in milliseconds; includes the Windows storage stack.",
            ["Topology scope"] = "Windows-reported device. RAID, virtual disks and Storage Spaces can conceal physical media."
        };
        return new(new(device.Id, ResourceKind.Device, $"Disk {device.Number} · {device.Model}", facts), new(device.Id, DateTimeOffset.Now, now, _segment, values));
    }
    public void Dispose() { _query?.Dispose(); _query = null; }
}

// One coordinator owns the catalog. At most eight operations and one discovery can be outstanding.
// A delayed native call keeps its slot; it is never replaced with an accumulating timeout worker.
public sealed class StorageCollector : IDisposable
{
    private sealed class Entry(StorageLocation location, string fallback)
    {
        public StorageLocation Location = location;
        public string Fallback = fallback;
        public Reading? Reading;
        public Task<Reading>? Work;
        public readonly DeviceActivity Activity = new();
        public double Due, Seen;
        public long Generation, WorkGeneration;
        public bool Present = true, WorkMonitored;
    }
    private readonly object _gate = new();
    private readonly Func<StorageLocation[]> _discover;
    private readonly Func<StorageLocation, long, bool, Reading>? _sample;
    private readonly Func<double> _clock;
    private readonly Func<List<Dictionary<string, string>>> _readRoles;
    public StorageCollector() : this(StorageNative.Discover, null, () => ObservationClock.Elapsed,
        () => HardwareInventory.Query("SELECT DeviceID,BootVolume,SystemVolume,PageFilePresent FROM Win32_Volume", ["DeviceID", "BootVolume", "SystemVolume", "PageFilePresent"])) { }
    internal StorageCollector(Func<StorageLocation[]> discover, Func<StorageLocation, long, bool, Reading>? sample, Func<double> clock, Func<List<Dictionary<string, string>>> readRoles)
    { _discover = discover; _sample = sample; _clock = clock; _readRoles = readRoles; }
    private readonly Dictionary<string, Entry> _entries = [];
    private Task<StorageLocation[]>? _discovery;
    private double _discoverAt, _firstDiscovery;
    private Task<List<Dictionary<string, string>>>? _roles;
    private Dictionary<string, Dictionary<string, string>> _roleFacts = [];
    private double _rolesAt;
    private string _status = "Discovering storage resources";
    public string Status { get { lock (_gate) return _status; } }
    private bool _disposed, _discovered;
    private Preferences _preferences = new();
    public bool Ready { get { lock (_gate) return _discovered && (_entries.Values.All(x => x.Reading is not null) || _clock() - _firstDiscovery > 5); } }
    public int Pending { get { lock (_gate) return _entries.Values.Count(x => x.Work is { IsCompleted: false }); } }
    public void Configure(Preferences preferences)
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                string id = entry.Reading?.Resource.Id ?? entry.Fallback;
                if (_preferences.Monitored.Contains(id) != preferences.Monitored.Contains(id))
                {
                    entry.Generation++; entry.Due = 0;
                    entry.Reading = Missing(entry, _clock(), "Monitoring selection changed; waiting for a new observation.", Quality.WarmingUp);
                }
            }
            _preferences = preferences;
        }
    }
    public Reading[] Poll()
    {
        lock (_gate)
        {
            if (_disposed) return [];
            double now = _clock();
            if (_discovery is { IsCompleted: true })
            {
                if (_discovery.IsCompletedSuccessfully)
                {
                    var paths = _discovery.Result.Take(512).ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in _entries.Values.Where(x => !paths.ContainsKey(x.Location.Path)))
                    {
                        if (entry.Present) { entry.Generation++; entry.Due = 0; }
                        entry.Present = false;
                    }
                    foreach (var location in paths.Values)
                    {
                        if (!_entries.TryGetValue(location.Path, out var entry))
                        {
                            string id = location.Kind == ResourceKind.Volume ? StorageNative.VolumeId(location.Path) : "device:unverified-" + Guid.NewGuid().ToString("N");
                            _entries[location.Path] = entry = new(location, id);
                        }
                        if (!entry.Present) { entry.Generation++; entry.Due = 0; }
                        entry.Present = true; entry.Seen = now;
                    }
                }
                _status = _discovery.IsCompletedSuccessfully ? "Storage discovery current; local volumes and Windows-reported devices" : "Storage discovery failed; existing observations may become stale";
                _ = _discovery.Exception;
                _discovery = null; if (!_discovered) _firstDiscovery = now; _discovered = true; _discoverAt = now + 30;
            }
            if (_discovery is null && now >= _discoverAt) _discovery = Task.Run(_discover);
            if (_roles is { IsCompleted: true })
            {
                _roleFacts = _roles.IsCompletedSuccessfully ? _roles.Result.ToDictionary(x => StorageNative.VolumeId(x["DeviceID"]), x => new Dictionary<string, string>
                {
                    ["Windows boot volume (WMI)"] = x["BootVolume"], ["System boot-files volume (WMI)"] = x["SystemVolume"],
                    ["Page file present (WMI)"] = x["PageFilePresent"], ["Role inventory sampled"] = DateTimeOffset.Now.ToString("O")
                }) : [];
                _ = _roles.Exception; _roles = null; _rolesAt = now + 30;
            }
            if (_roles is null && now >= _rolesAt) _roles = Task.Run(_readRoles);
            foreach (var entry in _entries.Values)
            {
                if (entry.Work is not { IsCompleted: true }) continue;
                if (entry.WorkGeneration == entry.Generation && entry.Present)
                {
                    entry.Reading = entry.Work.IsCompletedSuccessfully ? entry.Work.Result : Missing(entry, now, "Storage source failed; retrying on its own cadence.", Quality.Unavailable);
                    // Discovery resolves a device's persistent ID after scheduling. Honor saved monitoring immediately.
                    if (_preferences.Monitored.Contains(entry.Reading.Resource.Id) != entry.WorkMonitored)
                    {
                        entry.Generation++; entry.Due = now;
                        entry.Reading = Missing(entry, now, "Device identity resolved; establishing its monitoring baseline.", Quality.WarmingUp);
                    }
                }
                _ = entry.Work.Exception;
                entry.Work = null;
            }
            int slots = 8 - _entries.Values.Count(x => x.Work is not null);
            foreach (var entry in _entries.Values.Where(x => x.Present && x.Work is null && now >= x.Due).OrderBy(x => x.Due))
            {
                if (slots-- <= 0) break;
                var preferences = _preferences;
                long generation = entry.Generation; entry.WorkGeneration = generation;
                bool monitored = preferences.Monitored.Contains(entry.Reading?.Resource.Id ?? entry.Fallback);
                entry.WorkMonitored = monitored;
                entry.Due = now + (entry.Location.Kind == ResourceKind.Volume ? preferences.CapacitySeconds : monitored ? preferences.IntervalSeconds : 30);
                entry.Work = Task.Run(() => _sample is null ? Collect(entry, preferences, generation, monitored) : _sample(entry.Location, generation, monitored));
            }
            var selected = _preferences.Monitored.Concat(_preferences.Pins.Values).Concat(_preferences.StorageComparison).ToHashSet();
            foreach (var pair in _entries.Where(x => !x.Value.Present && x.Value.Work is null && !selected.Contains(x.Value.Reading?.Resource.Id ?? x.Value.Fallback) && now - x.Value.Seen > _preferences.RetentionMinutes * 60).ToArray())
            { pair.Value.Activity.Dispose(); _entries.Remove(pair.Key); }
            foreach (var pair in _entries.Where(x => !x.Value.Present && x.Value.Work is null && !selected.Contains(x.Value.Reading?.Resource.Id ?? x.Value.Fallback)).OrderByDescending(x => x.Value.Seen).Skip(512).ToArray())
            { pair.Value.Activity.Dispose(); _entries.Remove(pair.Key); }
            var presentIds = _entries.Values.Where(x => x.Present && x.Reading is not null).Select(x => x.Reading!.Resource.Id).ToHashSet();
            // A returning durable identity may have a new interface path. Its absent aliases are not competing devices.
            foreach (var pair in _entries.Where(x => !x.Value.Present && x.Value.Work is null && x.Value.Reading is not null && presentIds.Contains(x.Value.Reading.Resource.Id)).ToArray())
            { pair.Value.Activity.Dispose(); _entries.Remove(pair.Key); }
            var readings = _entries.Values.Where(x => x.Present || x.Reading is null || !presentIds.Contains(x.Reading.Resource.Id))
                .Select(x => !x.Present ? Missing(x, now, "Resource is no longer present; the saved identity remains selected.", Quality.Disconnected)
                    : x.Reading ?? Missing(x, now, "Discovering resource facts; slow operations keep one bounded worker slot.", Quality.WarmingUp)).ToList();
            foreach (var duplicate in readings.Where(x => x.Resource.Kind == ResourceKind.Device).GroupBy(x => x.Resource.Id).Where(x => x.Count() > 1).ToArray())
            {
                foreach (var item in duplicate) readings.Remove(item);
                var first = duplicate.First();
                readings.Add(first with { Observation = first.Observation with { Values = first.Observation.Values.ToDictionary(x => x.Key, x => Metric.Missing(x.Value.Unit, "Duplicate reported device identity; activity attribution is unavailable.")) } });
            }
            foreach (string id in selected.Where(x => (x.StartsWith("volume:") || x.StartsWith("device:")) && readings.All(r => r.Resource.Id != x)))
            {
                var kind = id.StartsWith("volume:") ? ResourceKind.Volume : ResourceKind.Device;
                readings.Add(new(new(id, kind, "Saved storage resource (absent)", new Dictionary<string, string>()), new(id, DateTimeOffset.Now, now, 0,
                    MissingValues(kind, "Resource is absent; no replacement is selected automatically.", Quality.Disconnected))));
            }
            return readings.Select(reading =>
            {
                var facts = new Dictionary<string, string>(reading.Resource.Facts);
                if (reading.Resource.Kind == ResourceKind.Volume)
                {
                    if (_roleFacts.TryGetValue(reading.Resource.Id, out var roles)) foreach (var pair in roles) facts[pair.Key] = pair.Value;
                    else facts["System / page-file inventory"] = "Unavailable or loading; no role is inferred from capacity.";
                }
                else
                {
                    var volumes = readings.Where(x => x.Resource.Kind == ResourceKind.Volume && x.Resource.Facts.GetValueOrDefault("Mapped device identities", "").Split(';').Contains(reading.Resource.Id)).ToArray();
                    facts["Related volumes (snapshot mappings)"] = volumes.Length == 0 ? "No confirmed volume mapping" : string.Join(", ", volumes.Select(x => x.Resource.Name));
                    facts["System / page-file facts"] = "Inspect related volumes for their separately sampled roles; device traffic includes all mapped volumes.";
                }
                return reading with { Resource = reading.Resource with { Facts = facts } };
            }).ToArray();
        }
    }
    private static Reading Collect(Entry entry, Preferences preferences, long generation, bool monitored)
    {
        try
        {
            if (entry.Location.Kind == ResourceKind.Volume)
            {
                var snapshot = StorageNative.ReadVolume(entry.Location.Path);
                return new(snapshot.Resource, new(snapshot.Resource.Id, snapshot.Time, snapshot.Elapsed, generation, snapshot.Values));
            }
            var device = StorageNative.ReadDevice(entry.Location.Path);
            if (!device.Durable) device = device with { Id = entry.Fallback };
            return entry.Activity.Collect(device, preferences.IntervalSeconds, generation, monitored);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            entry.Activity.Dispose();
            return Missing(entry, ObservationClock.Elapsed, ex is Win32Exception native ? $"Storage query unavailable (Windows {native.NativeErrorCode})." : ex.Message, Quality.Unavailable);
        }
    }
    private static Dictionary<string, Metric> MissingValues(ResourceKind kind, string reason, Quality quality)
    {
        var result = new Dictionary<string, Metric>();
        foreach (string key in kind == ResourceKind.Volume ? new[] { "Total", "Free", "Used / reserved", "Caller available", "Utilization" } : ["Read", "Write", "Activity", "Response time"])
            result[key] = Metric.Missing(key is "Utilization" or "Activity" ? Unit.Percent : key == "Response time" ? Unit.Milliseconds : kind == ResourceKind.Volume ? Unit.Bytes : Unit.BytesPerSecond, reason, quality);
        return result;
    }
    private static Reading Missing(Entry entry, double now, string reason, Quality quality)
    {
        var resource = entry.Reading?.Resource ?? new(entry.Fallback, entry.Location.Kind, entry.Location.Kind == ResourceKind.Volume ? entry.Location.Path : "Storage device (discovering)", new Dictionary<string, string>());
        return new(resource, new(resource.Id, DateTimeOffset.Now, now, entry.Generation, MissingValues(resource.Kind, reason, quality)));
    }
    public static Preferences InitialSelection(Preferences preferences, IEnumerable<Reading> readings)
    {
        if (preferences.StorageSelectionMade) return preferences;
        if (preferences.Pins.ContainsKey("storage") || preferences.Monitored.Any(x => x.StartsWith("volume:") || x.StartsWith("device:"))) return preferences with { StorageSelectionMade = true };
        var volumes = readings.Where(x => x.Resource.Kind == ResourceKind.Volume && x.Resource.Facts.GetValueOrDefault("Contains Windows") == "Yes").ToArray();
        if (volumes.Length != 1) return preferences with { StorageSelectionMade = true };
        string id = volumes[0].Resource.Id;
        return preferences with { StorageSelectionMade = true, Monitored = preferences.Monitored.Append(id).ToArray(),
            Pins = new(preferences.Pins) { ["storage"] = id }, MetricOrder = preferences.MetricOrder.Append("storage").Distinct().ToArray() };
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                if (entry.Work is { IsCompleted: false }) _ = entry.Work.ContinueWith(_ => entry.Activity.Dispose(), TaskScheduler.Default);
                else entry.Activity.Dispose();
            }
        }
    }
}
