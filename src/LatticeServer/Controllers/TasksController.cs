using System.Text.Json;
using System.Text.RegularExpressions;
using Anduril.Taskmanager.V1;
using Google.Protobuf.WellKnownTypes;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskStatus = Anduril.Taskmanager.V1.TaskStatus;
using Status = Anduril.Taskmanager.V1.Status;

namespace LatticeServer.Controllers;

[ApiController]
public class TasksController : ControllerBase
{
    private static readonly Regex TaskIdRegex = new(@"^[A-Za-z0-9_\-.]{5,36}$", RegexOptions.Compiled);

    private readonly ILogger<TasksController> _logger;
    private readonly TaskStore _store;

    public TasksController(ILogger<TasksController> logger, TaskStore store)
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>
    /// POST /api/v1/tasks - Create task
    /// </summary>
    [HttpPost("api/v1/tasks")]
    public async Task<IActionResult> CreateTask()
    {
        var body = await ReadBodyAsync();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid JSON: {ex.Message}" });
        }

        // Parse fields from the JSON request body (TaskCreation schema)
        var root = doc.RootElement;

        var taskId = root.TryGetProperty("taskId", out var tid) ? tid.GetString() : null;
        if (string.IsNullOrEmpty(taskId))
        {
            taskId = Guid.NewGuid().ToString();
        }
        else if (!TaskIdRegex.IsMatch(taskId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "task_id must match [A-Za-z0-9_-.]{5,36}" });
        }

        if (_store.GetTask(taskId) != null)
        {
            return Conflict(new { code = "ALREADY_EXISTS", message = $"Task {taskId} already exists" });
        }

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var task = new Anduril.Taskmanager.V1.Task
        {
            Version = new TaskVersion
            {
                TaskId = taskId,
                DefinitionVersion = 1,
                StatusVersion = 1,
            },
            CreateTime = now,
            LastUpdateTime = now,
            Status = new TaskStatus
            {
                Status = Status.Sent,
            },
        };

        // Parse displayName
        if (root.TryGetProperty("displayName", out var dn))
        {
            task.DisplayName = dn.GetString() ?? "";
        }

        // Parse specification
        if (root.TryGetProperty("specification", out var spec))
        {
            try
            {
                task.Specification = ProtobufJsonConverter.FromJson<Google.Protobuf.WellKnownTypes.Any>(spec.GetRawText());
            }
            catch { /* ignore invalid spec */ }
        }

        // Parse author
        if (root.TryGetProperty("author", out var author))
        {
            try
            {
                var principal = ProtobufJsonConverter.FromJson<Principal>(author.GetRawText());
                task.CreatedBy = principal;
                task.LastUpdatedBy = principal;
            }
            catch { /* ignore */ }
        }

        // Parse relations
        if (root.TryGetProperty("relations", out var relations))
        {
            try
            {
                task.Relations = ProtobufJsonConverter.FromJson<Relations>(relations.GetRawText());
            }
            catch { /* ignore */ }
        }

        // Parse description
        if (root.TryGetProperty("description", out var desc))
        {
            task.Description = desc.GetString() ?? "";
        }

        // Parse isExecutedElsewhere
        if (root.TryGetProperty("isExecutedElsewhere", out var iee))
        {
            task.IsExecutedElsewhere = iee.GetBoolean();
        }

        // Parse initialEntities
        if (root.TryGetProperty("initialEntities", out var ie) && ie.ValueKind == JsonValueKind.Array)
        {
            foreach (var entityEl in ie.EnumerateArray())
            {
                try
                {
                    var taskEntity = ProtobufJsonConverter.FromJson<TaskEntity>(entityEl.GetRawText());
                    task.InitialEntities.Add(taskEntity);
                }
                catch { /* skip invalid */ }
            }
        }

        // Parse retryStrategy
        if (root.TryGetProperty("retryStrategy", out var rs))
        {
            try
            {
                task.RetryStrategy = ProtobufJsonConverter.FromJson<RetryStrategy>(rs.GetRawText());
            }
            catch { /* ignore */ }
        }

        _logger.LogInformation("REST CreateTask: created task {TaskId}", taskId);
        _store.UpsertTask(task, EventType.Created);

        var result = ProtobufJsonRawResult(task);
        result.StatusCode = 201;
        return result;
    }

    /// <summary>
    /// GET /api/v1/tasks/{taskId} - Get task
    /// </summary>
    [HttpGet("api/v1/tasks/{taskId}")]
    public IActionResult GetTask(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "task_id is required" });
        }

        var task = _store.GetTask(taskId);
        if (task == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Task {taskId} not found" });
        }

        _logger.LogInformation("REST GetTask: {TaskId}", taskId);
        return ProtobufJsonRawResult(task);
    }

    /// <summary>
    /// PUT /api/v1/tasks/{taskId}/status - Update task status
    /// </summary>
    [HttpPut("api/v1/tasks/{taskId}/status")]
    public async Task<IActionResult> UpdateTaskStatus(string taskId)
    {
        var body = await ReadBodyAsync();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid JSON: {ex.Message}" });
        }

        var task = _store.GetTask(taskId);
        if (task == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Task {taskId} not found" });
        }

        // Reject updates to terminal tasks
        if (task.Status.Status is Status.DoneOk or Status.DoneNotOk)
        {
            return UnprocessableEntity(new { code = "FAILED_PRECONDITION", message = $"Task {taskId} is in terminal state {task.Status.Status}" });
        }

        var root = doc.RootElement;

        // Parse status version for optimistic concurrency
        uint statusVersion = 0;
        if (root.TryGetProperty("version", out var versionEl))
        {
            if (versionEl.TryGetProperty("statusVersion", out var sv))
            {
                statusVersion = sv.GetUInt32();
            }
        }
        else if (root.TryGetProperty("statusVersion", out var svDirect))
        {
            statusVersion = svDirect.GetUInt32();
        }

        if (statusVersion < task.Version.StatusVersion)
        {
            return Conflict(new { code = "ABORTED", message = $"Status version mismatch: expected >= {task.Version.StatusVersion}, got {statusVersion}" });
        }

        var updated = task.Clone();
        updated.Version.StatusVersion = statusVersion;
        updated.LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow);

        // Parse newStatus (per API spec: TaskStatusUpdate.newStatus)
        if (root.TryGetProperty("newStatus", out var statusEl))
        {
            try
            {
                updated.Status = ProtobufJsonConverter.FromJson<TaskStatus>(statusEl.GetRawText());
            }
            catch { /* keep existing */ }
        }

        // Parse author
        if (root.TryGetProperty("author", out var authorEl))
        {
            try
            {
                updated.LastUpdatedBy = ProtobufJsonConverter.FromJson<Principal>(authorEl.GetRawText());
            }
            catch { /* keep existing */ }
        }

        // Parse scheduledTime
        if (root.TryGetProperty("scheduledTime", out var stEl))
        {
            try
            {
                updated.ScheduledTime = Timestamp.Parser.ParseJson(stEl.GetRawText());
            }
            catch { /* ignore */ }
        }

        _logger.LogInformation("REST UpdateTaskStatus: task {TaskId} -> {Status}", taskId, updated.Status.Status);
        _store.UpsertTask(updated, EventType.Update);

        return ProtobufJsonRawResult(updated);
    }

    /// <summary>
    /// PUT /api/v1/tasks/{taskId}/cancel - Cancel task
    /// </summary>
    [HttpPut("api/v1/tasks/{taskId}/cancel")]
    public async Task<IActionResult> CancelTask(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "task_id is required" });
        }

        var task = _store.GetTask(taskId);
        if (task == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Task {taskId} not found" });
        }

        if (task.Status.Status is Status.DoneOk or Status.DoneNotOk)
        {
            return UnprocessableEntity(new { code = "FAILED_PRECONDITION", message = $"Task {taskId} is already in terminal state {task.Status.Status}" });
        }

        // Parse optional author from body
        Principal? author = null;
        var body = await ReadBodyAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("author", out var authorEl))
                {
                    author = ProtobufJsonConverter.FromJson<Principal>(authorEl.GetRawText());
                }
            }
            catch { /* ignore */ }
        }

        var updated = task.Clone();
        updated.LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow);
        updated.Version.StatusVersion++;

        if (author != null)
        {
            updated.LastUpdatedBy = author;
        }

        if (task.Status.Status is Status.Created or Status.ScheduledInManager)
        {
            updated.Status = new TaskStatus
            {
                Status = Status.DoneNotOk,
                TaskError = new TaskError
                {
                    Code = ErrorCode.Cancelled,
                    Message = "Task cancelled before delivery to agent",
                },
            };
        }
        else
        {
            updated.DeliveryState = new DeliveryState
            {
                Status = DeliveryStatus.PendingCancel,
            };
            updated.Status = new TaskStatus
            {
                Status = Status.CancelRequested,
            };
        }

        _logger.LogInformation("REST CancelTask: task {TaskId} -> {Status}", taskId, updated.Status.Status);
        _store.UpsertTask(updated, EventType.Update);

        return ProtobufJsonRawResult(updated);
    }

    /// <summary>
    /// POST /api/v1/tasks/query - Query tasks
    /// </summary>
    [HttpPost("api/v1/tasks/query")]
    public async Task<IActionResult> QueryTasks()
    {
        var body = await ReadBodyAsync();

        string? parentTaskId = null;
        string? statusFilter = null;
        string? startTime = null;
        string? endTime = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("parentTaskId", out var ptid))
                    parentTaskId = ptid.GetString();

                if (root.TryGetProperty("statusFilter", out var sf) &&
                    sf.TryGetProperty("status", out var status))
                    statusFilter = status.GetString();

                if (root.TryGetProperty("updateTimeRange", out var utr))
                {
                    if (utr.TryGetProperty("startTime", out var st))
                        startTime = st.GetString();
                    if (utr.TryGetProperty("endTime", out var et))
                        endTime = et.GetString();
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid JSON: {ex.Message}" });
            }
        }

        var allTasks = _store.GetAllTasks();
        IEnumerable<Anduril.Taskmanager.V1.Task> filtered = allTasks;

        if (!string.IsNullOrEmpty(parentTaskId))
        {
            filtered = filtered.Where(t =>
                t.Relations != null && t.Relations.ParentTaskId == parentTaskId);
        }
        else
        {
            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (System.Enum.TryParse<Status>(ConvertStatusString(statusFilter), true, out var parsedStatus))
                {
                    filtered = filtered.Where(t => t.Status.Status == parsedStatus);
                }
            }

            if (!string.IsNullOrEmpty(startTime) && DateTime.TryParse(startTime, out var startDt))
            {
                var startTs = Timestamp.FromDateTime(startDt.ToUniversalTime());
                filtered = filtered.Where(t => t.LastUpdateTime != null && t.LastUpdateTime >= startTs);
            }

            if (!string.IsNullOrEmpty(endTime) && DateTime.TryParse(endTime, out var endDt))
            {
                var endTs = Timestamp.FromDateTime(endDt.ToUniversalTime());
                filtered = filtered.Where(t => t.LastUpdateTime != null && t.LastUpdateTime <= endTs);
            }
        }

        var tasks = filtered.ToList();
        var jsonTasks = tasks.Select(t => JsonDocument.Parse(ProtobufJsonConverter.ToJson(t)).RootElement).ToList();

        return Ok(new { tasks = jsonTasks });
    }

    /// <summary>
    /// POST /api/v1/tasks/stream - Stream task events (SSE)
    /// </summary>
    [HttpPost("api/v1/tasks/stream")]
    public async System.Threading.Tasks.Task StreamTasks()
    {
        var body = await ReadBodyAsync();

        int heartbeatIntervalMs = 30000;
        bool excludePreexisting = false;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("heartbeatIntervalMs", out var hb))
                    heartbeatIntervalMs = hb.GetInt32();

                if (root.TryGetProperty("excludePreexistingTasks", out var ep))
                    excludePreexisting = ep.GetBoolean();
            }
            catch { /* use defaults */ }
        }

        if (heartbeatIntervalMs <= 0) heartbeatIntervalMs = 30000;

        SseHelper.SetSseHeaders(Response);
        var ct = HttpContext.RequestAborted;

        _logger.LogInformation("REST StreamTasks started");

        // Send preexisting non-terminal tasks
        if (!excludePreexisting)
        {
            var existing = _store.GetAllTasks()
                .Where(t => t.Status.Status is not (Status.DoneOk or Status.DoneNotOk));

            foreach (var task in existing)
            {
                var evt = new TaskEvent
                {
                    EventType = EventType.Preexisting,
                    Time = Timestamp.FromDateTime(DateTime.UtcNow),
                    Task = task,
                    TaskView = TaskView.Manager,
                };
                await SseHelper.WriteProtobufEventAsync(Response, "PREEXISTING", evt, ct);
            }
        }

        // Subscribe and stream live updates
        var subscription = _store.Subscribe();
        try
        {
            var heartbeatTask = SendSseHeartbeats(Response, heartbeatIntervalMs, ct);
            var streamTask = StreamTaskEvents(subscription, Response, ct);

            await System.Threading.Tasks.Task.WhenAny(heartbeatTask, streamTask);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _store.Unsubscribe(subscription);
        }
    }

    /// <summary>
    /// POST /api/v1/agent/listen - Listen as agent (long-poll)
    /// </summary>
    [HttpPost("api/v1/agent/listen")]
    public async Task<IActionResult> ListenAsAgent()
    {
        var body = await ReadBodyAsync();

        List<string>? entityIds = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("agentSelector", out var selector) &&
                    selector.TryGetProperty("entityIds", out var ids) &&
                    ids.ValueKind == JsonValueKind.Array)
                {
                    entityIds = ids.EnumerateArray()
                        .Select(e => e.GetString()!)
                        .Where(s => s != null)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid JSON: {ex.Message}" });
            }
        }

        var subscription = _store.Subscribe();
        try
        {
            var ct = HttpContext.RequestAborted;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await foreach (var taskEvent in subscription.Reader.ReadAllAsync(linkedCts.Token))
            {
                var task = taskEvent.Task;
                if (task == null) continue;

                // Filter by entity IDs if specified
                if (entityIds != null && entityIds.Count > 0)
                {
                    var assigneeEntityId = GetAssigneeEntityId(task);
                    if (assigneeEntityId == null || !entityIds.Contains(assigneeEntityId))
                        continue;
                }

                object? agentRequest = null;

                if (taskEvent.EventType == EventType.Created)
                {
                    agentRequest = new
                    {
                        executeRequest = JsonDocument.Parse(
                            ProtobufJsonConverter.ToJson(new ExecuteRequest { Task = task })).RootElement,
                    };
                }
                else if (task.Status?.Status == Status.CancelRequested)
                {
                    agentRequest = new
                    {
                        cancelRequest = new
                        {
                            taskId = task.Version.TaskId,
                            assignee = task.Relations?.Assignee != null
                                ? JsonDocument.Parse(ProtobufJsonConverter.ToJson(task.Relations.Assignee)).RootElement
                                : (JsonElement?)null,
                            author = task.LastUpdatedBy != null
                                ? JsonDocument.Parse(ProtobufJsonConverter.ToJson(task.LastUpdatedBy)).RootElement
                                : (JsonElement?)null,
                        },
                    };
                }
                else if (task.Status?.Status == Status.CompleteRequested)
                {
                    agentRequest = new
                    {
                        completeRequest = new { taskId = task.Version.TaskId },
                    };
                }

                if (agentRequest != null)
                {
                    return Ok(agentRequest);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _store.Unsubscribe(subscription);
        }

        // Timeout with no events
        return Ok(new { });
    }

    /// <summary>
    /// POST /api/v1/agent/stream - Stream as agent (SSE)
    /// </summary>
    [HttpPost("api/v1/agent/stream")]
    public async System.Threading.Tasks.Task StreamAsAgent()
    {
        var body = await ReadBodyAsync();

        List<string>? entityIds = null;
        int heartbeatIntervalMs = 30000;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("agentSelector", out var selector) &&
                    selector.TryGetProperty("entityIds", out var ids) &&
                    ids.ValueKind == JsonValueKind.Array)
                {
                    entityIds = ids.EnumerateArray()
                        .Select(e => e.GetString()!)
                        .Where(s => s != null)
                        .ToList();
                }

                if (root.TryGetProperty("heartbeatIntervalMs", out var hb))
                    heartbeatIntervalMs = hb.GetInt32();
            }
            catch { /* use defaults */ }
        }

        if (heartbeatIntervalMs <= 0) heartbeatIntervalMs = 30000;

        SseHelper.SetSseHeaders(Response);
        var ct = HttpContext.RequestAborted;

        _logger.LogInformation("REST StreamAsAgent started");

        var subscription = _store.Subscribe();
        try
        {
            var heartbeatTask = SendSseHeartbeats(Response, heartbeatIntervalMs, ct);
            var streamTask = StreamAgentEvents(subscription, entityIds, Response, ct);

            await System.Threading.Tasks.Task.WhenAny(heartbeatTask, streamTask);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _store.Unsubscribe(subscription);
        }
    }

    private static async System.Threading.Tasks.Task StreamTaskEvents(
        System.Threading.Channels.Channel<TaskEvent> subscription,
        HttpResponse response,
        CancellationToken ct)
    {
        await foreach (var taskEvent in subscription.Reader.ReadAllAsync(ct))
        {
            var eventName = taskEvent.EventType switch
            {
                EventType.Created => "CREATE",
                EventType.Update => "UPDATE",
                _ => "UPDATE",
            };
            await SseHelper.WriteProtobufEventAsync(response, eventName, taskEvent, ct);
        }
    }

    private static async System.Threading.Tasks.Task StreamAgentEvents(
        System.Threading.Channels.Channel<TaskEvent> subscription,
        List<string>? entityIds,
        HttpResponse response,
        CancellationToken ct)
    {
        await foreach (var taskEvent in subscription.Reader.ReadAllAsync(ct))
        {
            var task = taskEvent.Task;
            if (task == null) continue;

            // Filter by entity IDs
            if (entityIds != null && entityIds.Count > 0)
            {
                var assigneeEntityId = GetAssigneeEntityId(task);
                if (assigneeEntityId == null || !entityIds.Contains(assigneeEntityId))
                    continue;
            }

            string? eventName = null;
            Google.Protobuf.IMessage? payload = null;

            if (taskEvent.EventType == EventType.Created)
            {
                eventName = "execute";
                payload = new ExecuteRequest { Task = task };
            }
            else if (task.Status?.Status == Status.CancelRequested)
            {
                eventName = "cancel";
                payload = new CancelRequest
                {
                    TaskId = task.Version.TaskId,
                    Assignee = task.Relations?.Assignee,
                    Author = task.LastUpdatedBy,
                };
            }
            else if (task.Status?.Status == Status.CompleteRequested)
            {
                eventName = "complete";
                payload = new CompleteRequest { TaskId = task.Version.TaskId };
            }

            if (eventName != null && payload != null)
            {
                await SseHelper.WriteProtobufEventAsync(response, eventName, payload, ct);
            }
        }
    }

    private static async System.Threading.Tasks.Task SendSseHeartbeats(HttpResponse response, int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await System.Threading.Tasks.Task.Delay(intervalMs, ct);
            await SseHelper.WriteHeartbeatAsync(response, ct);
        }
    }

    private static string? GetAssigneeEntityId(Anduril.Taskmanager.V1.Task task)
    {
        var assignee = task.Relations?.Assignee;
        if (assignee == null) return null;

        return assignee.AgentCase switch
        {
            Principal.AgentOneofCase.System => assignee.System.EntityId,
            Principal.AgentOneofCase.Team => assignee.Team.EntityId,
            _ => null,
        };
    }

    /// <summary>
    /// Converts REST API status strings like "STATUS_SENT" to the protobuf enum name "Sent".
    /// </summary>
    private static string ConvertStatusString(string status)
    {
        // Handle "STATUS_DONE_OK" -> "DoneOk", "STATUS_SENT" -> "Sent", etc.
        var cleaned = status.Replace("STATUS_", "");
        // Convert UPPER_SNAKE_CASE to PascalCase
        var parts = cleaned.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("", parts.Select(p =>
            char.ToUpper(p[0]) + p[1..].ToLower()));
    }

    private async Task<string> ReadBodyAsync()
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync();
    }

    private ContentResult ProtobufJsonRawResult(Google.Protobuf.IMessage message)
    {
        return Content(ProtobufJsonConverter.ToJson(message), "application/json");
    }
}
