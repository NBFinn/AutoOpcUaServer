namespace AutoOpcUaServer.Gui.Services;

/// <summary>Things the view model needs from the window: file dialogs and the clipboard.</summary>
public interface IUiServices
{
    Task<IReadOnlyList<string>> PickFilesAsync();

    Task<string?> PickFolderAsync();

    Task CopyToClipboardAsync(string text);
}
