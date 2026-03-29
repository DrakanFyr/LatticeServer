using Grpc.Net.Client;

namespace LatticeClient;

public class LatticeConnection : IDisposable
{
    private readonly GrpcChannel? _grpcChannel;
    private readonly HttpClient? _restHttpClient;

    /// <summary>
    /// Entity Manager client. Available for both gRPC and REST transports.
    /// </summary>
    public IEntityManagerClient EntityManager { get; }

    /// <summary>
    /// Task Manager client. Available for both gRPC and REST transports.
    /// </summary>
    public ITaskManagerClient TaskManager { get; }

    /// <summary>
    /// Object Manager client (REST-only). Available for both transport modes since
    /// the Objects API has no gRPC equivalent.
    /// </summary>
    public ObjectManagerClient ObjectManager { get; }

    /// <summary>
    /// OAuth client (REST-only). Available for both transport modes since
    /// the OAuth API has no gRPC equivalent.
    /// </summary>
    public OAuthClient OAuth { get; }

    /// <summary>
    /// The transport mode in use by this connection.
    /// </summary>
    public TransportMode Transport { get; }

    /// <summary>
    /// Create a connection using gRPC transport (backward-compatible constructor).
    /// </summary>
    public LatticeConnection(string address)
        : this(address, TransportMode.Grpc)
    {
    }

    /// <summary>
    /// Create a connection using an existing gRPC channel (backward-compatible constructor).
    /// </summary>
    public LatticeConnection(GrpcChannel channel)
    {
        _grpcChannel = channel;
        Transport = TransportMode.Grpc;

        EntityManager = new EntityManagerClient(channel);
        TaskManager = new TaskManagerClient(channel);

        // REST-only clients use HTTP even when the main transport is gRPC.
        // Extract the base URL from the gRPC channel target for REST endpoints.
        var baseUrl = channel.Target;
        _restHttpClient = RestEntityManagerClient.CreateHttpClient(baseUrl, bearerToken: null);
        ObjectManager = new ObjectManagerClient(_restHttpClient);
        OAuth = new OAuthClient(_restHttpClient);
    }

    /// <summary>
    /// Create a connection with the specified transport mode.
    /// </summary>
    /// <param name="address">The server address (e.g., "http://localhost:5007").</param>
    /// <param name="transport">The transport protocol to use for Entities and Tasks APIs.</param>
    /// <param name="bearerToken">Optional bearer token for REST API authentication.</param>
    public LatticeConnection(string address, TransportMode transport, string? bearerToken = null)
    {
        Transport = transport;

        if (transport == TransportMode.Grpc)
        {
            _grpcChannel = GrpcChannel.ForAddress(address);
            EntityManager = new EntityManagerClient(_grpcChannel);
            TaskManager = new TaskManagerClient(_grpcChannel);
        }
        else
        {
            _restHttpClient = RestEntityManagerClient.CreateHttpClient(address, bearerToken);
            EntityManager = new RestEntityManagerClient(_restHttpClient);
            TaskManager = new RestTaskManagerClient(_restHttpClient);
        }

        // REST-only clients are always available
        var restClient = _restHttpClient ?? RestEntityManagerClient.CreateHttpClient(address, bearerToken);
        if (_restHttpClient == null)
            _restHttpClient = restClient;

        ObjectManager = new ObjectManagerClient(restClient);
        OAuth = new OAuthClient(restClient);
    }

    public void Dispose()
    {
        EntityManager.Dispose();
        TaskManager.Dispose();
        ObjectManager.Dispose();
        OAuth.Dispose();
        _grpcChannel?.Dispose();
        // Don't dispose _restHttpClient separately; it's shared with the REST clients
        // which handle their own disposal via the Dispose calls above.
        // If we created it ourselves (gRPC mode), dispose it here.
        if (Transport == TransportMode.Grpc)
            _restHttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}
