using Avalonia.Controls;
using Avalonia.Interactivity;
using AutoOpcUaServer.Gui.ViewModels;

namespace AutoOpcUaServer.Gui.Views;

public partial class NodeEditorWindow : Window
{
    public NodeEditorWindow()
    {
        InitializeComponent();
    }

    public NodeEditorWindow(NodeEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += () => Close(viewModel.Result);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PathBox.Focus();
        PathBox.CaretIndex = PathBox.Text?.Length ?? 0;
    }
}
