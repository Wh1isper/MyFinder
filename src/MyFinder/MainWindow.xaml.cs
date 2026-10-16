using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MyFinder.Core;
using MyFinder.Core.Monitoring;
using MyFinder.Desktop;
using MyFinder.Views;

namespace MyFinder;

public sealed partial class MainWindow : Window
{
    private readonly AppController _owner;
    private readonly Dictionary<string, TextBlock> _readings = [];
    private readonly Dictionary<string, TextBlock> _labels = [];
    private readonly Dictionary<string, ProgressBar> _levels = [];
    private StackPanel _items = null!;
    internal nint Handle { get; }
    internal Grid Root => Host;
    internal MainWindow(AppController owner)
    {
        _owner = owner;
        InitializeComponent();
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        NativeDesktop.Passive(Handle);
        Configure();
    }
    internal void Configure()
    {
        var preferences = _owner.Preferences;
        Host.Children.Clear(); _readings.Clear(); _labels.Clear(); _levels.Clear();
        _items = Ui.Stack(preferences.Style == PanelStyle.Capsules ? 4 : 0);
        _items.Orientation = preferences.Orientation == PanelOrientation.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        _items.Padding = new(4);
        var move = Ui.IconButton(preferences.Locked ? "Position locked" : "Move panel", preferences.Locked ? "\uE72E" : "\uE7C2", () => { }, "MovePanel");
        move.Style = (Style)Application.Current.Resources["PanelMetricStyle"];
        move.MinWidth = 24; move.Width = 24; move.Padding = new(4);
        move.VerticalAlignment = VerticalAlignment.Center; move.IsEnabled = !preferences.Locked;
        move.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            if (!preferences.Locked && e.GetCurrentPoint(move).Properties.IsLeftButtonPressed) { NativeDesktop.StartDrag(Handle); e.Handled = true; }
        }), true);
        _items.Children.Add(move);
        foreach (string key in preferences.MetricOrder)
        {
            var text = Ui.Text("—", Math.Max(12, (key is "network" or "storage" ? 13 : 16) * preferences.Scale));
            text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            text.IsTextSelectionEnabled = false;
            var stack = Ui.Stack(2);
            var label = Ui.Muted(key switch { "memory" => "MEM", "network" => "NET", "storage" => "DISK", _ => "CPU" }, Math.Max(12, 12 * preferences.Scale));
            label.IsTextSelectionEnabled = false; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
            _labels[key] = label;
            // Short text labels are clearer at this scale than ambiguous hardware glyphs.
            stack.Children.Add(label);
            stack.Children.Add(text);
            if (preferences.Style != PanelStyle.Minimal && key is "cpu" or "memory")
            {
                var level = new ProgressBar { Minimum = 0, Maximum = 100, Height = 2, IsTabStop = false };
                _levels[key] = level; stack.Children.Add(level);
            }
            var button = new Button
            {
                Content = stack, Padding = new(8 * preferences.Scale, 4 * preferences.Scale, 8 * preferences.Scale, 4 * preferences.Scale),
                Style = (Style)Application.Current.Resources[preferences.Style == PanelStyle.Capsules ? "DefaultButtonStyle" : "PanelMetricStyle"],
                Width = (key is "network" or "storage" ? 136 : 88) * preferences.Scale,
                CornerRadius = new(preferences.Style == PanelStyle.Capsules ? 8 : 4), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Click += (_, _) => _owner.Inspect(_owner.ResourceForMetric(key));
            AutomationProperties.SetAutomationId(button, "Inspect_" + key);
            Ui.Name(button, "Inspect " + key);
            _readings[key] = text; _items.Children.Add(button);
        }
        var settings = Ui.IconButton("Settings", "\uE713", _owner.ShowSettings, "OpenSettings");
        settings.Style = (Style)Application.Current.Resources["PanelMetricStyle"]; settings.Width = 32; settings.Padding = new(6);
        settings.VerticalAlignment = VerticalAlignment.Center; _items.Children.Add(settings);
        Host.Children.Add(new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled });
        _owner.ApplyAppearance(this, Host, false);
    }
    internal Windows.Foundation.Size Measure()
    {
        _items.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return _items.DesiredSize;
    }
    internal void Update(Reading[] readings)
    {
        foreach (var pair in _readings)
        {
            string id = _owner.ResourceForMetric(pair.Key);
            var reading = readings.FirstOrDefault(x => x.Resource.Id == id);
            pair.Value.Text = reading is null ? "Choose resource" : reading.Resource.Kind == ResourceKind.Network
                ? $"↓ {ValueFormat.Metric(reading.Observation.Get("Receive"), _owner.Preferences)}\n↑ {ValueFormat.Metric(reading.Observation.Get("Send"), _owner.Preferences)}"
                : reading.Resource.Kind == ResourceKind.Volume ? $"Used {ValueFormat.Metric(reading.Observation.Get("Utilization"), _owner.Preferences, true)}\n{ValueFormat.Metric(reading.Observation.Get("Used / reserved"), _owner.Preferences, true)}"
                : reading.Resource.Kind == ResourceKind.Device ? $"R {ValueFormat.Metric(reading.Observation.Get("Read"), _owner.Preferences, true)}\nW {ValueFormat.Metric(reading.Observation.Get("Write"), _owner.Preferences, true)}"
                : ValueFormat.Metric(reading.Observation.Get("Utilization", Unit.Percent), _owner.Preferences);
            // Stable labels keep the strip still; the full monitored identity remains in the tooltip and details.
            if (_levels.TryGetValue(pair.Key, out var level))
            {
                var value = reading?.Observation.Get("Utilization");
                level.Opacity = value?.Value is null ? 0 : 1;
                level.Value = value?.Value ?? 0;
            }
            ToolTipService.SetToolTip(pair.Value, reading is null ? "Choose a resource in Settings." : $"{reading.Resource.Name}\n{reading.Observation.Time:T}\n{reading.Observation.Get(reading.Resource.Kind == ResourceKind.Network ? "Receive" : "Utilization").Reason}");
            ToolTipService.SetToolTip(_labels[pair.Key], reading?.Resource.Name ?? "Choose a resource in Settings.");
        }
    }
}
