namespace AutoOpcUaServer.Gui.ViewModels;

public enum LogKind
{
    Info,
    Write,
    Warning,
    Error,
}

/// <summary>One line in the message list.</summary>
public sealed class LogEntry(LogKind kind, string message)
{
    public DateTime Time { get; } = DateTime.Now;

    public LogKind Kind { get; } = kind;

    public string Message { get; } = message;

    public string TimeText => Time.ToString("HH:mm:ss");

    public string KindText => Kind switch
    {
        LogKind.Write => "Schreiben",
        LogKind.Warning => "Warnung",
        LogKind.Error => "Fehler",
        _ => "Info",
    };

    public bool IsError => Kind == LogKind.Error;

    public bool IsWarning => Kind == LogKind.Warning;

    public bool IsWrite => Kind == LogKind.Write;
}
