using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MyFinder.Core;
using MyFinder.Core.Monitoring;
using Windows.Foundation;
using Windows.System;

namespace MyFinder.Views;

internal sealed class TrendChart : UserControl
{
    private readonly Canvas _canvas = new() { Height = 190, Background = Ui.Brush(Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
    private readonly TextBlock _legend = Ui.Muted("");
    private readonly Line _cursor = new() { StrokeThickness = 1, StrokeDashArray = [2, 3], IsHitTestVisible = false };
    private readonly TextBlock _inspection = Ui.Text("");
    private readonly Button _live;
    private double? _hover;
    private Observation[] _samples = [];
    private string[] _metrics = [];
    private Preferences _preferences = new();
    private bool _storage;
    private double _end, _duration, _gap;
    private double? _maximum;
    private double? _selected;
    public TrendChart()
    {
        IsTabStop = true; Ui.Id(this, "HistoryChart");
        _inspection.MinHeight = 40; _inspection.FontSize = 12; Ui.Id(_inspection, "HistoryInspection");
        _live = Ui.Button("Back to live", ResetInspection, "BackToLive");
        _live.HorizontalAlignment = HorizontalAlignment.Left; _live.Visibility = Visibility.Collapsed;
        ActualThemeChanged += (_, _) => Draw();
        Ui.Name(this, "Recent history. Left and Right inspect samples; End returns to live.");
        var stack = Ui.Stack(6);
        stack.Children.Add(_legend); stack.Children.Add(_canvas); stack.Children.Add(_inspection); stack.Children.Add(_live); Content = stack;
        _canvas.SizeChanged += (_, _) => Draw();
        _canvas.PointerMoved += (_, e) => { if (_selected is null) { _hover = PointerSample(e); InspectSelected(); } };
        _canvas.PointerExited += (_, _) => { _hover = null; InspectSelected(); };
        _canvas.PointerPressed += (_, e) => { _selected = PointerSample(e); _hover = null; Focus(FocusState.Pointer); InspectSelected(); e.Handled = true; };
        KeyDown += OnKey;
    }
    public void ResetInspection() { _selected = _hover = null; InspectSelected(); }
    public void Update(Observation[] samples, string[] metrics, Preferences preferences, int rangeMinutes, bool storage = false, double? maximum = null, double? maximumGap = null)
    {
        if (!_metrics.SequenceEqual(metrics) || _duration != rangeMinutes * 60) _selected = _hover = null;
        _samples = samples; _metrics = metrics; _preferences = preferences; _storage = storage; _maximum = maximum;
        _end = ObservationClock.Elapsed; _duration = rangeMinutes * 60; _gap = maximumGap ?? Math.Max(5, preferences.IntervalSeconds * 3);
        if (_selected is double selected && !_samples.Any(x => x.Elapsed == selected)) _selected = null;
        Draw();
    }
    private void Draw()
    {
        _canvas.Children.Clear();
        double width = Math.Max(20, _canvas.ActualWidth - 80), height = 148;
        double start = _end - _duration;
        double maximum = _samples.SelectMany(x => _metrics.Select(m => x.Get(m).Value ?? 0)).DefaultIfEmpty(0).Max();
        Unit unit = _samples.LastOrDefault()?.Get(_metrics.FirstOrDefault() ?? "").Unit ?? Unit.Percent;
        maximum = _maximum is > 0 ? _maximum.Value : unit == Unit.Percent ? 100 : Math.Max(1, maximum * 1.1);
        var foreground = Foreground;
        for (int row = 0; row <= 4; row++)
        {
            double y = 8 + row * height / 4;
            _canvas.Children.Add(new Line { X1 = 72, X2 = width + 72, Y1 = y, Y2 = y, Stroke = foreground, Opacity = .16, StrokeThickness = 1 });
            var label = Ui.Text(ValueFormat.Metric(new(maximum * (4 - row) / 4, unit), _preferences, _storage), 10);
            label.IsTextSelectionEnabled = false; label.MaxWidth = 68;
            Canvas.SetLeft(label, 0); Canvas.SetTop(label, y - 6); _canvas.Children.Add(label);
        }
        var left = Ui.Text($"−{_duration / 60:0} min", 11); Canvas.SetLeft(left, 72); Canvas.SetTop(left, 166); _canvas.Children.Add(left);
        var right = Ui.Text("Now", 11); Canvas.SetLeft(right, width + 44); Canvas.SetTop(right, 166); _canvas.Children.Add(right);
        for (int series = 0; series < _metrics.Length; series++)
        {
            var brush = Desktop.NativeDesktop.HighContrast() ? foreground : Ui.Brush(series == 0 ? Ui.Accent : Ui.Secondary);
            foreach (var segment in PlotReduction.Segments(_samples, _metrics[series], _gap, (int)width))
            {
                var line = new Polyline { Stroke = brush, StrokeThickness = 2, IsHitTestVisible = false };
                if (series > 0) line.StrokeDashArray = [3, 2];
                foreach (var sample in segment)
                {
                    double value = sample.Get(_metrics[series]).Value!.Value;
                    line.Points.Add(new(72 + Math.Clamp((sample.Elapsed - start) / _duration, 0, 1) * width, 8 + height * (1 - Math.Clamp(value / maximum, 0, 1))));
                }
                if (line.Points.Count == 1)
                {
                    var dot = new Ellipse { Width = 4, Height = 4, Fill = brush };
                    Canvas.SetLeft(dot, line.Points[0].X - 2); Canvas.SetTop(dot, line.Points[0].Y - 2); _canvas.Children.Add(dot);
                }
                else
                {
                    if (series == 0)
                    {
                        var area = new Polygon { Fill = brush, Opacity = .08, IsHitTestVisible = false };
                        area.Points.Add(new(line.Points[0].X, 8 + height));
                        foreach (var point in line.Points) area.Points.Add(point);
                        area.Points.Add(new(line.Points[^1].X, 8 + height)); _canvas.Children.Add(area);
                    }
                    _canvas.Children.Add(line);
                }
            }
        }
        string coverage = _samples.Length == 0 ? "No observations in this range" : $"Observed {_samples[0].Time:T} – {_samples[^1].Time:T}; gaps are left open";
        _legend.Text = string.Join(" / ", _metrics.Select((x, i) => x + (i == 0 ? " (solid)" : " (dashed)"))) + " · " + coverage;
        _cursor.Stroke = foreground; _cursor.Y1 = 8; _cursor.Y2 = 8 + height; _canvas.Children.Add(_cursor);
        InspectSelected();
    }
    private double? PointerSample(PointerRoutedEventArgs e)
    {
        if (_samples.Length == 0) return null;
        double x = e.GetCurrentPoint(_canvas).Position.X;
        double target = _end - _duration + Math.Clamp((x - 72) / Math.Max(1, _canvas.ActualWidth - 80), 0, 1) * _duration;
        return _samples.MinBy(p => Math.Abs(p.Elapsed - target))!.Elapsed;
    }
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (_samples.Length == 0) return;
        int index = _selected is double value ? Array.FindIndex(_samples, x => x.Elapsed >= value) : _samples.Length - 1;
        if (index < 0) index = _samples.Length - 1;
        switch (e.Key)
        {
            case VirtualKey.Left: index = Math.Max(0, index - 1); break;
            case VirtualKey.Right: index = Math.Min(_samples.Length - 1, index + 1); break;
            case VirtualKey.Home: index = 0; break;
            case VirtualKey.End: ResetInspection(); e.Handled = true; return;
            default: return;
        }
        _selected = _samples[index].Elapsed; _hover = null; InspectSelected(); e.Handled = true;
    }
    private void InspectSelected()
    {
        double? inspected = _selected ?? _hover;
        var selected = inspected is double value ? _samples.MinBy(x => Math.Abs(x.Elapsed - value)) : _samples.LastOrDefault();
        _live.Visibility = _selected is null ? Visibility.Collapsed : Visibility.Visible;
        _cursor.Visibility = inspected is null || selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (selected is null) { _inspection.Text = "No observed point is available."; return; }
        _cursor.X1 = _cursor.X2 = 72 + Math.Clamp((selected.Elapsed - (_end - _duration)) / Math.Max(1, _duration), 0, 1) * Math.Max(20, _canvas.ActualWidth - 80);
        _inspection.Text = inspected is null
            ? "Live · Hover to preview; click or use Left/Right to hold an observation."
            : (_selected is null ? "Preview · " : "Historical inspection · ") + selected.Time.ToString("T") + "\n" +
                string.Join("   |   ", _metrics.Select(m => { var v = selected.Get(m); return $"{m}: {ValueFormat.Metric(v, _preferences, _storage)} · {Ui.QualityLabel(v.Quality)}" + (v.Reason is null ? "" : $" — {v.Reason}"); }));
    }
}
