using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anduril.Taskmanager.V1;

namespace LatticeClient;

/// <summary>
/// Task Manager client that communicates via the REST API.
/// </summary>
public class RestTaskManagerClient : ITaskManagerClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public RestTaskManagerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    public RestTaskManagerClient(string baseUrl, string? bearerToken = null)
    {
        _httpClient = RestEntityManagerClient.CreateHttpClient(baseUrl, bearerToken);
        _ownsHttpClient = true;
    }

    public async Task<Anduril.Taskmanager.V1.Task> CreateTaskAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var json = ProtobufJson.ToJson(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/v1/tasks", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ProtobufJson.FromJson<Anduril.Taskmanager.V1.Task>(responseJson);
    }

    public async Task<Anduril.Taskmanager.V1.Task> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/v1/tasks/{Uri.EscapeDataString(taskId)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ProtobufJson.FromJson<Anduril.Taskmanager.V1.Task>(json);
    }

    public async Task<QueryTasksResponse> QueryTasksAsync(
        QueryTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        var json = BuildQueryTasksJson(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/v1/tasks/query", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseQueryTasksResponse(responseJson);
    }

    public async Task<Anduril.Taskmanager.V1.Task> UpdateStatusAsync(
        StatusUpdate statusUpdate,
        CancellationToken cancellationToken = default)
    {
        var taskId = statusUpdate.Version.TaskId;
        var json = BuildUpdateStatusJson(statusUpdate);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"api/v1/tasks/{Uri.EscapeDataString(taskId)}/status")
        {
            Content = content,
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ProtobufJson.FromJson<Anduril.Taskmanager.V1.Task>(responseJson);
    }

    public async Task<Anduril.Taskmanager.V1.Task> CancelTaskAsync(
        string taskId,
        Principal? author = null,
        CancellationToken cancellationToken = default)
    {
        var bodyJson = "{}";
        if (author != null)
        {
            bodyJson = $"{{\"author\":{ProtobufJson.ToJson(author)}}}";
        }

        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"api/v1/tasks/{Uri.EscapeDataString(taskId)}/cancel")
        {
            Content = content,
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ProtobufJson.FromJson<Anduril.Taskmanager.V1.Task>(responseJson);
    }

    public async IAsyncEnumerable<ListenAsAgentResponse> ListenAsAgentAsync(
        ListenAsAgentRequest? request = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request ??= new ListenAsAgentRequest();

        var bodyJson = BuildAgentStreamJson(request);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/agent/stream")
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
                continue;

            var agentResponse = ParseAgentSseEvent(sseEvent);
            if (agentResponse != null)
                yield return agentResponse;
        }
    }

    /// <summary>
    /// Stream all task events (manager view) via SSE. This is a REST-only capability
    /// not available through the gRPC interface.
    /// </summary>
    public async IAsyncEnumerable<TaskEvent> StreamTaskEventsAsync(
        bool excludePreexisting = false,
        int heartbeatIntervalMs = 30000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();
        if (excludePreexisting)
            parts.Add("\"excludePreexistingTasks\":true");
        if (heartbeatIntervalMs != 30000)
            parts.Add($"\"heartbeatIntervalMs\":{heartbeatIntervalMs}");

        var bodyJson = "{" + string.Join(",", parts) + "}";
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/tasks/stream")
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
                continue;

            var taskEvent = ProtobufJson.FromJson<TaskEvent>(sseEvent.Data);
            yield return taskEvent;
        }
    }

    /// <summary>
    /// Listen as agent using long-polling (single request/response). This is a REST-only
    /// capability that returns a single agent request per call.
    /// </summary>
    public async Task<ListenAsAgentResponse?> ListenAsAgentLongPollAsync(
        ListenAsAgentRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new ListenAsAgentRequest();

        var bodyJson = BuildAgentStreamJson(request);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/v1/agent/listen", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("executeRequest", out var execEl))
        {
            return new ListenAsAgentResponse
            {
                ExecuteRequest = ProtobufJson.FromJson<ExecuteRequest>(execEl.GetRawText()),
            };
        }
        if (root.TryGetProperty("cancelRequest", out var cancelEl))
        {
            return new ListenAsAgentResponse
            {
                CancelRequest = ProtobufJson.FromJson<CancelRequest>(cancelEl.GetRawText()),
            };
        }
        if (root.TryGetProperty("completeRequest", out var completeEl))
        {
            return new ListenAsAgentResponse
            {
                CompleteRequest = ProtobufJson.FromJson<CompleteRequest>(completeEl.GetRawText()),
            };
        }

        return null; // timeout with no events
    }

    private static string BuildUpdateStatusJson(StatusUpdate statusUpdate)
    {
        // The REST API expects "newStatus" instead of "status", and "version" or "statusVersion".
        // We build the JSON manually to match the REST API schema.
        var parts = new List<string>();

        if (statusUpdate.Version != null)
        {
            parts.Add($"\"version\":{ProtobufJson.ToJson(statusUpdate.Version)}");
        }

        if (statusUpdate.Status != null)
        {
            parts.Add($"\"newStatus\":{ProtobufJson.ToJson(statusUpdate.Status)}");
        }

        if (statusUpdate.Author != null)
        {
            parts.Add($"\"author\":{ProtobufJson.ToJson(statusUpdate.Author)}");
        }

        if (statusUpdate.ScheduledTime != null)
        {
            parts.Add($"\"scheduledTime\":{ProtobufJson.ToJson(statusUpdate.ScheduledTime)}");
        }

        return "{" + string.Join(",", parts) + "}";
    }

    private static string BuildQueryTasksJson(QueryTasksRequest request)
    {
        // Serialize proto to JSON and remap field names to match the REST API
        return ProtobufJson.ToJson(request);
    }

    private static string BuildAgentStreamJson(ListenAsAgentRequest request)
    {
        if (request.EntityIds != null && request.EntityIds.EntityIds_.Count > 0)
        {
            var idsJson = string.Join(",",
                request.EntityIds.EntityIds_.Select(id => $"\"{id}\""));
            return $"{{\"agentSelector\":{{\"entityIds\":[{idsJson}]}}}}";
        }
        return "{}";
    }

    private static ListenAsAgentResponse? ParseAgentSseEvent(SseEvent sseEvent)
    {
        return sseEvent.EventType switch
        {
            "execute" => new ListenAsAgentResponse
            {
                ExecuteRequest = ProtobufJson.FromJson<ExecuteRequest>(sseEvent.Data),
            },
            "cancel" => new ListenAsAgentResponse
            {
                CancelRequest = ProtobufJson.FromJson<CancelRequest>(sseEvent.Data),
            },
            "complete" => new ListenAsAgentResponse
            {
                CompleteRequest = ProtobufJson.FromJson<CompleteRequest>(sseEvent.Data),
            },
            _ => null,
        };
    }

    private static QueryTasksResponse ParseQueryTasksResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var result = new QueryTasksResponse();

        if (doc.RootElement.TryGetProperty("tasks", out var tasksEl) &&
            tasksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var taskEl in tasksEl.EnumerateArray())
            {
                var task = ProtobufJson.FromJson<Anduril.Taskmanager.V1.Task>(taskEl.GetRawText());
                result.Tasks.Add(task);
            }
        }

        if (doc.RootElement.TryGetProperty("pageToken", out var pt))
        {
            result.PageToken = pt.GetString() ?? "";
        }

        return result;
    }

    private static async System.Threading.Tasks.Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
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
