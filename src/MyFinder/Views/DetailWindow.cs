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
    private readonly TextBlock _heading = Ui.Text("", 24), _current = Ui.Text("", 18), _facts = Ui.Text(""), _quality = Ui.Text("");
    private readonly StackPanel _composition = Ui.Stack();
    private readonly TrendChart _chart = new();
    private string _selected = "cpu", _metricKey = "Utilization", _resourceSignature = "", _metricSignature = "";
    private int _rangeMinutes;
    private bool _updating;
    public bool IsOpen { get; private set; }
    public DetailWindow(AppController owner)
    {
        _owner = owner; _rangeMinutes = owner.Preferences.RangeMinutes;
        Title = "MyFinder - Details";
        var content = Ui.Stack(14); content.Padding = new(22);
        content.Children.Add(_heading);
        var controls = new Grid { ColumnSpacing = 10 };
        controls.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new()); controls.ColumnDefinitions.Add(new());
        controls.Children.Add(_resource); Grid.SetColumn(_metric, 1); controls.Children.Add(_metric); Grid.SetColumn(_range, 2); controls.Children.Add(_range);
        for (int i = 0; i < 3; i++) controls.RowDefinitions.Add(new() { Height = GridLength.Auto });
        controls.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 560;
            for (int i = 0; i < 3; i++)
            {
                controls.ColumnDefinitions[i].Width = narrow && i > 0 ? new(0) : new(i == 0 ? 2 : 1, GridUnitType.Star);
                Grid.SetColumn((FrameworkElement)controls.Children[i], narrow ? 0 : i);
                Grid.SetRow((FrameworkElement)controls.Children[i], narrow ? i : 0);
            }
        };
        content.Children.Add(controls); content.Children.Add(_quality); content.Children.Add(_current); content.Children.Add(_chart); content.Children.Add(_composition);
        content.Children.Add(Ui.Text("Resource facts and current observations", 18)); content.Children.Add(_facts);
        content.Children.Add(Ui.Button("Settings", owner.ShowSettings, "DetailSettings"));
        content.Children.Add(Ui.Button("Close details", Dismiss, "CloseDetails"));
        _root.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = _root;
        AppWindow.Resize(new(820, 740));
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;
        AppWindow.Closing += (_, e) => { e.Cancel = !_owner.IsExiting; if (e.Cancel) Dismiss(); };
        Activated += (_, e) => { if (!_owner.IsExiting && IsOpen && e.WindowActivationState == WindowActivationState.Deactivated) Dismiss(); };
        _root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { Dismiss(); e.Handled = true; } };
        _resource.SelectionChanged += (_, _) =>
        {
            if (_updating || _resource.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
            _selected = id; _metricKey = ""; _metricSignature = ""; _chart.ResetInspection(); Refresh();
        };
        _metric.SelectionChanged += (_, _) => { if (!_updating && _metric.SelectedItem is string key) { _metricKey = key; _chart.ResetInspection(); Refresh(); } };
        _range.SelectionChanged += (_, _) => { if (!_updating && _range.SelectedItem is int minutes) { _rangeMinutes = minutes; _chart.ResetInspection(); Refresh(); } };
        _owner.PreferencesChanged += () => { _owner.ApplyAppearance(this, _root, false); if (IsOpen) Refresh(); };
        Ui.Name(_resource, "Inspect resource without changing monitoring"); Ui.Name(_metric, "Trend metric"); Ui.Name(_range, "History range in minutes");
        _owner.ApplyAppearance(this, _root, false);
    }
    public void Open(string id)
    {
        _selected = id; _metricKey = ""; _metricSignature = ""; _rangeMinutes = _owner.Preferences.RangeMinutes; _chart.ResetInspection();
        IsOpen = true; _owner.PositionDetails(this); Refresh(); AppWindow.Show(); Activate();
    }
    public void Dismiss() { IsOpen = false; AppWindow.Hide(); }
    public void Refresh()
    {
        if (!IsOpen) return;
        var readings = _owner.Readings;
        string signature = string.Join(";", readings.Select(x => x.Resource.Id + x.Resource.Name));
        _updating = true;
        try
        {
            if (_resourceSignature != signature)
            {
                _resourceSignature = signature; _resource.Items.Clear();
                foreach (var reading in readings) _resource.Items.Add(new ComboBoxItem { Content = reading.Resource.Name, Tag = reading.Resource.Id });
            }
            _resource.SelectedItem = _resource.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == _selected);
            var choices = new[] { 1, 5, 15, 30, 60 }.Where(x => x <= _owner.Preferences.RetentionMinutes).ToArray();
            if (!_range.Items.Cast<int>().SequenceEqual(choices)) { _range.Items.Clear(); foreach (int value in choices) _range.Items.Add(value); }
            _rangeMinutes = Math.Min(_rangeMinutes, _owner.Preferences.RetentionMinutes); _range.SelectedItem = _rangeMinutes;
            var current = readings.FirstOrDefault(x => x.Resource.Id == _selected);
            if (current is null) { _heading.Text = _selected; _quality.Text = "Waiting for this resource. Its identity remains selected."; return; }
            _heading.Text = current.Resource.Name;
            var keys = TrendMetrics(current);
            string metricSignature = _selected + string.Join(";", keys);
            if (_metricSignature != metricSignature)
            {
                _metricSignature = metricSignature; _metric.Items.Clear(); foreach (string key in keys) _metric.Items.Add(key);
                if (!keys.Contains(_metricKey)) _metricKey = keys.FirstOrDefault() ?? "Utilization";
            }
            _metric.SelectedItem = _metricKey;
            var metric = current.Observation.Get(_metricKey);
            _quality.Text = $"Current snapshot {current.Observation.Time:T} · {metric.Quality}" + (metric.Reason is null ? "" : $" — {metric.Reason}") +
                (_owner.Session.History.BudgetLimited ? " · History shortened by the memory budget" : "");
            _current.Text = $"{_metricKey}: {ValueFormat.Metric(metric, _owner.Preferences)}";
            var samples = _owner.Session.History.Read(_selected, ObservationClock.Elapsed - _rangeMinutes * 60);
            _chart.Update(samples, [_metricKey], _owner.Preferences, _rangeMinutes, maximum: current.Resource.Kind == ResourceKind.Memory
                ? current.Observation.Get(_metricKey == "Committed" ? "Commit limit" : "Total").Value : null);
            _facts.Text = string.Join("\n", current.Observation.Values.Where(x => !x.Key.StartsWith("Logical ")).Select(x =>
                $"{x.Key}: {ValueFormat.Metric(x.Value, _owner.Preferences)}" + (x.Value.Reason is null ? "" : " — " + x.Value.Reason))) +
                "\n\n" + string.Join("\n", current.Resource.Facts.Select(x => $"{x.Key}: {x.Value}")) + "\n\nIdentity: " + current.Resource.Id;
            Composition(current);
        }
        finally { _updating = false; }
    }
    private static string[] TrendMetrics(Reading reading) => reading.Resource.Kind switch
    {
        ResourceKind.Cpu => reading.Observation.Values.Keys.Where(x => x == "Utilization" || x.StartsWith("Logical ")).ToArray(),
        ResourceKind.Memory => ["In use", "Committed", "Available"],
        _ => []
    };
    private void Composition(Reading reading)
    {
        _composition.Children.Clear();
        if (reading.Resource.Kind != ResourceKind.Memory) return;
        var used = reading.Observation.Get("In use"); var total = reading.Observation.Get("Total"); var free = reading.Observation.Get("Available");
        if (used.Value is not double u || total.Value is not double t || free.Value is not double f || t <= 0 || u < 0 || f < 0 || Math.Abs(u + f - t) > 1) return;
        _composition.Children.Add(Ui.Text($"Physical memory now · In use {ValueFormat.Metric(used, _owner.Preferences)} / Available {ValueFormat.Metric(free, _owner.Preferences)} / Usable {ValueFormat.Metric(total, _owner.Preferences)}"));
        if (_owner.Preferences.Composition == CompositionStyle.Bar)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = t, Value = u, Height = 14 };
            Ui.Name(bar, $"Physical memory in use {u / t:P1}"); _composition.Children.Add(bar);
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
            Ui.Name(canvas, $"In use {u / t:P1}; available {f / t:P1}"); _composition.Children.Add(canvas);
        }
    }
}
