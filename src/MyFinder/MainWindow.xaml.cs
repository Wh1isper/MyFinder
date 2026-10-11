using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MyFinder;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(680, 380));
    }
}
