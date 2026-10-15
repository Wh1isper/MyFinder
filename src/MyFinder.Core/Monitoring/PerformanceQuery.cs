using System.Runtime.InteropServices;

namespace MyFinder.Core.Monitoring;

// PDH formats native counter types and resolves English paths on localized Windows.
// Each query is owned and used by one collector thread.
public sealed class PerformanceQuery : IDisposable
{
    private nint _query;
    private int _collections;
    private readonly Dictionary<string, nint> _counters = [];
    private readonly Dictionary<string, uint> _errors = [];
    public uint Status { get; private set; }
    public PerformanceQuery() { Status = PdhOpenQueryW(null, 0, out _query); }
    public void Add(string key, string path)
    {
        if (_query == 0) { _errors[key] = Status; return; }
        uint status = PdhAddEnglishCounterW(_query, path, 0, out nint counter);
        if (status == 0) _counters[key] = counter; else _errors[key] = status;
    }
    public void Collect() { _collections++; if (_query != 0) Status = PdhCollectQueryData(_query); }
    public Metric Read(string key, Unit unit)
    {
        if (!_counters.TryGetValue(key, out nint counter)) return Metric.Missing(unit, $"Counter unavailable (PDH 0x{_errors.GetValueOrDefault(key):X8}).");
        if (Status != 0) return Metric.Missing(unit, $"Collection failed (PDH 0x{Status:X8}).");
        uint status = PdhGetFormattedCounterValue(counter, 0x200 | 0x8000, out _, out var value);
        if (status != 0 || value.Status > 1 || !double.IsFinite(value.Value))
            return Metric.Missing(unit, $"Counter has no valid sample (PDH 0x{(status != 0 ? status : value.Status):X8}).", _collections < 2 ? Quality.WarmingUp : Quality.Unavailable);
        return new(value.Value, unit);
    }
    public Dictionary<string, Metric> ReadArray(string key, Unit unit)
    {
        Dictionary<string, Metric> result = [];
        if (Status != 0 || !_counters.TryGetValue(key, out nint counter)) return result;
        // Topology can change between sizing and reading. Retry the sizing sequence once.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            uint size = 0;
            uint status = PdhGetFormattedCounterArrayW(counter, 0x200 | 0x8000, ref size, out _, 0);
            if (status != 0x800007D2 || size == 0 || size > 16 * 1024 * 1024) return result;
            nint buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, 0x200 | 0x8000, ref size, out uint count, buffer);
                if (status == 0x800007D2) continue;
                if (status != 0) return result;
                int stride = Marshal.SizeOf<CounterItem>();
                for (int i = 0; i < count && (i + 1L) * stride <= size; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(buffer + i * stride);
                    string name = Marshal.PtrToStringUni(item.Name) ?? "";
                    result[name] = item.Value.Status <= 1 && double.IsFinite(item.Value.Value)
                        ? new(item.Value.Value, unit) : Metric.Missing(unit, $"Counter has no valid sample (PDH 0x{item.Value.Status:X8}).", _collections < 2 ? Quality.WarmingUp : Quality.Unavailable);
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }
    public void Dispose() { if (_query != 0) { PdhCloseQuery(_query); _query = 0; } }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue
    { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public nint Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint items);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
