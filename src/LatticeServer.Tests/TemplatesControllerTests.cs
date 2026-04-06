using System.IO.Compression;
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

    // -------------------------------------------------------------------------
    // GET /api/v1/templates — hasCustomTaskTypes field
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListTemplates_IncludesHasCustomTaskTypesFalse()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, items.GetArrayLength());
        Assert.False(items[0].GetProperty("hasCustomTaskTypes").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/templates/{templateId}/task-configurations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTaskConfigurations_UnknownTemplate_Returns404()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/does-not-exist/task-configurations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTaskConfigurations_NoBehaviorDll_ReturnsEmptyArray()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/alpha/task-configurations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // ZIP template helpers
    // -------------------------------------------------------------------------

    private static byte[] CreateMinimalZip(string entityJson = MinimalEntityJson)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("entity.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(entityJson);
        }
        return ms.ToArray();
    }

    private static MultipartFormDataContent ZipContent(byte[] zipBytes, string filename = "template.zip")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zipBytes) { Headers = { ContentType = new("application/zip") } },
            "package", filename);
        return form;
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/templates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UploadTemplate_ValidZip_Returns202AndTemplateLoads()
    {
        var (client, appFactory) = await CreateClientAsync();
        var registry = appFactory.Services.GetRequiredService<TemplateRegistry>();

        var zip = CreateMinimalZip();
        var response = await client.PostAsync("/api/v1/templates",
            ZipContent(zip, "uploaded-alpha.zip"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("uploaded-alpha", body.RootElement.GetProperty("templateId").GetString());

        // Give the FileSystemWatcher time to detect and load the template
        await Task.Delay(600);
        await registry.InitialScanComplete;

        var listResponse = await client.GetAsync("/api/v1/templates");
        var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(list.EnumerateArray().Any(t => t.GetProperty("templateId").GetString() == "uploaded-alpha"));
    }

    [Fact]
    public async Task UploadTemplate_ZipMissingEntityJson_Returns400()
    {
        var (client, _) = await CreateClientAsync();

        // ZIP with only config.json, no entity.json
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("config.json");
            using var w = new StreamWriter(e.Open());
            w.Write("{}");
        }
        var response = await client.PostAsync("/api/v1/templates",
            ZipContent(ms.ToArray(), "bad.zip"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadTemplate_NotAZip_Returns400()
    {
        var (client, _) = await CreateClientAsync();

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not a zip")), "package", "bad.zip");

        var response = await client.PostAsync("/api/v1/templates", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadTemplate_DirectoryTemplateExists_Returns409()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var zip = CreateMinimalZip();
        var response = await client.PostAsync("/api/v1/templates",
            ZipContent(zip, "alpha.zip"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UploadTemplate_CustomTemplateId_UsesHeaderId()
    {
        var (client, _) = await CreateClientAsync();

        var zip = CreateMinimalZip();
        var form = ZipContent(zip, "somefile.zip");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/templates") { Content = form };
        request.Headers.Add("X-Template-Id", "custom-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("custom-id", body.RootElement.GetProperty("templateId").GetString());
    }

    // -------------------------------------------------------------------------
    // DELETE /api/v1/templates/{templateId}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteTemplate_ExistingZip_Returns204()
    {
        var (client, _) = await CreateClientAsync();

        // Write a ZIP directly to the temp dir
        var zipPath = Path.Combine(_tempDir, "to-delete.zip");
        File.WriteAllBytes(zipPath, CreateMinimalZip());
        await Task.Delay(400); // let watcher pick it up

        var response = await client.DeleteAsync("/api/v1/templates/to-delete");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public async Task DeleteTemplate_DirectoryTemplate_Returns409()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.DeleteAsync("/api/v1/templates/alpha");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTemplate_UnknownId_Returns404()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.DeleteAsync("/api/v1/templates/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/templates/{templateId}/download
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DownloadTemplate_ExistingZip_ReturnsZipBytes()
    {
        var (client, _) = await CreateClientAsync();

        var zipBytes = CreateMinimalZip();
        File.WriteAllBytes(Path.Combine(_tempDir, "downloadable.zip"), zipBytes);

        var response = await client.GetAsync("/api/v1/templates/downloadable/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        var returned = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(zipBytes, returned);
    }

    [Fact]
    public async Task DownloadTemplate_DirectoryTemplate_Returns409()
    {
        CreateTemplate("alpha");
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/alpha/download");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DownloadTemplate_UnknownId_Returns404()
    {
        var (client, _) = await CreateClientAsync();

        var response = await client.GetAsync("/api/v1/templates/does-not-exist/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
