namespace AutoOpcUaServer.Gui.Services;

/// <summary>Things the view model needs from the window: file dialogs and the clipboard.</summary>
public interface IUiServices
{
    Task<IReadOnlyList<string>> PickFilesAsync();

    Task<string?> PickFolderAsync();

    Task<string?> PickSaveFileAsync(string suggestedName);

    Task CopyToClipboardAsync(string text);

    /// <summary>Shows the node editor; returns the created or changed tag, or null if cancelled.</summary>
    Task<Model.TagDefinition?> EditNodeAsync(ViewModels.NodeEditorViewModel editor);
}
