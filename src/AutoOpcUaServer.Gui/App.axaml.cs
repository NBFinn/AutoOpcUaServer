using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AutoOpcUaServer.Gui.Services;
using AutoOpcUaServer.Gui.ViewModels;
using AutoOpcUaServer.Gui.Views;

namespace AutoOpcUaServer.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(new SettingsStore(SettingsStore.DefaultDirectory));
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            viewModel.Initialize(Program.StartupFiles);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
