using System.Net;
using System.Text;
using System.Text.Json;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for the /api/v1/templates REST endpoints.
/// Each test gets a fresh temp directory used as the template watch directory.
/// </summary>
public class TemplatesControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private string _tempDir = "";

    public TemplatesControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lattice-templates-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        return Task.CompletedTask;
    }

    private const string MinimalEntityJson =
        """{"entityId":"<new_uuid>","isLive":true,"noExpiry":true}""";

    /// <summary>Creates a template folder in the temp watch directory.</summary>
    private void CreateTemplate(string templateId, string? entityJson = null, string? configJson = null)
    {
        var dir = Path.Combine(_tempDir, templateId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"), entityJson ?? MinimalEntityJson);
        if (configJson != null)
            File.WriteAllText(Path.Combine(dir, "config.json"), configJson);
    }

    /// <summary>
    /// Builds a test client pointing at _tempDir for templates, with scenario service disabled.
    /// Waits for the TemplateRegistry initial scan to complete before returning.
    /// </summary>
    private async Task<(HttpClient Client, WebApplicationFactory<Program> AppFactory)> CreateClientAsync()
    {
        var appFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Use _tempDir as an absolute watch path — bypasses DataDirectory resolution
                    ["Templates:WatchDirectory"] = _tempDir,
                    ["ScenarioConfigPath"] = ""
                }));
        });
        var client = appFactory.CreateClient();
        var registry = appFactory.Services.GetRequiredService<TemplateRegistry>();
        await registry.InitialScanComplete;
        return (client, appFactory);
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    // -------------------------------------------------------------------------
    // GET /api/v1/templates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListTemplates_NoTemplates_ReturnsEmptyArray()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(body).RootElement;
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public async Task ListTemplates_WithLoadedTemplate_ReturnsTemplateInfo()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];
        Assert.Equal("alpha", item.GetProperty("templateId").GetString());
        Assert.False(item.GetProperty("isTaskable").GetBoolean()); // no behavior.dll
    }

    [Fact]
    public async Task ListTemplates_MultipleTemplates_ReturnsAll()
    {
        CreateTemplate("alpha");
        CreateTemplate("bravo");
        CreateTemplate("charlie");
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates");
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(3, items.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/templates/{templateId}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTemplate_UnknownId_Returns404()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplate_KnownId_ReturnsRawEntityJson()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/alpha");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Should contain the raw (un-substituted) template tokens
        Assert.Contains("<new_uuid>", body);
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/templates/{templateId}/spawn
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spawn_UnknownTemplate_Returns404()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.PostAsync(
            "/api/v1/templates/does-not-exist/spawn",
            JsonContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Spawn_KnownTemplate_Returns200WithEntityId()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("entityId", out var entityIdProp));
        Assert.False(string.IsNullOrEmpty(entityIdProp.GetString()));
    }

    [Fact]
    public async Task Spawn_TwiceFromSameTemplate_ReturnsDifferentEntityIds()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var r1 = await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));
        var r2 = await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));

        var id1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync())
            .RootElement.GetProperty("entityId").GetString();
        var id2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync())
            .RootElement.GetProperty("entityId").GetString();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task Spawn_WithNameOverride_EntityAppearsWithOverriddenName()
    {
        CreateTemplate("alpha", entityJson: """
            {"entityId":"<new_uuid>","isLive":true,"noExpiry":true,"aliases":{"name":"Default"}}
            """);
        var (client, appFactory) = await CreateClientAsync();

        var response = await client.PostAsync(
            "/api/v1/templates/alpha/spawn",
            JsonContent("""{"nameOverride":"Overridden"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entityId = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("entityId").GetString()!;

        var entityStore = appFactory.Services.GetRequiredService<EntityStore>();
        var entity = entityStore.GetEntity(entityId);
        Assert.NotNull(entity);
        Assert.Equal("Overridden", entity.Aliases.Name);
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/templates/instances
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListInstances_NoInstances_ReturnsEmptyArray()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/instances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public async Task ListInstances_AfterSpawn_ReturnsInstance()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));
        var response = await client.GetAsync("/api/v1/templates/instances");

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("alpha", items[0].GetProperty("templateId").GetString());
    }

    // -------------------------------------------------------------------------
    // DELETE /api/v1/templates/instances/{entityId}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Despawn_UnknownEntity_Returns404()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.DeleteAsync("/api/v1/templates/instances/nonexistent-id");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Despawn_KnownEntity_Returns200AndRemovesInstance()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var spawnResponse = await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));
        var entityId = JsonDocument.Parse(await spawnResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("entityId").GetString()!;

        var despawnResponse = await client.DeleteAsync($"/api/v1/templates/instances/{entityId}");
        Assert.Equal(HttpStatusCode.OK, despawnResponse.StatusCode);

        var instances = JsonDocument.Parse(
            await (await client.GetAsync("/api/v1/templates/instances")).Content.ReadAsStringAsync())
            .RootElement;
        Assert.Equal(0, instances.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // DELETE /api/v1/templates/instances
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DespawnAll_Returns200WithCount()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));
        await client.PostAsync("/api/v1/templates/alpha/spawn", JsonContent("{}"));

        var response = await client.DeleteAsync("/api/v1/templates/instances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("despawnedCount").GetInt32());
    }

    [Fact]
    public async Task DespawnAll_WithNoInstances_ReturnsZeroCount()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.DeleteAsync("/api/v1/templates/instances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("despawnedCount").GetInt32());
    }
}
