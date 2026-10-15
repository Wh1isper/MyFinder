using System.Runtime.InteropServices;

namespace MyFinder.Core.Monitoring;

public sealed class SystemCollector : IDisposable
{
    private readonly PerformanceQuery _query = new();
    private readonly Task<HardwareInventory> _inventory = Task.Run(HardwareInventory.Read);
    private double _previous = double.NaN;
    private long _segment;
    public SystemCollector()
    {
        _query.Add("total", @"\Processor Information(_Total)\% Processor Time");
        _query.Add("logical", @"\Processor Information(*)\% Processor Time");
        _query.Add("frequency", @"\Processor Information(_Total)\Processor Frequency");
        _query.Add("compression", @"\Memory\Compressed Page Size");
    }
    public Reading[] Collect(double interval)
    {
        double now = ObservationClock.Elapsed;
        bool baseline = double.IsNaN(_previous) || now - _previous > Math.Max(5, interval * 3);
        if (baseline) _segment++;
        _previous = now;
        var time = DateTimeOffset.Now;
        _query.Collect();
        var hardware = _inventory.IsCompletedSuccessfully ? _inventory.Result : new HardwareInventory(
            new Dictionary<string, string> { ["Hardware inventory"] = "Loading hardware facts…" },
            new Dictionary<string, string> { ["Hardware inventory"] = "Loading hardware facts…" });
        Dictionary<string, Metric> cpu = new()
        {
            ["Utilization"] = baseline ? Metric.Missing(Unit.Percent, "A second observation is required after startup or a gap.", Quality.WarmingUp) : _query.Read("total", Unit.Percent),
            ["Reported frequency"] = _query.Read("frequency", Unit.Megahertz),
            ["Uptime"] = new(GetTickCount64() / 1000d, Unit.Seconds)
        };
        foreach (var item in _query.ReadArray("logical", Unit.Percent))
            if (!item.Key.Contains("_Total", StringComparison.OrdinalIgnoreCase))
                cpu["Logical " + item.Key] = baseline ? Metric.Missing(Unit.Percent, "Establishing baseline.", Quality.WarmingUp) : item.Value;
        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        Dictionary<string, Metric> memory = [];
        if (GetPerformanceInfo(ref info, info.Size))
        {
            double page = info.PageSize;
            memory["Total"] = new(info.PhysicalTotal * page, Unit.Bytes);
            memory["Available"] = new(info.PhysicalAvailable * page, Unit.Bytes);
            memory["In use"] = new((info.PhysicalTotal - info.PhysicalAvailable) * page, Unit.Bytes);
            memory["Utilization"] = new(info.PhysicalTotal == 0 ? null : 100d * (info.PhysicalTotal - info.PhysicalAvailable) / info.PhysicalTotal, Unit.Percent);
            memory["Committed"] = new(info.CommitTotal * page, Unit.Bytes);
            memory["Commit limit"] = new(info.CommitLimit * page, Unit.Bytes);
            memory["Cache (overlapping)"] = new(info.SystemCache * page, Unit.Bytes);
            memory["Paged pool (overlapping)"] = new(info.KernelPaged * page, Unit.Bytes);
            memory["Nonpaged pool (overlapping)"] = new(info.KernelNonpaged * page, Unit.Bytes);
            cpu["Processes"] = new(info.ProcessCount, Unit.Count);
            cpu["Threads"] = new(info.ThreadCount, Unit.Count);
            cpu["Handles"] = new(info.HandleCount, Unit.Count);
            if (GetPhysicallyInstalledSystemMemory(out ulong installed))
            {
                memory["Installed"] = new(installed * 1024d, Unit.Bytes);
                memory["Hardware reserved"] = installed * 1024d >= info.PhysicalTotal * page
                    ? new(installed * 1024d - info.PhysicalTotal * page, Unit.Bytes)
                    : Metric.Missing(Unit.Bytes, "Firmware installed memory is less than usable memory.");
            }
            else memory["Installed"] = Metric.Missing(Unit.Bytes, "Firmware installed-memory query failed.");
        }
        else
        {
            string reason = $"Windows performance snapshot unavailable ({Marshal.GetLastWin32Error()}).";
            foreach (string key in new[] { "Total", "Available", "In use", "Committed", "Commit limit", "Cache (overlapping)", "Paged pool (overlapping)", "Nonpaged pool (overlapping)" }) memory[key] = Metric.Missing(Unit.Bytes, reason);
            foreach (string key in new[] { "Processes", "Threads", "Handles" }) cpu[key] = Metric.Missing(Unit.Count, reason);
        }
        memory["Compressed (subset of in use)"] = _query.Read("compression", Unit.Bytes);
        return [new(new("cpu", ResourceKind.Cpu, "CPU", hardware.Cpu), new("cpu", time, now, _segment, cpu)),
            new(new("memory", ResourceKind.Memory, "Memory", hardware.Memory), new("memory", time, now, _segment, memory))];
    }
    public void Dispose() => _query.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);
    [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
}
