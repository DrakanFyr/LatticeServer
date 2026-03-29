using Anduril.Entitymanager.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LatticeServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LatticeServer.Tests;

public class EntityManagerServiceTests
{
    private readonly EntityStore _store = new();
    private readonly EntityManagerService _service;
    private readonly ServerCallContext _context = TestHelper.CreateCallContext();

    public EntityManagerServiceTests()
    {
        _service = new EntityManagerService(NullLogger<EntityManagerService>.Instance, _store);
    }

    private static Entity CreateValidEntity(string entityId = "test-entity-1") => new()
    {
        EntityId = entityId,
        IsLive = true,
        NoExpiry = true,
        Description = "Test entity",
        Aliases = new Aliases { Name = "Test" },
    };

    private static Entity CreateEntityWithExpiry(string entityId, DateTime expiryTime) => new()
    {
        EntityId = entityId,
        IsLive = true,
        ExpiryTime = Timestamp.FromDateTime(expiryTime.ToUniversalTime()),
        Description = "Test entity with expiry",
    };

    // ───────────────────────────────────────────────
    // PublishEntity
    // ───────────────────────────────────────────────

    [Fact]
    public async Task PublishEntity_ValidEntity_Succeeds()
    {
        // Proto: "Create or update an entity and get a response confirming whether the Entity Manager API
        // successfully processes the entity."
        var request = new PublishEntityRequest { Entity = CreateValidEntity() };

        var response = await _service.PublishEntity(request, _context);

        Assert.NotNull(response);
        var stored = _store.GetEntity("test-entity-1");
        Assert.NotNull(stored);
        Assert.Equal("Test entity", stored.Description);
    }

