using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MyFinder.Core.Monitoring;

public sealed record AdapterSnapshot(Guid Id, ulong Luid, string Name, string Model, uint Type, bool Hardware,
    uint Status, ulong ReceiveSpeed, ulong SendSpeed, ulong Received, ulong Sent)
{
    public string ResourceId => "network:" + Id.ToString("D");
    public string Category => Type == 24 ? "Loopback" : Type == 131 ? "Tunnel" : Hardware ? "Physical" : "Virtual / software";
    public bool Connected => Status == 1;
    public Resource Resource => new(ResourceId, ResourceKind.Network, Name, new Dictionary<string, string>
    {
        ["Model"] = Model, ["Category"] = Category,
        ["Interface type"] = Type switch { 6 => "Ethernet", 71 => "Wi-Fi", 24 => "Loopback", 131 => "Tunnel", 23 => "PPP", _ => $"IANA type {Type}" },
        ["Link state"] = Status switch { 1 => "Up", 2 => "Down", 3 => "Testing", 4 => "Unknown", 5 => "Dormant", 6 => "Not present", 7 => "Lower layer down", _ => "Unknown" },
        ["Receive link speed"] = ReceiveSpeed == 0 ? "Not reported" : ValueFormat.Size(ReceiveSpeed, false, true) + "/s",
        ["Send link speed"] = SendSpeed == 0 ? "Not reported" : ValueFormat.Size(SendSpeed, false, true) + "/s",
        ["Counter scope"] = "Error-free interface octets (unicast, multicast and broadcast), not application payload. Virtual and physical interfaces may overlap; do not sum them."
    });
}

public static class NetworkInterfaces
{
    // MIB_IF_ROW2: fixed Windows ABI, Unicode strings and 8-byte ULONG64 alignment.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Row
    {
        public ulong Luid;
        public uint Index;
        public Guid Guid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint AddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Address;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMedium, AccessType, Direction;
        public byte Flags;
        public uint Status, AdminStatus, MediaState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitSpeed, ReceiveSpeed, InOctets, InUcastPackets, InNUcastPackets, InDiscards, InErrors,
            InUnknownProtocols, InUcastOctets, InMulticastOctets, InBroadcastOctets, OutOctets, OutUcastPackets,
            OutNUcastPackets, OutDiscards, OutErrors, OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQueueLength;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Table { public uint Count; public Row First; }
    [DllImport("iphlpapi.dll")] private static extern uint GetIfTable2(out nint table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(nint table);

    public static AdapterSnapshot[] Read()
    {
        uint error = GetIfTable2(out nint table);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            int count = Marshal.ReadInt32(table);
            int offset = Marshal.OffsetOf<Table>(nameof(Table.First)).ToInt32(), stride = Marshal.SizeOf<Row>();
            var result = new List<AdapterSnapshot>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Row>(table + offset + i * stride);
                if (row.Guid == Guid.Empty || (row.Flags & 2) != 0) continue; // Filter interfaces duplicate another interface's counters.
                result.Add(new(row.Guid, row.Luid, row.Alias, row.Description, row.Type, (row.Flags & 1) != 0,
                    row.Status, row.ReceiveSpeed, row.TransmitSpeed, row.InOctets, row.OutOctets));
            }
            return result.DistinctBy(x => x.Id).ToArray();
        }
        finally { FreeMibTable(table); }
    }

    // Called only by an explicit temporary reveal; no address enters resource facts, history or preferences.
    public static string RevealAddresses(string resourceId)
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x =>
            Guid.TryParse(x.Id, out var id) && "network:" + id.ToString("D") == resourceId);
        if (adapter is null) return "Adapter is no longer available.";
        var addresses = adapter.GetIPProperties().UnicastAddresses.Select(x => x.Address.ToString());
        return "Current unicast addresses: " + string.Join(", ", addresses) + "\nPhysical address: " + adapter.GetPhysicalAddress();
    }
}

