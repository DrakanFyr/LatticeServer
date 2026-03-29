using System.Net;
using System.Text;
using Anduril.Entitymanager.V1;
using Google.Protobuf.WellKnownTypes;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for PUT /api/v1/entities (Publish) and GET /api/v1/entities/{entityId} (Get).
/// </summary>
public class EntitiesControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EntitiesControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a fresh HttpClient backed by its own EntityStore so tests don't share state.
    /// Returns both the client and the store for direct verification.
    /// </summary>
    private (HttpClient Client, EntityStore Store) CreateIsolatedClient()
    {
        var store = new EntityStore();
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(store);
            });
        }).CreateClient();
        return (client, store);
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static string ValidEntityJson(string entityId = "test-entity-1", bool noExpiry = true) =>
        ProtobufJsonConverter.ToJson(new Entity
        {
            EntityId = entityId,
            IsLive = true,
            NoExpiry = noExpiry,
            Description = "Test entity",
            Aliases = new Aliases { Name = "Test" },
        });

    private static string EntityWithExpiryJson(string entityId, DateTime expiryTime) =>
        ProtobufJsonConverter.ToJson(new Entity
        {
            EntityId = entityId,
            IsLive = true,
            ExpiryTime = Timestamp.FromDateTime(expiryTime.ToUniversalTime()),
            Description = "Test entity with expiry",
        });

    // ───────────────────────────────────────────────
    // PUT /api/v1/entities - Publish Entity
    // ───────────────────────────────────────────────

    [Fact]
    public async Task PublishEntity_ValidEntity_Returns200WithEntity()
    {
        // Doc: "Publish an entity for ingest into the Entities API."
        // Doc: "The request was valid and accepted." (200)
        var (client, store) = CreateIsolatedClient();

        var response = await client.PutAsync("/api/v1/entities", JsonContent(ValidEntityJson()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var entity = ProtobufJsonConverter.FromJson<Entity>(body);
        Assert.Equal("test-entity-1", entity.EntityId);
        Assert.NotNull(store.GetEntity("test-entity-1"));
    }

    [Fact]
    public async Task PublishEntity_MissingEntityId_Returns400()
    {
        // Doc: "An entity ID must be provided when calling this endpoint."
        var (client, _) = CreateIsolatedClient();
        var json = ProtobufJsonConverter.ToJson(new Entity { IsLive = true, NoExpiry = true });

        var response = await client.PutAsync("/api/v1/entities", JsonContent(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("entity_id", body);
        Assert.Contains("INVALID_ARGUMENT", body);
    }

    [Fact]
    public async Task PublishEntity_InvalidJson_Returns400()
    {
        // Doc: "returns an error if the entity is invalid." (400 Bad request)
        var (client, _) = CreateIsolatedClient();

        var response = await client.PutAsync("/api/v1/entities", JsonContent("{not valid json}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ARGUMENT", body);
    }

    [Fact]
    public async Task PublishEntity_ExpiryTimeInPast_Returns400()
    {
        // Doc: "expiry_time must be greater than the current time"
        var (client, _) = CreateIsolatedClient();
        var json = EntityWithExpiryJson("past-entity", DateTime.UtcNow.AddMinutes(-5));

        var response = await client.PutAsync("/api/v1/entities", JsonContent(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("future", body);
    }

    [Fact]
    public async Task PublishEntity_ExpiryTimeOver30Days_Returns400()
    {
        // Doc: "less than 30 days in the future"
        var (client, _) = CreateIsolatedClient();
        var json = EntityWithExpiryJson("far-entity", DateTime.UtcNow.AddDays(31));

        var response = await client.PutAsync("/api/v1/entities", JsonContent(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("30 days", body);
    }

    [Fact]
    public async Task PublishEntity_MissingExpiryTime_WhenNoExpiryFalse_Returns400()
    {
        // Doc: "expiry_time is required when no_expiry is false"
        var (client, _) = CreateIsolatedClient();
        var json = ProtobufJsonConverter.ToJson(new Entity
        {
            EntityId = "no-expiry-entity",
            IsLive = true,
            // NoExpiry defaults to false, no ExpiryTime set
        });

        var response = await client.PutAsync("/api/v1/entities", JsonContent(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("expiry_time", body);
    }

    [Fact]
    public async Task PublishEntity_ValidExpiryTime_Returns200()
    {
        var (client, store) = CreateIsolatedClient();
        var json = EntityWithExpiryJson("expiry-entity", DateTime.UtcNow.AddDays(1));

        var response = await client.PutAsync("/api/v1/entities", JsonContent(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(store.GetEntity("expiry-entity"));
    }

    [Fact]
    public async Task PublishEntity_NoExpiryTrue_IgnoresExpiryValidation()
    {
        // Doc: "When no_expiry is true, expiry_time is ignored"
        var (client, store) = CreateIsolatedClient();

        var response = await client.PutAsync("/api/v1/entities",
            JsonContent(ValidEntityJson("no-expiry-entity", noExpiry: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(store.GetEntity("no-expiry-entity"));
    }

    [Fact]
    public async Task PublishEntity_UpdateExisting_OverwritesEntity()
    {
        // Doc: "If the entity referenced by the entity ID does not exist then it will be created.
        //       Otherwise the entity will be updated."
        var (client, store) = CreateIsolatedClient();
        await client.PutAsync("/api/v1/entities", JsonContent(ValidEntityJson("update-me")));

        var updated = new Entity
        {
            EntityId = "update-me",
            IsLive = true,
            NoExpiry = true,
            Description = "Updated description",
        };
        var response = await client.PutAsync("/api/v1/entities",
            JsonContent(ProtobufJsonConverter.ToJson(updated)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated description", store.GetEntity("update-me")!.Description);
    }

    [Fact]
    public async Task PublishEntity_IsLiveFalse_DeletesEntity()
    {
        // Doc: "is_live: If false and the entity is still live, triggers a DELETE event."
        var (client, store) = CreateIsolatedClient();
        await client.PutAsync("/api/v1/entities", JsonContent(ValidEntityJson("delete-me")));
        Assert.NotNull(store.GetEntity("delete-me"));

        var deleteEntity = new Entity
        {
            EntityId = "delete-me",
            IsLive = false,
            ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(10)),
        };
        var response = await client.PutAsync("/api/v1/entities",
            JsonContent(ProtobufJsonConverter.ToJson(deleteEntity)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(store.GetEntity("delete-me"));
    }

    [Fact]
    public async Task PublishEntity_ResponseIsProtobufJson()
    {
        // Doc: Response content-type is application/json with Entity schema
        var (client, _) = CreateIsolatedClient();

        var response = await client.PutAsync("/api/v1/entities", JsonContent(ValidEntityJson()));

        var body = await response.Content.ReadAsStringAsync();
        var entity = ProtobufJsonConverter.FromJson<Entity>(body);
        Assert.Equal("test-entity-1", entity.EntityId);
        Assert.True(entity.IsLive);
        Assert.Equal("Test entity", entity.Description);
    }

    // ───────────────────────────────────────────────
    // GET /api/v1/entities/{entityId} - Get Entity
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetEntity_ExistingEntity_Returns200WithEntity()
    {
        // Doc: "Entity retrieval was successful" (200)
        var (client, store) = CreateIsolatedClient();
        store.PublishEntity(new Entity
        {
            EntityId = "get-me",
            IsLive = true,
            NoExpiry = true,
            Description = "Retrievable entity",
        });

        var response = await client.GetAsync("/api/v1/entities/get-me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var entity = ProtobufJsonConverter.FromJson<Entity>(body);
        Assert.Equal("get-me", entity.EntityId);
        Assert.Equal("Retrievable entity", entity.Description);
    }

    [Fact]
    public async Task GetEntity_NonExistentEntity_Returns404()
    {
        // Doc: "The specified resource was not found" (404)
        var (client, _) = CreateIsolatedClient();

        var response = await client.GetAsync("/api/v1/entities/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async Task GetEntity_ReturnsProtobufJsonContentType()
    {
        var (client, store) = CreateIsolatedClient();
        store.PublishEntity(new Entity
        {
            EntityId = "json-check",
            IsLive = true,
            NoExpiry = true,
        });

        var response = await client.GetAsync("/api/v1/entities/json-check");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetEntity_AfterPublish_ReturnsPublishedEntity()
    {
        // End-to-end: publish then get via REST
        var (client, _) = CreateIsolatedClient();
        await client.PutAsync("/api/v1/entities", JsonContent(ValidEntityJson("roundtrip")));

        var response = await client.GetAsync("/api/v1/entities/roundtrip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var entity = ProtobufJsonConverter.FromJson<Entity>(body);
        Assert.Equal("roundtrip", entity.EntityId);
        Assert.Equal("Test entity", entity.Description);
    }

    [Fact]
    public async Task GetEntity_AfterDelete_Returns404()
    {
        // Publish then delete, then verify 404
        var (client, _) = CreateIsolatedClient();
        await client.PutAsync("/api/v1/entities", JsonContent(ValidEntityJson("to-delete")));

        var deleteEntity = new Entity
        {
            EntityId = "to-delete",
            IsLive = false,
            ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(10)),
        };
        await client.PutAsync("/api/v1/entities",
            JsonContent(ProtobufJsonConverter.ToJson(deleteEntity)));

        var response = await client.GetAsync("/api/v1/entities/to-delete");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
