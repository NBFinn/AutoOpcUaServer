using System.Collections.ObjectModel;
using AutoOpcUaServer.Export;
using AutoOpcUaServer.Gui.Services;
using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;
using AutoOpcUaServer.Server;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AutoOpcUaServer.Gui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int MaxLogEntries = 2000;
    private const char Separator = '/';

    private readonly SettingsStore _settingsStore;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, TagRowViewModel> _rowsByNodeId = new(StringComparer.Ordinal);
    private readonly List<TagDefinition> _manualTags = [];
    private ImportResult _import = new();
    private IReadOnlyList<TagDefinition> _tags = [];
    private ServerHost? _serverHost;

    public MainWindowViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => RefreshValues());
        Files.CollectionChanged += (_, _) =>
        {
            StartCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(Summary));
        };
        ApplySettings(settingsStore.Load());
    }

    /// <summary>Set by the window; null in tests that do not need dialogs.</summary>
    public IUiServices? Ui { get; set; }

    public ObservableCollection<string> Files { get; } = [];

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    public ObservableCollection<TagRowViewModel> VisibleTags { get; } = [];

    public ObservableCollection<LogEntry> Log { get; } = [];

    public IReadOnlyList<SecurityMode> SecurityModes { get; } = Enum.GetValues<SecurityMode>();

    // ---- Server settings -------------------------------------------------------------------

    [ObservableProperty]
    private decimal? _port = 4840;

    [ObservableProperty]
    private string _host = "localhost";

    [ObservableProperty]
    private string _endpointPath = "";

    [ObservableProperty]
    private string _applicationName = "AutoOpcUaServer";

    [ObservableProperty]
    private string _namespaceUri = "urn:AutoOpcUaServer:Tags";

    [ObservableProperty]
    private string _rootFolder = "";

    [ObservableProperty]
    private SecurityMode _security = SecurityMode.All;

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _allowAnonymous;

    [ObservableProperty]
    private bool _autoAccept;

    [ObservableProperty]
    private decimal? _simulationInterval = 1000;

    // ---- State -----------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopped), nameof(StatusText), nameof(CanEditValue))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(ReloadCommand), nameof(SetValueCommand), nameof(EditNodeCommand), nameof(DeleteNodeCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(ReloadCommand), nameof(AddNodeCommand), nameof(EditNodeCommand), nameof(DeleteNodeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogHeader), nameof(StatusText), nameof(HasErrors))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int _errorCount;

    [ObservableProperty]
    private string _endpointUrl = "";

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditValue))]
    [NotifyPropertyChangedFor(nameof(SelectedIsManual))]
    [NotifyCanExecuteChangedFor(nameof(SetValueCommand), nameof(EditNodeCommand), nameof(DeleteNodeCommand))]
    private TagRowViewModel? _selectedTag;

    [ObservableProperty]
    private string _editValue = "";

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsStopped => !IsRunning;

    public bool HasErrors => ErrorCount > 0;

    public bool CanEditValue => IsRunning && SelectedTag is not null;

    public string TagsHeader => $"Tags ({Tags.Count})";

    public bool HasNoTags => Tags.Count == 0;

    public bool SelectedIsManual => SelectedTag?.IsManual == true;

    public string LogHeader => ErrorCount > 0 ? $"Meldungen ({ErrorCount} Fehler)" : "Meldungen";

    public string StatusText => IsBusy ? "Bitte warten…"
        : IsRunning ? "Server läuft"
        : ErrorCount > 0 ? "Fehler in den Importdateien"
        : "Server gestoppt";

    public string Summary
    {
        get
        {
            var simulated = Tags.Count(t => t.Tag.Simulation != SimulationKind.None);
            return $"{Files.Count} Datei(en) · {Tags.Count} Tags ({_manualTags.Count} manuell angelegt) · {simulated} simuliert";
        }
    }

    /// <summary>Imports the remembered files plus files given on the command line.</summary>
    public void Initialize(IEnumerable<string> startupFiles)
    {
        foreach (var file in startupFiles.Select(Path.GetFullPath))
        {
            if (!Files.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(file);
            }
        }

        _ = ReloadAsync();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedTagChanged(TagRowViewModel? value) => EditValue = value?.Value ?? "";

    // ---- Files -----------------------------------------------------------------------------

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        if (Ui is not null)
        {
            await AddPathsAsync(await Ui.PickFilesAsync());
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (Ui?.PickFolderAsync() is { } pick && await pick is { } folder)
        {
            await AddPathsAsync([folder]);
        }
    }

    /// <summary>Adds files or folders (from dialogs or drag and drop) and re-imports.</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            if (!Files.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(path);
                added = true;
            }
        }

        if (added)
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveFileAsync(string? path)
    {
        if (path is not null && Files.Remove(path))
        {
            await ReloadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    public async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            var files = Files.ToList();
            var result = files.Count == 0
                ? new ImportResult()
                : await Task.Run(() => new TagImporter(Separator).Import(files));
            _import = result;

            foreach (var warning in result.Warnings)
            {
                AddLog(LogKind.Warning, warning);
            }

            foreach (var error in result.Errors)
            {
                AddLog(LogKind.Error, error);
            }

            if (files.Count > 0)
            {
                AddLog(
                    result.Errors.Count == 0 ? LogKind.Info : LogKind.Error,
                    result.Errors.Count == 0
                        ? $"{result.Tags.Count} Tags aus {result.Files.Count} Datei(en) importiert."
                        : $"Import fehlgeschlagen: {result.Errors.Count} Fehler.");
            }

            ApplyTags();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Combines imported and manually created tags, validates them and fills the table.</summary>
    private void ApplyTags()
    {
        var tags = new List<TagDefinition>();
        var errors = new List<string>();
        if (_import.Errors.Count == 0)
        {
            tags.AddRange(_import.Tags);
            tags.AddRange(_manualTags);
            TagValidator.Validate(tags, Separator, errors, new List<string>());
        }
        else
        {
            // Keep the manual nodes visible so they can still be edited or deleted.
            tags.AddRange(_manualTags);
        }

        foreach (var error in errors)
        {
            AddLog(LogKind.Error, error);
        }

        ErrorCount = _import.Errors.Count + errors.Count;
        if (ErrorCount > 0)
        {
            SelectedTabIndex = 1;
        }

        SetTags(tags);
    }

    private bool CanReload() => !IsRunning && !IsBusy;

    private void SetTags(IReadOnlyList<TagDefinition> tags)
    {
        var selected = SelectedTag?.Tag;
        _tags = tags;
        Tags.Clear();
        _rowsByNodeId.Clear();
        foreach (var tag in tags)
        {
            var row = new TagRowViewModel(tag, TagPaths.VariableNodeId(tag, Separator), _manualTags.Contains(tag));
            Tags.Add(row);
            _rowsByNodeId.TryAdd(row.NodeId, row);
        }

        ApplyFilter();
        SelectedTag = Tags.FirstOrDefault(t => t.Tag == selected);
        OnPropertyChanged(nameof(TagsHeader));
        OnPropertyChanged(nameof(HasNoTags));
        OnPropertyChanged(nameof(Summary));
        StartCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    // ---- Manually created nodes ------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAddNode))]
    private async Task AddNodeAsync()
    {
        if (Ui is null)
        {
            return;
        }

        // Prefill the folder of the selected tag – nodes are usually added next to similar ones.
        var folder = SelectedTag is { } selected && selected.Path.LastIndexOf(Separator) is > 0 and var slash
            ? selected.Path[..slash]
            : null;

        if (await Ui.EditNodeAsync(new NodeEditorViewModel(_tags, null, folder, Separator)) is { } tag)
        {
            await AddManualTagAsync(tag);
        }
    }

    private bool CanAddNode() => !IsBusy;

    /// <summary>
    /// Adds a manually created node. While the server runs, the node is added to the live address
    /// space immediately. Returns an error message if it could not be added.
    /// </summary>
    public async Task<string?> AddManualTagAsync(TagDefinition tag)
    {
        if (IsRunning && _serverHost?.Server?.TagNodeManager is { } nodeManager)
        {
            var error = await nodeManager.AddTagAsync(tag);
            if (error is not null)
            {
                AddLog(LogKind.Error, error);
                SelectedTabIndex = 1;
                return error;
            }

            _manualTags.Add(tag);
            SetTags([.. _tags, tag]);
            RefreshValues();
            AddLog(LogKind.Info, $"Node {tag.Path} angelegt und im laufenden Server verfügbar.");
        }
        else
        {
            _manualTags.Add(tag);
            ApplyTags();
            AddLog(LogKind.Info, $"Node {tag.Path} angelegt.");
        }

        SelectedTag = Tags.FirstOrDefault(t => t.Tag == tag);
        SaveSettings();
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanChangeNode))]
    private async Task EditNodeAsync()
    {
        if (Ui is null || SelectedTag is not { IsManual: true } row)
        {
            return;
        }

        var others = _tags.Where(t => t != row.Tag).ToList();
        if (await Ui.EditNodeAsync(new NodeEditorViewModel(others, row.Tag, null, Separator)) is { } tag)
        {
            ReplaceManualTag(row.Tag, tag);
        }
    }

    public void ReplaceManualTag(TagDefinition existing, TagDefinition updated)
    {
        var index = _manualTags.IndexOf(existing);
        if (index < 0)
        {
            return;
        }

        _manualTags[index] = updated;
        ApplyTags();
        SelectedTag = Tags.FirstOrDefault(t => t.Tag == updated);
        AddLog(LogKind.Info, $"Node {updated.Path} geändert.");
        SaveSettings();
    }

    [RelayCommand(CanExecute = nameof(CanChangeNode))]
    public void DeleteNode()
    {
        if (SelectedTag is not { IsManual: true } row)
        {
            return;
        }

        _manualTags.Remove(row.Tag);
        ApplyTags();
        AddLog(LogKind.Info, $"Node {row.Path} gelöscht.");
        SaveSettings();
    }

    // Changing or removing nodes of a running server would disconnect clients' monitored items,
    // so it is only possible while the server is stopped.
    private bool CanChangeNode() => !IsRunning && !IsBusy && SelectedTag?.IsManual == true;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (Ui is not null && await Ui.PickSaveFileAsync("tags.csv") is { } path)
        {
            await ExportToAsync(path);
        }
    }

    /// <summary>Saves all tags (imported and manual) as CSV that both the GUI and the console can import.</summary>
    public async Task ExportToAsync(string path)
    {
        try
        {
            await TagCsvWriter.WriteFileAsync(_tags, path);
            AddLog(LogKind.Info, $"{_tags.Count} Tags exportiert nach {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogKind.Error, $"Export fehlgeschlagen: {ex.Message}");
            SelectedTabIndex = 1;
        }
    }

    private bool CanExport() => _tags.Count > 0;

    private void ApplyFilter()
    {
        VisibleTags.Clear();
        var filter = Filter.Trim();
        foreach (var row in Tags.Where(r => filter.Length == 0 || r.Matches(filter)))
        {
            VisibleTags.Add(row);
        }
    }

    // ---- Server ----------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        IsBusy = true;
        SaveSettings();
        var options = CreateOptions();
        var host = new ServerHost(options, logging => logging.AddProvider(
            new CallbackLoggerProvider((level, message) => Post(() => AddLog(level >= LogLevel.Error ? LogKind.Error : LogKind.Warning, message)))));

        try
        {
            await host.StartAsync(_tags);
        }
        catch (Exception ex)
        {
            await host.DisposeAsync();
            AddLog(LogKind.Error, $"Server konnte nicht gestartet werden: {ex.Message}");
            SelectedTabIndex = 1;
            IsBusy = false;
            return;
        }

        _serverHost = host;
        var nodeManager = host.Server!.TagNodeManager!;
        nodeManager.ValueWritten += (nodeId, value) => Post(() => AddLog(LogKind.Write, $"{nodeId} = {ValueFormatter.Format(value)}"));
        nodeManager.Error += message => Post(() => AddLog(LogKind.Error, message));

        EndpointUrl = host.Server.GetEndpoints().Select(e => e.EndpointUrl).FirstOrDefault() ?? options.EndpointUrl;
        AddLog(LogKind.Info, $"Server gestartet: {EndpointUrl}");
        foreach (var endpoint in host.EndpointUrls)
        {
            AddLog(LogKind.Info, $"Endpoint {endpoint}");
        }

        IsRunning = true;
        IsBusy = false;
        RefreshValues();
        _refreshTimer.Start();
    }

    private bool CanStart() => !IsRunning && !IsBusy && ErrorCount == 0 && _tags.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStop))]
    public async Task StopAsync()
    {
        if (_serverHost is null)
        {
            return;
        }

        IsBusy = true;
        _refreshTimer.Stop();
        try
        {
            await _serverHost.DisposeAsync();
            AddLog(LogKind.Info, "Server gestoppt.");
        }
        catch (Exception ex)
        {
            AddLog(LogKind.Error, $"Fehler beim Stoppen: {ex.Message}");
        }
        finally
        {
            _serverHost = null;
            EndpointUrl = "";
            IsRunning = false;
            IsBusy = false;
            foreach (var row in Tags)
            {
                row.ResetValue();
            }
        }
    }

    private bool CanStop() => IsRunning && !IsBusy;

    /// <summary>Copies the current values of the running server into the table.</summary>
    public void RefreshValues()
    {
        if (_serverHost?.Server?.TagNodeManager is not { } nodeManager)
        {
            return;
        }

        foreach (var value in nodeManager.ReadValues())
        {
            if (_rowsByNodeId.TryGetValue(value.NodeId, out var row))
            {
                row.Update(value.Value, value.Timestamp);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditValue))]
    public void SetValue()
    {
        if (_serverHost?.Server?.TagNodeManager is not { } nodeManager || SelectedTag is not { } row)
        {
            return;
        }

        var error = nodeManager.SetValue(row.NodeId, EditValue);
        if (error is null)
        {
            AddLog(LogKind.Info, $"{row.NodeId} manuell gesetzt: {EditValue}");
            RefreshValues();
        }
        else
        {
            AddLog(LogKind.Error, error);
        }
    }

    [RelayCommand]
    private async Task CopyEndpointAsync()
    {
        if (Ui is not null && EndpointUrl.Length > 0)
        {
            await Ui.CopyToClipboardAsync(EndpointUrl);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
    }

    /// <summary>Stops the server and saves the settings; called when the window closes.</summary>
    public async Task ShutdownAsync()
    {
        SaveSettings();
        await StopAsync();
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private ServerOptions CreateOptions()
    {
        var options = new ServerOptions
        {
            Port = (int)(Port ?? 4840),
            Host = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim(),
            EndpointPath = EndpointPath.Trim(),
            ApplicationName = string.IsNullOrWhiteSpace(ApplicationName) ? "AutoOpcUaServer" : ApplicationName.Trim(),
            NamespaceUri = string.IsNullOrWhiteSpace(NamespaceUri) ? "urn:AutoOpcUaServer:Tags" : NamespaceUri.Trim(),
            RootFolder = RootFolder.Trim(),
            Security = Security,
            AutoAcceptUntrustedCertificates = AutoAccept,
            AllowAnonymous = AllowAnonymous,
            SimulationIntervalMs = Math.Max(50, (int)(SimulationInterval ?? 1000)),
            PkiDirectory = _settingsStore.PkiDirectory,
        };

        if (!string.IsNullOrWhiteSpace(UserName))
        {
            options.UserName = UserName.Trim();
            options.Password = Password;
        }

        return options;
    }

    private void ApplySettings(GuiSettings settings)
    {
        foreach (var file in settings.Files)
        {
            Files.Add(file);
        }

        Port = settings.Port;
        Host = settings.Host;
        EndpointPath = settings.EndpointPath;
        ApplicationName = settings.ApplicationName;
        NamespaceUri = settings.NamespaceUri;
        RootFolder = settings.RootFolder;
        Security = settings.Security;
        UserName = settings.UserName;
        AllowAnonymous = settings.AllowAnonymous;
        AutoAccept = settings.AutoAcceptUntrustedCertificates;
        SimulationInterval = settings.SimulationIntervalMs;

        var mapper = new TagRowMapper(Separator);
        var errors = new List<string>();
        foreach (var row in settings.ManualTags)
        {
            if (mapper.Map(row, NodeEditorViewModel.ManualSource, errors) is { } tag)
            {
                _manualTags.Add(tag);
            }
        }

        foreach (var error in errors)
        {
            AddLog(LogKind.Error, $"Gespeicherter Node ungültig: {error}");
        }
    }

    public void SaveSettings() => _settingsStore.Save(new GuiSettings
    {
        Files = Files.ToList(),
        Port = (int)(Port ?? 4840),
        Host = Host,
        EndpointPath = EndpointPath,
        ApplicationName = ApplicationName,
        NamespaceUri = NamespaceUri,
        RootFolder = RootFolder,
        Security = Security,
        UserName = UserName,
        AllowAnonymous = AllowAnonymous,
        AutoAcceptUntrustedCertificates = AutoAccept,
        SimulationIntervalMs = (int)(SimulationInterval ?? 1000),
        ManualTags = _manualTags.Select(TagRowMapper.ToRow).ToList(),
    });

    private void AddLog(LogKind kind, string message)
    {
        Log.Insert(0, new LogEntry(kind, message));
        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
