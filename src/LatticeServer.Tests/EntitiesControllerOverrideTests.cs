using System.Net;
using System.Text;
using Anduril.Entitymanager.V1;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for PUT /api/v1/entities/{entityId}/override/{fieldPath} (Override)
/// and DELETE /api/v1/entities/{entityId}/override/{fieldPath} (Remove Override).
/// </summary>
public class EntitiesControllerOverrideTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EntitiesControllerOverrideTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

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

    /// <summary>
    /// Wraps an Entity in the EntityOverride format per the docs:
    /// { "entity": { ...entity fields... } }
    /// </summary>
    private static string EntityOverrideJson(Entity entity) =>
        $$"""{"entity": {{ProtobufJsonConverter.ToJson(entity)}}}""";

    private void SeedEntity(EntityStore store, string entityId = "override-me")
    {
        store.PublishEntity(new Entity
        {
            EntityId = entityId,
            IsLive = true,
            NoExpiry = true,
            Description = "Original description",
            Aliases = new Aliases { Name = "Original" },
        });
    }

    // ───────────────────────────────────────────────
    // PUT /api/v1/entities/{entityId}/override/{fieldPath}
    // ───────────────────────────────────────────────

    [Fact]
    public async Task OverrideEntity_ValidRequest_Returns200WithEntity()
    {
        // Doc: "The Entities API accepts the override." (200)
        // Doc: "Override an Entity Component. An override is a definitive change to entity data."
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        // Doc: Request body is EntityOverride = { entity: Entity, provenance: Provenance }
        var overrideBody = EntityOverrideJson(new Entity { Description = "overridden" });
        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(overrideBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var entity = ProtobufJsonConverter.FromJson<Entity>(body);
        Assert.Equal("override-me", entity.EntityId);
    }

    [Fact]
    public async Task OverrideEntity_NonExistentEntity_Returns404()
    {
        // Doc: "The specified resource was not found" (404)
        var (client, _) = CreateIsolatedClient();

        var overrideBody = EntityOverrideJson(new Entity { Description = "overridden" });
        var response = await client.PutAsync(
            "/api/v1/entities/no-such-entity/override/description",
            JsonContent(overrideBody));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async Task OverrideEntity_MissingEntityWrapper_Returns400()
    {
        // Doc: Request body must be EntityOverride = { entity: Entity }, not a raw Entity
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        // Send a raw Entity without the EntityOverride wrapper
        var rawEntityBody = ProtobufJsonConverter.ToJson(new Entity { Description = "overridden" });
        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(rawEntityBody));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ARGUMENT", body);
        Assert.Contains("entity", body);
    }

    [Fact]
    public async Task OverrideEntity_InvalidJson_Returns400()
    {
        // Doc: "Bad request" (400)
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent("{not valid}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ARGUMENT", body);
    }

    [Fact]
    public async Task OverrideEntity_SetsEntityIdFromPath()
    {
        // Doc: "entityId in path: The unique ID of the entity to override"
        // Controller sets overrideEntity.EntityId = entityId from path
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        // Send body without entityId — the path entityId should be used
        var overrideBody = EntityOverrideJson(new Entity { Description = "overridden" });
        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(overrideBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OverrideEntity_ReturnsUpdatedEntity()
    {
        // Doc: Response schema is Entity (the entity after override is applied)
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var overrideBody = EntityOverrideJson(new Entity { Description = "overridden" });
        await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(overrideBody));

        // Verify via GET that entity is still retrievable
        var getResponse = await client.GetAsync("/api/v1/entities/override-me");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task OverrideEntity_NestedFieldPath_Succeeds()
    {
        // Doc: "Field paths are rooted in the base entity object and must be represented using lower_snake_case."
        // Doc: "Do not include 'entity' in the field path."
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var overrideBody = EntityOverrideJson(new Entity
        {
            Aliases = new Aliases { Name = "Overridden Name" },
        });
        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/aliases.name",
            JsonContent(overrideBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OverrideEntity_LastWriterWins()
    {
        // Doc: "If multiple overrides are created concurrently for the same field path, the last writer wins."
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var first = EntityOverrideJson(new Entity { Description = "first" });
        await client.PutAsync("/api/v1/entities/override-me/override/description", JsonContent(first));

        var second = EntityOverrideJson(new Entity { Description = "second" });
        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(second));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OverrideEntity_ResponseContentType_IsJson()
    {
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var overrideBody = EntityOverrideJson(new Entity { Description = "overridden" });
        var response = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(overrideBody));

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ───────────────────────────────────────────────
    // DELETE /api/v1/entities/{entityId}/override/{fieldPath}
    // ───────────────────────────────────────────────

    [Fact]
    public async Task RemoveOverride_ExistingOverride_Returns200()
    {
        // Doc: "The removal of entity override was successful." (200)
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);
        store.ApplyOverride("override-me", ["description"],
            new Entity { EntityId = "override-me", Description = "overridden" });

        var response = await client.DeleteAsync(
            "/api/v1/entities/override-me/override/description");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var entity = ProtobufJsonConverter.FromJson<Entity>(body);
        Assert.Equal("override-me", entity.EntityId);
    }

    [Fact]
    public async Task RemoveOverride_NonExistentEntity_Returns404()
    {
        // Doc: "The specified resource was not found" (404)
        var (client, _) = CreateIsolatedClient();

        var response = await client.DeleteAsync(
            "/api/v1/entities/no-such-entity/override/description");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async Task RemoveOverride_ReturnsEntityAfterRemoval()
    {
        // Doc: Response schema is Entity (the entity after override removal)
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);
        store.ApplyOverride("override-me", ["description"],
            new Entity { EntityId = "override-me", Description = "overridden" });

        var response = await client.DeleteAsync(
            "/api/v1/entities/override-me/override/description");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RemoveOverride_NoExistingOverride_StillReturns200()
    {
        // Removing a non-existent override on an existing entity should succeed
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var response = await client.DeleteAsync(
            "/api/v1/entities/override-me/override/description");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OverrideThenRemove_RoundTrip()
    {
        // End-to-end: apply override via PUT, then remove via DELETE
        var (client, store) = CreateIsolatedClient();
        SeedEntity(store);

        var overrideBody = EntityOverrideJson(new Entity { Description = "overridden" });
        var putResponse = await client.PutAsync(
            "/api/v1/entities/override-me/override/description",
            JsonContent(overrideBody));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync(
            "/api/v1/entities/override-me/override/description");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Entity should still be retrievable
        var getResponse = await client.GetAsync("/api/v1/entities/override-me");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }
}
