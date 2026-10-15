using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyFinder.Core;
using MyFinder.Core.Desktop;
using MyFinder.Core.Monitoring;
using MyFinder.Desktop;
using MyFinder.Views;
using Windows.System.Power;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MyFinder;

internal sealed class AppController
{
    private readonly PreferenceStore _store;
    private readonly Mutex _instance;
    private readonly UISettings _ui = new();
    private readonly Dictionary<Window, PlainBackdrop> _backdrops = [];
    private MainWindow _panel = null!;
    private DetailWindow? _details;
    private SettingsWindow? _settings;
    private DesktopMessages _messages = null!;
    private TrayIcon _tray = null!;
    private DispatcherTimer _timer = null!;
    private bool _exiting, _shown, _fullscreen, _ownsInstance, _trayReady;
    private int _ticks;
    private string _displaySignature = "";
    private double _lastSample = -1;
    public Preferences Preferences { get; private set; }
    public MonitoringSession Session { get; private set; } = null!;
    public Reading[] Readings { get; private set; } = [];
    public string? Warning => _store.Warning;
    public string AppearanceStatus { get; private set; } = "Opaque background";
    public IReadOnlyList<Display> Displays { get; private set; } = [];
    public bool IsExiting => _exiting;
    public bool Retreated => Preferences.Shown && Preferences.Retreat && _fullscreen;
    public event Action? PreferencesChanged;
    public AppController()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyFinder");
        int profileOption = Array.IndexOf(arguments, "--profile");
        if (profileOption >= 0 && profileOption + 1 < arguments.Length) profile = Path.GetFullPath(arguments[profileOption + 1]);
        _store = new(profile);
        Preferences = _store.Load();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_store.DirectoryPath.ToUpperInvariant())))[..24];
        _instance = new(false, "Local\\MyFinder-" + key);
    }
    public void Start()
    {
        try { _ownsInstance = _instance.WaitOne(0); } catch (AbandonedMutexException) { _ownsInstance = true; }
        if (!_ownsInstance) { _instance.Dispose(); Application.Current.Exit(); return; }
        Session = new(Preferences);
        _panel = new(this);
        _messages = new(_panel.Handle);
        _messages.HideRequested += () => Update(Preferences with { Shown = false });
        _messages.ContextChanged += RefreshAppearance;
        _messages.Moved += RememberPlacement;
        _messages.TrayActivated += () => Update(Preferences with { Shown = !Preferences.Shown });
        _messages.TrayMenu += ShowTrayMenu;
        _tray = new(_panel.Handle);
        _messages.TrayRecreated += () => { _trayReady = _tray.Add(); };
        _trayReady = _tray.Add();
        _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _ui.ColorValuesChanged += SystemAppearanceChanged;
        _ui.AdvancedEffectsEnabledChanged += SystemAppearanceChanged;
        _ui.TextScaleFactorChanged += SystemAppearanceChanged;
        PowerManager.EnergySaverStatusChanged += PowerChanged;
        Reconcile(true);
        if (Environment.GetCommandLineArgs().Contains("--settings") || Warning is not null || !_trayReady) ShowSettings();
    }
    public async void Update(Preferences preferences)
    {
        if (_exiting) return;
        Preferences = preferences.Normalize();
        Session.Configure(Preferences);
        _panel.Configure();
        PreferencesChanged?.Invoke();
        Reconcile(true);
        bool saved = await _store.SaveAsync(Preferences);
        if (!saved) _settings?.RefreshStatus();
    }
    private void Tick()
    {
        if (_exiting) return;
        _ticks++;
        if (!_trayReady && _ticks % 20 == 0) _trayReady = _tray.Add();
        Reconcile(_ticks % 8 == 0);
        Readings = Session.Snapshot();
        if (!Preferences.NetworkSelectionMade && Session.NetworkReady)
        {
            Update(NetworkCollector.InitialSelection(Preferences, Readings) ?? Preferences with { NetworkSelectionMade = true });
            if (!Preferences.Pins.ContainsKey("network")) ShowSettings();
        }
        if (!Preferences.StorageSelectionMade && Session.StorageReady)
            Update(StorageCollector.InitialSelection(Preferences, Readings));
        double latest = Readings.Select(x => x.Observation.Elapsed).DefaultIfEmpty(-1).Max();
        if (latest != _lastSample || _ticks % 4 == 0)
        {
            _lastSample = latest;
            if (_shown) { _panel.Update(Readings); FitPanel(); }
            if (_details?.IsOpen == true) _details.Refresh();
            if (_settings?.IsOpen == true && _ticks % 4 == 0) _settings.RefreshStatus();
        }
    }
    public string ResourceForMetric(string metric) => Preferences.Pins.GetValueOrDefault(metric, metric);
    private void Reconcile(bool topology)
    {
        if (_exiting || _messages.Moving) return;
        if (topology || Displays.Count == 0)
        {
            var displays = NativeDesktop.Displays();
            if (displays.Count > 0) Displays = displays;
            string signature = string.Join(";", Displays.Select(x => $"{x.Id}:{x.Bounds}:{x.Work}"));
            if (_displaySignature != signature) { _displaySignature = signature; topology = true; }
        }
        if (Displays.Count == 0) return;
        var display = Presentation.ChooseDisplay(Displays, Preferences.PreferredDisplay);
        bool? fullscreen = NativeDesktop.Fullscreen(display, (uint)Environment.ProcessId);
        if (fullscreen.HasValue) _fullscreen = fullscreen.Value;
        bool show = Presentation.ShouldShow(Preferences.Shown, Preferences.Retreat, _fullscreen, _messages.LockedSession);
        if (topology) FitPanel();
        if (_shown != show)
        {
            _shown = show;
            if (show) _panel.AppWindow.Show(false);
            else { _details?.Dismiss(); _panel.AppWindow.Hide(); }
        }
        _tray.SetTip(Retreated ? "MyFinder — fullscreen retreat (Show respects this policy)" : Preferences.Shown ? "MyFinder — monitoring" : "MyFinder — hidden; monitoring continues");
    }
    private void FitPanel()
    {
        if (Displays.Count == 0 || _messages.Moving) return;
        var display = Presentation.ChooseDisplay(Displays, Preferences.PreferredDisplay);
        var placement = Preferences.Placements.GetValueOrDefault(display.Id, new(display.Id));
        var size = _panel.Measure();
        double dpi = NativeDesktop.Dpi(_panel.Handle);
        var rect = Presentation.Place(display.Work, (int)Math.Ceiling(size.Width * dpi), (int)Math.Ceiling(size.Height * dpi), placement);
        if (NativeDesktop.Bounds(_panel.Handle) != rect) NativeDesktop.Move(_panel.Handle, rect);
    }
    private void RememberPlacement()
    {
        if (Preferences.Locked || Displays.Count == 0) return;
        var bounds = NativeDesktop.Bounds(_panel.Handle);
        var display = Displays.MaxBy(x => Math.Max(0, Math.Min(bounds.Right, x.Bounds.Right) - Math.Max(bounds.X, x.Bounds.X)) * (long)Math.Max(0, Math.Min(bounds.Bottom, x.Bounds.Bottom) - Math.Max(bounds.Y, x.Bounds.Y)))!;
        var placements = new Dictionary<string, Placement>(Preferences.Placements) { [display.Id] = Presentation.Remember(display, bounds) };
        Update(Preferences with { PreferredDisplay = display.Id, Placements = placements });
    }
    public void ChoosePlacement(string displayId, Alignment alignment)
    {
        var placement = Preferences.Placements.GetValueOrDefault(displayId, new(displayId)) with { Alignment = alignment };
        var placements = new Dictionary<string, Placement>(Preferences.Placements) { [displayId] = placement };
        Update(Preferences with { PreferredDisplay = displayId, Placements = placements });
    }
    public void Inspect(string id)
    {
        if (_exiting || Retreated || !Preferences.Shown) return;
        _details ??= new(this);
        _details.Open(id);
    }
    public void ShowSettings()
    {
        if (_exiting) return;
        _settings ??= new(this);
        _settings.Open();
    }
    private void ShowTrayMenu()
    {
        switch (_tray.Menu(Preferences.Shown, Retreated))
        {
            case 1: Update(Preferences with { Shown = !Preferences.Shown }); break;
            case 2:
                if (!Preferences.Shown) Update(Preferences with { Shown = true });
                Inspect("cpu"); break;
            case 3: ShowSettings(); break;
            case 4: Exit(); break;
        }
    }
    public void ApplyAppearance(Window window, Grid root, bool solid)
    {
        bool contrast = NativeDesktop.HighContrast();
        root.RequestedTheme = contrast ? ElementTheme.Default : Preferences.Theme switch { Appearance.Light => ElementTheme.Light, Appearance.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        Color system = _ui.GetColorValue(UIColorType.Background);
        bool dark = Preferences.Theme == Appearance.Dark || Preferences.Theme == Appearance.System && system.R < 128;
        Color color = contrast ? system : dark ? Color.FromArgb(255, 28, 32, 39) : Color.FromArgb(255, 247, 249, 252);
        bool allowed = !solid && !contrast && _ui.AdvancedEffectsEnabled && PowerManager.EnergySaverStatus != EnergySaverStatus.On;
        color.A = allowed ? (byte)Math.Round(Preferences.BackgroundOpacity * 255) : (byte)255;
        if (!solid) AppearanceStatus = allowed ? $"Background opacity: {color.A / 255d:P0}; foreground remains opaque" : "Solid background: system transparency, high contrast or energy-saver policy";
        try
        {
            if (!_backdrops.TryGetValue(window, out var backdrop))
            {
                backdrop = new(WinRT.Interop.WindowNative.GetWindowHandle(window)); backdrop.SetColor(color); _backdrops[window] = backdrop; window.SystemBackdrop = backdrop;
            }
            backdrop.SetColor(color);
            root.Background = Ui.Brush(Color.FromArgb(0, 0, 0, 0));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            window.SystemBackdrop = null;
            _backdrops.Remove(window);
            color.A = 255; root.Background = Ui.Brush(color);
            AppearanceStatus = "Solid background: native composition unavailable";
        }
    }
    public void PositionDetails(Window window)
    {
        if (Displays.Count == 0) return;
        var display = Presentation.ChooseDisplay(Displays, Preferences.PreferredDisplay);
        var size = window.AppWindow.Size;
        var panel = NativeDesktop.Bounds(_panel.Handle);
        int x = Math.Clamp(panel.X, display.Work.X, Math.Max(display.Work.X, display.Work.Right - size.Width));
        int y = panel.Bottom + 8;
        if (y + size.Height > display.Work.Bottom) y = panel.Y - size.Height - 8;
        y = Math.Clamp(y, display.Work.Y, Math.Max(display.Work.Y, display.Work.Bottom - size.Height));
        window.AppWindow.MoveAndResize(new(x, y, Math.Min(size.Width, display.Work.Width), Math.Min(size.Height, display.Work.Height)));
    }
    private void SystemAppearanceChanged(UISettings sender, object args) => RefreshAppearance();
    private void PowerChanged(object? sender, object args) => RefreshAppearance();
    private void RefreshAppearance() => _panel.DispatcherQueue.TryEnqueue(() => { if (!_exiting) { _panel.Configure(); PreferencesChanged?.Invoke(); Reconcile(true); } });
    public async void Exit()
    {
        if (_exiting) return;
        _exiting = true; _timer.Stop();
        _ui.ColorValuesChanged -= SystemAppearanceChanged; _ui.AdvancedEffectsEnabledChanged -= SystemAppearanceChanged; _ui.TextScaleFactorChanged -= SystemAppearanceChanged;
        PowerManager.EnergySaverStatusChanged -= PowerChanged;
        _tray.Dispose(); _messages.Dispose();
        _details?.Dismiss(); _settings?.Dismiss();
        await _store.SaveAsync(Preferences);
        await Session.DisposeAsync();
        _instance.ReleaseMutex(); _instance.Dispose();
        foreach (var window in _backdrops.Keys.ToArray()) window.SystemBackdrop = null;
        _backdrops.Clear();
        _details?.Close(); _settings?.Close(); _panel.Close();
        Application.Current.Exit();
    }
}
