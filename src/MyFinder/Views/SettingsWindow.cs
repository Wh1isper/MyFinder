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
    private readonly NavigationView _navigation = new()
    {
        IsSettingsVisible = false, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        PaneDisplayMode = NavigationViewPaneDisplayMode.Auto, OpenPaneLength = 204,
        CompactModeThresholdWidth = 640, ExpandedModeThresholdWidth = 900,
        PaneTitle = "MyFinder", AlwaysShowHeader = true
    };
    private readonly ScrollViewer _body = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _status = Ui.Muted("");
    private readonly InfoBar _warning = new() { IsClosable = false, Severity = InfoBarSeverity.Warning };
    private readonly List<Action> _synchronize = [];
    private readonly List<Action> _inspectionState = [];
    private readonly Button _visibility;
    private TextBlock? _diagnostics, _appearancePolicy;
    private string _page = "Appearance";
    public bool IsOpen { get; private set; }

    public SettingsWindow(AppController owner)
    {
        _owner = owner; Title = "MyFinder - Settings"; Content = _root; AppWindow.Resize(new(1000, 820));
        Ui.Id(_navigation, "SettingsNavigation"); Ui.Id(_status, "MonitoringStatus"); Ui.Id(_body, "SettingsBody");
        _root.RowDefinitions.Add(new()); _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var (name, glyph) in new[] { ("Appearance", "\uE790"), ("Placement", "\uE7F4"), ("Resources", "\uE9D9"), ("History", "\uE81C"), ("Status", "\uE946") })
        {
            var item = new NavigationViewItem { Content = name, Tag = name, Icon = Ui.Icon(glyph) };
            Ui.Id(item, "SettingsPage_" + name); _navigation.MenuItems.Add(item);
        }
        _navigation.Content = _body;
        _navigation.SelectionChanged += (_, e) => { if (e.SelectedItem is NavigationViewItem item) { _page = (string)item.Tag; BuildPage(); } };
        _root.Children.Add(_navigation);
        var footer = Ui.Stack(8); footer.Padding = new(16, 10, 16, 12);
        footer.Children.Add(_warning);
        var actions = new Grid { ColumnSpacing = 16 };
        actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _status.VerticalAlignment = VerticalAlignment.Center; actions.Children.Add(_status);
        _visibility = Ui.Button("Hide panel", () => _owner.Update(_owner.Preferences with { Shown = !_owner.Preferences.Shown }), "TogglePanelVisibility");
        Grid.SetColumn(_visibility, 1); actions.Children.Add(_visibility);
        footer.Children.Add(actions); Grid.SetRow(footer, 1); _root.Children.Add(footer);
        AppWindow.Closing += (_, e) => { e.Cancel = !_owner.IsExiting; if (e.Cancel) Dismiss(); };
        _root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { Dismiss(); e.Handled = true; } };
        owner.PreferencesChanged += () =>
        {
            _owner.ApplyAppearance(this, _root, true);
            foreach (var sync in _synchronize.ToArray()) sync();
            RefreshStatus();
        };
    }
    public void Open()
    {
        IsOpen = true;
        if (_navigation.SelectedItem is null) _navigation.SelectedItem = _navigation.MenuItems[0]; else BuildPage();
        _owner.ApplyAppearance(this, _root, true); _owner.PositionDetails(this); AppWindow.Show(); Activate();
    }
    public void SelectPage(string page) => _navigation.SelectedItem = _navigation.MenuItems.OfType<NavigationViewItem>().First(x => (string)x.Tag == page);
    public void Dismiss() { IsOpen = false; AppWindow.Hide(); }
    private void BuildPage()
    {
        _synchronize.Clear(); _inspectionState.Clear(); _diagnostics = null; _appearancePolicy = null;
        var form = Ui.Stack(20); form.Padding = new(24, 12, 24, 24); form.MaxWidth = 900; form.HorizontalAlignment = HorizontalAlignment.Stretch;
        _navigation.Header = _page;
        switch (_page)
        {
            case "Appearance": Appearance(form); break;
            case "Placement": Placement(form); break;
            case "Resources": Resources(form); break;
            case "History": History(form); break;
            case "Status": Status(form); break;
        }
        _body.Content = form; _body.ChangeView(null, 0, null); RefreshStatus();
    }
    private static StackPanel Section(StackPanel page, string title, string description)
    {
        var section = Ui.Stack(12); section.Children.Add(Ui.Heading(title)); section.Children.Add(Ui.Muted(description));
        var content = Ui.Stack(12); section.Children.Add(content); page.Children.Add(section); return content;
    }
    private void Appearance(StackPanel form)
    {
        var layout = Section(form, "Make room for what matters", "Layout, visual style and readable size are independent. Changes apply immediately.");
        Choice(layout, "Orientation", "The same metrics, arranged across or down.", Enum.GetValues<PanelOrientation>(), () => _owner.Preferences.Orientation, value => _owner.Update(_owner.Preferences with { Orientation = value }));
        Choice(layout, "Style", "Balanced labels, a minimal readout, or individual capsules.", Enum.GetValues<PanelStyle>(), () => _owner.Preferences.Style, value => _owner.Update(_owner.Preferences with { Style = value }));
        Choice(layout, "Appearance", "Follow Windows, or choose a light or dark surface.", Enum.GetValues<Core.Appearance>(), () => _owner.Preferences.Theme, value => _owner.Update(_owner.Preferences with { Theme = value }));
        Slider(layout, "Readable size", "Scale the panel without changing the monitored resources.", .8, 2, .1, () => _owner.Preferences.Scale, value => _owner.Update(_owner.Preferences with { Scale = value }));
        Slider(layout, "Background opacity", "Text and controls stay opaque. System policies can require a solid background.", .25, 1, .05, () => _owner.Preferences.BackgroundOpacity, value => _owner.Update(_owner.Preferences with { BackgroundOpacity = value }));
        _appearancePolicy = Ui.Muted(""); Ui.Id(_appearancePolicy, "AppearancePolicy"); layout.Children.Add(_appearancePolicy);
        var metrics = Section(form, "Compact metrics", "Choose the tiles and their order. Monitoring is configured separately in Resources.");
        var orderLabel = Ui.Muted(""); Ui.Id(orderLabel, "CompactMetricOrder"); metrics.Children.Add(orderLabel);
        void SyncOrder() => orderLabel.Text = "Panel order: " + (_owner.Preferences.MetricOrder.Length == 0 ? "No metric tiles" : string.Join(" → ", _owner.Preferences.MetricOrder.Select(MetricName)));
        SyncOrder(); _synchronize.Add(SyncOrder);
        foreach (string id in new[] { "cpu", "memory", "network", "storage" })
        {
            var row = Ui.Stack(8);
            Toggle(row, MetricName(id), () => _owner.Preferences.MetricOrder.Contains(id), value =>
            {
                var order = _owner.Preferences.MetricOrder.ToList(); if (value && !order.Contains(id)) order.Add(id); if (!value) order.Remove(id);
                _owner.Update(_owner.Preferences with { MetricOrder = order.ToArray() });
            }, "Show " + MetricName(id) + " on the compact panel", "ShowMetric_" + id);
            var first = Ui.Button("Move first", () =>
            {
                if (_owner.Preferences.MetricOrder.Contains(id))
                    _owner.Update(_owner.Preferences with { MetricOrder = new[] { id }.Concat(_owner.Preferences.MetricOrder.Where(x => x != id)).ToArray() });
            }, "MoveMetricFirst_" + id);
            Ui.Name(first, "Move " + MetricName(id) + " first");
            void SyncFirst() => first.IsEnabled = _owner.Preferences.MetricOrder.Contains(id) && _owner.Preferences.MetricOrder.FirstOrDefault() != id;
            SyncFirst(); _synchronize.Add(SyncFirst);
            metrics.Children.Add(Ui.Card(Ui.Columns(row, first)));
        }
    }
    private static string MetricName(string id) => id switch { "cpu" => "CPU", "memory" => "Memory", "network" => "Network", "storage" => "Storage", _ => id };
    private void Placement(StackPanel form)
    {
        var section = Section(form, "Where and when", "The tray remains available when the panel is hidden. Edge alignment never enables auto-hide.");
        Toggle(section, "Retreat during fullscreen on the panel's display", () => _owner.Preferences.Retreat, value => _owner.Update(_owner.Preferences with { Retreat = value }));
        Toggle(section, "Lock panel position", () => _owner.Preferences.Locked, value => _owner.Update(_owner.Preferences with { Locked = value }));
        var displays = _owner.Displays;
        if (displays.Count == 0) { section.Children.Add(Ui.Muted("No display is available.")); return; }
        var selected = displays.FirstOrDefault(x => x.Id == _owner.Preferences.PreferredDisplay) ?? displays.FirstOrDefault(x => x.Primary) ?? displays[0];
        var display = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in displays) display.Items.Add(new ComboBoxItem { Content = $"{item.Name} ({item.Bounds.Width}×{item.Bounds.Height})", Tag = item.Id });
        display.SelectedItem = display.Items.OfType<ComboBoxItem>().First(x => (string)x.Tag == selected.Id);
        display.SelectionChanged += (_, _) =>
        {
            if (display.SelectedItem is ComboBoxItem item)
            {
                string id = (string)item.Tag;
                _owner.ChoosePlacement(id, _owner.Preferences.Placements.GetValueOrDefault(id, new(id)).Alignment);
            }
        };
        Row(section, "Preferred display", "A missing display uses a reachable fallback without forgetting this choice.", display);
        Choice(section, "Alignment", "Choose Free to retain a dragged position.", Enum.GetValues<Alignment>(),
            () => _owner.Preferences.Placements.GetValueOrDefault(CurrentDisplay(), new(CurrentDisplay())).Alignment,
            value => _owner.ChoosePlacement(CurrentDisplay(), value));
        string CurrentDisplay() => _owner.Preferences.PreferredDisplay.Length == 0 ? selected.Id : _owner.Preferences.PreferredDisplay;
    }
    private void Resources(StackPanel form)
    {
        var refresh = Ui.Button("Refresh list", BuildPage, "RefreshResources"); refresh.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTipService.SetToolTip(refresh, "Show the latest discovered resources"); form.Children.Add(refresh);
        var inspectionState = Ui.Muted(""); form.Children.Add(inspectionState);
        void SyncInspection() { inspectionState.Text = _owner.Retreated ? "Inspection resumes after fullscreen. Monitoring continues." : "Show the panel to inspect resources. Monitoring continues while hidden."; inspectionState.Visibility = !_owner.Preferences.Shown || _owner.Retreated ? Visibility.Visible : Visibility.Collapsed; }
        SyncInspection(); _inspectionState.Add(SyncInspection);
        var system = Section(form, "System", "Monitoring runs independently of open windows.");
        foreach (string id in new[] { "cpu", "memory" })
        {
            var card = Ui.Stack(8); card.Children.Add(Ui.Text(MetricName(id), 16));
            Toggle(card, "Monitor", () => _owner.Preferences.Monitored.Contains(id), value => Monitor(id, value), "Monitor " + MetricName(id));
            card.Children.Add(InspectButton("Inspect", id, "InspectResource_" + id, "Inspect " + MetricName(id))); system.Children.Add(Ui.Card(card));
        }
        var network = Section(form, "Network adapters", "Monitor, compact pin and inspection are independent. Addresses remain hidden until explicitly revealed in details.");
        Toggle(network, "Network rates in bits/s (otherwise bytes/s)", () => _owner.Preferences.NetworkBits, value => _owner.Update(_owner.Preferences with { NetworkBits = value }));
        ResourceChoices(network, "network", ResourceKind.Network);
        var storage = Section(form, "Storage", "Volumes show capacity; devices show shared activity. Compare up to four explicitly chosen devices.");
        Toggle(storage, "Storage sizes and rates in binary units (KiB, MiB, GiB)", () => _owner.Preferences.StorageBinary, value => _owner.Update(_owner.Preferences with { StorageBinary = value }));
        storage.Children.Add(Ui.Columns(InspectButton("Overview", "storage:overview", "OpenStorageOverview", "Open storage overview"),
            InspectButton("Compare devices", "storage:comparison", "OpenStorageComparison", "Open storage comparison")));
        ResourceChoices(storage, "storage", ResourceKind.Volume, ResourceKind.Device);
    }
    private Button InspectButton(string label, string resource, string id, string name)
    {
        var button = Ui.Button(label, () => { if (!_owner.Preferences.Shown || _owner.Retreated) return; Dismiss(); _owner.Inspect(resource); }, id);
        Ui.Name(button, name); button.HorizontalAlignment = HorizontalAlignment.Left;
        void Sync() => button.IsEnabled = _owner.Preferences.Shown && !_owner.Retreated;
        Sync(); _inspectionState.Add(Sync); return button;
    }
    private void Monitor(string id, bool value, string? pin = null)
    {
        var monitored = _owner.Preferences.Monitored.ToHashSet(); if (value) monitored.Add(id); else monitored.Remove(id);
        _owner.Update(_owner.Preferences with { Monitored = monitored.ToArray(), NetworkSelectionMade = pin == "network" || _owner.Preferences.NetworkSelectionMade, StorageSelectionMade = pin == "storage" || _owner.Preferences.StorageSelectionMade });
    }
    private void ResourceChoices(StackPanel form, string pin, params ResourceKind[] kinds)
    {
        var readings = _owner.Readings.Where(x => kinds.Contains(x.Resource.Kind)).OrderBy(x => x.Resource.Kind).ThenBy(x => x.Resource.Name).ToArray();
        var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        choice.Items.Add(new ComboBoxItem { Content = "No pin", Tag = "" });
        foreach (var reading in readings) choice.Items.Add(new ComboBoxItem { Content = reading.Resource.Name, Tag = reading.Resource.Id });
        string pinned = _owner.Preferences.Pins.GetValueOrDefault(pin, "");
        if (pinned.Length > 0 && !readings.Any(x => x.Resource.Id == pinned))
            choice.Items.Add(new ComboBoxItem { Content = pinned + " (unavailable)", Tag = pinned });
        bool syncing = false;
        void Sync() { syncing = true; choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == _owner.Preferences.Pins.GetValueOrDefault(pin, "")); syncing = false; }
        Sync(); _synchronize.Add(Sync);
        choice.SelectionChanged += (_, _) =>
        {
            if (syncing || choice.SelectedItem is not ComboBoxItem item) return;
            var pins = new Dictionary<string, string>(_owner.Preferences.Pins); pins.Remove(pin);
            if ((string)item.Tag != "") pins[pin] = (string)item.Tag;
            _owner.Update(_owner.Preferences with { Pins = pins, NetworkSelectionMade = pin == "network" || _owner.Preferences.NetworkSelectionMade, StorageSelectionMade = pin == "storage" || _owner.Preferences.StorageSelectionMade });
        };
        Ui.Name(choice, "Compact " + pin + " pin"); Row(form, "Compact " + pin + " pin", "Pinning does not enable monitoring.", choice);
        if (readings.Length == 0) form.Children.Add(Ui.Muted("No resources discovered yet. Refresh to retry; other monitoring continues."));
        foreach (var reading in readings)
        {
            string id = reading.Resource.Id;
            string label = reading.Resource.Name + " · " + reading.Resource.Facts.GetValueOrDefault("Category", reading.Resource.Kind.ToString());
            var card = Ui.Stack(8);
            card.Children.Add(Ui.Text(reading.Resource.Name, 16)); card.Children.Add(Ui.Muted(reading.Resource.Facts.GetValueOrDefault("Category", reading.Resource.Kind.ToString())));
            Toggle(card, "Monitor", () => _owner.Preferences.Monitored.Contains(id), value => Monitor(id, value, pin), "Monitor " + label);
            if (reading.Resource.Kind == ResourceKind.Device)
            {
                var notice = Ui.Muted("");
                Toggle(card, "Include in comparison", () => _owner.Preferences.StorageComparison.Contains(id), value =>
                {
                    var selection = _owner.Preferences.StorageComparison.ToHashSet();
                    if (value && selection.Count >= 4 && !selection.Contains(id)) { notice.Text = "Choose at most four devices. Remove one before adding another."; foreach (var sync in _synchronize) sync(); return; }
                    if (value) selection.Add(id); else selection.Remove(id);
                    notice.Text = ""; _owner.Update(_owner.Preferences with { StorageComparison = selection.ToArray() });
                }, "Compare " + label);
                card.Children.Add(notice);
            }
            card.Children.Add(InspectButton("Inspect", id, "InspectResource_" + id, "Inspect " + label));
            form.Children.Add(Ui.Card(card));
        }
    }
    private void History(StackPanel form)
    {
        var section = Section(form, "Recent observations", "Hiding the panel and closing details preserve monitored histories. Extending retention cannot recover expired observations.");
        Choice(section, "Sampling interval (seconds)", "CPU, memory, network and device activity.", new[] { .5, 1d, 2d }, () => _owner.Preferences.IntervalSeconds, value => _owner.Update(_owner.Preferences with { IntervalSeconds = value }));
        Choice(section, "Volume capacity interval (seconds)", "Capacity changes more slowly than device activity.", new[] { 10, 30, 60 }, () => _owner.Preferences.CapacitySeconds, value => _owner.Update(_owner.Preferences with { CapacitySeconds = value }));
        Choice(section, "Retain recent history (minutes)", "Shared storage remains bounded by time and memory.", new[] { 5, 15, 30, 60 }, () => _owner.Preferences.RetentionMinutes, value => _owner.Update(_owner.Preferences with { RetentionMinutes = value }));
        Choice(section, "Default chart range (minutes)", "Limited to the retained window; does not change collection cadence.", new[] { 1, 5, 15, 30, 60 }, () => _owner.Preferences.RangeMinutes, value => _owner.Update(_owner.Preferences with { RangeMinutes = Math.Min(value, _owner.Preferences.RetentionMinutes) }));
        Choice(section, "Memory and capacity composition", "Only coherent, non-overlapping parts form a whole.", Enum.GetValues<CompositionStyle>(), () => _owner.Preferences.Composition, value => _owner.Update(_owner.Preferences with { Composition = value }));
    }
    private void Status(StackPanel form)
    {
        var section = Section(form, "Monitoring status", "Current collection and appearance state. Missing readings remain unavailable, not zero.");
        _diagnostics = Ui.Text(""); Ui.Id(_diagnostics, "MonitoringDiagnostics"); section.Children.Add(Ui.Card(_diagnostics));
        section.Children.Add(Ui.Muted("Current runtime baseline: Windows 11 build 22000 or newer. Hardware and OS qualification boundaries are documented in MONITORING.md."));
        var application = Section(form, "Application", "Closing this window keeps monitoring active. Exit stops collection and removes the tray icon.");
        var exit = Ui.Button("Exit MyFinder", _owner.Exit, "ExitMyFinder"); exit.HorizontalAlignment = HorizontalAlignment.Left; application.Children.Add(exit);
    }
    public void RefreshStatus()
    {
        foreach (var sync in _inspectionState) sync();
        var stats = _owner.Session.History.Statistics;
        _status.Text = !_owner.Preferences.Shown ? "Panel hidden · Monitoring continues" : _owner.Retreated ? "Hidden for fullscreen · Monitoring continues" : "Panel visible";
        string visibilityAction = _owner.Preferences.Shown ? "Hide panel" : "Show panel";
        _visibility.Content = visibilityAction; Ui.Name(_visibility, visibilityAction);
        if (_appearancePolicy is not null)
        {
            _appearancePolicy.Text = _owner.AppearanceStatus;
            _appearancePolicy.Visibility = _owner.Preferences.BackgroundOpacity < 1 && _owner.AppearanceStatus.StartsWith("Solid background:") ? Visibility.Visible : Visibility.Collapsed;
        }
        _warning.IsOpen = _owner.Warning is not null; _warning.Message = _owner.Warning ?? "";
        if (_diagnostics is not null) _diagnostics.Text = $"{stats.Samples:N0} retained observations\n{stats.EstimatedBytes / 1048576d:F1} MiB estimated history storage\n{_owner.Session.StorageStatus} · {_owner.Session.StoragePending} pending operations\n\n{_owner.AppearanceStatus}";
    }
    private static void Row(StackPanel form, string label, string description, FrameworkElement control)
    {
        var text = Ui.Stack(4); text.Children.Add(Ui.Text(label)); text.Children.Add(Ui.Muted(description));
        control.VerticalAlignment = VerticalAlignment.Center;
        Ui.Name(control, label); form.Children.Add(Ui.Card(Ui.Columns(text, control)));
    }
    private void Choice<T>(StackPanel form, string label, string description, IEnumerable<T> values, Func<T> selected, Action<T> update)
    {
        var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values) choice.Items.Add(new ComboBoxItem { Content = value!.ToString(), Tag = value });
        bool syncing = false;
        void Sync()
        {
            syncing = true;
            choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag is T value && EqualityComparer<T>.Default.Equals(value, selected()));
            syncing = false;
        }
        Sync(); _synchronize.Add(Sync);
        choice.SelectionChanged += (_, _) => { if (!syncing && choice.SelectedItem is ComboBoxItem { Tag: T value }) { update(value); Sync(); } };
        Row(form, label, description, choice);
    }
    private void Toggle(StackPanel form, string label, Func<bool> value, Action<bool> update, string? accessibleName = null, string? id = null)
    {
        var toggle = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
        bool syncing = false;
        void Sync() { syncing = true; toggle.IsChecked = value(); syncing = false; }
        Sync(); _synchronize.Add(Sync);
        toggle.Checked += (_, _) => { if (!syncing) update(true); }; toggle.Unchecked += (_, _) => { if (!syncing) update(false); };
        Ui.Name(toggle, accessibleName ?? label); if (id is not null) Ui.Id(toggle, id); form.Children.Add(toggle);
    }
    private void Slider(StackPanel form, string label, string description, double minimum, double maximum, double step, Func<double> value, Action<double> update)
    {
        var slider = new Slider { Minimum = minimum, Maximum = maximum, StepFrequency = step, Value = value() };
        var number = Ui.Muted($"{value():P0}"); var content = Ui.Stack(0); content.Children.Add(number); content.Children.Add(slider);
        bool pending = false, syncing = false;
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        void Save() { debounce.Stop(); if (!pending) return; pending = false; update(slider.Value); }
        debounce.Tick += (_, _) => Save();
        slider.ValueChanged += (_, _) => { number.Text = $"{slider.Value:P0}"; if (!syncing) { pending = true; debounce.Stop(); debounce.Start(); } };
        slider.Unloaded += (_, _) => Save();
        _synchronize.Add(() => { if (!pending) { syncing = true; slider.Value = value(); syncing = false; } });
        Ui.Name(slider, label); Row(form, label, description, content);
    }
}
