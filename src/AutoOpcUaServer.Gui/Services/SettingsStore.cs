using System.Text.Json;
using AutoOpcUaServer.Server;

namespace AutoOpcUaServer.Gui.Services;

/// <summary>Settings remembered between runs of the GUI. The password is intentionally not stored.</summary>
public sealed class GuiSettings
{
    public List<string> Files { get; set; } = [];

    public int Port { get; set; } = 4840;

    public string Host { get; set; } = "localhost";

    public string EndpointPath { get; set; } = "";

    public string ApplicationName { get; set; } = "AutoOpcUaServer";

    public string NamespaceUri { get; set; } = "urn:AutoOpcUaServer:Tags";

    public string RootFolder { get; set; } = "";

    public SecurityMode Security { get; set; } = SecurityMode.All;

    public string UserName { get; set; } = "";

    public bool AllowAnonymous { get; set; }

    public bool AutoAcceptUntrustedCertificates { get; set; }

    public int SimulationIntervalMs { get; set; } = 1000;
}

/// <summary>Loads and saves <see cref="GuiSettings"/> as JSON in the user's application data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string directory)
    {
        Directory = directory;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoOpcUaServer");

    public string Directory { get; }

    /// <summary>Certificate store used by the GUI, independent of the working directory.</summary>
    public string PkiDirectory => Path.Combine(Directory, "pki");

    private string FilePath => Path.Combine(Directory, "settings.json");

    public GuiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new GuiSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Broken or unreadable settings are not worth failing the start for.
        }

        return new GuiSettings();
    }

    public void Save(GuiSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
