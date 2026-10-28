namespace AutoOpcUaServer.Server;

public enum SecurityMode
{
    /// <summary>Only unencrypted endpoints (SecurityPolicy None).</summary>
    None,

    /// <summary>Only signed / signed and encrypted endpoints (Basic256Sha256, Aes128/Aes256).</summary>
    Secure,

    /// <summary>Both None and secure endpoints.</summary>
    All,
}

/// <summary>Settings for the OPC UA server, filled from the command line.</summary>
public sealed class ServerOptions
{
    public List<string> InputPaths { get; } = [];

    public int Port { get; set; } = 4840;

    public string Host { get; set; } = "localhost";

    /// <summary>Optional path appended to the endpoint URL, e.g. "opc.tcp://host:4840/MyServer".</summary>
    public string EndpointPath { get; set; } = "";

    public string ApplicationName { get; set; } = "AutoOpcUaServer";

    public string NamespaceUri { get; set; } = "urn:AutoOpcUaServer:Tags";

    /// <summary>Folder below "Objects" that contains all tags. Empty = tags directly below "Objects".</summary>
    public string RootFolder { get; set; } = "";

    public char PathSeparator { get; set; } = '/';

    public SecurityMode Security { get; set; } = SecurityMode.All;

    public bool AutoAcceptUntrustedCertificates { get; set; }

    /// <summary>User name / password login. If null, only anonymous login is offered.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>Whether anonymous login is still allowed when a user name is configured.</summary>
    public bool AllowAnonymous { get; set; }

    public int SimulationIntervalMs { get; set; } = 1000;

    public string PkiDirectory { get; set; } = "pki";

    public bool LogWrites { get; set; } = true;

    public bool ValidateOnly { get; set; }

    public string EndpointUrl =>
        $"opc.tcp://{Host}:{Port}" + (string.IsNullOrWhiteSpace(EndpointPath) ? "" : "/" + EndpointPath.Trim('/'));
}
