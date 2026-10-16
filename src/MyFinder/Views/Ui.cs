using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MyFinder.Views;

internal static class Ui
{
    public static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    public static Button Button(string text, Action click, string? id = null)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => click();
        AutomationProperties.SetName(button, text);
        if (id is not null) AutomationProperties.SetAutomationId(button, id);
        return button;
    }
    public static string QualityLabel(Core.Monitoring.Quality quality) => quality switch
    {
        Core.Monitoring.Quality.Fresh => "Up to date",
        Core.Monitoring.Quality.WarmingUp => "Warming up",
        Core.Monitoring.Quality.NotMonitored => "Monitoring off",
        Core.Monitoring.Quality.Stale => "Out of date",
        _ => quality.ToString()
    };
    public static SolidColorBrush Brush(Color color) => new(color);
    public static Color Accent => Color.FromArgb(255, 47, 139, 225);
    public static Color Secondary => Color.FromArgb(255, 190, 110, 32);
    public static StackPanel Stack(double spacing = 10) => new() { Spacing = spacing };
    public static void Name(DependencyObject element, string name) => AutomationProperties.SetName(element, name);
    public static void Id(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);
    public static TextBlock Muted(string text, double size = 12) => new() { Text = text, FontSize = size, Style = (Style)Application.Current.Resources["SecondaryTextStyle"], IsTextSelectionEnabled = true };
    public static TextBlock Heading(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["SectionTitleStyle"] };
    public static Border Card(UIElement child) => new() { Child = child, Style = (Style)Application.Current.Resources["SurfaceCardStyle"] };
    public static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size, FontFamily = new("Segoe Fluent Icons") };
    public static Button IconButton(string label, string glyph, Action click, string id)
    {
        var button = Button(label, click, id);
        button.Content = Icon(glyph); button.MinWidth = 32; button.MinHeight = 32; button.Padding = new(8);
        ToolTipService.SetToolTip(button, label); return button;
    }
    public static Grid Columns(params FrameworkElement[] children)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (int i = 0; i < children.Length; i++)
        {
            grid.ColumnDefinitions.Add(new());
            grid.Children.Add(children[i]);
        }
        void ArrangeColumns()
        {
            var visible = children.Where(x => x.Visibility == Visibility.Visible).ToArray();
            bool narrow = grid.ActualWidth < visible.Length * 210;
            int rows = Math.Max(1, narrow ? visible.Length : 1);
            if (grid.RowDefinitions.Count != rows)
            {
                grid.RowDefinitions.Clear(); for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            }
            for (int i = 0; i < children.Length; i++)
                grid.ColumnDefinitions[i].Width = new(i < (narrow ? 1 : visible.Length) ? 1 : 0, GridUnitType.Star);
            for (int i = 0; i < visible.Length; i++) { Grid.SetColumn(visible[i], narrow ? 0 : i); Grid.SetRow(visible[i], narrow ? i : 0); }
        }
        grid.SizeChanged += (_, _) => ArrangeColumns();
        foreach (var child in children) child.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => ArrangeColumns());
        return grid;
    }
}
