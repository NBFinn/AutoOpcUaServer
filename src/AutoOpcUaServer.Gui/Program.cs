using Avalonia;

namespace AutoOpcUaServer.Gui;

internal static class Program
{
    /// <summary>Files passed on the command line (e.g. "Open with…") are imported on start.</summary>
    public static string[] StartupFiles { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        StartupFiles = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
