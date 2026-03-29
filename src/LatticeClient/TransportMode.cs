namespace LatticeClient;

/// <summary>
/// Specifies the transport protocol to use for communicating with LatticeServer.
/// </summary>
public enum TransportMode
{
    /// <summary>
    /// Use gRPC transport (HTTP/2, protobuf binary serialization).
    /// </summary>
    Grpc,

    /// <summary>
    /// Use REST transport (HTTP/1.1 or HTTP/2, JSON serialization, SSE for streaming).
    /// </summary>
    Rest,
}
