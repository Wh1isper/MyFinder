using System.Runtime.InteropServices;

namespace MyFinder.Core.Monitoring;

public sealed record HardwareInventory(IReadOnlyDictionary<string, string> Cpu, IReadOnlyDictionary<string, string> Memory)
{
    public static HardwareInventory Read()
    {
        Dictionary<string, string> cpu = new() { ["Source"] = "Windows processor information counters; utilization measures non-idle time." };
        Dictionary<string, string> memory = new() { ["Accounting"] = "In use = usable physical minus available. Cache, compression and pools overlap this partition." };
        try
        {
            var processors = Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,VirtualizationFirmwareEnabled,L2CacheSize,L3CacheSize FROM Win32_Processor",
                ["Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "VirtualizationFirmwareEnabled", "L2CacheSize", "L3CacheSize"]);
            cpu["Model"] = string.Join(" / ", processors.Select(x => x["Name"]).Distinct());
            cpu["Sockets"] = processors.Count.ToString();
            cpu["Physical cores"] = Sum(processors, "NumberOfCores");
            cpu["Logical processors"] = Sum(processors, "NumberOfLogicalProcessors");
            cpu["Firmware maximum frequency"] = string.Join(" / ", processors.Select(x => x["MaxClockSpeed"] + " MHz").Distinct());
            cpu["Firmware virtualization"] = string.Join(" / ", processors.Select(x => x["VirtualizationFirmwareEnabled"]).Distinct());
            cpu["L2 cache (reported per socket)"] = string.Join(" / ", processors.Select(x => x["L2CacheSize"] + " KiB"));
            cpu["L3 cache (reported per socket)"] = string.Join(" / ", processors.Select(x => x["L3CacheSize"] + " KiB"));
            var modules = Query("SELECT Capacity,ConfiguredClockSpeed,FormFactor,DeviceLocator FROM Win32_PhysicalMemory", ["Capacity", "ConfiguredClockSpeed", "FormFactor", "DeviceLocator"]);
            memory["Populated modules"] = modules.Count.ToString();
            memory["Configured transfer rate (firmware)"] = string.Join(" / ", modules.Select(x => x["ConfiguredClockSpeed"] + " MT/s").Distinct());
            memory["Form factor"] = string.Join(" / ", modules.Select(x => x["FormFactor"] switch { "8" => "DIMM", "12" => "SODIMM", "0" => "Unknown", var n => "SMBIOS code " + n }).Distinct());
            memory["Module locations"] = string.Join(", ", modules.Select(x => x["DeviceLocator"]));
            var arrays = Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray", ["MemoryDevices"]);
            memory["Firmware memory slots"] = Sum(arrays, "MemoryDevices");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            cpu["Hardware inventory"] = memory["Hardware inventory"] = $"Unavailable: {ex.Message} (0x{ex.HResult:X8})";
        }
        return new(cpu, memory);
    }
    private static string Sum(List<Dictionary<string, string>> rows, string key)
    {
        ulong total = 0;
        if (rows.Count == 0) return "Not reported";
        foreach (var row in rows)
        {
            if (!ulong.TryParse(row[key], out ulong value)) return "Not reported";
            total += value;
        }
        return total.ToString();
    }

    public static List<Dictionary<string, string>> Query(string query, string[] columns)
    {
        object? locator = null, service = null, objects = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI scripting is unavailable.");
            locator = Activator.CreateInstance(type)!;
            service = ((dynamic)locator).ConnectServer(".", "root\\cimv2");
            objects = ((dynamic)service).ExecQuery(query, "WQL", 0);
            List<Dictionary<string, string>> rows = [];
            foreach (object item in (System.Collections.IEnumerable)objects)
            {
                try
                {
                    Dictionary<string, string> row = [];
                    foreach (string column in columns)
                    {
                        object? properties = null, property = null;
                        try
                        {
                            properties = ((dynamic)item).Properties_;
                            property = ((dynamic)properties).Item(column);
                            object? value = ((dynamic)property).Value;
                            row[column] = value?.ToString()?.Trim() ?? "Not reported";
                        }
                        finally { Release(property); Release(properties); }
                    }
                    rows.Add(row);
                }
                finally { Release(item); }
            }
            return rows;
        }
        finally { Release(objects); Release(service); Release(locator); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
