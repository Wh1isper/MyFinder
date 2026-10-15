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
    _ => throw new ArgumentException("Specify a known scenario.")
};
Console.WriteLine(JsonSerializer.Serialize(result));

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
