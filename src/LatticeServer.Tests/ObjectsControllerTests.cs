using System.Net;
using System.Security.Cryptography;
using System.Text;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for the Objects REST API endpoints.
/// Tests are derived from the OpenAPI specs in Docs/rest/objects/*.md.
/// </summary>
public class ObjectsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ObjectsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, ObjectStore Store) CreateIsolatedClient()
    {
        var store = new ObjectStore();
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(store);
            });
        }).CreateClient();
        return (client, store);
    }

    private static ByteArrayContent BinaryContent(byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static string ComputeSha256Hex(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    // ───────────────────────────────────────────────
    // POST /api/v1/objects/{objectPath} - Upload Object
    // ───────────────────────────────────────────────

    [Fact]
    public async Task UploadObject_Returns200WithPathMetadata()
    {
        // Doc: Upload returns 200 with PathMetadata schema
        var (client, _) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("hello world");

        var response = await client.PostAsync("/api/v1/objects/test/file.txt", BinaryContent(data));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // PathMetadata must contain content_identifier with path and checksum
        Assert.True(root.TryGetProperty("content_identifier", out var ci));
        Assert.Equal("test/file.txt", ci.GetProperty("path").GetString());
        Assert.False(string.IsNullOrEmpty(ci.GetProperty("checksum").GetString()));

        // Must contain size_bytes
        Assert.True(root.TryGetProperty("size_bytes", out var size_bytes));
        Assert.Equal(data.Length, size_bytes.GetInt32());

        // Must contain last_updated_at (date-time)
        Assert.True(root.TryGetProperty("last_updated_at", out _));

        // Must contain expiry_time (date-time)
        Assert.True(root.TryGetProperty("expiry_time", out _));
    }

    [Fact]
    public async Task UploadObject_ReturnsSha256Checksum()
    {
        // Doc: ContentIdentifier.checksum is "The SHA-256 checksum of this object."
        var (client, _) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("checksum test data");
        var expectedChecksum = ComputeSha256Hex(data);

        var response = await client.PostAsync("/api/v1/objects/checksum-test.bin", BinaryContent(data));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var checksum = doc.RootElement
            .GetProperty("content_identifier")
            .GetProperty("checksum")
            .GetString();
        Assert.Equal(expectedChecksum, checksum);
    }

    [Fact]
    public async Task UploadObject_DefaultTtl_ExpiryAbout90Days()
    {
        // Doc: "If no TTL is supplied, the server applies a default TTL.
        //       In most cases, the default TTL is 90 days."
        var (client, _) = CreateIsolatedClient();
        var data = new byte[] { 1, 2, 3 };

        var response = await client.PostAsync("/api/v1/objects/ttl-default.bin", BinaryContent(data));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var expiryStr = doc.RootElement.GetProperty("expiry_time").GetString()!;
        var expiry = DateTime.Parse(expiryStr).ToUniversalTime();
        var expectedExpiry = DateTime.UtcNow.AddDays(90);
        // Allow 30 seconds of tolerance
        Assert.True(Math.Abs((expiry - expectedExpiry).TotalSeconds) < 30,
            $"Expiry {expiry} should be ~90 days from now ({expectedExpiry})");
    }

    [Fact]
    public async Task UploadObject_WithTtlHeader_SetsCustomExpiry()
    {
        // Doc: "Time-To-Live: An optional expiry TTL... number of nanoseconds"
        var (client, _) = CreateIsolatedClient();
        var data = new byte[] { 1, 2, 3 };
        var oneHourNanos = TimeSpan.FromHours(1).Ticks * 100; // ticks * 100 = nanoseconds

        var content = BinaryContent(data);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/ttl-custom.bin")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("Time-To-Live", oneHourNanos.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var expiryStr = doc.RootElement.GetProperty("expiry_time").GetString()!;
        var expiry = DateTime.Parse(expiryStr).ToUniversalTime();
        var expectedExpiry = DateTime.UtcNow.AddHours(1);
        Assert.True(Math.Abs((expiry - expectedExpiry).TotalSeconds) < 30,
            $"Expiry {expiry} should be ~1 hour from now ({expectedExpiry})");
    }

    [Fact]
    public async Task UploadObject_EmptyPath_Returns400()
    {
        // Doc: objectPath is required
        var (client, _) = CreateIsolatedClient();

        // POST to /api/v1/objects/ with no path - should hit the list endpoint or 400
        var response = await client.PostAsync("/api/v1/objects/", BinaryContent(new byte[] { 1 }));

        // Either 400 or 405 is acceptable when path is missing
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.MethodNotAllowed,
            $"Expected 400 or 405 but got {response.StatusCode}");
    }

    [Fact]
    public async Task UploadObject_OverwritesExistingObject()
    {
        // Upload same path twice; second upload should overwrite
        var (client, _) = CreateIsolatedClient();
        var data1 = Encoding.UTF8.GetBytes("version 1");
        var data2 = Encoding.UTF8.GetBytes("version 2");

        await client.PostAsync("/api/v1/objects/overwrite.txt", BinaryContent(data1));
        var response = await client.PostAsync("/api/v1/objects/overwrite.txt", BinaryContent(data2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(data2.Length, doc.RootElement.GetProperty("size_bytes").GetInt32());
        Assert.Equal(ComputeSha256Hex(data2),
            doc.RootElement.GetProperty("content_identifier").GetProperty("checksum").GetString());
    }

    [Fact]
    public async Task UploadObject_NestedPath_Works()
    {
        // Doc: objectPath supports slashes for nested paths
        var (client, _) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("nested");

        var response = await client.PostAsync("/api/v1/objects/a/b/c/deep.txt", BinaryContent(data));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("a/b/c/deep.txt",
            doc.RootElement.GetProperty("content_identifier").GetProperty("path").GetString());
    }

    // ───────────────────────────────────────────────
    // GET /api/v1/objects/{objectPath} - Get Object
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetObject_ReturnsOctetStreamWithData()
    {
        // Doc: 200 with application/octet-stream binary data
        var (client, store) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("binary content here");
        store.Upload("get-test.bin", data, null);

        var response = await client.GetAsync("/api/v1/objects/get-test.bin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(data, body);
    }

    [Fact]
    public async Task GetObject_ReturnsMetadataHeaders()
    {
        // Controller sets X-Object-Checksum, X-Object-Size, X-Object-Last-Updated, X-Object-Expiry
        var (client, store) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("header check");
        var obj = store.Upload("header-test.bin", data, null);

        var response = await client.GetAsync("/api/v1/objects/header-test.bin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Object-Checksum"));
        Assert.Equal(obj.Checksum, response.Headers.GetValues("X-Object-Checksum").First());
        Assert.True(response.Headers.Contains("X-Object-Size"));
        Assert.Equal(data.Length.ToString(), response.Headers.GetValues("X-Object-Size").First());
        Assert.True(response.Headers.Contains("X-Object-Last-Updated"));
        Assert.True(response.Headers.Contains("X-Object-Expiry"));
    }

    [Fact]
    public async Task GetObject_NotFound_Returns404WithError()
    {
        // Doc: 404 "The specified resource was not found"
        var (client, _) = CreateIsolatedClient();

        var response = await client.GetAsync("/api/v1/objects/does-not-exist.bin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task GetObject_AfterUpload_RoundTrip()
    {
        // Upload via POST, then retrieve via GET
        var (client, _) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("roundtrip data");

        await client.PostAsync("/api/v1/objects/roundtrip.bin", BinaryContent(data));
        var response = await client.GetAsync("/api/v1/objects/roundtrip.bin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(data, body);
    }

    // ───────────────────────────────────────────────
    // DELETE /api/v1/objects/{objectPath} - Delete Object
    // ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteObject_Existing_Returns204()
    {
        // Doc: 204 "Successful operation"
        var (client, store) = CreateIsolatedClient();
        store.Upload("delete-me.bin", new byte[] { 1 }, null);

        var response = await client.DeleteAsync("/api/v1/objects/delete-me.bin");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteObject_NonExistent_Returns204()
    {
        // Doc: Only defines 204, 400, 401, 500 responses — no 404.
        // This implies delete is idempotent: deleting a non-existent object succeeds.
        var (client, _) = CreateIsolatedClient();

        var response = await client.DeleteAsync("/api/v1/objects/never-existed.bin");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteObject_ThenGet_Returns404()
    {
        // After deletion, GET should return 404
        var (client, store) = CreateIsolatedClient();
        store.Upload("delete-then-get.bin", new byte[] { 1, 2, 3 }, null);

        await client.DeleteAsync("/api/v1/objects/delete-then-get.bin");
        var response = await client.GetAsync("/api/v1/objects/delete-then-get.bin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // HEAD /api/v1/objects/{objectPath} - Get Object Metadata
    // ───────────────────────────────────────────────

    [Fact]
    public async Task HeadObject_ReturnsMetadataHeaders()
    {
        // Doc: 200 with empty body, metadata in headers
        var (client, store) = CreateIsolatedClient();
        var data = Encoding.UTF8.GetBytes("metadata check");
        var obj = store.Upload("head-test.bin", data, null);

        var request = new HttpRequestMessage(HttpMethod.Head, "/api/v1/objects/head-test.bin");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Object-Checksum"));
        Assert.Equal(obj.Checksum, response.Headers.GetValues("X-Object-Checksum").First());
        Assert.True(response.Headers.Contains("X-Object-Size"));
        Assert.Equal(data.Length.ToString(), response.Headers.GetValues("X-Object-Size").First());
        Assert.True(response.Headers.Contains("X-Object-Last-Updated"));
        Assert.True(response.Headers.Contains("X-Object-Expiry"));
    }

    [Fact]
    public async Task HeadObject_ReturnsContentLength()
    {
        // Doc: Response includes content-length for the object
        var (client, store) = CreateIsolatedClient();
        var data = new byte[42];
        store.Upload("head-length.bin", data, null);

        var request = new HttpRequestMessage(HttpMethod.Head, "/api/v1/objects/head-length.bin");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(42, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task HeadObject_NotFound_Returns404()
    {
        // Object doesn't exist → 404
        var (client, _) = CreateIsolatedClient();

        var request = new HttpRequestMessage(HttpMethod.Head, "/api/v1/objects/no-such-object.bin");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // GET /api/v1/objects - List Objects
    // ───────────────────────────────────────────────

    [Fact]
    public async Task ListObjects_ReturnsPathMetadatasArray()
    {
        // Doc: ListResponse has "path_metadatas" array
        var (client, store) = CreateIsolatedClient();
        store.Upload("list/a.txt", new byte[] { 1 }, null);
        store.Upload("list/b.txt", new byte[] { 2 }, null);

        var response = await client.GetAsync("/api/v1/objects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("path_metadatas", out var arr),
            $"Response should contain 'path_metadatas'. Actual JSON: {json}");
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(2, arr.GetArrayLength());
    }

    [Fact]
    public async Task ListObjects_ItemsContainPathMetadataFields()
    {
        // Doc: Each item is a PathMetadata with content_identifier, size_bytes, last_updated_at, expiry_time
        var (client, store) = CreateIsolatedClient();
        var data = new byte[] { 10, 20, 30 };
        store.Upload("fields-test.bin", data, null);

        var response = await client.GetAsync("/api/v1/objects");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("path_metadatas")[0];

        // content_identifier with path and checksum
        var ci = item.GetProperty("content_identifier");
        Assert.Equal("fields-test.bin", ci.GetProperty("path").GetString());
        Assert.Equal(ComputeSha256Hex(data), ci.GetProperty("checksum").GetString());

        // size_bytes
        Assert.Equal(data.Length, item.GetProperty("size_bytes").GetInt32());

        // last_updated_at
        Assert.True(item.TryGetProperty("last_updated_at", out _));

        // expiry_time
        Assert.True(item.TryGetProperty("expiry_time", out _));
    }

    [Fact]
    public async Task ListObjects_WithPrefix_FiltersResults()
    {
        // Doc: "Filters the objects based on the specified prefix path"
        var (client, store) = CreateIsolatedClient();
        store.Upload("alpha/one.txt", new byte[] { 1 }, null);
        store.Upload("alpha/two.txt", new byte[] { 2 }, null);
        store.Upload("beta/one.txt", new byte[] { 3 }, null);

        var response = await client.GetAsync("/api/v1/objects?prefix=alpha/");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("path_metadatas");
        Assert.Equal(2, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            var path = item.GetProperty("content_identifier").GetProperty("path").GetString()!;
            Assert.StartsWith("alpha/", path);
        }
    }

    [Fact]
    public async Task ListObjects_WithMaxPageSize_LimitsResults()
    {
        // Doc: "Sets the maximum number of items that should be returned on a single page."
        var (client, store) = CreateIsolatedClient();
        for (int i = 0; i < 5; i++)
            store.Upload($"page/{i}.txt", new byte[] { (byte)i }, null);

        var response = await client.GetAsync("/api/v1/objects?maxPageSize=2");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("path_metadatas");
        Assert.Equal(2, items.GetArrayLength());
    }

    [Fact]
    public async Task ListObjects_Empty_ReturnsEmptyArray()
    {
        var (client, _) = CreateIsolatedClient();

        var response = await client.GetAsync("/api/v1/objects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("path_metadatas");
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public async Task ListObjects_ExcludesExpiredObjects()
    {
        // Doc: Expired objects should not appear in list results
        var (client, store) = CreateIsolatedClient();
        store.Upload("fresh.txt", new byte[] { 1 }, null); // 90-day TTL
        // Upload with a very short TTL that will have expired by the time we list
        // We use the store directly to create an already-expired object
        store.Upload("expired.txt", new byte[] { 2 }, 1L); // 1 nanosecond TTL → already expired

        // Small delay to ensure expiry
        await Task.Delay(50);

        var response = await client.GetAsync("/api/v1/objects");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("path_metadatas");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("fresh.txt",
            items[0].GetProperty("content_identifier").GetProperty("path").GetString());
    }

    [Fact]
    public async Task ListObjects_WithSinceTimestamp_FiltersOlderObjects()
    {
        // Doc: "Sets the age for the oldest objects to query across the environment."
        var (client, store) = CreateIsolatedClient();
        store.Upload("old.txt", new byte[] { 1 }, null);

        // Record a timestamp after the first upload
        var sinceTime = DateTime.UtcNow.AddSeconds(1);
        await Task.Delay(1100); // Wait so next upload is after sinceTime

        store.Upload("new.txt", new byte[] { 2 }, null);

        var response = await client.GetAsync($"/api/v1/objects?sinceTimestamp={sinceTime:O}");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("path_metadatas");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("new.txt",
            items[0].GetProperty("content_identifier").GetProperty("path").GetString());
    }
}
