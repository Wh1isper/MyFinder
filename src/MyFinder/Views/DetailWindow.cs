using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MyFinder.Core;
using MyFinder.Core.Monitoring;
using Windows.System;

namespace MyFinder.Views;

internal sealed class DetailWindow : Window
{
    private readonly AppController _owner;
    private readonly Grid _root = new();
    private readonly ComboBox _resource = new() { Header = "Inspect resource", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _metric = new() { Header = "Trend", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _range = new() { Header = "Recent range", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _heading = Ui.Text("", 22), _quality = Ui.Muted("");
    private readonly ReadingSummary _summary = new();
    private readonly FactTable _observations = new(), _hardware = new();
    private readonly ListView _rail = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid _body = new() { ColumnSpacing = 16 };
    private readonly StackPanel _single = Ui.Stack(16);
    private readonly Border _compositionCard;
    private readonly Button _manageResources;
    private readonly TextBlock _notice = Ui.Muted("");
    private readonly StackPanel _composition = Ui.Stack();
    private readonly TrendChart _chart = new();
    private readonly StorageView _storageView;
    private readonly TextBlock _addresses = Ui.Text("");
    private readonly TextBlock _privacyStatus = Ui.Muted("Addresses are hidden.");
    private readonly StackPanel _privacyControls = Ui.Stack(8);
    private readonly Button _reveal = Ui.Button("Reveal addresses for 30 seconds", () => { }, "RevealAddresses");
    private readonly DispatcherTimer _privacy = new() { Interval = TimeSpan.FromSeconds(30) };
    private Task<string>? _addressWork;
    private int _revealGeneration;
    private string _selected = "cpu", _metricKey = "Utilization", _resourceSignature = "", _metricSignature = "";
    private int _rangeMinutes;
    private bool _updating, _revealing;
    public bool IsOpen { get; private set; }
    public DetailWindow(AppController owner)
    {
        _owner = owner; _rangeMinutes = owner.Preferences.RangeMinutes;
        _storageView = new(owner.Inspect, owner.ShowResourceSettings);
        _manageResources = Ui.Button("Manage resources", owner.ShowResourceSettings, "ManageResources");
        _manageResources.HorizontalAlignment = HorizontalAlignment.Left;
        Title = "MyFinder - Details";
        _root.Padding = new(16, 10, 16, 16); _root.RowSpacing = 8;
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto }); _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto }); _root.RowDefinitions.Add(new());
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _heading.VerticalAlignment = VerticalAlignment.Center; Ui.Id(_heading, "DetailHeading");
        _heading.TextWrapping = TextWrapping.NoWrap; _heading.TextTrimming = TextTrimming.CharacterEllipsis;
        header.Children.Add(_heading);
        var settings = Ui.IconButton("Settings", "\uE713", owner.ShowSettings, "DetailSettings"); Grid.SetColumn(settings, 1); header.Children.Add(settings);
        _root.Children.Add(header); Grid.SetRow(_quality, 1); _root.Children.Add(_quality);
        _body.ColumnDefinitions.Add(new() { Width = new(210) }); _body.ColumnDefinitions.Add(new());
        Ui.Id(_rail, "ResourceRail"); Ui.Name(_rail, "Inspect a resource; monitoring and compact pins stay unchanged");
        _body.Children.Add(_rail); Grid.SetColumn(_scroll, 1); _body.Children.Add(_scroll); Grid.SetRow(_body, 3); _root.Children.Add(_body);
        var toolbar = Ui.Columns(_resource, _metric, _range); Grid.SetRow(toolbar, 2); _root.Children.Add(toolbar);
        Ui.Id(toolbar, "DetailToolbar"); Ui.Id(_resource, "DetailResource"); Ui.Id(_metric, "DetailMetric"); Ui.Id(_range, "DetailRange");
        Ui.Id(_quality, "DetailQuality"); Ui.Id(_scroll, "DetailReadings");
        var content = Ui.Stack(12); content.Padding = new(0, 0, 8, 12);
        content.Children.Add(_notice); content.Children.Add(_manageResources); content.Children.Add(_storageView);
        _single.Children.Add(_summary);
        var trend = Ui.Stack(12); trend.Children.Add(Ui.Heading("Recent history")); trend.Children.Add(_chart); _single.Children.Add(Ui.Card(trend));
        _compositionCard = Ui.Card(_composition); _single.Children.Add(_compositionCard);
        var observations = Ui.Stack(14); observations.Children.Add(Ui.Heading("Current observations")); observations.Children.Add(_observations);
        var hardware = Ui.Stack(14); hardware.Children.Add(Ui.Heading("Resource facts")); hardware.Children.Add(_hardware);
        _single.Children.Add(Ui.Columns(Ui.Card(observations), Ui.Card(hardware)));
        _reveal.HorizontalAlignment = HorizontalAlignment.Left;
        Ui.Id(_privacyStatus, "AddressPrivacyStatus"); Ui.Id(_addresses, "RevealedAddresses");
        _privacyControls.Children.Add(Ui.Heading("Network addresses")); _privacyControls.Children.Add(_privacyStatus);
        _privacyControls.Children.Add(_reveal); _privacyControls.Children.Add(_addresses);
        _single.Children.Add(_privacyControls); content.Children.Add(_single);
        _scroll.Content = content;
        _root.SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 940;
            _body.ColumnDefinitions[0].Width = new(wide ? 210 : 0);
            _rail.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            _resource.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        };
        _rail.SelectionChanged += (_, _) =>
        {
            if (!_updating && _rail.SelectedItem is ListViewItem item && item.Tag is string id) SelectResource(id);
        };
        _reveal.Click += async (_, _) =>
        {
            if (_revealing || _addresses.Text.Length > 0) { HideAddresses(); return; }
            if (_addressWork is { IsCompleted: false }) { _privacyStatus.Text = "The previous address query is finishing. Try again shortly."; return; }
            int generation = ++_revealGeneration; string id = _selected;
            _revealing = true; SetRevealLabel("Cancel address reveal"); _privacyStatus.Text = "Loading addresses…";
            _addressWork = Task.Run(() =>
            {
                try { return NetworkInterfaces.RevealAddresses(id); }
                catch (System.Net.NetworkInformation.NetworkInformationException) { return "Address query unavailable."; }
            });
            string text = await _addressWork;
            if (!IsOpen || generation != _revealGeneration || id != _selected) return;
            _addresses.Text = text; _revealing = false; SetRevealLabel("Hide addresses");
            _privacyStatus.Text = "Visible for 30 seconds, or until you leave this resource."; _privacy.Start(); _addressWork = null;
        };
        _privacy.Tick += (_, _) => HideAddresses();
        Content = _root;
        double dpi = Desktop.NativeDesktop.Dpi(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppWindow.Resize(new((int)(760 * dpi), (int)(640 * dpi)));
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;
        AppWindow.Closing += (_, e) => { e.Cancel = !_owner.IsExiting; if (e.Cancel) Dismiss(); };
        Activated += (_, e) => { if (!_owner.IsExiting && IsOpen && e.WindowActivationState == WindowActivationState.Deactivated) Dismiss(); };
        _root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { Dismiss(); e.Handled = true; } };
        _resource.SelectionChanged += (_, _) =>
        {
            if (_updating || _resource.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
            SelectResource(id);
        };
        _metric.SelectionChanged += (_, _) => { if (!_updating && _metric.SelectedItem is string key) { _metricKey = key; _chart.ResetInspection(); Refresh(); } };
        _range.SelectionChanged += (_, _) => { if (!_updating && _range.SelectedItem is int minutes) { _rangeMinutes = minutes; _chart.ResetInspection(); Refresh(); } };
        _owner.PreferencesChanged += () => { _owner.ApplyAppearance(this, _root, false); if (IsOpen) Refresh(); };
        Ui.Name(_resource, "Inspect resource without changing monitoring"); Ui.Name(_metric, "Trend metric"); Ui.Name(_range, "History range in minutes");
        _owner.ApplyAppearance(this, _root, false);
    }
    public void Open(string id)
    {
        HideAddresses();
        _selected = id; _metricKey = ""; _metricSignature = ""; _rangeMinutes = _owner.Preferences.RangeMinutes; _chart.ResetInspection();
        IsOpen = true; _owner.PositionDetails(this); Refresh(); AppWindow.Show(); Activate();
    }
    private void SelectResource(string id)
    {
        HideAddresses(); _selected = id; _metricKey = ""; _metricSignature = "";
        _chart.ResetInspection(); _scroll.ChangeView(null, 0, null); Refresh();
    }
    private void SetRevealLabel(string label) { _reveal.Content = label; Ui.Name(_reveal, label); }
    private void HideAddresses()
    {
        _revealGeneration++; _privacy.Stop(); _revealing = false; _addresses.Text = "";
        SetRevealLabel("Reveal addresses for 30 seconds"); _privacyStatus.Text = "Addresses are hidden.";
        if (_addressWork?.IsCompleted == true) _addressWork = null;
    }
    public void Dismiss() { HideAddresses(); IsOpen = false; AppWindow.Hide(); }
    public void Refresh()
    {
        if (!IsOpen) return;
        var readings = _owner.Readings;
        string missingSelection = _selected is "storage:overview" or "storage:comparison" || readings.Any(x => x.Resource.Id == _selected) ? "" : _selected;
        string signature = missingSelection + ":" + string.Join(";", readings.Select(x => x.Resource.Id + x.Resource.Name)) + ":" + string.Join(";", _owner.Preferences.Monitored);
        _updating = true;
        try
        {
            if (_resourceSignature != signature)
            {
                _resourceSignature = signature; _resource.Items.Clear();
                foreach (var reading in readings.OrderBy(x => x.Resource.Kind is ResourceKind.Cpu or ResourceKind.Memory ? 0 : _owner.Preferences.Monitored.Contains(x.Resource.Id) ? 1 : 2).ThenBy(x => x.Resource.Kind).ThenBy(x => x.Resource.Name))
                    _resource.Items.Add(new ComboBoxItem { Content = reading.Resource.Name, Tag = reading.Resource.Id });
                _resource.Items.Add(new ComboBoxItem { Content = "Storage overview", Tag = "storage:overview" });
                _resource.Items.Add(new ComboBoxItem { Content = "Storage comparison", Tag = "storage:comparison" });
                if (!_resource.Items.OfType<ComboBoxItem>().Any(x => (string)x.Tag == _selected))
                    _resource.Items.Add(new ComboBoxItem { Content = _selected + " (unavailable)", Tag = _selected });
                _rail.Items.Clear();
                foreach (var choice in _resource.Items.OfType<ComboBoxItem>())
                {
                    var label = Ui.Text((string)choice.Content); label.IsTextSelectionEnabled = false;
                    var item = new ListViewItem { Content = label, Tag = choice.Tag, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(12) };
                    Ui.Id(item, "InspectRail_" + (string)choice.Tag); _rail.Items.Add(item);
                }
            }
            _resource.SelectedItem = _resource.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == _selected);
            _rail.SelectedItem = _rail.Items.OfType<ListViewItem>().FirstOrDefault(x => (string)x.Tag == _selected);
            var choices = new[] { 1, 5, 15, 30, 60 }.Where(x => x <= _owner.Preferences.RetentionMinutes).ToArray();
            if (!_range.Items.Cast<int>().SequenceEqual(choices)) { _range.Items.Clear(); foreach (int value in choices) _range.Items.Add(value); }
            _rangeMinutes = Math.Min(_rangeMinutes, _owner.Preferences.RetentionMinutes); _range.SelectedItem = _rangeMinutes;
            bool multiple = _selected is "storage:overview" or "storage:comparison";
            _storageView.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
            _single.Visibility = multiple ? Visibility.Collapsed : Visibility.Visible;
            _metric.Visibility = Visibility.Visible; _range.Visibility = Visibility.Visible;
            _notice.Visibility = _manageResources.Visibility = Visibility.Collapsed;
            if (multiple)
            {
                bool compare = _selected == "storage:comparison";
                _heading.Text = compare ? "Storage comparison" : "Storage overview"; ToolTipService.SetToolTip(_heading, _heading.Text);
                _privacyControls.Visibility = Visibility.Collapsed;
                _quality.Text = compare ? "Each device retains its own readings and quality." : "Current capacity and activity · Monitored resources only";
                _range.Visibility = compare && _owner.Preferences.StorageComparison.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                string[] comparisonKeys = ["Transfers", "Activity", "Response time"];
                if (_metricSignature != _selected)
                {
                    _metricSignature = _selected; _metric.Items.Clear(); foreach (string key in comparisonKeys) _metric.Items.Add(key);
                    if (!comparisonKeys.Contains(_metricKey)) _metricKey = "Transfers";
                }
                _metric.SelectedItem = _metricKey; _metric.Visibility = compare && _owner.Preferences.StorageComparison.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                _storageView.Update(readings, _owner.Preferences, _owner.Session.History, _rangeMinutes, compare, _metricKey);
                return;
            }
            var current = readings.FirstOrDefault(x => x.Resource.Id == _selected);
            if (current is null)
            {
                _heading.Text = _selected; ToolTipService.SetToolTip(_heading, _selected);
                _quality.Text = "Resource unavailable · Selection preserved";
                _notice.Text = "Waiting for this resource to become available. You can check its monitoring selection in Resources.";
                _notice.Visibility = _manageResources.Visibility = Visibility.Visible;
                _metric.Visibility = _range.Visibility = _single.Visibility = Visibility.Collapsed; return;
            }
            _heading.Text = current.Resource.Name; ToolTipService.SetToolTip(_heading, current.Resource.Name);
            bool network = current.Resource.Kind == ResourceKind.Network;
            bool storage = current.Resource.Kind is ResourceKind.Volume or ResourceKind.Device;
            _privacyControls.Visibility = network ? Visibility.Visible : Visibility.Collapsed;
            var keys = TrendMetrics(current);
            string metricSignature = _selected + string.Join(";", keys);
            if (_metricSignature != metricSignature)
            {
                _metricSignature = metricSignature; _metric.Items.Clear(); foreach (string key in keys) _metric.Items.Add(key);
                if (!keys.Contains(_metricKey)) _metricKey = keys.FirstOrDefault() ?? "Utilization";
            }
            _metric.SelectedItem = _metricKey; _metric.Visibility = keys.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            string[] series = _metricKey == "Traffic" ? ["Receive", "Send"] : _metricKey == "Transfers" ? ["Read", "Write"] : [_metricKey];
            var metric = current.Observation.Get(series[0]);
            _quality.Text = $"Current snapshot {current.Observation.Time:T} · {Ui.QualityLabel(metric.Quality)}";
            _notice.Text = string.Join("\n", new[] { metric.Reason, _owner.Session.History.BudgetLimited ? "History shortened by the memory budget." : null }.Where(x => !string.IsNullOrEmpty(x)));
            _notice.Visibility = _notice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _manageResources.Visibility = metric.Quality == Quality.NotMonitored ? Visibility.Visible : Visibility.Collapsed;
            _summary.Update(current, _owner.Preferences);
            var samples = _owner.Session.History.Read(_selected, ObservationClock.Elapsed - _rangeMinutes * 60);
            _chart.Update(samples, series, _owner.Preferences, _rangeMinutes, storage,
                maximum: current.Resource.Kind == ResourceKind.Memory ? current.Observation.Get(_metricKey == "Committed" ? "Commit limit" : "Total").Value
                    : current.Resource.Kind == ResourceKind.Volume && _metricKey != "Utilization" ? current.Observation.Get("Total").Value : null,
                maximumGap: current.Resource.Kind == ResourceKind.Volume ? _owner.Preferences.CapacitySeconds * 3 : null);
            _observations.Update(current.Observation.Values.Where(x => !x.Key.StartsWith("Logical ")).Select(x =>
                new KeyValuePair<string, string>(x.Key, (network && x.Value.Unit == Unit.Bytes && x.Value.Value is double bytes ? ValueFormat.Size(bytes, false) : ValueFormat.Metric(x.Value, _owner.Preferences, storage)) + (x.Value.Reason is null ? "" : " — " + x.Value.Reason))));
            _hardware.Update(current.Resource.Facts.Append(new KeyValuePair<string, string>("Identity", current.Resource.Id)));
            Composition(current);
        }
        finally { _updating = false; }
    }
    private static string[] TrendMetrics(Reading reading) => reading.Resource.Kind switch
    {
        ResourceKind.Cpu => reading.Observation.Values.Keys.Where(x => x == "Utilization" || x.StartsWith("Logical ")).ToArray(),
        ResourceKind.Memory => ["In use", "Committed", "Available"],
        ResourceKind.Network => ["Traffic"],
        ResourceKind.Volume => ["Used / reserved", "Free", "Utilization"],
        ResourceKind.Device => ["Transfers", "Activity", "Response time"],
        _ => []
    };
    private void Composition(Reading reading)
    {
        _composition.Children.Clear();
        _compositionCard.Visibility = Visibility.Collapsed;
        if (reading.Resource.Kind is not (ResourceKind.Memory or ResourceKind.Volume)) return;
        bool storage = reading.Resource.Kind == ResourceKind.Volume;
        string usedKey = storage ? "Used / reserved" : "In use", freeKey = storage ? "Free" : "Available";
        var used = reading.Observation.Get(usedKey); var total = reading.Observation.Get("Total"); var free = reading.Observation.Get(freeKey);
        if (used.Value is not double u || total.Value is not double t || free.Value is not double f || t <= 0 || u < 0 || f < 0 || Math.Abs(u + f - t) > 1) return;
        _compositionCard.Visibility = Visibility.Visible;
        _composition.Children.Add(Ui.Heading(storage ? "Volume capacity" : "Physical memory"));
        _composition.Children.Add(Ui.Text($"{(storage ? "Volume capacity" : "Physical memory")} · {usedKey} {ValueFormat.Metric(used, _owner.Preferences, storage)} / {freeKey} {ValueFormat.Metric(free, _owner.Preferences, storage)} / Total {ValueFormat.Metric(total, _owner.Preferences, storage)}"));
        if (_owner.Preferences.Composition == CompositionStyle.Bar)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = t, Value = u, Height = 14 };
            Ui.Name(bar, $"{usedKey} {u / t:P1}"); _composition.Children.Add(bar);
        }
        else
        {
            var canvas = new Canvas { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left };
            var background = new Ellipse { Width = 76, Height = 76, StrokeThickness = 12, Stroke = Ui.Brush(Ui.Secondary) };
            Canvas.SetLeft(background, 10); Canvas.SetTop(background, 10); canvas.Children.Add(background);
            if (u > 0)
            {
                double fraction = Math.Min(.999999, u / t), angle = 2 * Math.PI * fraction;
                var figure = new PathFigure { StartPoint = new(48, 10), IsClosed = false };
                figure.Segments.Add(new ArcSegment { Point = new(48 + 38 * Math.Sin(angle), 48 - 38 * Math.Cos(angle)), Size = new(38, 38), IsLargeArc = fraction > .5, SweepDirection = SweepDirection.Clockwise });
                var geometry = new PathGeometry(); geometry.Figures.Add(figure);
                canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = Ui.Brush(Ui.Accent), StrokeThickness = 12 });
            }
            Ui.Name(canvas, $"{usedKey} {u / t:P1}; {freeKey} {f / t:P1}"); _composition.Children.Add(canvas);
        }
    }
}
