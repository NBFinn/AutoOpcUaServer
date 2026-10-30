using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AutoOpcUaServer.Gui.Services;
using AutoOpcUaServer.Gui.ViewModels;
using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Gui.Views;

public partial class MainWindow : Window, IUiServices
{
    private static readonly FilePickerFileType TagFiles = new("Taglisten (CSV, Excel, JSON)")
    {
        Patterns = ["*.csv", "*.tsv", "*.txt", "*.xlsx", "*.xlsm", "*.json"],
    };

    private bool _shutdownDone;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        TagGrid.DoubleTapped += (_, _) =>
        {
            if (ViewModel?.EditNodeCommand.CanExecute(null) == true)
            {
                ViewModel.EditNodeCommand.Execute(null);
            }
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } viewModel)
        {
            viewModel.Ui = this;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_shutdownDone || ViewModel is not { } viewModel)
        {
            return;
        }

        // Stop the server before the window goes away so the port is released cleanly.
        e.Cancel = true;
        await viewModel.ShutdownAsync();
        _shutdownDone = true;
        Close();
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Taglisten auswählen",
            AllowMultiple = true,
            FileTypeFilter = [TagFiles, FilePickerFileTypes.All],
        });

        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Ordner mit Taglisten auswählen",
        });

        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Tags exportieren",
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV-Datei") { Patterns = ["*.csv"] }],
        });

        return file?.TryGetLocalPath();
    }

    public Task<TagDefinition?> EditNodeAsync(NodeEditorViewModel editor) =>
        new NodeEditorWindow(editor).ShowDialog<TagDefinition?>(this);

    public async Task CopyToClipboardAsync(string text)
    {
        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync(text);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && ViewModel?.IsStopped == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { IsStopped: true } viewModel || e.DataTransfer.TryGetFiles() is not { } items)
        {
            return;
        }

        await viewModel.AddPathsAsync(items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList());
    }
}
