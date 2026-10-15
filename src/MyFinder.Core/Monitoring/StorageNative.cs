using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MyFinder.Core.Monitoring;

public sealed record StorageLocation(string Path, ResourceKind Kind);
public sealed record StorageDevice(string Id, string Path, uint Number, string Model, string Bus, bool Durable);
public sealed record VolumeSnapshot(Resource Resource, IReadOnlyDictionary<string, Metric> Values, uint[] Disks, DateTimeOffset Time, double Elapsed);

public static class StorageNative
{
    private static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public uint Size; public Guid Class; public uint Flags; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Space
    {
        public ulong Total, Available, PoolUnavailable, CallerTotal, CallerAvailable, CallerPoolUnavailable,
            Used, Reserved, StorageReserve, Committed, PoolAvailable;
        public uint Sectors, Bytes;
    }
    public static StorageLocation[] Discover()
    {
        using var errors = new MediaErrorScope();
        var locations = new List<StorageLocation>();
        var name = new StringBuilder(1024);
        nint volume = FindFirstVolumeW(name, name.Capacity);
        if (volume != -1)
        {
            try { do { locations.Add(new(name.ToString(), ResourceKind.Volume)); } while (FindNextVolumeW(volume, name, name.Capacity)); }
            finally { FindVolumeClose(volume); }
        }
        Guid guid = DiskInterface;
        nint devices = SetupDiGetClassDevsW(ref guid, null, 0, 0x12);
        if (devices == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(devices, 0, ref guid, index, ref data))
                { if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception(Marshal.GetLastWin32Error()); break; }
                SetupDiGetDeviceInterfaceDetailW(devices, ref data, 0, 0, out uint size, 0);
                if (size is < 8 or > 65536) continue;
                nint buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(devices, ref data, buffer, size, out _, 0))
                        locations.Add(new(Marshal.PtrToStringUni(buffer + 4)!, ResourceKind.Device));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
        return locations.Distinct().ToArray();
    }
    public static string VolumeId(string path) => "volume:" + path.ToLowerInvariant();
    public static VolumeSnapshot ReadVolume(string path)
    {
        using var errors = new MediaErrorScope();
        var time = DateTimeOffset.Now; double elapsed = ObservationClock.Elapsed;
        int status = GetDiskSpaceInformationW(path, out var space);
        var values = new Dictionary<string, Metric>();
        if (status >= 0 && space.Total >= space.Available && space.Sectors > 0 && space.Bytes > 0)
        {
            double unit = (double)space.Sectors * space.Bytes;
            values["Total"] = new(space.Total * unit, Unit.Bytes);
            values["Free"] = new(space.Available * unit, Unit.Bytes);
            values["Used / reserved"] = new((space.Total - space.Available) * unit, Unit.Bytes);
            values["Caller available"] = new(space.CallerAvailable * unit, Unit.Bytes);
            values["Utilization"] = space.Total > 0 ? new(100d * (space.Total - space.Available) / space.Total, Unit.Percent) : Metric.Missing(Unit.Percent, "Empty volume capacity.");
        }
        else
        {
            foreach (string key in new[] { "Total", "Free", "Used / reserved", "Caller available" }) values[key] = Metric.Missing(Unit.Bytes, $"Capacity unavailable (HRESULT 0x{status:X8}).");
            values["Utilization"] = Metric.Missing(Unit.Percent, "Volume capacity unavailable.");
        }
        var label = new StringBuilder(261); var filesystem = new StringBuilder(261);
        bool information = GetVolumeInformationW(path, label, label.Capacity, out _, out _, out uint flags, filesystem, filesystem.Capacity);
        string capacitySource = "GetDiskSpaceInformation actual allocation units";
        // FAT/exFAT need the older query. Its caller-limited total is an actual total only on a filesystem without quota support.
        if (status < 0 && information && (flags & 0x20) == 0 && GetDiskFreeSpaceExW(path, out ulong caller, out ulong total, out ulong free) && total >= free)
        {
            values["Total"] = new(total, Unit.Bytes); values["Free"] = new(free, Unit.Bytes);
            values["Used / reserved"] = new(total - free, Unit.Bytes); values["Caller available"] = new(caller, Unit.Bytes);
            values["Utilization"] = total > 0 ? new(100d * (total - free) / total, Unit.Percent) : Metric.Missing(Unit.Percent, "Empty volume capacity.");
            capacitySource = "GetDiskFreeSpaceEx on a filesystem reporting no quota support";
        }
        var paths = new char[32768];
        string[] mounts = GetVolumePathNamesForVolumeNameW(path, paths, (uint)paths.Length, out _) ? new string(paths).Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
        uint[] disks = [];
        string mapping;
        using (var handle = Open(path.TrimEnd('\\')))
        {
            byte[] output = new byte[8 + 24 * 1024];
            if (!handle.IsInvalid && DeviceIoControl(handle, 0x00560000, null, 0, output, (uint)output.Length, out uint length, 0) && length >= 8)
            {
                uint count = BitConverter.ToUInt32(output, 0);
                if (count <= 1024 && 8L + count * 24L <= length)
                {
                    disks = Enumerable.Range(0, (int)count).Select(i => BitConverter.ToUInt32(output, 8 + i * 24)).Distinct().ToArray();
                    mapping = "Reported disk numbers: " + string.Join(", ", disks) + ". Whole-device activity is shared across its volumes.";
                }
                else mapping = "Volume extent snapshot is incomplete; no device activity is attributed.";
            }
            else mapping = $"Volume extents unavailable (Windows {Marshal.GetLastWin32Error()}); capacity remains independent.";
        }
        var identities = new List<string>();
        foreach (uint disk in disks)
        {
            try { var device = ReadDevice(@"\\.\PhysicalDrive" + disk); if (device.Durable) identities.Add(device.Id); }
            catch (Win32Exception) { }
        }
        string display = mounts.Length > 0 ? string.Join(", ", mounts) : path;
        var facts = new Dictionary<string, string>
        {
            ["Filesystem"] = information ? filesystem.ToString() : "Unavailable", ["Label"] = information ? label.ToString() : "Unavailable",
            ["Mount paths"] = mounts.Length > 0 ? string.Join(", ", mounts) : "No mounted path", ["Device mapping"] = mapping,
            ["Mapped device identities"] = string.Join(";", identities),
            ["Mapping completeness"] = disks.Length > 0 && identities.Count == disks.Length ? "Complete for this snapshot" : "Unknown or partial; no per-volume activity is inferred",
            ["Capacity source"] = capacitySource,
            ["Capacity meaning"] = "Actual volume total/free, independent of caller quota. Used / reserved = total minus free; includes unavailable/reserved space.",
            ["Contains Windows"] = mounts.Any(x => Environment.GetFolderPath(Environment.SpecialFolder.Windows).StartsWith(x, StringComparison.OrdinalIgnoreCase)) ? "Yes" : "No"
        };
        return new(new(VolumeId(path), ResourceKind.Volume, display + (label.Length > 0 ? " " + label : ""), facts), values, disks, time, elapsed);
    }
    public static StorageDevice ReadDevice(string path)
    {
        using var errors = new MediaErrorScope();
        using var handle = Open(path);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[] number = new byte[12];
        if (!DeviceIoControl(handle, 0x002d1080, null, 0, number, 12, out uint size, 0) || size < 12) throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[] descriptor = Property(handle, 0);
        string vendor = AsciiField(descriptor, 12), model = AsciiField(descriptor, 16), serial = AsciiField(descriptor, 24);
        uint bus = descriptor.Length >= 32 ? BitConverter.ToUInt32(descriptor, 28) : 0;
        string? identity = DeviceIdentifier(Property(handle, 2));
        if (identity is null && serial.Length > 0) identity = $"serial:{bus}:{vendor}:{model}:{serial}";
        return new(identity is null ? "" : "device:" + Hash(identity), path, BitConverter.ToUInt32(number, 4),
            (vendor + " " + model).Trim(), bus switch { 7 => "USB", 11 => "SATA", 17 => "NVMe", 15 => "Virtual", 16 => "Storage Spaces", 1 => "SCSI", 8 => "RAID", _ => $"Storage bus {bus}" }, identity is not null);
    }
    public static string? DeviceIdentifier(byte[] descriptor)
    {
        if (descriptor.Length < 12) return null;
        uint count = BitConverter.ToUInt32(descriptor, 8);
        int offset = 12;
        for (uint i = 0; i < count && offset + 16 <= descriptor.Length; i++)
        {
            uint type = BitConverter.ToUInt32(descriptor, offset + 4), association = BitConverter.ToUInt32(descriptor, offset + 12);
            int length = BitConverter.ToUInt16(descriptor, offset + 8), next = BitConverter.ToUInt16(descriptor, offset + 10);
            if (offset + 16L + length > descriptor.Length) return null;
            if (association == 0 && type is 2 or 3 or 8 && length > 0 && descriptor.AsSpan(offset + 16, length).ContainsAnyExcept((byte)0))
                return $"vpd:{type}:" + Convert.ToHexString(descriptor.AsSpan(offset + 16, length));
            if (next < 16) break;
            offset += next;
        }
        return null;
    }
    private static string AsciiField(byte[] value, int field)
    {
        if (value.Length < field + 4) return "";
        uint offset = BitConverter.ToUInt32(value, field);
        if (offset == 0 || offset >= value.Length) return "";
        int end = Array.IndexOf(value, (byte)0, (int)offset); if (end < 0) end = value.Length;
        return Encoding.ASCII.GetString(value, (int)offset, end - (int)offset).Trim();
    }
    private static byte[] Property(SafeFileHandle handle, int property)
    {
        byte[] query = new byte[12]; BitConverter.GetBytes(property).CopyTo(query, 0);
        byte[] header = new byte[8];
        if (!DeviceIoControl(handle, 0x002d1400, query, 12, header, 8, out _, 0)) return [];
        uint size = BitConverter.ToUInt32(header, 4); if (size is < 8 or > 65536) return [];
        byte[] output = new byte[size];
        return DeviceIoControl(handle, 0x002d1400, query, 12, output, size, out uint returned, 0) ? output[..(int)returned] : [];
    }
    private sealed class MediaErrorScope : IDisposable
    {
        private readonly uint _old;
        private readonly bool _changed;
        public MediaErrorScope() { _changed = SetThreadErrorMode(1, out _old); }
        public void Dispose() { if (_changed) SetThreadErrorMode(_old, out _); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadErrorMode(uint mode, out uint old);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetDiskFreeSpaceExW(string path, out ulong available, out ulong total, out ulong free);
    private static SafeFileHandle Open(string path) => CreateFileW(path, 0, 7, 0, 3, 0, 0);
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint FindFirstVolumeW(StringBuilder name, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool FindNextVolumeW(nint handle, StringBuilder name, int length);
    [DllImport("kernel32.dll")] private static extern bool FindVolumeClose(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetDiskSpaceInformationW(string path, out Space space);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetVolumeInformationW(string path, StringBuilder label, int length, out uint serial, out uint component, out uint flags, StringBuilder filesystem, int fsLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetVolumePathNamesForVolumeNameW(string volume, [Out] char[] paths, uint length, out uint required);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[]? input, uint inputSize, byte[] output, uint outputSize, out uint returned, nint overlapped);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevsW(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(nint devices, nint info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailW(nint devices, ref InterfaceData data, nint detail, uint size, out uint required, nint info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint devices);
}
