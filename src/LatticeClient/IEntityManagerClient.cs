using Anduril.Entitymanager.V1;

namespace LatticeClient;

/// <summary>
/// Interface for Entity Manager operations, supported by both gRPC and REST transports.
/// </summary>
public interface IEntityManagerClient : IDisposable
{
    /// <summary>
    /// Create or update a single entity.
    /// </summary>
    Task PublishEntityAsync(
        Entity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update multiple entities from an async enumerable stream.
    /// </summary>
    Task PublishEntitiesAsync(
        IAsyncEnumerable<Entity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update multiple entities from an enumerable collection.
    /// </summary>
    Task PublishEntitiesAsync(
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an entity by its ID.
    /// </summary>
    Task<Entity> GetEntityAsync(
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Override entity component fields.
    /// </summary>
    Task OverrideEntityAsync(
        Entity entity,
        IEnumerable<string> fieldPaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove entity component overrides.
    /// </summary>
    Task RemoveEntityOverrideAsync(
        string entityId,
        IEnumerable<string> fieldPaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream entity component events. Returns entity events as they occur,
    /// starting with preexisting entities.
    /// </summary>
    IAsyncEnumerable<StreamEntityComponentsResponse> StreamEntityComponentsAsync(
        StreamEntityComponentsRequest? request = null,
        CancellationToken cancellationToken = default);
}
