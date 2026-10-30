using System.Net;
using System.Net.Sockets;
using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;
using AutoOpcUaServer.Server;
using Opc.Ua;
using Opc.Ua.Client;

namespace AutoOpcUaServer.Tests;

/// <summary>Starts the real server on a free port and talks to it with the OPC UA client stack.</summary>
public sealed class ServerIntegrationTests : IAsyncLifetime
{
    private const string Csv = """
        Path;DataType;Value;Access;Description;Simulation;Min;Max
        Plant/Line1/Setpoint;Double;22,5;RW;Sollwert;;;
        Plant/Line1/Temperature;Double;0;R;;Ramp;0;100
        Plant/Line1/Recipe;Int32[];1|2|3;RW;;;;
        Plant/Mode;String;Auto;R;;;;
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("autoopcua-server-").FullName;
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });
    private ServerHost? _host;
    private ISession? _session;
    private string _endpointUrl = "";
    private ushort _ns;

    public async Task InitializeAsync()
    {
        var csv = Path.Combine(_dir, "tags.csv");
        await File.WriteAllTextAsync(csv, Csv);
        var import = new TagImporter().Import([csv]);
        Assert.Empty(import.Errors);

        var options = new ServerOptions
        {
            Port = GetFreePort(),
            PkiDirectory = Path.Combine(_dir, "pki"),
            SimulationIntervalMs = 100,
            LogWrites = false,
            UserName = "admin",
            Password = "geheim",
            AllowAnonymous = true,
        };

        _host = new ServerHost(options);
        await _host.StartAsync(import.Tags);
        _endpointUrl = options.EndpointUrl;
        _session = await ConnectAsync(new UserIdentity());
        _ns = (ushort)_session.NamespaceUris.GetIndex(options.NamespaceUri);
    }

    public async Task DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.CloseAsync();
            _session.Dispose();
        }

        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        Directory.Delete(_dir, recursive: true);
    }

    private ISession Session => _session ?? throw new InvalidOperationException("Not connected.");

    private NodeId Id(string id) => new(id, _ns);

    [Fact]
    public async Task Imported_values_can_be_read()
    {
        var setpoint = await Session.ReadValueAsync(Id("Plant.Line1.Setpoint"));
        var recipe = await Session.ReadValueAsync(Id("Plant.Line1.Recipe"));
        var mode = await Session.ReadValueAsync(Id("Plant.Mode"));

        Assert.Equal(22.5, setpoint.Value);
        Assert.Equal(new[] { 1, 2, 3 }, recipe.Value);
        Assert.Equal("Auto", mode.Value);
    }

    [Fact]
    public async Task Writable_variable_accepts_writes()
    {
        var status = await WriteAsync("Plant.Line1.Setpoint", 30.0);

        Assert.Equal(StatusCodes.Good, status);
        Assert.Equal(30.0, (await Session.ReadValueAsync(Id("Plant.Line1.Setpoint"))).Value);
    }

    [Fact]
    public async Task Read_only_variable_rejects_writes()
    {
        var status = await WriteAsync("Plant.Mode", "Manual");

        Assert.Equal(StatusCodes.BadNotWritable, status);
    }

    [Fact]
    public async Task Simulated_variable_changes()
    {
        var first = (double)(await Session.ReadValueAsync(Id("Plant.Line1.Temperature"))).Value;
        await Task.Delay(400);
        var second = (double)(await Session.ReadValueAsync(Id("Plant.Line1.Temperature"))).Value;

        Assert.NotEqual(first, second);
        Assert.InRange(second, 0, 100);
    }

    [Fact]
    public async Task Folders_are_browsable_from_objects()
    {
        var browser = new Browser(Session) { BrowseDirection = BrowseDirection.Forward, ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences };
        var objects = await browser.BrowseAsync(ObjectIds.ObjectsFolder);
        var plant = Assert.Single(objects, r => r.BrowseName.Name == "Plant");

        var children = await browser.BrowseAsync(ExpandedNodeId.ToNodeId(plant.NodeId, Session.NamespaceUris));

        Assert.Equal(["Line1", "Mode"], children.Select(c => c.BrowseName.Name).Order());
    }

    [Fact]
    public async Task Tags_added_at_runtime_are_browsable_and_readable()
    {
        var nodeManager = _host!.Server!.TagNodeManager!;

        Assert.Null(await nodeManager.AddTagAsync(new TagDefinition { Path = "Plant/Line1/Added", DataType = TagDataType.Int32, Value = "7" }));
        Assert.Null(await nodeManager.AddTagAsync(new TagDefinition { Path = "Extra/Sub/Flag", DataType = TagDataType.Boolean, Value = "true" }));
        Assert.Null(await nodeManager.AddTagAsync(new TagDefinition { Path = "Solo", DataType = TagDataType.String, Value = "hi" }));
        Assert.Null(await nodeManager.AddTagAsync(new TagDefinition { Path = "Extra/Counter", DataType = TagDataType.Int16, Simulation = SimulationKind.Counter }));

        Assert.Equal(7, (await Session.ReadValueAsync(Id("Plant.Line1.Added"))).Value);
        Assert.Equal(true, (await Session.ReadValueAsync(Id("Extra.Sub.Flag"))).Value);
        Assert.Equal("hi", (await Session.ReadValueAsync(Id("Solo"))).Value);

        var browser = new Browser(Session) { BrowseDirection = BrowseDirection.Forward, ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences };
        var objects = (await browser.BrowseAsync(ObjectIds.ObjectsFolder)).Select(r => r.BrowseName.Name).ToList();
        Assert.Contains("Extra", objects);
        Assert.Contains("Solo", objects);

        var line1 = await browser.BrowseAsync(Id("Plant.Line1"));
        Assert.Contains(line1, r => r.BrowseName.Name == "Added");

        await Task.Delay(400);
        Assert.NotEqual((short)0, (await Session.ReadValueAsync(Id("Extra.Counter"))).Value);
    }

    [Fact]
    public async Task Conflicting_runtime_tag_is_rejected()
    {
        var nodeManager = _host!.Server!.TagNodeManager!;

        var duplicate = await nodeManager.AddTagAsync(new TagDefinition { Path = "Plant/Mode", DataType = TagDataType.String });
        var folderClash = await nodeManager.AddTagAsync(new TagDefinition { Path = "Plant/Line1", DataType = TagDataType.String });

        Assert.Contains("already defined", duplicate);
        Assert.Contains("both as a variable and as a folder", folderClash);
    }

    [Fact]
    public async Task User_with_correct_password_can_log_in()
    {
        using var session = await ConnectAsync(new UserIdentity("admin", "geheim"u8));

        Assert.Equal("Auto", (await session.ReadValueAsync(Id("Plant.Mode"))).Value);
        await session.CloseAsync();
    }

    [Fact]
    public async Task User_with_wrong_password_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<ServiceResultException>(() => ConnectAsync(new UserIdentity("admin", "falsch"u8)));

        Assert.Equal(StatusCodes.BadUserAccessDenied, ex.StatusCode);
    }

    private async Task<StatusCode> WriteAsync(string id, object value)
    {
        var nodes = new WriteValueCollection
        {
            new WriteValue { NodeId = Id(id), AttributeId = Attributes.Value, Value = new DataValue(new Variant(value)) },
        };

        var response = await Session.WriteAsync(null, nodes, CancellationToken.None);
        return response.Results[0];
    }

    private async Task<ISession> ConnectAsync(IUserIdentity identity)
    {
        var config = new ApplicationConfiguration(_telemetry)
        {
            ApplicationName = "AutoOpcUaServer.Tests",
            ApplicationUri = "urn:localhost:AutoOpcUaServer.Tests",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(_dir, "client", "own"), SubjectName = "CN=AutoOpcUaServer.Tests" },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(_dir, "client", "issuer") },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(_dir, "client", "trusted") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(_dir, "client", "rejected") },
                AutoAcceptUntrustedCertificates = true,
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 10000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
        };
        await config.ValidateAsync(ApplicationType.Client);

        var endpointDescription = await CoreClientUtils.SelectEndpointAsync(config, _endpointUrl, useSecurity: false, _telemetry);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(config));
        return await new DefaultSessionFactory(_telemetry).CreateAsync(
            config, endpoint, false, false, "test", 60000, identity, null, CancellationToken.None);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
