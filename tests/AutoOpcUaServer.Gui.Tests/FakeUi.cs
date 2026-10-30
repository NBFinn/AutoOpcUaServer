using AutoOpcUaServer.Gui.Services;
using AutoOpcUaServer.Gui.ViewModels;
using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Gui.Tests;

/// <summary>Replaces dialogs in tests: the editor callback fills in the form like a user would.</summary>
internal sealed class FakeUi : IUiServices
{
    public Action<NodeEditorViewModel>? FillEditor { get; set; }

    public NodeEditorViewModel? LastEditor { get; private set; }

    public string? SavePath { get; set; }

    public Task<IReadOnlyList<string>> PickFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(string suggestedName) => Task.FromResult(SavePath);

    public Task CopyToClipboardAsync(string text) => Task.CompletedTask;

    public Task<TagDefinition?> EditNodeAsync(NodeEditorViewModel editor)
    {
        LastEditor = editor;
        FillEditor?.Invoke(editor);
        editor.Save();
        return Task.FromResult(editor.Result);
    }
}
