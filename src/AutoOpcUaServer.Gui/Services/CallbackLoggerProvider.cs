using Microsoft.Extensions.Logging;

namespace AutoOpcUaServer.Gui.Services;

/// <summary>Forwards log messages of the OPC UA stack to a callback (the message list of the GUI).</summary>
public sealed class CallbackLoggerProvider : ILoggerProvider
{
    private readonly Action<LogLevel, string> _callback;

    public CallbackLoggerProvider(Action<LogLevel, string> callback)
    {
        _callback = callback;
    }

    public ILogger CreateLogger(string categoryName) => new CallbackLogger(_callback);

    public void Dispose()
    {
    }

    private sealed class CallbackLogger(Action<LogLevel, string> callback) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null && !message.Contains(exception.Message, StringComparison.Ordinal))
            {
                message += $" ({exception.Message})";
            }

            callback(logLevel, message);
        }
    }
}
