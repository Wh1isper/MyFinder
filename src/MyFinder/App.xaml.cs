using Microsoft.UI.Xaml;

namespace MyFinder;

public partial class App : Application
{
    private AppController? _controller;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "--profile");
            string profile = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyFinder");
            try { Directory.CreateDirectory(profile); File.WriteAllText(Path.Combine(profile, "last-error.txt"), e.Exception.ToString()); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _controller = new AppController();
        _controller.Start();
    }
}
