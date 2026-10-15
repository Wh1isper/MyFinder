using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MyFinder.Desktop;

// DWM must honor the host surface's alpha as well as the composition brush's alpha.
// DwmEnableBlurBehindWindow enables that channel; Windows 8+ no longer adds blur.
internal sealed class PlainBackdrop(nint handle) : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? _compositor;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private Subclass? _procedure;
    private Color _color;
    public void SetColor(Color color) { _color = color; if (_brush is not null) _brush.Color = color; }
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
        ConfigureHost();
        _procedure = HostMessage;
        if (!SetWindowSubclass(handle, _procedure, 2, 0)) throw new COMException("Cannot configure the alpha surface.");
        nint dc = GetDC(handle);
        try { ClearHost(dc); } finally { ReleaseDC(handle, dc); }
        _compositor = new();
        _brush = _compositor.CreateColorBrush(_color);
        target.SystemBackdrop = _brush;
    }
    private void ConfigureHost()
    {
        var margins = new Margins();
        Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(handle, ref margins));
        var alpha = new BlurBehind { Flags = 1, Enabled = 1 };
        Marshal.ThrowExceptionForHR(DwmEnableBlurBehindWindow(handle, ref alpha));
    }
    private void ClearHost(nint dc)
    {
        if (dc != 0 && GetClientRect(handle, out var rect)) FillRect(dc, ref rect, GetStockObject(4)); // BLACK_BRUSH: zero-alpha host pixels
    }
    private nint HostMessage(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        if (message == 0x14) { ClearHost((nint)wparam); return 1; }
        if (message == 0x31E) ConfigureHost();
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (_procedure is not null) RemoveWindowSubclass(handle, _procedure, 2);
        _procedure = null;
        target.SystemBackdrop = null;
        _brush?.Dispose(); _brush = null;
        _compositor?.Dispose(); _compositor = null;
        var alpha = new BlurBehind { Flags = 1 };
        DwmEnableBlurBehindWindow(handle, ref alpha);
        base.OnTargetDisconnected(target);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind { public uint Flags; public int Enabled; public nint Region; public int Transition; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate nint Subclass(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind value);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wparam, nint lparam);
}
