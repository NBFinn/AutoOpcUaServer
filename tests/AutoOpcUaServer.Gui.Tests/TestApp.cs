using Avalonia;
using Avalonia.Headless;
using AutoOpcUaServer.Gui;
using AutoOpcUaServer.Gui.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace AutoOpcUaServer.Gui.Tests;

public static class TestApp
{
    // Skia rendering instead of the stub renderer so windows can be captured as images.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
