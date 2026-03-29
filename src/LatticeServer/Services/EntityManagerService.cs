using Grpc.Core;
using Anduril.Entitymanager.V1;
using GrpcStatus = Grpc.Core.Status;

namespace LatticeServer.Services;

public class EntityManagerService : EntityManagerAPI.EntityManagerAPIBase
{
    private readonly ILogger<EntityManagerService> _logger;
    private readonly EntityStore _store;

    public EntityManagerService(ILogger<EntityManagerService> logger, EntityStore store)
    {
        _logger = logger;
        _store = store;
    }

    public override Task<PublishEntityResponse> PublishEntity(PublishEntityRequest request, ServerCallContext context)
    {
        var entity = request.Entity;
        if (entity == null)
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "Entity is required"));
        }

        if (string.IsNullOrEmpty(entity.EntityId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "entity_id is required"));
        }

        // When no_expiry is true, expiry_time is ignored per the proto spec.
        if (!entity.NoExpiry)
        {
            if (entity.ExpiryTime == null)
            {
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "expiry_time is required when no_expiry is false"));
            }

            var expiryDateTime = entity.ExpiryTime.ToDateTime();
            if (expiryDateTime <= DateTime.UtcNow)
            {
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "expiry_time must be in the future"));
            }

            if (expiryDateTime > DateTime.UtcNow.AddDays(30))
            {
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "expiry_time must be less than 30 days in the future"));
            }
        }

        _logger.LogInformation("PublishEntity called for entity {EntityId}", entity.EntityId);
        _store.PublishEntity(entity);

        return Task.FromResult(new PublishEntityResponse());
    }

    public override async Task<PublishEntitiesResponse> PublishEntities(
        IAsyncStreamReader<PublishEntitiesRequest> requestStream, ServerCallContext context)
    {
        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            var entity = request.Entity;
            if (entity == null || string.IsNullOrEmpty(entity.EntityId))
            {
                // Silently drop invalid entities per the proto spec
                continue;
            }

            if (!entity.NoExpiry)
            {
                if (entity.ExpiryTime != null)
                {
                    var expiryDateTime = entity.ExpiryTime.ToDateTime();
                    if (expiryDateTime <= DateTime.UtcNow || expiryDateTime > DateTime.UtcNow.AddDays(30))
                    {
                        // Silently drop invalid entities
                        continue;
                    }
                }
                else
                {
                    // Missing expiry_time and no_expiry not set — silently drop
                    continue;
                }
            }

            _logger.LogInformation("PublishEntities received entity {EntityId}", entity.EntityId);
            _store.PublishEntity(entity);
        }

        return new PublishEntitiesResponse();
    }

    public override Task<GetEntityResponse> GetEntity(GetEntityRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.EntityId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "entity_id is required"));
        }

        var entity = _store.GetEntity(request.EntityId);
        if (entity == null)
        {
            throw new RpcException(new GrpcStatus(StatusCode.NotFound, $"Entity {request.EntityId} not found"));
        }

        _logger.LogInformation("GetEntity called for {EntityId}", request.EntityId);
        return Task.FromResult(new GetEntityResponse { Entity = entity });
    }

    public override Task<OverrideEntityResponse> OverrideEntity(OverrideEntityRequest request, ServerCallContext context)
    {
        if (request.Entity == null || string.IsNullOrEmpty(request.Entity.EntityId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "Entity with entity_id is required"));
        }

        if (request.FieldPath.Count == 0)
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "At least one field_path is required"));
        }

        _logger.LogInformation("OverrideEntity called for entity {EntityId}", request.Entity.EntityId);
        var status = _store.ApplyOverride(request.Entity.EntityId, request.FieldPath, request.Entity);

        return Task.FromResult(new OverrideEntityResponse { Status = status });
    }

    public override Task<RemoveEntityOverrideResponse> RemoveEntityOverride(
        RemoveEntityOverrideRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.EntityId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "entity_id is required"));
        }

        if (request.FieldPath.Count == 0)
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "At least one field_path is required"));
        }

        _logger.LogInformation("RemoveEntityOverride called for {EntityId}", request.EntityId);
        _store.RemoveOverride(request.EntityId, request.FieldPath);

        return Task.FromResult(new RemoveEntityOverrideResponse());
    }

    public override async Task StreamEntityComponents(
        StreamEntityComponentsRequest request,
        IServerStreamWriter<StreamEntityComponentsResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("StreamEntityComponents started");

        // Send all pre-existing entities as PREEXISTING events
        var existingEntities = _store.GetAllEntities();
        foreach (var entity in existingEntities)
        {
            var preexistingEvent = new StreamEntityComponentsResponse
            {
                EntityEvent = new EntityEvent
                {
                    EventType = EventType.Preexisting,
                    Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    Entity = entity,
                }
            };
            await responseStream.WriteAsync(preexistingEvent, context.CancellationToken);
        }

        // If preexisting_only, close the stream after sending all pre-existing entities
        if (request.PreexistingOnly)
        {
            return;
        }

        // Subscribe to future events
        var subscription = _store.Subscribe();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        try
        {
            // Run heartbeat and event streaming concurrently
            var heartbeatTask = request.HeartbeatPeriodMillis > 0
                ? SendHeartbeats(responseStream, request.HeartbeatPeriodMillis, cts.Token)
                : Task.Delay(Timeout.Infinite, cts.Token);

            var streamTask = StreamEvents(subscription, responseStream, cts.Token);

            // When either task completes, cancel the other and await both
            await Task.WhenAny(heartbeatTask, streamTask);
            await cts.CancelAsync();

            // Await both to observe any exceptions (suppress cancellation)
            try { await heartbeatTask; } catch (OperationCanceledException) { }
            try { await streamTask; } catch (OperationCanceledException) { }
        }
        finally
        {
            _store.Unsubscribe(subscription);
        }
    }

    private static async Task SendHeartbeats(
        IServerStreamWriter<StreamEntityComponentsResponse> responseStream,
        uint periodMillis,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(periodMillis), cancellationToken);
            var heartbeat = new StreamEntityComponentsResponse
            {
                Heartbeat = new Heartbeat
                {
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                }
            };
            await responseStream.WriteAsync(heartbeat, cancellationToken);
        }
    }

    private static async Task StreamEvents(
        System.Threading.Channels.Channel<EntityEvent> subscription,
        IServerStreamWriter<StreamEntityComponentsResponse> responseStream,
        CancellationToken cancellationToken)
    {
        await foreach (var entityEvent in subscription.Reader.ReadAllAsync(cancellationToken))
        {
            var response = new StreamEntityComponentsResponse
            {
                EntityEvent = entityEvent,
            };
            await responseStream.WriteAsync(response, cancellationToken);
        }
    }
}
