using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyFinder.Core;

public enum PanelOrientation { Horizontal, Vertical }
public enum PanelStyle { Balanced, Minimal, Capsules }
public enum Appearance { System, Light, Dark }
public enum Alignment { Free, Top, Bottom, Left, Right }
public enum CompositionStyle { Bar, Ring }
public sealed record Placement(string Display, double X = .5, double Y = 0, Alignment Alignment = Alignment.Top);

public sealed record Preferences
{
    public int Version { get; init; } = 1;
    public bool Shown { get; init; } = true;
    public bool Retreat { get; init; } = true;
    public bool Locked { get; init; }
    public PanelOrientation Orientation { get; init; }
    public PanelStyle Style { get; init; }
    public Appearance Theme { get; init; }
    public double Scale { get; init; } = 1;
    public double BackgroundOpacity { get; init; } = 1;
    public double IntervalSeconds { get; init; } = 1;
    public int RetentionMinutes { get; init; } = 15;
    public int RangeMinutes { get; init; } = 5;
    public CompositionStyle Composition { get; init; }
    public bool NetworkBits { get; init; }
    public bool NetworkSelectionMade { get; init; }
    public bool StorageBinary { get; init; }
    public string PreferredDisplay { get; init; } = "";
    public Dictionary<string, Placement> Placements { get; init; } = [];
    public string[] MetricOrder { get; init; } = ["cpu", "memory"];
    public string[] Monitored { get; init; } = ["cpu", "memory"];
    public Dictionary<string, string> Pins { get; init; } = [];

    public Preferences Normalize() => this with
    {
        Version = 1,
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .8, 2) : 1,
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, .25, 1) : 1,
        IntervalSeconds = IntervalSeconds is .5 or 1 or 2 ? IntervalSeconds : 1,
        RetentionMinutes = RetentionMinutes is 5 or 15 or 30 or 60 ? RetentionMinutes : 15,
        RangeMinutes = Math.Clamp(RangeMinutes, 1, RetentionMinutes is 5 or 15 or 30 or 60 ? RetentionMinutes : 15),
        Orientation = Enum.IsDefined(Orientation) ? Orientation : PanelOrientation.Horizontal,
        Style = Enum.IsDefined(Style) ? Style : PanelStyle.Balanced,
        Theme = Enum.IsDefined(Theme) ? Theme : Appearance.System,
        Composition = Enum.IsDefined(Composition) ? Composition : CompositionStyle.Bar,
        PreferredDisplay = PreferredDisplay ?? "",
        Monitored = (Monitored ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(512).ToArray(),
        MetricOrder = (MetricOrder ?? ["cpu", "memory"]).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(16).ToArray(),
        Pins = (Pins ?? []).Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.Key, x => x.Value),
        Placements = (Placements ?? []).Where(x => x.Value is not null).Take(32).ToDictionary(x => x.Key, x => x.Value with
        {
            X = double.IsFinite(x.Value.X) ? Math.Clamp(x.Value.X, 0, 1) : .5,
            Y = double.IsFinite(x.Value.Y) ? Math.Clamp(x.Value.Y, 0, 1) : 0,
            Alignment = Enum.IsDefined(x.Value.Alignment) ? x.Value.Alignment : Alignment.Free
        })
    };
}

public sealed class PreferenceStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly SemaphoreSlim _save = new(1, 1);
    private long _requested;
    private long _written;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? Warning { get; private set; }
    public bool ReadOnly { get; private set; }
    public string FilePath => Path.Combine(DirectoryPath, "preferences.json");

    public Preferences Load()
    {
        var result = new Preferences();
        if (!File.Exists(FilePath)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a preference object.");
            if (document.RootElement.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int value) && value > 1)
            {
                ReadOnly = true;
                Warning = "Preferences were created by a newer MyFinder. They will not be overwritten.";
                return result;
            }
            // Read fields independently so a damaged optional choice cannot erase valid preferences.
            foreach (var property in typeof(Preferences).GetProperties())
            {
                if (!document.RootElement.TryGetProperty(property.Name, out var field)) continue;
                try
                {
                    if (field.ValueKind == JsonValueKind.Null) throw new JsonException("Null preference field.");
                    property.SetValue(result, field.Deserialize(property.PropertyType, JsonOptions));
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException)
                { Warning = "Some invalid preference fields were reset; valid choices were retained."; }
            }
            return result.Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ReadOnly = true;
            Warning = $"Preferences could not be read; the original file is preserved. {ex.Message}";
            return result;
        }
    }

    public async Task<bool> SaveAsync(Preferences preferences)
    {
        if (ReadOnly) return false;
        long sequence = Interlocked.Increment(ref _requested);
        string json = JsonSerializer.Serialize(preferences.Normalize(), JsonOptions);
        await _save.WaitAsync().ConfigureAwait(false);
        try
        {
            if (sequence < _written) return true;
            Directory.CreateDirectory(DirectoryPath);
            string temporary = FilePath + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                stream.Flush(true);
            }
            File.Move(temporary, FilePath, true);
            _written = sequence;
            Warning = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Warning = $"Preferences could not be saved: {ex.Message}"; return false; }
        finally { _save.Release(); }
    }
}