public sealed class NetworkCollector
{
    private sealed class State(Resource resource, ulong luid, double seen, DateTimeOffset start)
    {
        public Resource Resource = resource;
        public ulong Luid = luid;
        public double Seen = seen;
        public DateTimeOffset Start = start;
        public CounterRate Receive = new(), Send = new();
        public void Break() { Receive.Break(); Send.Break(); }
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, State> _states = [];
    private Preferences _preferences = new();
    public void Configure(Preferences preferences)
    {
        lock (_gate)
        {
            foreach (var pair in _states)
                if (_preferences.Monitored.Contains(pair.Key) != preferences.Monitored.Contains(pair.Key)) pair.Value.Break();
            _preferences = preferences;
        }
    }
    public Reading[] Collect()
    {
        AdapterSnapshot[] adapters;
        string? failure = null;
        try { adapters = NetworkInterfaces.Read(); }
        catch (Win32Exception ex) { adapters = []; failure = $"Interface snapshot unavailable (Windows {ex.NativeErrorCode})."; }
        return Observe(adapters, ObservationClock.Elapsed, DateTimeOffset.Now, failure);
    }
    public Reading[] Observe(AdapterSnapshot[] adapters, double now, DateTimeOffset time, string? failure = null)
    {
        lock (_gate)
        {
            var preferences = _preferences;
            var present = adapters.ToDictionary(x => x.ResourceId);
            var selected = preferences.Monitored.Concat(preferences.Pins.Values).Where(x => x.StartsWith("network:")).ToHashSet();
            foreach (var adapter in adapters.OrderByDescending(x => selected.Contains(x.ResourceId)).Take(512))
            {
                if (!_states.TryGetValue(adapter.ResourceId, out var state)) _states[adapter.ResourceId] = state = new(adapter.Resource, adapter.Luid, now, time);
                if (state.Luid != adapter.Luid) { state.Break(); state.Luid = adapter.Luid; }
                state.Resource = adapter.Resource; state.Seen = now;
            }
            foreach (string id in selected)
                if (!_states.ContainsKey(id)) _states[id] = new(new(id, ResourceKind.Network, "Saved adapter (absent)", new Dictionary<string, string>()), 0, now, time);
            foreach (var pair in _states.Where(x => !selected.Contains(x.Key) && !present.ContainsKey(x.Key) && now - x.Value.Seen > preferences.RetentionMinutes * 60).ToArray()) _states.Remove(pair.Key);
            foreach (var pair in _states.Where(x => !selected.Contains(x.Key)).OrderByDescending(x => x.Value.Seen).Skip(512).ToArray()) _states.Remove(pair.Key);
            var result = new List<Reading>();
            foreach (var pair in _states)
            {
                var state = pair.Value;
                bool monitored = preferences.Monitored.Contains(pair.Key);
                present.TryGetValue(pair.Key, out var adapter);
                Quality quality = !monitored ? Quality.NotMonitored : failure is not null ? Quality.Unavailable : adapter?.Connected == true ? Quality.Fresh : Quality.Disconnected;
                string reason = !monitored ? "Enable monitoring in Settings." : failure ?? "Adapter is absent or its link is not up.";
                Metric receive, send;
                if (quality != Quality.Fresh)
                {
                    state.Break(); receive = Metric.Missing(Unit.BytesPerSecond, reason, quality); send = receive;
                }
                else
                {
                    double gap = Math.Max(5, preferences.IntervalSeconds * 3);
                    receive = state.Receive.Observe(adapter!.Received, now, gap);
                    send = state.Send.Observe(adapter.Sent, now, gap);
                }
                var facts = new Dictionary<string, string>(state.Resource.Facts)
                {
                    ["Observation session started"] = state.Start.ToString("O"),
                    ["Totals"] = "Observed segments only. Startup, reset, disconnected, paused and sleep gaps are excluded; not a lifetime or billing total."
                };
                if (adapter is null) facts["Link state"] = failure is null ? "Absent" : "Unavailable";
                var values = new Dictionary<string, Metric>
                {
                    ["Receive"] = receive, ["Send"] = send,
                    ["Observed received"] = new(state.Receive.Total, Unit.Bytes), ["Observed sent"] = new(state.Send.Total, Unit.Bytes),
                    ["Receive coverage"] = new(state.Receive.CoverageSeconds, Unit.Seconds), ["Send coverage"] = new(state.Send.CoverageSeconds, Unit.Seconds),
                    ["Receive segments"] = new(state.Receive.Segment, Unit.Count), ["Send segments"] = new(state.Send.Segment, Unit.Count)
                };
                result.Add(new(state.Resource with { Facts = facts }, new(pair.Key, time, now, state.Receive.Segment + state.Send.Segment, values)));
            }
            return result.ToArray();
        }
    }
    public static Preferences? InitialSelection(Preferences preferences, IEnumerable<Reading> readings)
    {
        if (preferences.NetworkSelectionMade) return null;
        if (preferences.Pins.ContainsKey("network") || preferences.Monitored.Any(x => x.StartsWith("network:"))) return preferences with { NetworkSelectionMade = true };
        var candidates = readings.Where(x => x.Resource.Kind == ResourceKind.Network && x.Resource.Facts.GetValueOrDefault("Category") == "Physical" && x.Resource.Facts.GetValueOrDefault("Link state") == "Up").ToArray();
        if (candidates.Length != 1) return null;
        string id = candidates[0].Resource.Id;
        return preferences with { NetworkSelectionMade = true, Monitored = preferences.Monitored.Append(id).ToArray(),
            MetricOrder = preferences.MetricOrder.Append("network").Distinct().ToArray(), Pins = new(preferences.Pins) { ["network"] = id } };
    }
}
