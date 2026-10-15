using System.Text.Json;
using MyFinder.Core;
using MyFinder.Core.Desktop;
using MyFinder.Core.Monitoring;

// Pester owns assertions. This host invokes production logic on its actual .NET runtime.
object result = args.FirstOrDefault() switch
{
    "policy" => Policy(),
    "rates" => Rates(),
    "history" => History(),
    "preferences" => await PreferencesScenario(args[1]),
    "system" => await NativeSystem(),
    "session" => await SessionScenario(),
    "expiry" => await ExpiryScenario(),
    "network-policy" => NetworkPolicy(),
    "network" => await NativeNetwork(),
    "storage" => await NativeStorage(),
    "storage-policy" => await StoragePolicy(),
    "storage-session" => await StorageSession(args[1]),
    _ => throw new ArgumentException("Specify a known scenario.")
};
Console.WriteLine(JsonSerializer.Serialize(result));

static async Task<object> StoragePolicy()
{
    double now = 0;
    StorageLocation[] topology = [new("slow", ResourceKind.Volume), new("fast", ResourceKind.Volume), new("device-path", ResourceKind.Device)];
    using var release = new ManualResetEventSlim(false);
    int slowCalls = 0, fastCalls = 0;
    string deviceIdentity = "device:first";
    using var collector = new StorageCollector(() => topology, (location, generation, monitored) =>
    {
        if (location.Path == "slow") { Interlocked.Increment(ref slowCalls); release.Wait(TimeSpan.FromSeconds(10)); }
        else Interlocked.Increment(ref fastCalls);
        string id = location.Kind == ResourceKind.Volume ? StorageNative.VolumeId(location.Path) : deviceIdentity;
        return new(new(id, location.Kind, "Same display name", new Dictionary<string, string>()), new(id, DateTimeOffset.UnixEpoch.AddSeconds(now), now, generation,
            new Dictionary<string, Metric> { [location.Kind == ResourceKind.Volume ? "Total" : "Read"] = new(100, Unit.Bytes) }));
    }, () => now, () => []);
    var preferences = new Preferences { Monitored = ["volume:slow", "volume:fast", "device:first"], Pins = new() { ["storage"] = "volume:fast" } };
    collector.Configure(preferences);
    Reading[] readings = [];
    for (int i = 0; i < 30; i++) { readings = collector.Poll(); await Task.Delay(10); }
    bool fastProgress = readings.Single(x => x.Resource.Id == "volume:fast").Observation.Get("Total").Value == 100;
    int blockedCalls = slowCalls;
    collector.Configure(preferences with { Monitored = ["device:first"] });
    collector.Configure(preferences);
    bool cacheInvalidated = collector.Poll().Single(x => x.Resource.Id == "volume:fast").Observation.Get("Total").Value is null;
    release.Set();
    for (int i = 0; i < 20; i++) { readings = collector.Poll(); await Task.Delay(10); }
    long lateGeneration = readings.Single(x => x.Resource.Id == "volume:slow").Observation.Segment;
    long beforeGeneration = readings.Single(x => x.Resource.Id == "volume:fast").Observation.Segment;
    topology = [new("device-path", ResourceKind.Device)]; now = 31;
    for (int i = 0; i < 20; i++) { readings = collector.Poll(); await Task.Delay(10); }
    bool missing = readings.Single(x => x.Resource.Id == "volume:fast").Observation.Get("Total").Quality == Quality.Disconnected;
    topology = [new("fast", ResourceKind.Volume), new("device-path", ResourceKind.Device)]; deviceIdentity = "device:replacement"; now = 62;
    for (int i = 0; i < 20; i++) { readings = collector.Poll(); await Task.Delay(10); }
    bool boundary = readings.Single(x => x.Resource.Id == "volume:fast").Observation.Segment > beforeGeneration;
    bool replaced = readings.Any(x => x.Resource.Id == "device:replacement") && readings.Single(x => x.Resource.Id == "device:first").Observation.Get("Read").Quality == Quality.Disconnected;
    topology = [new("fast", ResourceKind.Volume), new("new-device-path", ResourceKind.Device)]; now = 93;
    for (int i = 0; i < 20; i++) { readings = collector.Poll(); await Task.Delay(10); }
    bool returnedAtNewPath = readings.Single(x => x.Resource.Id == "device:replacement").Observation.Get("Read").Value == 100;
    var normalized = (preferences with { StorageComparison = ["volume:fast", "device:1", "device:1", "device:2", "device:3", "device:4", "device:5"] }).Normalize();
    byte[] descriptor = new byte[36]; BitConverter.GetBytes((uint)1).CopyTo(descriptor, 8); BitConverter.GetBytes((uint)3).CopyTo(descriptor, 16); BitConverter.GetBytes((ushort)8).CopyTo(descriptor, 20); descriptor[28] = 0x50;
    string? identity = StorageNative.DeviceIdentifier(descriptor);
    BitConverter.GetBytes((uint)1).CopyTo(descriptor, 24); // A port identifier cannot identify the whole device.
    return new { FastProgress = fastProgress, CacheInvalidated = cacheInvalidated, ReturnedAtNewPath = returnedAtNewPath, BlockedCalls = blockedCalls, SlowCalls = slowCalls, LateGeneration = lateGeneration, Missing = missing, Boundary = boundary, Replaced = replaced,
        ComparisonLimit = normalized.StorageComparison.Length, VolumeExcluded = !normalized.StorageComparison.Contains("volume:fast"), Vpd = identity is not null,
        PortRejected = StorageNative.DeviceIdentifier(descriptor) is null, TruncatedRejected = StorageNative.DeviceIdentifier(descriptor[..15]) is null,
        UnchangedPin = preferences.Pins["storage"] == "volume:fast", FastCalls = fastCalls };
}
static async Task<object> StorageSession(string directory)
{
    Directory.CreateDirectory(directory);
    var initial = StorageNative.Discover();
    var volumes = initial.Where(x => x.Kind == ResourceKind.Volume).Select(x => StorageNative.ReadVolume(x.Path)).ToArray();
    var devices = initial.Where(x => x.Kind == ResourceKind.Device).Select(x => StorageNative.ReadDevice(x.Path)).Where(x => x.Durable).ToArray();
    var ids = volumes.Select(x => x.Resource.Id).Concat(devices.Select(x => x.Id)).Concat(["cpu", "memory"]).ToArray();
    var preferences = new Preferences { Monitored = ids, CapacitySeconds = 10, IntervalSeconds = .5 };
    await using var session = new MonitoringSession(preferences);
    await Task.Delay(2500);
    string file = Path.Combine(directory, "myfinder-controlled-io-" + Guid.NewGuid().ToString("N") + ".bin");
    const int bytes = 32 * 1024 * 1024;
    await using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.DeleteOnClose | FileOptions.Asynchronous))
    {
        byte[] buffer = new byte[1024 * 1024]; Random.Shared.NextBytes(buffer);
        for (int i = 0; i < 32; i++) await stream.WriteAsync(buffer);
        stream.Flush(true); stream.Position = 0;
        while (await stream.ReadAsync(buffer) > 0) { }
        await Task.Delay(1200);
    }
    await Task.Delay(10000);
    var histories = volumes.ToDictionary(x => x.Resource.Id, x => session.History.Read(x.Resource.Id, 0));
    string pausedId = volumes[0].Resource.Id;
    session.Configure(preferences with { Monitored = ids.Where(x => x != pausedId).ToArray() });
    await Task.Delay(800);
    var paused = session.Snapshot().Single(x => x.Resource.Id == pausedId).Observation.Get("Total").Quality.ToString();
    session.Configure(preferences); await Task.Delay(1200);
    return new { Volumes = volumes.Length, Devices = devices.Length, ContinuingVolumes = histories.Count(x => x.Value.Length >= 2),
        Paused = paused, Resumed = session.History.Read(pausedId, 0).Last().Segment != histories[pausedId].Last().Segment,
        CpuSamples = session.History.Read("cpu", 0).Length, PayloadBytes = bytes, FileRemoved = !File.Exists(file),
        PeakWrite = devices.SelectMany(x => session.History.Read(x.Id, 0)).Select(x => x.Get("Write").Value ?? 0).DefaultIfEmpty(0).Max(),
        MaximumStoragePending = session.StoragePending, HistoryBytes = session.History.Statistics.EstimatedBytes };
}
static async Task<object> NativeStorage()
{
    using var collector = new StorageCollector(); collector.Configure(new());
    Reading[] readings = [];
    for (int i = 0; i < 60; i++) { readings = collector.Poll(); if (collector.Ready) break; await Task.Delay(100); }
    var preferences = new Preferences { Monitored = readings.Select(x => x.Resource.Id).ToArray(), CapacitySeconds = 10 };
    collector.Configure(preferences);
    for (int i = 0; i < 40; i++) { readings = collector.Poll(); await Task.Delay(100); }
    return new { collector.Ready, collector.Pending, Readings = readings };
}
static object NetworkPolicy()
{
    var a = new AdapterSnapshot(Guid.NewGuid(), 1, "Adapter", "Fixture", 6, true, 1, 1000000, 1000000, 100, 200);
    var b = a with { Id = Guid.NewGuid(), Luid = 2, Hardware = false };
    var preferences = new Preferences { Monitored = [a.ResourceId, b.ResourceId], Pins = new() { ["network"] = a.ResourceId } };
    var collector = new NetworkCollector(); collector.Configure(preferences);
    var first = collector.Observe([a, b], 0, DateTimeOffset.UnixEpoch);
    var second = collector.Observe([a with { Received = 1100, Sent = 2200, Name = "Renamed" }, b], 1, DateTimeOffset.UnixEpoch.AddSeconds(1));
    collector.Configure(preferences with { Monitored = [b.ResourceId] });
    collector.Configure(preferences); // Even a stop/re-enable between samples must break the baseline.
    var resumed = collector.Observe([a with { Received = 999999 }, b], 2, DateTimeOffset.UnixEpoch.AddSeconds(2));
    var disconnected = collector.Observe([a with { Status = 2 }, b], 3, DateTimeOffset.UnixEpoch.AddSeconds(3));
    var replaced = collector.Observe([b with { Name = a.Name }], 4, DateTimeOffset.UnixEpoch.AddSeconds(4));
    var failed = collector.Observe([], 5, DateTimeOffset.UnixEpoch.AddSeconds(5), "Fixture failure");
    var empty = new NetworkCollector(); empty.Configure(new Preferences { Monitored = [] });
    for (int i = 0; i < 600; i++) empty.Observe([a with { Id = Guid.NewGuid() }], i, DateTimeOffset.UnixEpoch.AddSeconds(i));
    int retained = empty.Observe([], 2000, DateTimeOffset.UnixEpoch.AddSeconds(2000)).Length;
    var selected = NetworkCollector.InitialSelection(new(), first);
    return new { First = first[0].Observation.Get("Receive").Quality.ToString(), Rate = second[0].Observation.Get("Receive").Value,
        RenamedIdentity = second[0].Resource.Id == a.ResourceId, OtherContinued = second[1].Observation.Get("Receive").Value == 0,
        Resume = resumed[0].Observation.Get("Receive").Quality.ToString(), Total = resumed[0].Observation.Get("Observed received").Value,
        Disconnected = disconnected[0].Observation.Get("Receive").Quality.ToString(), MissingIdentity = replaced[0].Resource.Id == a.ResourceId && replaced[0].Observation.Get("Receive").Quality == Quality.Disconnected,
        Failure = failed[0].Observation.Get("Receive").Quality.ToString(), Retained = retained,
        Pin = selected?.Pins.GetValueOrDefault("network") == a.ResourceId,
        Ambiguous = NetworkCollector.InitialSelection(new(), [first[0], first[0] with { Resource = a.Resource with { Id = "other" } }]) is null,
        DoesNotRepin = NetworkCollector.InitialSelection(preferences with { NetworkSelectionMade = true }, first) is null,
        ByteRate = ValueFormat.Metric(new(125000, Unit.BytesPerSecond), new()), BitRate = ValueFormat.Metric(new(125000, Unit.BytesPerSecond), new() { NetworkBits = true }) };
}
static async Task<object> NativeNetwork()
{
    var adapters = NetworkInterfaces.Read();
    var collector = new NetworkCollector(); collector.Configure(new Preferences { Monitored = adapters.Select(x => x.ResourceId).ToArray() });
    collector.Collect();
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    long received = 0;
    try
    {
        using var sender = new System.Net.Sockets.TcpClient();
        var accept = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync((System.Net.IPEndPoint)listener.LocalEndpoint);
        using var receiver = await accept;
        var read = Task.Run(async () => { byte[] buffer = new byte[65536]; int length; while ((length = await receiver.GetStream().ReadAsync(buffer)) > 0) received += length; });
        byte[] payload = new byte[65536];
        for (int i = 0; i < 256; i++) await sender.GetStream().WriteAsync(payload);
        sender.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send);
        await read.WaitAsync(TimeSpan.FromSeconds(10));
    }
    finally { listener.Stop(); }
    await Task.Delay(1200);
    var readings = collector.Collect();
    var loopback = readings.Where(x => x.Resource.Facts.GetValueOrDefault("Category") == "Loopback").ToArray();
    return new { Count = adapters.Length, Unique = adapters.Select(x => x.Id).Distinct().Count() == adapters.Length,
        ManagedIdentities = adapters.Count(x => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Any(n => Guid.TryParse(n.Id, out var id) && id == x.Id)),
        ValidRates = readings.Where(x => x.Observation.Get("Receive").Quality == Quality.Fresh).All(x => x.Observation.Get("Receive").Value >= 0 && x.Observation.Get("Send").Value >= 0),
        Fresh = readings.Count(x => x.Observation.Get("Receive").Quality == Quality.Fresh), PayloadReceived = received,
        LoopbackReceived = loopback.Sum(x => x.Observation.Get("Observed received").Value ?? 0), LoopbackSent = loopback.Sum(x => x.Observation.Get("Observed sent").Value ?? 0),
        Adapters = readings.Select(x => new { x.Resource.Id, x.Resource.Name, x.Resource.Facts, Receive = x.Observation.Get("Receive"), Send = x.Observation.Get("Send") }) };
}
static object Policy()
{
    var primary = new Display("primary", "Primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true);
    var other = new Display("secondary", "Secondary", new(-1280, 0, 1280, 1024), new(-1280, 0, 1280, 984), false);
    return new
    {
        HiddenDuringFullscreen = Presentation.ShouldShow(false, true, true),
        HiddenAfterFullscreen = Presentation.ShouldShow(false, true, false),
        ShowDuringFullscreen = Presentation.ShouldShow(true, true, true),
        Restore = Presentation.ShouldShow(true, true, false),
        RetreatDisabled = Presentation.ShouldShow(true, false, true),
        Locked = Presentation.ShouldShow(true, false, false, true),
        Preferred = Presentation.ChooseDisplay([primary, other], "secondary").Id,
        Fallback = Presentation.ChooseDisplay([primary], "secondary").Id,
        Restored = Presentation.ChooseDisplay([primary, other], "secondary").Id,
        Small = Presentation.Place(new(0, 0, 200, 160), 500, 300, new("primary", 1, 1)),
        Negative = Presentation.Place(other.Work, 200, 100, new("secondary", 1, 1, Alignment.Bottom)),
        Maximized = Presentation.CoversDisplay(primary.Bounds, primary.Bounds, true),
        Borderless = Presentation.CoversDisplay(primary.Bounds, primary.Bounds, false),
        OtherDisplay = Presentation.CoversDisplay(other.Bounds, primary.Bounds, false)
    };
}
static object Rates()
{
    var rate = new CounterRate();
    var first = rate.Observe(1000, 1, 4);
    var second = rate.Observe(2000, 2, 4);
    var third = rate.Observe(4000, 4, 4);
    var zero = rate.Observe(4000, 5, 4);
    var reset = rate.Observe(1, 6, 4);
    var resume = rate.Observe(1_000_000, 30, 4);
    return new { First = first.Quality.ToString(), Second = second.Value, Third = third.Value, Zero = zero.Value, Reset = reset.Quality.ToString(), Resume = resume.Quality.ToString(), rate.Total, rate.CoverageSeconds, rate.Segment };
}
static Observation Sample(string id, double time, double? value, long segment = 1) => new(id, DateTimeOffset.UnixEpoch.AddSeconds(time), time, segment,
    new Dictionary<string, Metric> { ["value"] = value is null ? Metric.Missing(Unit.Percent, "Gap") : new(value, Unit.Percent) });
static object History()
{
    var history = new HistoryStore(4096);
    for (int i = 0; i < 100; i++) history.Append(Sample(i % 2 == 0 ? "a" : "b", i, i), 10);
    var stats = history.Statistics;
    var a = history.Read("a", 0);
    var independent = history.Read("b", 0).Length;
    history.Prune(200, 10);
    var empty = history.Statistics;
    var fixture = Enumerable.Range(0, 1000).Select(i => Sample("a", i, i == 400 ? 100 : 2, i < 600 ? 1 : 2)).ToArray();
    fixture[500] = Sample("a", 500, null);
    var plotted = PlotReduction.Segments(fixture, "value", 4, 20);
    return new { stats.EstimatedBytes, stats.Samples, history.BudgetLimited, Independent = independent, First = a[0].Elapsed,
        Empty = empty.Samples, Segments = plotted.Length, Spike = plotted.SelectMany(x => x).Max(x => x.Get("value").Value), Reduced = plotted.Sum(x => x.Length) };
}
static async Task<object> PreferencesScenario(string directory)
{
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "preferences.json");
    File.WriteAllText(path, "{\"Version\":1,\"Shown\":false,\"Scale\":\"broken\",\"Theme\":\"Dark\",\"Monitored\":[\"cpu\",\"memory\"]}");
    var store = new PreferenceStore(directory);
    var loaded = store.Load();
    var writes = Enumerable.Range(0, 20).Select(i => store.SaveAsync(loaded with { RangeMinutes = i % 5 + 1 })).ToArray();
    await Task.WhenAll(writes);
    var persisted = new PreferenceStore(directory).Load();
    File.WriteAllText(path, "{\"Version\":99,\"Shown\":false}");
    var newer = new PreferenceStore(directory); newer.Load();
    bool newerSave = await newer.SaveAsync(new());
    string original = File.ReadAllText(path);
    File.WriteAllText(path, "{\"Version\":\"bad\",\"Shown\":null,\"Theme\":\"Dark\"}");
    var invalid = new PreferenceStore(directory).Load();
    File.WriteAllText(path, "broken");
    var corrupt = new PreferenceStore(directory); corrupt.Load();
    bool corruptSave = await corrupt.SaveAsync(new());
    string blockedPath = Path.Combine(directory, "blocked"); File.WriteAllText(blockedPath, "existing file");
    var blocked = new PreferenceStore(blockedPath);
    bool blockedSave = await blocked.SaveAsync(new());
    return new { loaded.Shown, loaded.Scale, Theme = loaded.Theme.ToString(), LastRange = persisted.RangeMinutes, AllSaved = writes.All(x => x.Result), NewerReadOnly = newer.ReadOnly, NewerSaved = newerSave, NewerPreserved = original.Contains("99"),
        InvalidVersionSurvived = invalid.Theme == Appearance.Dark, NullUsesDefault = invalid.Shown,
        CorruptPreserved = !corruptSave && File.ReadAllText(path) == "broken", SaveFailureVisible = !blockedSave && blocked.Warning is not null };
}
static async Task<object> NativeSystem()
{
    using var collector = new SystemCollector();
    var first = collector.Collect(1);
    Reading[] readings = first;
    for (int i = 0; i < 6; i++) { await Task.Delay(1000); readings = collector.Collect(1); }
    var cpu = readings.Single(x => x.Resource.Id == "cpu");
    var memory = readings.Single(x => x.Resource.Id == "memory");
    return new { Cpu = cpu.Observation.Get("Utilization").Value, Logical = cpu.Observation.Values.Keys.Count(x => x.StartsWith("Logical ")),
        Total = memory.Observation.Get("Total").Value, Used = memory.Observation.Get("In use").Value, Available = memory.Observation.Get("Available").Value,
        Processes = cpu.Observation.Get("Processes").Value, First = first[0].Observation.Get("Utilization").Quality.ToString(), CpuFacts = cpu.Resource.Facts, MemoryFacts = memory.Resource.Facts,
        Fields = memory.Observation.Values.ToDictionary(x => x.Key, x => new { x.Value.Value, Quality = x.Value.Quality.ToString(), x.Value.Reason }) };
}
static async Task<object> ExpiryScenario()
{
    await using var session = new MonitoringSession(new Preferences { Monitored = [], IntervalSeconds = .5, RetentionMinutes = 5 });
    double now = ObservationClock.Elapsed;
    session.History.Append(Sample("expired", now - 299, 1), 300);
    int before = session.History.Read("expired", double.MinValue).Length;
    await Task.Delay(1800);
    int after = session.History.Read("expired", double.MinValue).Length;
    session.Configure(new Preferences { Monitored = [], RetentionMinutes = 15 });
    return new { Before = before, After = after, Restored = session.History.Read("expired", double.MinValue).Length };
}
static async Task<object> SessionScenario()
{
    var preferences = new Preferences { IntervalSeconds = .5 };
    await using var session = new MonitoringSession(preferences);
    await Task.Delay(1800);
    var before = session.History.Read("memory", 0);
    session.Configure(preferences with { Monitored = ["cpu"] });
    await Task.Delay(800);
    var paused = session.Snapshot().Single(x => x.Resource.Id == "memory");
    session.Configure(preferences);
    await Task.Delay(800);
    var after = session.History.Read("memory", 0);
    return new { Before = before.Length, After = after.Length, Cpu = session.History.Read("cpu", 0).Length, Paused = paused.Observation.Get("In use").Quality.ToString(), Boundary = before[^1].Segment != after[^1].Segment };
}
