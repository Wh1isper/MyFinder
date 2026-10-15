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
    public static SolidColorBrush Brush(Color color) => new(color);
    public static Color Accent => Color.FromArgb(255, 47, 139, 225);
    public static Color Secondary => Color.FromArgb(255, 190, 110, 32);
    public static StackPanel Stack(double spacing = 10) => new() { Spacing = spacing };
    public static void Name(DependencyObject element, string name) => AutomationProperties.SetName(element, name);
}
