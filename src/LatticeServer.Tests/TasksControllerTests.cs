using System.Net;
using System.Text;
using System.Text.Json;
using Anduril.Taskmanager.V1;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = Anduril.Taskmanager.V1.TaskStatus;
using Status = Anduril.Taskmanager.V1.Status;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for the Tasks REST API endpoints:
///   POST /api/v1/tasks (Create)
///   GET  /api/v1/tasks/{taskId} (Get)
///   PUT  /api/v1/tasks/{taskId}/status (UpdateStatus)
///   PUT  /api/v1/tasks/{taskId}/cancel (Cancel)
///   POST /api/v1/tasks/query (Query)
/// </summary>
public class TasksControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TasksControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a fresh HttpClient backed by its own TaskStore so tests don't share state.
    /// </summary>
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

    /// <summary>
    /// Helper to create a task via the REST API and return the response body as a Task proto.
    /// </summary>
    private async Task<(HttpResponseMessage Response, Anduril.Taskmanager.V1.Task? Task)> CreateTaskViaApi(
        HttpClient client, string? taskId = null, string? description = null)
    {
        var props = new List<string>();
        if (taskId != null) props.Add($"\"taskId\": \"{taskId}\"");
        if (description != null) props.Add($"\"description\": \"{description}\"");

        var json = "{" + string.Join(", ", props) + "}";
        var response = await client.PostAsync("/api/v1/tasks", JsonContent(json));

        Anduril.Taskmanager.V1.Task? task = null;
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        }
        return (response, task);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/tasks - Create Task
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_ValidRequest_Returns201()
    {
        // Doc: "'201': Task creation was successful"
        var (client, _) = CreateIsolatedClient();

        var (response, _) = await CreateTaskViaApi(client, taskId: "test-task-01");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_ValidRequest_ReturnsTaskWithStatusSent()
    {
        // Proto spec: "sets the initial task state to STATUS_SENT"
        var (client, _) = CreateIsolatedClient();

        var (_, task) = await CreateTaskViaApi(client, taskId: "test-task-02");

        Assert.NotNull(task);
        Assert.Equal(Status.Sent, task.Status.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_NoTaskId_GeneratesGuid()
    {
        // Doc: "If non-empty, will set the requested Task ID, otherwise will generate a new random GUID."
        var (client, _) = CreateIsolatedClient();

        var (response, task) = await CreateTaskViaApi(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(task);
        Assert.False(string.IsNullOrEmpty(task.Version.TaskId));
        Assert.True(Guid.TryParse(task.Version.TaskId, out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_CustomTaskId_UsesProvidedId()
    {
        // Doc: "If non-empty, will set the requested Task ID"
        var (client, _) = CreateIsolatedClient();

        var (_, task) = await CreateTaskViaApi(client, taskId: "my-custom-id-123");

        Assert.NotNull(task);
        Assert.Equal("my-custom-id-123", task.Version.TaskId);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_InvalidTaskId_TooShort_Returns400()
    {
        // Doc: "Will reject if supplied Task ID does not match [A-Za-z0-9_-.]{5,36}."
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/tasks", JsonContent("""{"taskId": "ab"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ARGUMENT", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_InvalidTaskId_IllegalChars_Returns400()
    {
        // Doc: "Will reject if supplied Task ID does not match [A-Za-z0-9_-.]{5,36}."
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/tasks", JsonContent("""{"taskId": "task with spaces!"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_DuplicateTaskId_Returns409()
    {
        // Doc: Task creation should reject duplicates (ALREADY_EXISTS)
        var (client, _) = CreateIsolatedClient();

        await CreateTaskViaApi(client, taskId: "dupe-task-01");
        var (response, _) = await CreateTaskViaApi(client, taskId: "dupe-task-01");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ALREADY_EXISTS", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_InvalidJson_Returns400()
    {
        // Doc: "'400': Bad request"
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/tasks", JsonContent("{invalid json}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ARGUMENT", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_SetsVersionFields()
    {
        // Doc: "starts at 1 on creation" for both definitionVersion and statusVersion
        var (client, _) = CreateIsolatedClient();

        var (_, task) = await CreateTaskViaApi(client, taskId: "version-test");

        Assert.NotNull(task);
        Assert.Equal(1u, task.Version.DefinitionVersion);
        Assert.Equal(1u, task.Version.StatusVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_SetsTimestamps()
    {
        var (client, _) = CreateIsolatedClient();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var (_, task) = await CreateTaskViaApi(client, taskId: "time-test-1");

        Assert.NotNull(task);
        Assert.NotNull(task.CreateTime);
        Assert.NotNull(task.LastUpdateTime);
        Assert.True(task.CreateTime.ToDateTime() >= before);
        Assert.True(task.LastUpdateTime.ToDateTime() >= before);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_WithDescription_ParsesDescription()
    {
        // Doc: TaskCreation has "description" field
        var (client, _) = CreateIsolatedClient();

        var (_, task) = await CreateTaskViaApi(client, taskId: "desc-test-1", description: "My task description");

        Assert.NotNull(task);
        Assert.Equal("My task description", task.Description);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_WithAuthor_SetsCreatedByAndLastUpdatedBy()
    {
        // Doc: TaskCreation has "author" field → sets createdBy and lastUpdatedBy
        var (client, _) = CreateIsolatedClient();

        var json = """
        {
            "taskId": "author-test-1",
            "author": {
                "user": { "userId": "user-123" }
            }
        }
        """;
        var response = await client.PostAsync("/api/v1/tasks", JsonContent(json));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal("user-123", task.CreatedBy.User.UserId);
        Assert.Equal("user-123", task.LastUpdatedBy.User.UserId);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_WithRelations_ParsesRelations()
    {
        // Doc: TaskCreation has "relations" field
        var (client, _) = CreateIsolatedClient();

        var json = """
        {
            "taskId": "rel-test-01",
            "relations": {
                "parentTaskId": "parent-task-1",
                "assignee": {
                    "system": { "entityId": "entity-abc" }
                }
            }
        }
        """;
        var response = await client.PostAsync("/api/v1/tasks", JsonContent(json));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal("parent-task-1", task.Relations.ParentTaskId);
        Assert.Equal("entity-abc", task.Relations.Assignee.System.EntityId);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_WithIsExecutedElsewhere_ParsesFlag()
    {
        // Doc: TaskCreation has "isExecutedElsewhere" field
        var (client, _) = CreateIsolatedClient();

        var json = """{"taskId": "exec-elsewhere", "isExecutedElsewhere": true}""";
        var response = await client.PostAsync("/api/v1/tasks", JsonContent(json));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.True(task.IsExecutedElsewhere);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateTask_StoredInStore()
    {
        var (client, store) = CreateIsolatedClient();

        await CreateTaskViaApi(client, taskId: "store-test-1");

        Assert.NotNull(store.GetTask("store-test-1"));
    }

    // ───────────────────────────────────────────────
    // GET /api/v1/tasks/{taskId} - Get Task
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task GetTask_ExistingTask_Returns200()
    {
        // Doc: "'200': Task retrieval was successful."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "get-test-01");

        var response = await client.GetAsync("/api/v1/tasks/get-test-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTask_ExistingTask_ReturnsCorrectData()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "get-data-01", description: "Retrieve me");

        var response = await client.GetAsync("/api/v1/tasks/get-data-01");
        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);

        Assert.Equal("get-data-01", task.Version.TaskId);
        Assert.Equal("Retrieve me", task.Description);
        Assert.Equal(Status.Sent, task.Status.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTask_NonExistent_Returns404()
    {
        // Doc: "'404': The specified resource was not found"
        var (client, _) = CreateIsolatedClient();

        var response = await client.GetAsync("/api/v1/tasks/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTask_AfterCreate_RoundTrip()
    {
        var (client, _) = CreateIsolatedClient();
        var (_, created) = await CreateTaskViaApi(client, taskId: "roundtrip-01", description: "Round trip");

        var response = await client.GetAsync("/api/v1/tasks/roundtrip-01");
        var body = await response.Content.ReadAsStringAsync();
        var retrieved = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);

        Assert.Equal(created!.Version.TaskId, retrieved.Version.TaskId);
        Assert.Equal(created.Description, retrieved.Description);
    }

    // ───────────────────────────────────────────────
    // PUT /api/v1/tasks/{taskId}/status - Update Task Status
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_ValidUpdate_Returns200()
    {
        // Doc: "'200': Task status update was successful"
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "update-test-1");

        var json = """
        {
            "statusVersion": 1,
            "newStatus": {
                "status": "STATUS_EXECUTING"
            }
        }
        """;
        var response = await client.PutAsync("/api/v1/tasks/update-test-1/status", JsonContent(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_StoresClientStatusVersion()
    {
        // The server stores the client-provided status version as-is (no increment).
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "ver-inc-test");

        var json = """{"statusVersion": 1, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PutAsync("/api/v1/tasks/ver-inc-test/status", JsonContent(json));

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(1u, task.Version.StatusVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_ChangesTaskStatus()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "status-change");

        var json = """{"statusVersion": 1, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PutAsync("/api/v1/tasks/status-change/status", JsonContent(json));

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(Status.Executing, task.Status.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_VersionMismatch_Returns409()
    {
        // Doc: "The system rejects updates with mismatched versions to prevent race conditions."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "mismatch-test");

        var json = """{"statusVersion": 0, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PutAsync("/api/v1/tasks/mismatch-test/status", JsonContent(json));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ABORTED", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_TerminalState_Returns422()
    {
        // Doc: "Terminal states (STATUS_DONE_OK and STATUS_DONE_NOT_OK) are permanent;
        //       once a task reaches these states, no further updates are allowed."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "terminal-test");

        // Move to DONE_OK
        var doneJson = """{"statusVersion": 1, "newStatus": {"status": "STATUS_DONE_OK"}}""";
        await client.PutAsync("/api/v1/tasks/terminal-test/status", JsonContent(doneJson));

        // Try to update again
        var updateJson = """{"statusVersion": 2, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PutAsync("/api/v1/tasks/terminal-test/status", JsonContent(updateJson));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("FAILED_PRECONDITION", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_TaskNotFound_Returns404()
    {
        // Doc: "'404': The specified resource was not found"
        var (client, _) = CreateIsolatedClient();

        var json = """{"statusVersion": 1, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PutAsync("/api/v1/tasks/nonexistent/status", JsonContent(json));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_InvalidJson_Returns400()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "bad-json-upd");

        var response = await client.PutAsync("/api/v1/tasks/bad-json-upd/status", JsonContent("{bad}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_WithAuthor_SetsLastUpdatedBy()
    {
        // Doc: TaskStatusUpdate has "author" field
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "author-upd-1");

        var json = """
        {
            "statusVersion": 1,
            "newStatus": {"status": "STATUS_ACK"},
            "author": {"user": {"userId": "updater-99"}}
        }
        """;
        var response = await client.PutAsync("/api/v1/tasks/author-upd-1/status", JsonContent(json));
        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);

        Assert.Equal("updater-99", task.LastUpdatedBy.User.UserId);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_MultipleUpdates_AcceptsIncreasingVersions()
    {
        // Test the full lifecycle: Created -> Executing -> DoneOk with increasing client versions
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "lifecycle-01");

        // Update 1: Created -> Executing (client sends version 2)
        var json1 = """{"statusVersion": 2, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var resp1 = await client.PutAsync("/api/v1/tasks/lifecycle-01/status", JsonContent(json1));
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Update 2: Executing -> DoneOk (client sends version 3)
        var json2 = """{"statusVersion": 3, "newStatus": {"status": "STATUS_DONE_OK"}}""";
        var resp2 = await client.PutAsync("/api/v1/tasks/lifecycle-01/status", JsonContent(json2));
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        var body = await resp2.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(3u, task.Version.StatusVersion);
        Assert.Equal(Status.DoneOk, task.Status.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatus_DoneNotOk_IsTerminal()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "done-not-ok-1");

        var doneJson = """{"statusVersion": 1, "newStatus": {"status": "STATUS_DONE_NOT_OK"}}""";
        await client.PutAsync("/api/v1/tasks/done-not-ok-1/status", JsonContent(doneJson));

        var updateJson = """{"statusVersion": 2, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PutAsync("/api/v1/tasks/done-not-ok-1/status", JsonContent(updateJson));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // PUT /api/v1/tasks/{taskId}/cancel - Cancel Task
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_PreSentTask_CancelsImmediately()
    {
        // Doc: "If the task has not been sent to an agent, it cancels immediately and transitions
        //       the task to a terminal state (STATUS_DONE_NOT_OK with ERROR_CODE_CANCELLED)."
        // Note: REST CreateTask sets STATUS_SENT, so to test pre-sent cancellation we seed
        //       a task with STATUS_CREATED directly in the store.
        var (client, store) = CreateIsolatedClient();
        var task = new Anduril.Taskmanager.V1.Task
        {
            Version = new TaskVersion { TaskId = "cancel-pre-1", DefinitionVersion = 1, StatusVersion = 1 },
            CreateTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            LastUpdateTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            Status = new TaskStatus { Status = Status.Created },
        };
        store.UpsertTask(task, Anduril.Taskmanager.V1.EventType.Created);

        var response = await client.PutAsync("/api/v1/tasks/cancel-pre-1/cancel", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var cancelled = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(Status.DoneNotOk, cancelled.Status.Status);
        Assert.Equal(ErrorCode.Cancelled, cancelled.Status.TaskError.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_SentTask_SetsCancelRequested()
    {
        // Doc: "If the task has already been sent to an agent, the cancellation request is routed
        //       to the agent with a delivery status of DELIVERY_STATUS_PENDING_CANCEL."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "cancel-sent-1");

        // Move task to SENT status (past Created)
        var updateJson = """{"statusVersion": 1, "newStatus": {"status": "STATUS_SENT"}}""";
        await client.PutAsync("/api/v1/tasks/cancel-sent-1/status", JsonContent(updateJson));

        var response = await client.PutAsync("/api/v1/tasks/cancel-sent-1/cancel", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(Status.CancelRequested, task.Status.Status);
        Assert.Equal(DeliveryStatus.PendingCancel, task.DeliveryState.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_TerminalTask_Returns422()
    {
        // Doc: Tasks in terminal state cannot be cancelled
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "cancel-term-1");

        // Move to terminal
        var doneJson = """{"statusVersion": 1, "newStatus": {"status": "STATUS_DONE_OK"}}""";
        await client.PutAsync("/api/v1/tasks/cancel-term-1/status", JsonContent(doneJson));

        var response = await client.PutAsync("/api/v1/tasks/cancel-term-1/cancel", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("FAILED_PRECONDITION", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_TaskNotFound_Returns404()
    {
        // Doc: "'404': The specified resource was not found"
        var (client, _) = CreateIsolatedClient();

        var response = await client.PutAsync("/api/v1/tasks/nonexistent-cancel/cancel", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_WithAuthor_SetsLastUpdatedBy()
    {
        // Doc: TaskCancellation has "author" field
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "cancel-auth-1");

        var json = """{"author": {"user": {"userId": "canceller-1"}}}""";
        var response = await client.PutAsync("/api/v1/tasks/cancel-auth-1/cancel", JsonContent(json));

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal("canceller-1", task.LastUpdatedBy.User.UserId);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_IncrementsStatusVersion()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "cancel-ver-01");

        var response = await client.PutAsync("/api/v1/tasks/cancel-ver-01/cancel", JsonContent("{}"));

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(2u, task.Version.StatusVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelTask_ExecutingTask_SetsCancelRequested()
    {
        // Executing tasks should get CANCEL_REQUESTED, not immediate cancellation
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "cancel-exec-1");

        var updateJson = """{"statusVersion": 1, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        await client.PutAsync("/api/v1/tasks/cancel-exec-1/status", JsonContent(updateJson));

        var response = await client.PutAsync("/api/v1/tasks/cancel-exec-1/cancel", JsonContent("{}"));

        var body = await response.Content.ReadAsStringAsync();
        var task = ProtobufJsonConverter.FromJson<Anduril.Taskmanager.V1.Task>(body);
        Assert.Equal(Status.CancelRequested, task.Status.Status);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/tasks/query - Query Tasks
    // ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_NoFilters_ReturnsAllTasks()
    {
        // Doc: "By default, with no filters applied, this returns the latest version of all tasks."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "query-task-1");
        await CreateTaskViaApi(client, taskId: "query-task-2");
        await CreateTaskViaApi(client, taskId: "query-task-3");

        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var tasks = doc.RootElement.GetProperty("tasks");
        Assert.Equal(3, tasks.GetArrayLength());
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_EmptyBody_ReturnsAllTasks()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "query-empty-1");

        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent(""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var tasks = doc.RootElement.GetProperty("tasks");
        Assert.Equal(1, tasks.GetArrayLength());
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_StatusFilter_ReturnsMatchingOnly()
    {
        // Doc: "statusFilter: Status of the Task to filter by, inclusive."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "qf-created-1");
        await CreateTaskViaApi(client, taskId: "qf-executing");

        // Move one to executing
        var updateJson = """{"statusVersion": 1, "newStatus": {"status": "STATUS_EXECUTING"}}""";
        await client.PutAsync("/api/v1/tasks/qf-executing/status", JsonContent(updateJson));

        var queryJson = """{"statusFilter": {"status": "STATUS_EXECUTING"}}""";
        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent(queryJson));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var tasks = doc.RootElement.GetProperty("tasks");
        Assert.Equal(1, tasks.GetArrayLength());
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_ParentTaskIdFilter_ReturnsChildren()
    {
        // Doc: "parentTaskId: If present matches Tasks with this parent Task ID."
        // Doc: "Note: this is mutually exclusive with all other query parameters"
        var (client, _) = CreateIsolatedClient();

        // Create parent and child tasks
        await CreateTaskViaApi(client, taskId: "parent-task");

        var childJson = """
        {
            "taskId": "child-task-1",
            "relations": {"parentTaskId": "parent-task"}
        }
        """;
        await client.PostAsync("/api/v1/tasks", JsonContent(childJson));

        var childJson2 = """
        {
            "taskId": "child-task-2",
            "relations": {"parentTaskId": "parent-task"}
        }
        """;
        await client.PostAsync("/api/v1/tasks", JsonContent(childJson2));

        // Create unrelated task
        await CreateTaskViaApi(client, taskId: "unrelated-01");

        var queryJson = """{"parentTaskId": "parent-task"}""";
        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent(queryJson));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var tasks = doc.RootElement.GetProperty("tasks");
        Assert.Equal(2, tasks.GetArrayLength());
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_TimeRangeFilter_ReturnsMatchingTasks()
    {
        // Doc: "updateTimeRange: If provided, only provides Tasks updated within the time range."
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "time-range-1");

        var startTime = DateTime.UtcNow.AddMinutes(-1).ToString("o");
        var endTime = DateTime.UtcNow.AddMinutes(1).ToString("o");

        var queryJson = $$$"""
        {
            "updateTimeRange": {
                "startTime": "{{{startTime}}}",
                "endTime": "{{{endTime}}}"
            }
        }
        """;
        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent(queryJson));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var tasks = doc.RootElement.GetProperty("tasks");
        Assert.True(tasks.GetArrayLength() >= 1);
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_InvalidJson_Returns400()
    {
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent("{bad}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_NoMatchingStatus_ReturnsEmpty()
    {
        var (client, _) = CreateIsolatedClient();
        await CreateTaskViaApi(client, taskId: "no-match-01");

        var queryJson = """{"statusFilter": {"status": "STATUS_DONE_OK"}}""";
        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent(queryJson));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var tasks = doc.RootElement.GetProperty("tasks");
        Assert.Equal(0, tasks.GetArrayLength());
    }

    [Fact]
    public async System.Threading.Tasks.Task QueryTasks_ResponseFormat_HasTasksArray()
    {
        // Doc: Response schema is TaskQueryResults with "tasks" array
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/tasks/query", JsonContent("{}"));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("tasks", out _));
    }
}
