using Microsoft.UI.Xaml.Controls;
using MyFinder.Core;
using MyFinder.Core.Monitoring;

namespace MyFinder.Views;

internal sealed class StorageView(Action<string> inspect, Action manageResources) : UserControl
{
    private readonly StackPanel _rows = Ui.Stack(14);
    private readonly Dictionary<string, TextBlock> _current = [];
    private readonly Dictionary<string, ReadingSummary> _summaries = [];
    private readonly Dictionary<string, TrendChart> _charts = [];
    private string _signature = "";
    public void Update(Reading[] readings, Preferences preferences, HistoryStore history, int range, bool compare, string metric)
    {
        Content ??= _rows;
        var resources = readings.Where(x => x.Resource.Kind is ResourceKind.Volume or ResourceKind.Device &&
            (compare ? preferences.StorageComparison.Contains(x.Resource.Id) && x.Resource.Kind == ResourceKind.Device : preferences.Monitored.Contains(x.Resource.Id))).ToArray();
        string signature = compare + ":" + string.Join(";", resources.Select(x => x.Resource.Id + x.Resource.Name));
        if (_signature != signature)
        {
            _signature = signature; _rows.Children.Clear(); _current.Clear(); _charts.Clear(); _summaries.Clear();
            _rows.Children.Add(Ui.Text(compare ? "Explicit comparison subset · shared scales · no summed device percentages" : "All monitored volumes and devices · capacity and activity remain separate", 18));
            if (resources.Length == 0)
            {
                _rows.Children.Add(Ui.Text(compare ? "Choose up to four devices to compare. Comparison does not enable monitoring." : "Choose volumes or devices to monitor."));
                var manage = Ui.Button("Choose resources", manageResources, "ChooseStorageResources");
                manage.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left; _rows.Children.Add(manage);
            }
            foreach (var resource in resources)
            {
                string id = resource.Resource.Id;
                var section = Ui.Stack(12);
                section.Children.Add(Ui.Button(resource.Resource.Name + " · " + resource.Resource.Kind, () => inspect(id), "InspectOverview_" + id));
                var text = Ui.Muted(""); _current[id] = text; section.Children.Add(text);
                var summary = new ReadingSummary(); _summaries[id] = summary; section.Children.Add(summary);
                if (compare) { var chart = new TrendChart(); _charts[id] = chart; section.Children.Add(chart); }
                _rows.Children.Add(Ui.Card(section));
            }
        }
        string[] series = metric == "Response time" ? ["Response time"] : metric == "Activity" ? ["Activity"] : ["Read", "Write"];
        var histories = resources.ToDictionary(x => x.Resource.Id, x => compare ? history.Read(x.Resource.Id, ObservationClock.Elapsed - range * 60) : Array.Empty<Observation>());
        double maximum = metric == "Activity" ? 100 : Math.Max(1, histories.Values.SelectMany(x => x).SelectMany(x => series.Select(m => x.Get(m).Value ?? 0)).DefaultIfEmpty(0).Max() * 1.1);
        foreach (var reading in resources)
        {
            var primary = reading.Observation.Get(reading.Resource.Kind == ResourceKind.Volume ? "Used / reserved" : "Read");
            _current[reading.Resource.Id].Text = $"Snapshot {reading.Observation.Time:T} · {Ui.QualityLabel(primary.Quality)}" + (primary.Reason is null ? "" : " — " + primary.Reason);
            _summaries[reading.Resource.Id].Update(reading, preferences);
            if (compare) _charts[reading.Resource.Id].Update(histories[reading.Resource.Id], series, preferences, range, true, maximum);
        }
    }
}
