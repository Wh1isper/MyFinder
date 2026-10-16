using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyFinder.Core;
using MyFinder.Core.Monitoring;

namespace MyFinder.Views;

// Reuse the same headline controls while observations change; layout never owns collection.
internal sealed class ReadingSummary : Grid
{
    private readonly List<TextBlock> _values = [];
    private string _signature = "";
    public ReadingSummary()
    {
        ColumnSpacing = 12; RowSpacing = 12;
        for (int i = 0; i < 4; i++) ColumnDefinitions.Add(new());
        SizeChanged += (_, _) => ArrangeCards();
        Ui.Id(this, "CurrentReadings");
    }
    public void Update(Reading reading, Preferences preferences)
    {
        string[] keys = reading.Resource.Kind switch
        {
            ResourceKind.Cpu => ["Utilization", "Reported frequency", "Processes", "Uptime"],
            ResourceKind.Memory => ["In use", "Available", "Committed", "Total"],
            ResourceKind.Network => ["Receive", "Send", "Observed received", "Observed sent"],
            ResourceKind.Volume => ["Used / reserved", "Free", "Total", "Utilization"],
            ResourceKind.Device => ["Read", "Write", "Activity", "Response time"],
            _ => []
        };
        string signature = string.Join(";", keys);
        if (_signature != signature)
        {
            _signature = signature; Children.Clear(); _values.Clear();
            foreach (string key in keys)
            {
                var stack = Ui.Stack(6); stack.Children.Add(Ui.Muted(key));
                var value = Ui.Text("", 20); value.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                _values.Add(value); stack.Children.Add(value); Children.Add(Ui.Card(stack));
            }
            ArrangeCards();
        }
        for (int i = 0; i < keys.Length; i++)
        {
            var metric = reading.Observation.Get(keys[i]);
            _values[i].Text = reading.Resource.Kind == ResourceKind.Network && metric.Unit == Unit.Bytes && metric.Value is double bytes
                ? ValueFormat.Size(bytes, false) : ValueFormat.Metric(metric, preferences, reading.Resource.Kind is ResourceKind.Volume or ResourceKind.Device);
            ToolTipService.SetToolTip(_values[i], $"{keys[i]} · {metric.Quality}" + (metric.Reason is null ? "" : " — " + metric.Reason));
        }
    }
    private void ArrangeCards()
    {
        int columns = ActualWidth >= 600 ? 4 : ActualWidth >= 360 ? 2 : 1;
        int rows = Math.Max(1, (Children.Count + columns - 1) / columns);
        if (RowDefinitions.Count != rows)
        {
            RowDefinitions.Clear(); for (int i = 0; i < rows; i++) RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        for (int i = 0; i < 4; i++) ColumnDefinitions[i].Width = new(i < columns ? 1 : 0, GridUnitType.Star);
        for (int i = 0; i < Children.Count; i++) { SetColumn((FrameworkElement)Children[i], i % columns); SetRow((FrameworkElement)Children[i], i / columns); }
    }
}

internal sealed class FactTable : StackPanel
{
    private readonly List<TextBlock> _values = [];
    private string _signature = "";
    public FactTable() { Spacing = 12; }
    public void Update(IEnumerable<KeyValuePair<string, string>> facts)
    {
        var rows = facts.ToArray();
        string signature = string.Join("\n", rows.Select(x => x.Key));
        if (_signature != signature)
        {
            _signature = signature; Children.Clear(); _values.Clear();
            foreach (var row in rows)
            {
                var grid = new Grid { ColumnSpacing = 16 };
                grid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
                var label = Ui.Muted(row.Key); var value = Ui.Text("");
                grid.Children.Add(label); Grid.SetColumn(value, 1); grid.Children.Add(value); _values.Add(value); Children.Add(grid);
            }
        }
        for (int i = 0; i < rows.Length; i++) if (_values[i].Text != rows[i].Value) _values[i].Text = rows[i].Value;
    }
}
