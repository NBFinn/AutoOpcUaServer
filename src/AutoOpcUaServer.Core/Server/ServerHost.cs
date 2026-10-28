using AutoOpcUaServer.Model;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace AutoOpcUaServer.Server;

/// <summary>Creates the OPC UA application configuration, the certificate and starts the server.</summary>
public sealed class ServerHost : IAsyncDisposable
{
    private readonly ServerOptions _options;
    private readonly ITelemetryContext _telemetry;
    private ApplicationInstance? _application;

    /// <param name="options">Server settings.</param>
    /// <param name="configureLogging">
    /// Where the OPC UA stack writes its warnings and errors. Defaults to the console.
    /// </param>
    public ServerHost(ServerOptions options, Action<ILoggingBuilder>? configureLogging = null)
    {
        _options = options;

        // The OPC UA stack only reports problems; normal operation is logged by the application.
        configureLogging ??= builder => builder.AddSimpleConsole(o => o.SingleLine = true);
        _telemetry = DefaultTelemetry.Create(builder =>
        {
            configureLogging(builder);
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    }

    public TagServer? Server { get; private set; }

    public async Task StartAsync(IReadOnlyList<TagDefinition> tags)
    {
        var configuration = await CreateConfigurationAsync();

        _application = new ApplicationInstance(_telemetry)
        {
            ApplicationName = _options.ApplicationName,
            ApplicationType = ApplicationType.Server,
            ApplicationConfiguration = configuration,
        };

        // Creates a self-signed application certificate in the pki directory on first start.
        var hasCertificate = await _application.CheckApplicationInstanceCertificatesAsync(silent: true);
        if (!hasCertificate)
        {
            throw new InvalidOperationException("The application certificate is invalid and could not be created.");
        }

        Server = new TagServer(tags, _options);
        await _application.StartAsync(Server);
    }

    public IEnumerable<string> EndpointUrls =>
        Server?.GetEndpoints().Select(e => $"{e.EndpointUrl} [{SecurityPolicies.GetDisplayName(e.SecurityPolicyUri)}, {e.SecurityMode}]").Distinct()
        ?? [];

    public async ValueTask DisposeAsync()
    {
        if (Server is not null)
        {
            await Server.StopAsync();
            Server.Dispose();
            Server = null;
        }
    }

    private async Task<ApplicationConfiguration> CreateConfigurationAsync()
    {
        var pki = Path.GetFullPath(_options.PkiDirectory);
        var hostName = _options.Host is "localhost" or "0.0.0.0" ? Utils.GetHostName() : _options.Host;

        var configuration = new ApplicationConfiguration(_telemetry)
        {
            ApplicationName = _options.ApplicationName,
            ApplicationUri = $"urn:{hostName}:{_options.ApplicationName}",
            ProductUri = "urn:AutoOpcUaServer",
            ApplicationType = ApplicationType.Server,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "own"),
                    SubjectName = $"CN={_options.ApplicationName}, DC={hostName}",
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "issuer"),
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "trusted"),
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "rejected"),
                },
                AutoAcceptUntrustedCertificates = _options.AutoAcceptUntrustedCertificates,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = { _options.EndpointUrl },
                SecurityPolicies = CreateSecurityPolicies(),
                UserTokenPolicies = CreateUserTokenPolicies(),
                MinRequestThreadCount = 5,
                MaxRequestThreadCount = 100,
                MaxQueuedRequestCount = 200,
                DiagnosticsEnabled = true,

                // Standalone server: do not try to register with a local discovery server.
                MaxRegistrationInterval = 0,
            },
        };

        await configuration.ValidateAsync(ApplicationType.Server);

        configuration.CertificateValidator.CertificateValidation += (_, e) =>
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted && _options.AutoAcceptUntrustedCertificates)
            {
                e.Accept = true;
            }
        };

        return configuration;
    }

    private ServerSecurityPolicyCollection CreateSecurityPolicies()
    {
        var policies = new ServerSecurityPolicyCollection();
        if (_options.Security is SecurityMode.None or SecurityMode.All)
        {
            policies.Add(new ServerSecurityPolicy
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
            });
        }

        if (_options.Security is SecurityMode.Secure or SecurityMode.All)
        {
            foreach (var policy in new[] { SecurityPolicies.Basic256Sha256, SecurityPolicies.Aes128_Sha256_RsaOaep, SecurityPolicies.Aes256_Sha256_RsaPss })
            {
                policies.Add(new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.Sign, SecurityPolicyUri = policy });
                policies.Add(new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = policy });
            }
        }

        return policies;
    }

    private UserTokenPolicyCollection CreateUserTokenPolicies()
    {
        var policies = new UserTokenPolicyCollection();
        if (_options.UserName is null || _options.AllowAnonymous)
        {
            policies.Add(new UserTokenPolicy(UserTokenType.Anonymous));
        }

        if (_options.UserName is not null)
        {
            // Without a security policy the password would be sent in clear text over None endpoints.
            policies.Add(new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 });
        }

        return policies;
    }
}
