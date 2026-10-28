using System.Globalization;
using AutoOpcUaServer.Server;

namespace AutoOpcUaServer;

/// <summary>Parses command line arguments into <see cref="ServerOptions"/>.</summary>
public static class CommandLine
{
    public const string Usage = """
        AutoOpcUaServer - starts an OPC UA server from CSV, Excel or JSON tag lists.

        Usage:
          AutoOpcUaServer <file|directory>... [options]

        Arguments:
          <file|directory>          .csv/.tsv/.txt, .xlsx/.xlsm or .json files, or directories containing them.

        Options:
          -p, --port <port>         TCP port (default: 4840)
              --host <name>         Host name used in the endpoint URL (default: localhost)
              --path <path>         Path appended to the endpoint URL, e.g. "MyServer"
          -n, --name <name>         Application name (default: AutoOpcUaServer)
              --namespace <uri>     Namespace URI of the tags (default: urn:AutoOpcUaServer:Tags)
              --root <folder>       Put all tags into this folder below "Objects"
              --separator <char>    Path separator inside the tag path (default: /)
              --security <mode>     none | secure | all (default: all)
              --user <user:pass>    Require user name/password login
              --allow-anonymous     Keep anonymous login enabled when --user is set
              --auto-accept         Automatically trust client certificates
              --interval <ms>       Simulation update interval (default: 1000)
              --pki <dir>           Certificate store directory (default: ./pki)
              --quiet               Do not log client writes
              --validate            Only check the input files and exit
          -h, --help                Show this help
        """;

    /// <summary>Returns the parsed options, or null and an error message when the arguments are invalid.</summary>
    public static ServerOptions? Parse(string[] args, out string? error, out bool showHelp)
    {
        var options = new ServerOptions();
        error = null;
        showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Option '{arg}' needs a value.");
                }

                return args[++i];
            }

            try
            {
                switch (arg)
                {
                    case "-h" or "--help" or "/?":
                        showHelp = true;
                        return options;
                    case "-p" or "--port":
                        options.Port = ParseInt(Next(), arg, 1, 65535);
                        break;
                    case "--host":
                        options.Host = Next();
                        break;
                    case "--path":
                        options.EndpointPath = Next();
                        break;
                    case "-n" or "--name":
                        options.ApplicationName = Next();
                        break;
                    case "--namespace":
                        options.NamespaceUri = Next();
                        break;
                    case "--root":
                        options.RootFolder = Next();
                        break;
                    case "--separator":
                        var separator = Next();
                        if (separator.Length != 1)
                        {
                            throw new ArgumentException("--separator must be a single character.");
                        }

                        options.PathSeparator = separator[0];
                        break;
                    case "--security":
                        var mode = Next();
                        if (!Enum.TryParse<SecurityMode>(mode, ignoreCase: true, out var security) || int.TryParse(mode, out _))
                        {
                            throw new ArgumentException($"Unknown security mode '{mode}' (use none, secure or all).");
                        }

                        options.Security = security;
                        break;
                    case "--user":
                        var credentials = Next();
                        var colon = credentials.IndexOf(':');
                        if (colon <= 0)
                        {
                            throw new ArgumentException("--user expects 'user:password'.");
                        }

                        options.UserName = credentials[..colon];
                        options.Password = credentials[(colon + 1)..];
                        break;
                    case "--allow-anonymous":
                        options.AllowAnonymous = true;
                        break;
                    case "--auto-accept":
                        options.AutoAcceptUntrustedCertificates = true;
                        break;
                    case "--interval":
                        options.SimulationIntervalMs = ParseInt(Next(), arg, 50, int.MaxValue);
                        break;
                    case "--pki":
                        options.PkiDirectory = Next();
                        break;
                    case "--quiet":
                        options.LogWrites = false;
                        break;
                    case "--validate":
                        options.ValidateOnly = true;
                        break;
                    default:
                        if (arg.StartsWith('-') && arg.Length > 1)
                        {
                            throw new ArgumentException($"Unknown option '{arg}'.");
                        }

                        options.InputPaths.Add(arg);
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        if (options.InputPaths.Count == 0)
        {
            error = "No input file given.";
            return null;
        }

        return options;
    }

    private static int ParseInt(string text, string option, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            throw new ArgumentException($"Option '{option}' expects a number between {min} and {max}.");
        }

        return value;
    }
}
