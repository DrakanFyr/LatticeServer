using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LatticeClient;

/// <summary>
/// Metadata about a stored object's content identifier.
/// </summary>
public record ContentIdentifier(string Path, string Checksum);

/// <summary>
/// Metadata about a stored object.
/// </summary>
public record PathMetadata(
    ContentIdentifier ContentIdentifier,
    long SizeBytes,
    DateTime LastUpdatedAt,
    DateTime? ExpiryTime);

/// <summary>
/// Metadata returned from object HEAD requests via response headers.
/// </summary>
public record ObjectMetadata(
    string? Checksum,
    long? SizeBytes,
    DateTime? LastUpdatedAt,
    DateTime? ExpiryTime);

/// <summary>
/// Client for the Lattice Objects REST API. Objects are binary blobs stored with path-based keys.
/// This API is REST-only (no gRPC equivalent).
/// </summary>
public class ObjectManagerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public ObjectManagerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    public ObjectManagerClient(string baseUrl, string? bearerToken = null)
    {
        _httpClient = RestEntityManagerClient.CreateHttpClient(baseUrl, bearerToken);
        _ownsHttpClient = true;
    }

    /// <summary>
    /// List objects, optionally filtered by prefix and timestamp.
    /// </summary>
    public async Task<IReadOnlyList<PathMetadata>> ListObjectsAsync(
        string? prefix = null,
        DateTime? sinceTimestamp = null,
        int? maxPageSize = null,
        bool? allObjectsInMesh = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrEmpty(prefix))
            queryParts.Add($"prefix={Uri.EscapeDataString(prefix)}");
        if (sinceTimestamp.HasValue)
            queryParts.Add($"sinceTimestamp={Uri.EscapeDataString(sinceTimestamp.Value.ToString("O"))}");
        if (maxPageSize.HasValue)
            queryParts.Add($"maxPageSize={maxPageSize.Value}");
        if (allObjectsInMesh == true)
            queryParts.Add("allObjectsInMesh=true");

        var url = "api/v1/objects";
        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseListResponse(json);
    }

    /// <summary>
    /// Get an object's binary data by its path.
    /// </summary>
    public async Task<byte[]> GetObjectAsync(
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/v1/objects/{objectPath}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Get an object's binary data along with its metadata headers.
    /// </summary>
    public async Task<(byte[] Data, ObjectMetadata Metadata)> GetObjectWithMetadataAsync(
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/v1/objects/{objectPath}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var metadata = ParseMetadataHeaders(response);
        return (data, metadata);
    }

    /// <summary>
    /// Upload an object. The object must be 1 GiB or smaller.
    /// </summary>
    public async Task<PathMetadata> UploadObjectAsync(
        string objectPath,
        byte[] data,
        long? ttlNanoseconds = null,
        CancellationToken cancellationToken = default)
    {
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/objects/{objectPath}")
        {
            Content = content,
        };

        if (ttlNanoseconds.HasValue)
        {
            request.Headers.Add("Time-To-Live", ttlNanoseconds.Value.ToString());
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePathMetadata(JsonDocument.Parse(json).RootElement);
    }

    /// <summary>
    /// Upload an object from a stream. The object must be 1 GiB or smaller.
    /// </summary>
    public async Task<PathMetadata> UploadObjectAsync(
        string objectPath,
        Stream dataStream,
        long? ttlNanoseconds = null,
        CancellationToken cancellationToken = default)
    {
        using var content = new StreamContent(dataStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/objects/{objectPath}")
        {
            Content = content,
        };

        if (ttlNanoseconds.HasValue)
        {
            request.Headers.Add("Time-To-Live", ttlNanoseconds.Value.ToString());
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePathMetadata(JsonDocument.Parse(json).RootElement);
    }

    /// <summary>
    /// Delete an object by its path.
    /// </summary>
    public async Task DeleteObjectAsync(
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync(
            $"api/v1/objects/{objectPath}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Get object metadata without downloading the object data (HEAD request).
    /// </summary>
    public async Task<ObjectMetadata> GetObjectMetadataAsync(
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, $"api/v1/objects/{objectPath}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return ParseMetadataHeaders(response);
    }

    private static ObjectMetadata ParseMetadataHeaders(HttpResponseMessage response)
    {
        string? checksum = null;
        long? sizeBytes = null;
        DateTime? lastUpdatedAt = null;
        DateTime? expiryTime = null;

        if (response.Headers.TryGetValues("X-Object-Checksum", out var checksumValues))
            checksum = checksumValues.FirstOrDefault();

        if (response.Headers.TryGetValues("X-Object-Size", out var sizeValues) &&
            long.TryParse(sizeValues.FirstOrDefault(), out var parsedSize))
            sizeBytes = parsedSize;

        if (response.Headers.TryGetValues("X-Object-Last-Updated", out var lastUpdatedValues) &&
            DateTime.TryParse(lastUpdatedValues.FirstOrDefault(), out var parsedLastUpdated))
            lastUpdatedAt = parsedLastUpdated;

        if (response.Headers.TryGetValues("X-Object-Expiry", out var expiryValues) &&
            DateTime.TryParse(expiryValues.FirstOrDefault(), out var parsedExpiry))
            expiryTime = parsedExpiry;

        return new ObjectMetadata(checksum, sizeBytes, lastUpdatedAt, expiryTime);
    }

    private static IReadOnlyList<PathMetadata> ParseListResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var result = new List<PathMetadata>();

        if (doc.RootElement.TryGetProperty("path_metadatas", out var metadatasEl) &&
            metadatasEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in metadatasEl.EnumerateArray())
            {
                result.Add(ParsePathMetadata(item));
            }
        }

        return result;
    }

    private static PathMetadata ParsePathMetadata(JsonElement element)
    {
        var contentId = element.GetProperty("content_identifier");
        var path = contentId.GetProperty("path").GetString() ?? "";
        var checksum = contentId.GetProperty("checksum").GetString() ?? "";

        var sizeBytes = element.TryGetProperty("size_bytes", out var sb)
            ? sb.GetInt64() : 0;

        DateTime lastUpdatedAt = default;
        if (element.TryGetProperty("last_updated_at", out var lua) &&
            DateTime.TryParse(lua.GetString(), out var parsedLua))
            lastUpdatedAt = parsedLua;

        DateTime? expiryTime = null;
        if (element.TryGetProperty("expiry_time", out var et) &&
            DateTime.TryParse(et.GetString(), out var parsedEt))
            expiryTime = parsedEt;

        return new PathMetadata(
            new ContentIdentifier(path, checksum),
            sizeBytes,
            lastUpdatedAt,
            expiryTime);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"REST API error ({(int)response.StatusCode} {response.StatusCode}): {errorBody}");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
