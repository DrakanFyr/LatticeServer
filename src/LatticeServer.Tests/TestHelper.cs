using Grpc.Core;
using NSubstitute;

namespace LatticeServer.Tests;

internal static class TestHelper
{
    /// <summary>
    /// Creates a mock ServerCallContext for unit testing gRPC service methods.
    /// </summary>
    public static ServerCallContext CreateCallContext(CancellationToken cancellationToken = default)
    {
        var context = Substitute.For<ServerCallContext>();
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }
}
