using System.Runtime.InteropServices;
using MyFinder.Core.Desktop;

namespace MyFinder.Desktop;

internal static class NativeDesktop
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect Rect => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public uint Size; public NativeRect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    private delegate bool MonitorCallback(nint monitor, nint hdc, nint bounds, nint data);
    internal static IReadOnlyList<Display> Displays()
    {
        List<Display> result = [];
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (!GetMonitorInfoW(monitor, ref info)) return true;
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
            bool identified = EnumDisplayDevicesW(info.Device, 0, ref device, 1);
            result.Add(new(identified && device.Id.Length > 0 ? device.Id : info.Device,
                identified ? device.Description : info.Device, info.Monitor.Rect, info.Work.Rect, (info.Flags & 1) != 0));
            return true;
        };
        EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        return result;
    }
    internal static Rect Bounds(nint hwnd) { GetWindowRect(hwnd, out var rect); return rect.Rect; }
    internal static bool? Fullscreen(Display display, uint ownProcess)
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == GetShellWindow() || foreground == GetDesktopWindow()) return false;
        var className = new System.Text.StringBuilder(256);
        GetClassNameW(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        GetWindowThreadProcessId(foreground, out uint process);
        if (process == ownProcess) return null;
        if (!IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        DwmGetWindowAttribute(foreground, 14, out int cloaked, sizeof(int));
        return cloaked == 0 && Presentation.CoversDisplay(Bounds(foreground), display.Bounds, IsZoomed(foreground));
    }
    internal static void Passive(nint hwnd)
    {
        nint style = GetWindowLongPtrW(hwnd, -20);
        SetWindowLongPtrW(hwnd, -20, style | 0x08000080); // NOACTIVATE | TOOLWINDOW
    }
    internal static void ShowPassive(nint hwnd) => ShowWindow(hwnd, 4);
    internal static void Hide(nint hwnd) => ShowWindow(hwnd, 0);
    internal static void Move(nint hwnd, Rect rect) => SetWindowPos(hwnd, -1, rect.X, rect.Y, rect.Width, rect.Height, 0x0010);
    internal static void StartDrag(nint hwnd) { ReleaseCapture(); SendMessageW(hwnd, 0xA1, 2, 0); }
    internal static double Dpi(nint hwnd) => GetDpiForWindow(hwnd) / 96d;
    [StructLayout(LayoutKind.Sequential)] private struct ContrastInfo { public uint Size, Flags; public nint Scheme; }
    internal static bool HighContrast()
    {
        var info = new ContrastInfo { Size = (uint)Marshal.SizeOf<ContrastInfo>() };
        return SystemParametersInfoW(0x42, info.Size, ref info, 0) && (info.Flags & 1) != 0;
    }
    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(uint action, uint size, ref ContrastInfo info, uint flags);
    internal static bool ForegroundOwned() { GetWindowThreadProcessId(GetForegroundWindow(), out uint process); return process == Environment.ProcessId; }

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}

internal sealed class DesktopMessages : IDisposable
{
    private readonly nint _hwnd;
    private readonly Subclass _callback;
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private bool _disposed;
    public event Action? HideRequested;
    public event Action? ContextChanged;
    public event Action? Moved;
    public event Action? TrayRecreated;
    public event Action? TrayActivated;
    public event Action? TrayMenu;
    public bool LockedSession { get; private set; }
    public bool Moving { get; private set; }
    public DesktopMessages(nint hwnd)
    {
        _hwnd = hwnd; _callback = Procedure;
        if (!SetWindowSubclass(hwnd, _callback, 1, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        WTSRegisterSessionNotification(hwnd, 0);
    }
    private nint Procedure(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        if (message == 0x10 && !_disposed) { HideRequested?.Invoke(); return 0; }
        if (message == 0x21) return 3; // mouse clicks work without activating the passive panel
        if (message == 0x231) Moving = true;
        if (message == 0x232) { Moving = false; Moved?.Invoke(); }
        if (message == 0x02B1) { if (wparam == 7) LockedSession = true; if (wparam == 8) LockedSession = false; ContextChanged?.Invoke(); }
        if (message is 0x007E or 0x02E0 or 0x001A or 0x0218) ContextChanged?.Invoke();
        if (message == _taskbarCreated) TrayRecreated?.Invoke();
        if (message == 0x8001)
        {
            uint action = (uint)(long)lparam & 0xFFFF;
            if (action is 0x0203 or 0x0400 or 0x0401) TrayActivated?.Invoke();
            if (action is 0x0205 or 0x007B) TrayMenu?.Invoke();
        }
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    public void Dispose()
    {
        _disposed = true;
        WTSUnRegisterSessionNotification(_hwnd);
        RemoveWindowSubclass(_hwnd, _callback, 1);
    }
    private delegate nint Subclass(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint hwnd);
}

internal sealed class TrayIcon(nint hwnd) : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public nint Hwnd; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    private NotifyData Data(string tip) => new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Hwnd = hwnd, Id = 1, Flags = 1 | 2 | 4 | 0x80,
        Callback = 0x8001, Icon = LoadIconW(0, 32516), Tip = tip, Info = "", Title = "", Version = 4 };
    public bool Add()
    {
        var data = Data("MyFinder — monitoring continues while hidden");
        bool added = Shell_NotifyIconW(0, ref data);
        if (added) Shell_NotifyIconW(4, ref data);
        return added;
    }
    public void SetTip(string tip) { var data = Data(tip.Length > 127 ? tip[..127] : tip); Shell_NotifyIconW(1, ref data); }
    public int Menu(bool shown, bool retreated)
    {
        nint menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, 0, 1, shown ? "Hide panel" : "Show panel");
            if (retreated) AppendMenuW(menu, 1, 0, "Panel is retreated for fullscreen");
            AppendMenuW(menu, 0, 2, "Inspect CPU");
            AppendMenuW(menu, 0, 3, "Settings");
            AppendMenuW(menu, 0x800, 0, "");
            AppendMenuW(menu, 0, 4, "Exit MyFinder");
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            return (int)TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, 0);
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose() { var data = Data(""); Shell_NotifyIconW(2, ref data); }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint operation, ref NotifyData data);
    [DllImport("user32.dll")] private static extern nint LoadIconW(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
