using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyFinder.Core;
using MyFinder.Core.Monitoring;
using Windows.System;

namespace MyFinder.Views;

internal sealed class SettingsWindow : Window
{
    private readonly AppController _owner;
    private readonly Grid _root = new();
    private TextBlock _status = Ui.Text("");
    public bool IsOpen { get; private set; }
    public SettingsWindow(AppController owner)
    {
        _owner = owner; Title = "MyFinder - Settings"; Content = _root; AppWindow.Resize(new(600, 780));
        AppWindow.Closing += (_, e) => { e.Cancel = !_owner.IsExiting; if (e.Cancel) Dismiss(); };
        _root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { Dismiss(); e.Handled = true; } };
        owner.PreferencesChanged += () => { _owner.ApplyAppearance(this, _root, true); RefreshStatus(); };
    }
    public void Open() { Build(); IsOpen = true; _owner.PositionDetails(this); AppWindow.Show(); Activate(); }
    public void Dismiss() { IsOpen = false; AppWindow.Hide(); }
    private void Build()
    {
        var preferences = _owner.Preferences;
        _status = Ui.Text("");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_status, "MonitoringStatus");
        var form = Ui.Stack(16); form.Padding = new(24);
        form.Children.Add(Ui.Text("MyFinder settings", 26)); form.Children.Add(_status);
        form.Children.Add(Ui.Button("Show panel", () => _owner.Update(_owner.Preferences with { Shown = true }), "ShowPanel"));
        form.Children.Add(Ui.Button("Hide panel", () => _owner.Update(_owner.Preferences with { Shown = false }), "HidePanel"));
        Toggle(form, "Retreat during fullscreen on the panel's display", preferences.Retreat, value => _owner.Update(_owner.Preferences with { Retreat = value }));
        Toggle(form, "Lock panel position", preferences.Locked, value => _owner.Update(_owner.Preferences with { Locked = value }));
        Choice(form, "Orientation", Enum.GetValues<PanelOrientation>(), preferences.Orientation, value => _owner.Update(_owner.Preferences with { Orientation = value }));
        Choice(form, "Style", Enum.GetValues<PanelStyle>(), preferences.Style, value => _owner.Update(_owner.Preferences with { Style = value }));
        Choice(form, "Appearance", Enum.GetValues<Appearance>(), preferences.Theme, value => _owner.Update(_owner.Preferences with { Theme = value }));
        Slider(form, "Readable size", .8, 2, .1, preferences.Scale, value => _owner.Update(_owner.Preferences with { Scale = value }));
        Slider(form, "Background opacity (foreground stays opaque)", .25, 1, .05, preferences.BackgroundOpacity, value => _owner.Update(_owner.Preferences with { BackgroundOpacity = value }));
        if (_owner.Displays.Count > 0)
        {
            var displays = _owner.Displays;
            var selected = displays.FirstOrDefault(x => x.Id == preferences.PreferredDisplay) ?? displays.FirstOrDefault(x => x.Primary) ?? displays[0];
            var display = new ComboBox { Header = "Preferred display", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var item in displays) display.Items.Add(new ComboBoxItem { Content = $"{item.Name} ({item.Bounds.Width}×{item.Bounds.Height})", Tag = item.Id });
            display.SelectedItem = display.Items.OfType<ComboBoxItem>().First(x => (string)x.Tag == selected.Id);
            display.SelectionChanged += (_, _) => { if (display.SelectedItem is ComboBoxItem item) _owner.ChoosePlacement((string)item.Tag, Alignment.Top); };
            form.Children.Add(display);
            Choice(form, "Alignment (never auto-hides)", Enum.GetValues<Alignment>(), preferences.Placements.GetValueOrDefault(selected.Id, new(selected.Id)).Alignment,
                alignment => _owner.ChoosePlacement(_owner.Preferences.PreferredDisplay.Length == 0 ? selected.Id : _owner.Preferences.PreferredDisplay, alignment));
        }
        form.Children.Add(Ui.Text("Observation and presentation", 20));
        Choice(form, "Sampling interval (seconds)", new[] { .5, 1d, 2d }, preferences.IntervalSeconds, value => _owner.Update(_owner.Preferences with { IntervalSeconds = value }));
        Choice(form, "Retain recent history (minutes)", new[] { 5, 15, 30, 60 }, preferences.RetentionMinutes, value => _owner.Update(_owner.Preferences with { RetentionMinutes = value }));
        Choice(form, "Default chart range (minutes)", new[] { 1, 5, 15, 30, 60 }, preferences.RangeMinutes, value => _owner.Update(_owner.Preferences with { RangeMinutes = Math.Min(value, _owner.Preferences.RetentionMinutes) }));
        Choice(form, "Memory and capacity composition", Enum.GetValues<CompositionStyle>(), preferences.Composition, value => _owner.Update(_owner.Preferences with { Composition = value }));
        form.Children.Add(Ui.Text("Extending retention cannot recover expired history. Hiding the panel and closing details preserve monitored histories."));
        form.Children.Add(Ui.Text("Monitored resources", 20));
        foreach (string id in new[] { "cpu", "memory" })
            Toggle(form, "Monitor " + id, preferences.Monitored.Contains(id), value =>
            {
                var monitored = _owner.Preferences.Monitored.ToHashSet(); if (value) monitored.Add(id); else monitored.Remove(id);
                _owner.Update(_owner.Preferences with { Monitored = monitored.ToArray() });
            });
        form.Children.Add(Ui.Text("Network adapters", 20));
        form.Children.Add(Ui.Text("Monitor, compact pin and detail inspection are independent. Choose an adapter below when first-run discovery is ambiguous. Addresses stay hidden unless temporarily revealed in details."));
        Toggle(form, "Network rates in bits/s (otherwise bytes/s)", preferences.NetworkBits, value => _owner.Update(_owner.Preferences with { NetworkBits = value }));
        ResourceChoices(form, ResourceKind.Network, "network");
        form.Children.Add(Ui.Button("Refresh available resources", Build, "RefreshResources"));
        form.Children.Add(Ui.Text("Compact metrics and order", 20));
        foreach (string id in new[] { "cpu", "memory", "network" })
        {
            Toggle(form, "Show " + id + " on the compact panel", preferences.MetricOrder.Contains(id), value =>
            {
                var order = _owner.Preferences.MetricOrder.ToList(); if (value && !order.Contains(id)) order.Add(id); if (!value) order.Remove(id);
                _owner.Update(_owner.Preferences with { MetricOrder = order.ToArray() });
            });
            form.Children.Add(Ui.Button("Move " + id + " first", () => _owner.Update(_owner.Preferences with { MetricOrder = new[] { id }.Concat(_owner.Preferences.MetricOrder.Where(x => x != id)).ToArray() })));
        }
        form.Children.Add(Ui.Button("Inspect CPU", () => { Dismiss(); _owner.Inspect("cpu"); }, "InspectCpuFromSettings"));
        form.Children.Add(Ui.Button("Close settings", Dismiss, "CloseSettings"));
        form.Children.Add(Ui.Button("Exit MyFinder", _owner.Exit, "ExitMyFinder"));
        _root.Children.Clear(); _root.Children.Add(new ScrollViewer { Content = form, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _owner.ApplyAppearance(this, _root, true); RefreshStatus();
    }
    public void RefreshStatus()
    {
        var stats = _owner.Session.History.Statistics;
        _status.Text = $"Panel intent: {(_owner.Preferences.Shown ? "shown" : "hidden")}" + (_owner.Retreated ? " · temporarily retreated for fullscreen" : "") +
            $"\n{_owner.AppearanceStatus}\n{stats.Samples:N0} retained observations · {stats.EstimatedBytes / 1048576d:F1} MiB estimated history storage" +
            (_owner.Preferences.Pins.ContainsKey("network") ? "" : "\nNetwork: choose a monitored adapter and compact pin below.") +
            (_owner.Warning is null ? "" : "\n" + _owner.Warning);
    }
    private void ResourceChoices(StackPanel form, ResourceKind kind, string pin)
    {
        var readings = _owner.Readings.Where(x => x.Resource.Kind == kind).OrderBy(x => x.Resource.Name).ToArray();
        if (readings.Length == 0) form.Children.Add(Ui.Text("No resources discovered yet. Refresh to retry; other monitoring continues."));
        foreach (var reading in readings)
        {
            string id = reading.Resource.Id;
            string label = reading.Resource.Name + " · " + reading.Resource.Facts.GetValueOrDefault("Category", kind.ToString());
            Toggle(form, "Monitor " + label, _owner.Preferences.Monitored.Contains(id), value =>
            {
                var monitored = _owner.Preferences.Monitored.ToHashSet(); if (value) monitored.Add(id); else monitored.Remove(id);
                _owner.Update(_owner.Preferences with { Monitored = monitored.ToArray(), NetworkSelectionMade = true });
            });
            form.Children.Add(Ui.Button("Inspect " + label, () => { Dismiss(); _owner.Inspect(id); }, "InspectResource_" + id));
        }
        var choice = new ComboBox { Header = "Compact " + pin + " pin (does not enable monitoring)", HorizontalAlignment = HorizontalAlignment.Stretch };
        choice.Items.Add(new ComboBoxItem { Content = "No pin", Tag = "" });
        foreach (var reading in readings) choice.Items.Add(new ComboBoxItem { Content = reading.Resource.Name, Tag = reading.Resource.Id });
        string selected = _owner.Preferences.Pins.GetValueOrDefault(pin, "");
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == selected);
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem item) return;
            var pins = new Dictionary<string, string>(_owner.Preferences.Pins); pins.Remove(pin);
            if ((string)item.Tag != "") pins[pin] = (string)item.Tag;
            _owner.Update(_owner.Preferences with { Pins = pins, NetworkSelectionMade = true });
        };
        Ui.Name(choice, "Compact " + pin + " pin"); form.Children.Add(choice);
    }
    private static void Choice<T>(StackPanel form, string label, IEnumerable<T> values, T selected, Action<T> update)
    {
        var choice = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values) choice.Items.Add(value!);
        choice.SelectedItem = selected;
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is T value) update(value); };
        Ui.Name(choice, label); form.Children.Add(choice);
    }
    private static void Toggle(StackPanel form, string label, bool value, Action<bool> update)
    {
        var toggle = new CheckBox { Content = label, IsChecked = value };
        toggle.Checked += (_, _) => update(true); toggle.Unchecked += (_, _) => update(false);
        Ui.Name(toggle, label); form.Children.Add(toggle);
    }
    private static void Slider(StackPanel form, string label, double minimum, double maximum, double step, double value, Action<double> update)
    {
        var slider = new Slider { Header = label, Minimum = minimum, Maximum = maximum, StepFrequency = step, Value = value };
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        debounce.Tick += (_, _) => { debounce.Stop(); update(slider.Value); };
        slider.ValueChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        slider.Unloaded += (_, _) => debounce.Stop();
        Ui.Name(slider, label); form.Children.Add(slider);
    }
}
