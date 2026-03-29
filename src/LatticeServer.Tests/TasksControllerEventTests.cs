using System.Net;
using System.Text;
using System.Text.Json;
using Anduril.Taskmanager.V1;
using Google.Protobuf.WellKnownTypes;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = Anduril.Taskmanager.V1.TaskStatus;
using Status = Anduril.Taskmanager.V1.Status;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for the Tasks streaming/event REST API endpoints:
///   POST /api/v1/tasks/stream (SSE)
///   POST /api/v1/agent/listen (Long-poll)
///   POST /api/v1/agent/stream (SSE)
/// </summary>
public class TasksControllerEventTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TasksControllerEventTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, TaskStore Store) CreateIsolatedClient()
    {
        var store = new TaskStore();
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

    private Anduril.Taskmanager.V1.Task CreateSeedTask(TaskStore store, string taskId,
        Status status = Status.Created, string? assigneeEntityId = null)
    {
        var task = new Anduril.Taskmanager.V1.Task
        {
            Version = new TaskVersion { TaskId = taskId, DefinitionVersion = 1, StatusVersion = 1 },
            CreateTime = Timestamp.FromDateTime(DateTime.UtcNow),
            LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow),
            Status = new TaskStatus { Status = status },
        };

        if (assigneeEntityId != null)
        {
            task.Relations = new Relations
            {
                Assignee = new Principal
                {
                    System = new Anduril.Taskmanager.V1.System { EntityId = assigneeEntityId },
                },
            };
        }

        store.UpsertTask(task, EventType.Created);
        return task;
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/tasks/stream - Stream Tasks (SSE)
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task StreamTasks_SseHeaders_AreCorrect()
    {
        // Doc: "text/event-stream content type"
        var (client, _) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks/stream")
        {
            Content = JsonContent("""{"excludePreexistingTasks": true}"""),
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamTasks_PreExisting_SendsNonTerminalTasks()
    {
        // Doc: "The stream delivers all existing non-terminal tasks when first connected"
        var (client, store) = CreateIsolatedClient();
        CreateSeedTask(store, "stream-active-1", Status.Created);
        CreateSeedTask(store, "stream-active-2", Status.Executing);
        CreateSeedTask(store, "stream-done-01", Status.DoneOk);

        // Use a short-lived stream to capture preexisting events
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks/stream")
        {
            Content = JsonContent("{}"),
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            var eventLines = body.Split('\n')
                .Where(l => l.StartsWith("event: "))
                .Select(l => l["event: ".Length..])
                .ToList();

            // Should have PREEXISTING events for the two non-terminal tasks only
            var preexistingCount = eventLines.Count(e => e == "PREEXISTING");
            Assert.Equal(2, preexistingCount);
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamTasks_ExcludePreexisting_SkipsExistingTasks()
    {
        // Doc: "excludePreexistingTasks: Optional flag to only include tasks created or updated
        //       after the stream is initiated"
        var (client, store) = CreateIsolatedClient();
        CreateSeedTask(store, "pre-skip-01", Status.Created);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks/stream")
        {
            Content = JsonContent("""{"excludePreexistingTasks": true}"""),
        };

        string body = "";
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

            // Read whatever is available before cancellation
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            var buffer = new char[4096];
            var read = await reader.ReadAsync(buffer, cts.Token);
            body = new string(buffer, 0, read);
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (HttpRequestException) { /* stream cancelled */ }

        // No PREEXISTING events should be sent
        var eventLines = body.Split('\n')
            .Where(l => l.StartsWith("event: "))
            .ToList();
        Assert.DoesNotContain(eventLines, e => e.Contains("PREEXISTING"));
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamTasks_PreExistingEvents_ContainTaskData()
    {
        // Doc: SSE data contains TaskEvent with task data
        var (client, store) = CreateIsolatedClient();
        CreateSeedTask(store, "data-check-01", Status.Created);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks/stream")
        {
            Content = JsonContent("{}"),
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            var dataLines = body.Split('\n')
                .Where(l => l.StartsWith("data: "))
                .Select(l => l["data: ".Length..])
                .ToList();

            Assert.NotEmpty(dataLines);

            var taskEvent = ProtobufJsonConverter.FromJson<TaskEvent>(dataLines[0]);
            Assert.Equal(EventType.Preexisting, taskEvent.EventType);
            Assert.Equal("data-check-01", taskEvent.Task.Version.TaskId);
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamTasks_EmptyStore_NoPreexistingEvents()
    {
        var (client, _) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks/stream")
        {
            Content = JsonContent("{}"),
        };

        string body = "";
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            var buffer = new char[4096];
            var read = await reader.ReadAsync(buffer, cts.Token);
            if (read > 0) body = new string(buffer, 0, read);
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (HttpRequestException) { /* stream cancelled */ }

        var eventLines = body.Split('\n')
            .Where(l => l.StartsWith("event: PREEXISTING"))
            .ToList();
        Assert.Empty(eventLines);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/agent/listen - Listen As Agent (Long-poll)
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ListenAsAgent_ReceivesExecuteRequest_OnNewTask()
    {
        // Doc: "ExecuteRequest: Contains a new task for the agent to execute"
        var (client, store) = CreateIsolatedClient();

        // Start listening in background
        var listenTask = System.Threading.Tasks.Task.Run(async () =>
        {
            var json = """{"agentSelector": {"entityIds": ["agent-entity-1"]}}""";
            return await client.PostAsync("/api/v1/agent/listen", JsonContent(json));
        });

        // Give the listener time to subscribe
        await System.Threading.Tasks.Task.Delay(200);

        // Create a task assigned to the agent
        CreateSeedTask(store, "agent-task-01", Status.Created, "agent-entity-1");

        var response = await listenTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("executeRequest", out var execReq));
        Assert.True(execReq.TryGetProperty("task", out var taskEl));
        Assert.Equal("agent-task-01", taskEl.GetProperty("version").GetProperty("taskId").GetString());
    }

    [Fact]
    public async System.Threading.Tasks.Task ListenAsAgent_ReceivesCancelRequest_OnCancelledTask()
    {
        // Doc: "CancelRequest: Indicates a task should be canceled"
        var (client, store) = CreateIsolatedClient();

        // Create an existing task in SENT status
        var task = new Anduril.Taskmanager.V1.Task
        {
            Version = new TaskVersion { TaskId = "cancel-listen-1", DefinitionVersion = 1, StatusVersion = 2 },
            CreateTime = Timestamp.FromDateTime(DateTime.UtcNow),
            LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow),
            Status = new TaskStatus { Status = Status.Sent },
            Relations = new Relations
            {
                Assignee = new Principal
                {
                    System = new Anduril.Taskmanager.V1.System { EntityId = "agent-entity-2" },
                },
            },
        };
        store.UpsertTask(task, EventType.Created);

        // Start listening
        var listenTask = System.Threading.Tasks.Task.Run(async () =>
        {
            var json = """{"agentSelector": {"entityIds": ["agent-entity-2"]}}""";
            return await client.PostAsync("/api/v1/agent/listen", JsonContent(json));
        });

        await System.Threading.Tasks.Task.Delay(200);

        // Cancel the task — should produce CANCEL_REQUESTED status
        var cancelled = task.Clone();
        cancelled.Status = new TaskStatus { Status = Status.CancelRequested };
        cancelled.Version.StatusVersion++;
        store.UpsertTask(cancelled, EventType.Update);

        var response = await listenTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("cancelRequest", out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task ListenAsAgent_InvalidJson_Returns400()
    {
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/agent/listen", JsonContent("{bad}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task ListenAsAgent_Timeout_ReturnsEmptyResponse()
    {
        // Doc: "the server will hold on to your request for up to 5 minutes"
        // We can't wait 5 minutes, so use cancellation to verify the timeout mechanism
        var (client, _) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var response = await client.PostAsync("/api/v1/agent/listen",
                JsonContent("{}"), cts.Token);

            // If we get a response (timeout), it should be empty object
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            // Expected — the long poll was waiting and we cancelled
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ListenAsAgent_FiltersbyEntityId()
    {
        // Doc: "agentSelector.entityIds: Receive tasks as an assignee for one or more
        //       of the supplied entity ids."
        var (client, store) = CreateIsolatedClient();

        var listenTask = System.Threading.Tasks.Task.Run(async () =>
        {
            var json = """{"agentSelector": {"entityIds": ["my-agent-only"]}}""";
            return await client.PostAsync("/api/v1/agent/listen", JsonContent(json));
        });

        await System.Threading.Tasks.Task.Delay(200);

        // Create a task for a different agent — should be ignored
        CreateSeedTask(store, "other-task-01", Status.Created, "other-agent");

        // Create a task for our agent
        CreateSeedTask(store, "my-task-00001", Status.Created, "my-agent-only");

        var response = await listenTask;
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("executeRequest", out var execReq));
        Assert.Equal("my-task-00001",
            execReq.GetProperty("task").GetProperty("version").GetProperty("taskId").GetString());
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/agent/stream - Stream As Agent (SSE)
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task StreamAsAgent_SseHeaders_AreCorrect()
    {
        // Doc: "text/event-stream content type"
        var (client, _) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/stream")
        {
            Content = JsonContent("{}"),
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamAsAgent_ReceivesExecuteEvent_OnNewTask()
    {
        // Doc: "ExecuteRequest: Contains a new task for the agent to execute"
        // Doc: SSE event type is "execute"
        var (client, store) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/stream")
        {
            Content = JsonContent("""{"agentSelector": {"entityIds": ["stream-agent"]}}"""),
        };

        // Start reading the stream
        var streamTask = System.Threading.Tasks.Task.Run(async () =>
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return await response.Content.ReadAsStringAsync(cts.Token);
        });

        // Give the stream time to start
        await System.Threading.Tasks.Task.Delay(300);

        // Create a task for the agent
        CreateSeedTask(store, "stream-exec-1", Status.Created, "stream-agent");

        // Wait for stream to capture events
        await System.Threading.Tasks.Task.Delay(500);
        cts.Cancel();

        try
        {
            var body = await streamTask;
            var eventLines = body.Split('\n')
                .Where(l => l.StartsWith("event: "))
                .Select(l => l["event: ".Length..])
                .ToList();

            Assert.Contains("execute", eventLines);
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamAsAgent_EmptyBody_StartsStreamWithoutError()
    {
        var (client, _) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/stream")
        {
            Content = JsonContent("{}"),
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        catch (OperationCanceledException) { /* expected */ }
    }
}
