using AutoOpcUaServer;
using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;
using AutoOpcUaServer.Server;

var options = CommandLine.Parse(args, out var error, out var showHelp);
if (showHelp)
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}

if (options is null)
{
    Console.Error.WriteLine($"Error: {error}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLine.Usage);
    return 2;
}

var import = new TagImporter(options.PathSeparator).Import(options.InputPaths);

foreach (var warning in import.Warnings)
{
    Console.WriteLine($"Warning: {warning}");
}

if (import.Errors.Count > 0)
{
    foreach (var message in import.Errors)
    {
        Console.Error.WriteLine($"Error: {message}");
    }

    Console.Error.WriteLine($"Import failed with {import.Errors.Count} error(s).");
    return 1;
}

var simulated = import.Tags.Count(t => t.Simulation != SimulationKind.None);
Console.WriteLine($"Imported {import.Tags.Count} tag(s) ({simulated} simulated) from {import.Files.Count} file(s).");

if (options.ValidateOnly)
{
    Console.WriteLine("Validation successful.");
    return 0;
}

await using var host = new ServerHost(options);
try
{
    await host.StartAsync(import.Tags);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: the server could not be started: {ex.Message}");
    return 3;
}

var nodeManager = host.Server!.TagNodeManager!;
nodeManager.Error += message => Console.Error.WriteLine($"Error: {message}");
if (options.LogWrites)
{
    nodeManager.ValueWritten += (nodeId, value) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} write {nodeId} = {ValueFormatter.Format(value)}");
}

Console.WriteLine($"Server '{options.ApplicationName}' is running. Endpoints:");
foreach (var endpoint in host.EndpointUrls)
{
    Console.WriteLine($"  {endpoint}");
}

Console.WriteLine($"Login: {(options.UserName is null ? "anonymous" : $"user '{options.UserName}'" + (options.AllowAnonymous ? " or anonymous" : ""))}");
Console.WriteLine("Press Ctrl+C to stop.");

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();

await stop.Task;
Console.WriteLine("Stopping server...");
return 0;