    [Fact]
    public async Task PublishEntity_NullEntity_ThrowsInvalidArgument()
    {
        var request = new PublishEntityRequest();

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.PublishEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task PublishEntity_MissingEntityId_ThrowsInvalidArgument()
    {
        // Proto: "entity_id: Unique string identifier" (required)
        var request = new PublishEntityRequest
        {
            Entity = new Entity { IsLive = true, NoExpiry = true }
        };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.PublishEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("entity_id", ex.Status.Detail);
    }

    [Fact]
    public async Task PublishEntity_ExpiryTimeInPast_ThrowsInvalidArgument()
    {
        // Proto: "expiry_time must be greater than the current time"
        var request = new PublishEntityRequest
        {
            Entity = CreateEntityWithExpiry("past-entity", DateTime.UtcNow.AddMinutes(-5))
        };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.PublishEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("future", ex.Status.Detail);
    }

    [Fact]
    public async Task PublishEntity_ExpiryTimeOver30Days_ThrowsInvalidArgument()
    {
        // Proto: "less than 30 days in the future"
        var request = new PublishEntityRequest
        {
            Entity = CreateEntityWithExpiry("far-entity", DateTime.UtcNow.AddDays(31))
        };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.PublishEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("30 days", ex.Status.Detail);
    }

    [Fact]
    public async Task PublishEntity_ValidExpiryTime_Succeeds()
    {
        // Proto: "expiry_time must be greater than the current time and less than 30 days in the future"
        var request = new PublishEntityRequest
        {
            Entity = CreateEntityWithExpiry("expiry-entity", DateTime.UtcNow.AddDays(1))
        };

        var response = await _service.PublishEntity(request, _context);
        Assert.NotNull(response);
        Assert.NotNull(_store.GetEntity("expiry-entity"));
    }

    [Fact]
    public async Task PublishEntity_MissingExpiryTime_WhenNoExpiryFalse_ThrowsInvalidArgument()
    {
        // Proto: "expiry_time" is required when no_expiry is not set
        var request = new PublishEntityRequest
        {
            Entity = new Entity
            {
                EntityId = "no-expiry-entity",
                IsLive = true,
                // NoExpiry defaults to false, no ExpiryTime set
            }
        };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.PublishEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task PublishEntity_NoExpiry_IgnoresExpiryTimeValidation()
    {
        // Proto: "When no_expiry is true, expiry_time is ignored per the proto spec."
        var entity = new Entity
        {
            EntityId = "no-expiry-entity",
            IsLive = true,
            NoExpiry = true,
            // No ExpiryTime set — should be fine since no_expiry is true
        };

        var response = await _service.PublishEntity(new PublishEntityRequest { Entity = entity }, _context);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task PublishEntity_UpdateExistingEntity_Overwrites()
    {
        // Proto: "if an entity_id is provided, Entity Manager updates the entity"
        var entity = CreateValidEntity("update-me");
        await _service.PublishEntity(new PublishEntityRequest { Entity = entity }, _context);

        entity.Description = "Updated description";
        await _service.PublishEntity(new PublishEntityRequest { Entity = entity }, _context);

        var stored = _store.GetEntity("update-me");
        Assert.Equal("Updated description", stored!.Description);
    }

    [Fact]
    public async Task PublishEntity_IsLiveFalse_DeletesEntity()
    {
        // Proto: "is_live: Boolean that when true, creates or updates the entity.
        //         If false and the entity is still live, triggers a DELETE event."
        var entity = CreateValidEntity("delete-me");
        await _service.PublishEntity(new PublishEntityRequest { Entity = entity }, _context);
        Assert.NotNull(_store.GetEntity("delete-me"));

        var deleteEntity = new Entity
        {
            EntityId = "delete-me",
            IsLive = false,
            ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(10)),
        };
        await _service.PublishEntity(new PublishEntityRequest { Entity = deleteEntity }, _context);

        Assert.Null(_store.GetEntity("delete-me"));
    }

    // ───────────────────────────────────────────────
    // GetEntity
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetEntity_ExistingEntity_ReturnsEntity()
    {
        // Proto: "Get an entity using its entityId."
        var entity = CreateValidEntity("get-me");
        _store.PublishEntity(entity);

        var response = await _service.GetEntity(
            new GetEntityRequest { EntityId = "get-me" }, _context);

        Assert.NotNull(response.Entity);
        Assert.Equal("get-me", response.Entity.EntityId);
    }

    [Fact]
    public async Task GetEntity_NonExistentEntity_ThrowsNotFound()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.GetEntity(new GetEntityRequest { EntityId = "does-not-exist" }, _context));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetEntity_EmptyEntityId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.GetEntity(new GetEntityRequest { EntityId = "" }, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ───────────────────────────────────────────────
    // OverrideEntity
    // ───────────────────────────────────────────────

    [Fact]
    public async Task OverrideEntity_ValidRequest_ReturnsApplied()
    {
        // Proto: "Override an Entity Component. An override is a definitive change to entity data."
        _store.PublishEntity(CreateValidEntity("override-me"));

        var request = new OverrideEntityRequest
        {
            Entity = new Entity { EntityId = "override-me", Description = "overridden" },
            FieldPath = { "description" },
        };

        var response = await _service.OverrideEntity(request, _context);
        Assert.Equal(OverrideStatus.Applied, response.Status);
    }

    [Fact]
    public async Task OverrideEntity_NonExistentEntity_ReturnsRejected()
    {
        var request = new OverrideEntityRequest
        {
            Entity = new Entity { EntityId = "no-such-entity", Description = "overridden" },
            FieldPath = { "description" },
        };

        var response = await _service.OverrideEntity(request, _context);
        Assert.Equal(OverrideStatus.Rejected, response.Status);
    }

    [Fact]
    public async Task OverrideEntity_MissingEntityId_ThrowsInvalidArgument()
    {
        var request = new OverrideEntityRequest
        {
            Entity = new Entity { Description = "overridden" },
            FieldPath = { "description" },
        };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.OverrideEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task OverrideEntity_MissingFieldPath_ThrowsInvalidArgument()
    {
        // Proto: "The field paths that will be extracted from the Entity" (required)
        var request = new OverrideEntityRequest
        {
            Entity = new Entity { EntityId = "override-me", Description = "overridden" },
            // No field_path
        };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.OverrideEntity(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ───────────────────────────────────────────────
    // RemoveEntityOverride
    // ───────────────────────────────────────────────

    [Fact]
    public async Task RemoveEntityOverride_ExistingOverride_Succeeds()
    {
        // Proto: "Remove an override for an Entity component."
        _store.PublishEntity(CreateValidEntity("remove-override"));
        _store.ApplyOverride("remove-override", ["description"],
            new Entity { EntityId = "remove-override", Description = "overridden" });

        var request = new RemoveEntityOverrideRequest
        {
            EntityId = "remove-override",
            FieldPath = { "description" },
        };

        var response = await _service.RemoveEntityOverride(request, _context);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task RemoveEntityOverride_MissingEntityId_ThrowsInvalidArgument()
    {
        var request = new RemoveEntityOverrideRequest { FieldPath = { "description" } };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.RemoveEntityOverride(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task RemoveEntityOverride_MissingFieldPath_ThrowsInvalidArgument()
    {
        var request = new RemoveEntityOverrideRequest { EntityId = "some-entity" };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.RemoveEntityOverride(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ───────────────────────────────────────────────
    // StreamEntityComponents (preexisting_only mode)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StreamEntityComponents_PreexistingOnly_SendsExistingEntitiesThenCloses()
    {
        // Proto: "Subscribe to a finite stream of preexisting events which closes when there are no
        //         additional pre-existing events to process."
        _store.PublishEntity(CreateValidEntity("stream-1"));
        _store.PublishEntity(CreateValidEntity("stream-2"));

        var request = new StreamEntityComponentsRequest { PreexistingOnly = true };
        var writer = new TestServerStreamWriter<StreamEntityComponentsResponse>();

        await _service.StreamEntityComponents(request, writer, _context);

        Assert.Equal(2, writer.Messages.Count);
        Assert.All(writer.Messages, m =>
        {
            Assert.NotNull(m.EntityEvent);
            Assert.Equal(EventType.Preexisting, m.EntityEvent.EventType);
        });
    }

    [Fact]
    public async Task StreamEntityComponents_NoEntities_PreexistingOnly_ReturnsEmpty()
    {
        var request = new StreamEntityComponentsRequest { PreexistingOnly = true };
        var writer = new TestServerStreamWriter<StreamEntityComponentsResponse>();

        await _service.StreamEntityComponents(request, writer, _context);

        Assert.Empty(writer.Messages);
    }

    // ───────────────────────────────────────────────
    // EntityStore event generation
    // ───────────────────────────────────────────────

    [Fact]
    public void Store_PublishNewEntity_GeneratesCreatedEvent()
    {
        // Proto: "entity was created." (EVENT_TYPE_CREATED)
        var entity = CreateValidEntity("new-entity");
        var evt = _store.PublishEntity(entity);

        Assert.Equal(EventType.Created, evt.EventType);
        Assert.Equal("new-entity", evt.Entity.EntityId);
    }

    [Fact]
    public void Store_PublishExistingEntity_GeneratesUpdateEvent()
    {
        // Proto: "entity was updated." (EVENT_TYPE_UPDATE)
        var entity = CreateValidEntity("update-entity");
        _store.PublishEntity(entity);

        entity.Description = "changed";
        var evt = _store.PublishEntity(entity);

        Assert.Equal(EventType.Update, evt.EventType);
    }

    [Fact]
    public void Store_PublishIsLiveFalse_GeneratesDeletedEvent()
    {
        // Proto: "entity was deleted." (EVENT_TYPE_DELETED)
        _store.PublishEntity(CreateValidEntity("delete-entity"));

        var deleteEntity = new Entity { EntityId = "delete-entity", IsLive = false };
        var evt = _store.PublishEntity(deleteEntity);

        Assert.Equal(EventType.Deleted, evt.EventType);
    }

    [Fact]
    public void Store_Subscription_ReceivesEvents()
    {
        // Proto: "Establishes a server streaming RPC that returns a continuous stream of entities"
        var subscription = _store.Subscribe();

        _store.PublishEntity(CreateValidEntity("sub-entity"));

        Assert.True(subscription.Reader.TryRead(out var evt));
        Assert.Equal(EventType.Created, evt!.EventType);
        Assert.Equal("sub-entity", evt.Entity.EntityId);

        _store.Unsubscribe(subscription);
    }
}

/// <summary>
/// Test implementation of IServerStreamWriter that captures messages.
/// </summary>
internal class TestServerStreamWriter<T> : IServerStreamWriter<T>
{
    public List<T> Messages { get; } = [];
    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
