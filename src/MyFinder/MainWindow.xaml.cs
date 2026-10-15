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
        Host.Children.Clear(); _readings.Clear();
        _items = Ui.Stack(4);
        _items.Orientation = preferences.Orientation == PanelOrientation.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        _items.Padding = new(6);
        var move = Ui.Button(preferences.Locked ? "Locked" : "Move", () => { }, "MovePanel");
        move.FontSize = 11; move.Padding = new(6); move.IsEnabled = !preferences.Locked;
        move.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            if (!preferences.Locked && e.GetCurrentPoint(move).Properties.IsLeftButtonPressed) { NativeDesktop.StartDrag(Handle); e.Handled = true; }
        }), true);
        _items.Children.Add(move);
        foreach (string key in preferences.MetricOrder)
        {
            var text = Ui.Text("Warming up", Math.Max(12, 16 * preferences.Scale));
            text.IsTextSelectionEnabled = false;
            var stack = Ui.Stack(2);
            if (preferences.Style != PanelStyle.Minimal) stack.Children.Add(Ui.Text(key.ToUpperInvariant(), 10 * preferences.Scale));
            stack.Children.Add(text);
            var button = new Button
            {
                Content = stack, Padding = new(8 * preferences.Scale, 4 * preferences.Scale, 8 * preferences.Scale, 4 * preferences.Scale),
                MinWidth = (preferences.Style == PanelStyle.Minimal ? 104 : 126) * preferences.Scale,
                CornerRadius = new(preferences.Style == PanelStyle.Capsules ? 18 : 5), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Click += (_, _) => _owner.Inspect(_owner.ResourceForMetric(key));
            AutomationProperties.SetAutomationId(button, "Inspect_" + key);
            Ui.Name(button, "Inspect " + key);
            _readings[key] = text; _items.Children.Add(button);
        }
        var settings = Ui.Button("Settings", _owner.ShowSettings, "OpenSettings"); settings.FontSize = 11; settings.Padding = new(6); _items.Children.Add(settings);
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
            pair.Value.Text = reading is null ? "Unavailable" : ValueFormat.Metric(reading.Observation.Get("Utilization", Unit.Percent), _owner.Preferences);
            ToolTipService.SetToolTip(pair.Value, reading is null ? "Choose a resource in Settings." : $"{reading.Resource.Name}\n{reading.Observation.Time:T}\n{reading.Observation.Get("Utilization").Reason}");
        }
    }
}
