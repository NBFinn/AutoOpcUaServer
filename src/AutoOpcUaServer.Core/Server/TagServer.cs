using System.Security.Cryptography;
using System.Text;
using AutoOpcUaServer.Model;
using Opc.Ua;
using Opc.Ua.Server;

namespace AutoOpcUaServer.Server;

/// <summary>OPC UA server that exposes the imported tags through a <see cref="TagNodeManager"/>.</summary>
public sealed class TagServer : StandardServer
{
    private readonly IReadOnlyList<TagDefinition> _tags;
    private readonly ServerOptions _options;

    public TagServer(IReadOnlyList<TagDefinition> tags, ServerOptions options)
    {
        _tags = tags;
        _options = options;
    }

    public TagNodeManager? TagNodeManager { get; private set; }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        TagNodeManager = new TagNodeManager(server, configuration, _tags, _options);
        return new MasterNodeManager(server, configuration, null, TagNodeManager);
    }

    protected override ServerProperties LoadServerProperties() => new()
    {
        ManufacturerName = "AutoOpcUaServer",
        ProductName = _options.ApplicationName,
        ProductUri = "urn:AutoOpcUaServer",
        SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
        BuildNumber = Utils.GetAssemblyBuildNumber(),
        BuildDate = Utils.GetAssemblyTimestamp(),
    };

    protected override void OnServerStarted(IServerInternal server)
    {
        base.OnServerStarted(server);
        server.SessionManager.ImpersonateUser += (_, args) => OnImpersonateUser(args);
    }

    private void OnImpersonateUser(ImpersonateEventArgs args)
    {
        switch (args.NewIdentity)
        {
            case UserNameIdentityToken token:
                if (_options.UserName is null
                    || token.UserName != _options.UserName
                    || !CryptographicOperations.FixedTimeEquals(token.DecryptedPassword ?? [], Encoding.UTF8.GetBytes(_options.Password ?? "")))
                {
                    throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Invalid user name or password.");
                }

                args.Identity = new UserIdentity(token);
                break;

            case AnonymousIdentityToken or null:
                if (_options.UserName is not null && !_options.AllowAnonymous)
                {
                    throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected, "Anonymous login is disabled.");
                }

                break;

            default:
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected, "Unsupported user identity token.");
        }
    }
}
