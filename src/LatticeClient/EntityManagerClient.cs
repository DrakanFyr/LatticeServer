using Anduril.Entitymanager.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace LatticeClient;

public class EntityManagerClient : IEntityManagerClient
{
    private readonly EntityManagerAPI.EntityManagerAPIClient _client;
    private readonly GrpcChannel? _ownedChannel;

    public EntityManagerClient(GrpcChannel channel)
    {
        _client = new EntityManagerAPI.EntityManagerAPIClient(channel);
    }

    public EntityManagerClient(string address)
    {
        _ownedChannel = GrpcChannel.ForAddress(address);
        _client = new EntityManagerAPI.EntityManagerAPIClient(_ownedChannel);
    }

    public async Task PublishEntityAsync(
        Entity entity,
        CancellationToken cancellationToken = default)
    {
        var request = new PublishEntityRequest { Entity = entity };
        await _client.PublishEntityAsync(request, cancellationToken: cancellationToken);
    }

    public async Task PublishEntitiesAsync(
        IAsyncEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        using var call = _client.PublishEntities(cancellationToken: cancellationToken);

        await foreach (var entity in entities.WithCancellation(cancellationToken))
        {
            await call.RequestStream.WriteAsync(new PublishEntitiesRequest { Entity = entity }, cancellationToken);
        }

        await call.RequestStream.CompleteAsync();
        await call.ResponseAsync;
    }

    public async Task PublishEntitiesAsync(
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        using var call = _client.PublishEntities(cancellationToken: cancellationToken);

        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await call.RequestStream.WriteAsync(new PublishEntitiesRequest { Entity = entity }, cancellationToken);
        }

        await call.RequestStream.CompleteAsync();
        await call.ResponseAsync;
    }

    public async Task<Entity> GetEntityAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var request = new GetEntityRequest { EntityId = entityId };
        var response = await _client.GetEntityAsync(request, cancellationToken: cancellationToken);
        return response.Entity;
    }

    public async Task OverrideEntityAsync(
        Entity entity,
        IEnumerable<string> fieldPaths,
        CancellationToken cancellationToken = default)
    {
        var request = new OverrideEntityRequest { Entity = entity };
        request.FieldPath.AddRange(fieldPaths);
        await _client.OverrideEntityAsync(request, cancellationToken: cancellationToken);
    }

    public async Task RemoveEntityOverrideAsync(
        string entityId,
        IEnumerable<string> fieldPaths,
        CancellationToken cancellationToken = default)
    {
        var request = new RemoveEntityOverrideRequest { EntityId = entityId };
        request.FieldPath.AddRange(fieldPaths);
        await _client.RemoveEntityOverrideAsync(request, cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<StreamEntityComponentsResponse> StreamEntityComponentsAsync(
        StreamEntityComponentsRequest? request = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request ??= new StreamEntityComponentsRequest();

        using var call = _client.StreamEntityComponents(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    public void Dispose()
    {
        _ownedChannel?.Dispose();
        GC.SuppressFinalize(this);
    }
}
