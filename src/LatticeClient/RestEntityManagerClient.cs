using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using Anduril.Entitymanager.V1;

namespace LatticeClient;

/// <summary>
/// Entity Manager client that communicates via the REST API.
/// </summary>
public class RestEntityManagerClient : IEntityManagerClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public RestEntityManagerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    public RestEntityManagerClient(string baseUrl, string? bearerToken = null)
    {
        _httpClient = CreateHttpClient(baseUrl, bearerToken);
        _ownsHttpClient = true;
    }

    public async Task PublishEntityAsync(
        Entity entity,
        CancellationToken cancellationToken = default)
    {
        var json = ProtobufJson.ToJson(entity);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PutAsync("api/v1/entities", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task PublishEntitiesAsync(
        IAsyncEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        await foreach (var entity in entities.WithCancellation(cancellationToken))
        {
            await PublishEntityAsync(entity, cancellationToken);
        }
    }

    public async Task PublishEntitiesAsync(
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PublishEntityAsync(entity, cancellationToken);
        }
    }

    public async Task<Entity> GetEntityAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/v1/entities/{Uri.EscapeDataString(entityId)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ProtobufJson.FromJson<Entity>(json);
    }

    public async Task OverrideEntityAsync(
        Entity entity,
        IEnumerable<string> fieldPaths,
        CancellationToken cancellationToken = default)
    {
        var entityId = entity.EntityId;
        var entityJson = ProtobufJson.ToJson(entity);
        var bodyJson = $"{{\"entity\":{entityJson}}}";

        foreach (var fieldPath in fieldPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PutAsync(
                $"api/v1/entities/{Uri.EscapeDataString(entityId)}/override/{fieldPath}",
                content,
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task RemoveEntityOverrideAsync(
        string entityId,
        IEnumerable<string> fieldPaths,
        CancellationToken cancellationToken = default)
    {
        foreach (var fieldPath in fieldPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _httpClient.DeleteAsync(
                $"api/v1/entities/{Uri.EscapeDataString(entityId)}/override/{fieldPath}",
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async IAsyncEnumerable<StreamEntityComponentsResponse> StreamEntityComponentsAsync(
        StreamEntityComponentsRequest? request = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request ??= new StreamEntityComponentsRequest();

        var bodyJson = BuildStreamRequestJson(request);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/entities/stream")
        {
            Content = content,
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var sseEvent in SseReader.ReadEventsAsync(stream, cancellationToken))
        {
            if (sseEvent.EventType == "heartbeat")
            {
                var heartbeat = ProtobufJson.FromJson<Heartbeat>(sseEvent.Data);
                yield return new StreamEntityComponentsResponse { Heartbeat = heartbeat };
            }
            else
            {
                var entityEvent = ProtobufJson.FromJson<EntityEvent>(sseEvent.Data);
                yield return new StreamEntityComponentsResponse { EntityEvent = entityEvent };
            }
        }
    }

    private static string BuildStreamRequestJson(StreamEntityComponentsRequest request)
    {
        var parts = new List<string>();

        if (request.HeartbeatPeriodMillis > 0)
            parts.Add($"\"heartbeatIntervalMs\":{request.HeartbeatPeriodMillis}");

        if (request.PreexistingOnly)
            parts.Add("\"preExistingOnly\":true");

        return "{" + string.Join(",", parts) + "}";
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

    internal static HttpClient CreateHttpClient(string baseUrl, string? bearerToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrEmpty(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return client;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
