using Anduril.Taskmanager.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LatticeServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Status = Anduril.Taskmanager.V1.Status;
using Task = System.Threading.Tasks.Task;
using TaskStatus = Anduril.Taskmanager.V1.TaskStatus;

namespace LatticeServer.Tests;

public class TaskManagerServiceTests
{
    private readonly TaskStore _store = new();
    private readonly TaskManagerService _service;
    private readonly ServerCallContext _context = TestHelper.CreateCallContext();

    public TaskManagerServiceTests()
    {
        _service = new TaskManagerService(NullLogger<TaskManagerService>.Instance, _store);
    }

    private static CreateTaskRequest CreateValidTaskRequest(string? taskId = null) => new()
    {
        TaskId = taskId ?? "test-task-12345",
        Description = "Test task",
        Author = new Principal
        {
            User = new User { UserId = "test-user" },
        },
        Relations = new Relations
        {
            Assignee = new Principal
            {
                System = new Anduril.Taskmanager.V1.System
                {
                    ServiceName = "TestService",
                    EntityId = "agent-entity-1",
                },
            },
        },
    };

    private async Task<Anduril.Taskmanager.V1.Task> CreateTask(string? taskId = null)
    {
        var response = await _service.CreateTask(CreateValidTaskRequest(taskId), _context);
        return response.Task;
    }

    // ───────────────────────────────────────────────
    // CreateTask
    // ───────────────────────────────────────────────

    [Fact]
    public async Task CreateTask_ValidRequest_ReturnsTaskWithStatusSent()
    {
        // Proto: "sets the initial task state to STATUS_SENT"
        var response = await _service.CreateTask(CreateValidTaskRequest(), _context);

        Assert.NotNull(response.Task);
        Assert.Equal(Status.Sent, response.Task.Status.Status);
    }

    [Fact]
    public async Task CreateTask_ValidRequest_SetsVersionFields()
    {
        // Proto: "Unset (0) initially, starts at 1 on creation"
        var response = await _service.CreateTask(CreateValidTaskRequest(), _context);

        Assert.Equal(1u, response.Task.Version.DefinitionVersion);
        Assert.Equal(1u, response.Task.Version.StatusVersion);
        Assert.Equal("test-task-12345", response.Task.Version.TaskId);
    }

    [Fact]
    public async Task CreateTask_NoTaskId_GeneratesGuid()
    {
        // Proto: "assigns a unique ID, if one is not provided"
        var request = CreateValidTaskRequest();
        request.TaskId = "";

        var response = await _service.CreateTask(request, _context);

        Assert.NotEmpty(response.Task.Version.TaskId);
        Assert.True(Guid.TryParse(response.Task.Version.TaskId, out _));
    }

    [Fact]
    public async Task CreateTask_InvalidTaskIdFormat_ThrowsInvalidArgument()
    {
        // Proto: "Will reject if supplied Task ID does not match [A-Za-z0-9_-.]{5,36}"
        var request = CreateValidTaskRequest();
        request.TaskId = "ab"; // Too short (< 5 chars)

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.CreateTask(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTask_TaskIdTooLong_ThrowsInvalidArgument()
    {
        // Proto: "[A-Za-z0-9_-.]{5,36}" — more than 36 chars
        var request = CreateValidTaskRequest();
        request.TaskId = new string('a', 37);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.CreateTask(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTask_TaskIdWithInvalidChars_ThrowsInvalidArgument()
    {
        var request = CreateValidTaskRequest();
        request.TaskId = "test task!@#"; // spaces and special chars

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.CreateTask(request, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTask_ValidTaskIdFormats_Succeed()
    {
        // Proto: "[A-Za-z0-9_-.]{5,36}"
        var validIds = new[] { "abcde", "my-task-1", "Task_123.v2", new string('x', 36) };

        foreach (var id in validIds)
        {
            var store = new TaskStore();
            var service = new TaskManagerService(NullLogger<TaskManagerService>.Instance, store);
            var request = CreateValidTaskRequest(id);

            var response = await service.CreateTask(request, _context);
            Assert.Equal(id, response.Task.Version.TaskId);
        }
    }

    [Fact]
    public async Task CreateTask_DuplicateTaskId_ThrowsAlreadyExists()
    {
        // Proto implies task IDs are unique
        await _service.CreateTask(CreateValidTaskRequest("dup-task-123"), _context);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.CreateTask(CreateValidTaskRequest("dup-task-123"), _context));
        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }

    [Fact]
    public async Task CreateTask_SetsAuthorAndTimestamps()
    {
        var response = await _service.CreateTask(CreateValidTaskRequest(), _context);

        Assert.NotNull(response.Task.CreatedBy);
        Assert.Equal("test-user", response.Task.CreatedBy.User.UserId);
        Assert.NotNull(response.Task.CreateTime);
        Assert.NotNull(response.Task.LastUpdateTime);
    }

    [Fact]
    public async Task CreateTask_PreservesRelations()
    {
        // Proto: "Any relationships associated with this Task, such as a parent Task or an assignee"
        var response = await _service.CreateTask(CreateValidTaskRequest(), _context);

        Assert.NotNull(response.Task.Relations);
        Assert.Equal("agent-entity-1", response.Task.Relations.Assignee.System.EntityId);
    }

    // ───────────────────────────────────────────────
    // GetTask
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetTask_ExistingTask_ReturnsTask()
    {
        // Proto: "Retrieves a specific Task by its ID"
        await CreateTask("get-task-12345");

        var response = await _service.GetTask(
            new GetTaskRequest { TaskId = "get-task-12345" }, _context);

        Assert.NotNull(response.Task);
        Assert.Equal("get-task-12345", response.Task.Version.TaskId);
    }

    [Fact]
    public async Task GetTask_NonExistentTask_ThrowsNotFound()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.GetTask(new GetTaskRequest { TaskId = "no-such-task" }, _context));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetTask_EmptyTaskId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.GetTask(new GetTaskRequest { TaskId = "" }, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ───────────────────────────────────────────────
    // UpdateStatus
    // ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_ValidUpdate_IncrementsStatusVersion()
    {
        // Proto: "Each status update increments the task's status_version."
        var task = await CreateTask("status-task-1");
        Assert.Equal(1u, task.Version.StatusVersion);

        var response = await _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "status-task-1", StatusVersion = 1 },
                Status = new TaskStatus { Status = Status.Ack },
                Author = new Principal { User = new User { UserId = "agent" } },
            },
        }, _context);

        Assert.Equal(1u, response.Task.Version.StatusVersion);
        Assert.Equal(Status.Ack, response.Task.Status.Status);
    }

    [Fact]
    public async Task UpdateStatus_VersionMismatch_ThrowsAborted()
    {
        // Proto: "clients must provide the current version to ensure consistency.
        //         The system rejects updates with mismatched versions to prevent race conditions."
        await CreateTask("version-task");

        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "version-task", StatusVersion = 0 },
                Status = new TaskStatus { Status = Status.Ack },
            },
        }, _context));

        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
        Assert.Contains("version mismatch", ex.Status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatus_TerminalDoneOk_ThrowsFailedPrecondition()
    {
        // Proto: "Terminal states (STATUS_DONE_OK and STATUS_DONE_NOT_OK) are permanent;
        //         once a task reaches these states, no further updates are allowed."
        var task = await CreateTask("terminal-ok");

        // Move to DONE_OK
        await _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "terminal-ok", StatusVersion = task.Version.StatusVersion },
                Status = new TaskStatus { Status = Status.DoneOk },
            },
        }, _context);

        // Try to update again
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "terminal-ok", StatusVersion = 2 },
                Status = new TaskStatus { Status = Status.Executing },
            },
        }, _context));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_TerminalDoneNotOk_ThrowsFailedPrecondition()
    {
        var task = await CreateTask("terminal-nok");

        await _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "terminal-nok", StatusVersion = task.Version.StatusVersion },
                Status = new TaskStatus { Status = Status.DoneNotOk },
            },
        }, _context);

        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "terminal-nok", StatusVersion = 2 },
                Status = new TaskStatus { Status = Status.Executing },
            },
        }, _context));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_MissingStatusUpdate_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.UpdateStatus(new UpdateStatusRequest(), _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_MissingVersion_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Status = new TaskStatus { Status = Status.Ack },
            },
        }, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_NonExistentTask_ThrowsNotFound()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "ghost-task", StatusVersion = 1 },
                Status = new TaskStatus { Status = Status.Ack },
            },
        }, _context));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_FullLifecycleSequence_Succeeds()
    {
        // Proto: Status lifecycle: SENT -> MACHINE_RECEIPT -> ACK -> WILCO -> EXECUTING -> DONE_OK
        var task = await CreateTask("lifecycle-task");
        Assert.Equal(Status.Sent, task.Status.Status);

        var statusSequence = new[]
        {
            Status.MachineReceipt,
            Status.Ack,
            Status.Wilco,
            Status.Executing,
            Status.DoneOk,
        };

        uint currentVersion = task.Version.StatusVersion;
        foreach (var status in statusSequence)
        {
            var response = await _service.UpdateStatus(new UpdateStatusRequest
            {
                StatusUpdate = new StatusUpdate
                {
                    Version = new TaskVersion { TaskId = "lifecycle-task", StatusVersion = currentVersion },
                    Status = new TaskStatus { Status = status },
                    Author = new Principal { User = new User { UserId = "agent" } },
                },
            }, _context);

            Assert.Equal(status, response.Task.Status.Status);
            Assert.Equal(currentVersion, response.Task.Version.StatusVersion);
            currentVersion++;
        }
    }

    // ───────────────────────────────────────────────
    // CancelTask
    // ───────────────────────────────────────────────

    [Fact]
    public async Task CancelTask_NotSentYet_ImmediatelyCancels()
    {
        // Proto: "If the task has not been sent to an agent, it cancels the task immediately and transitions
        //         to a terminal state (STATUS_DONE_NOT_OK with ERROR_CODE_CANCELLED)."
        // Created tasks with STATUS_CREATED or STATUS_SCHEDULED_IN_MANAGER are "not sent"
        // Our CreateTask sets STATUS_SENT, so we need to manipulate the store directly
        var task = new Anduril.Taskmanager.V1.Task
        {
            Version = new TaskVersion { TaskId = "cancel-presend", DefinitionVersion = 1, StatusVersion = 1 },
            Status = new TaskStatus { Status = Status.Created },
        };
        _store.UpsertTask(task, EventType.Created);

        var response = await _service.CancelTask(new CancelTaskRequest
        {
            TaskId = "cancel-presend",
            Author = new Principal { User = new User { UserId = "operator" } },
        }, _context);

        Assert.Equal(Status.DoneNotOk, response.Task.Status.Status);
        Assert.Equal(ErrorCode.Cancelled, response.Task.Status.TaskError.Code);
    }

    [Fact]
    public async Task CancelTask_AlreadySent_SetsPendingCancel()
    {
        // Proto: "If the task has been sent to an agent, the cancellation request is routed to the agent
        //         with a delivery status of DELIVERY_STATUS_PENDING_CANCEL."
        await CreateTask("cancel-sent-1");

        var response = await _service.CancelTask(new CancelTaskRequest
        {
            TaskId = "cancel-sent-1",
            Author = new Principal { User = new User { UserId = "operator" } },
        }, _context);

        Assert.Equal(Status.CancelRequested, response.Task.Status.Status);
        Assert.Equal(DeliveryStatus.PendingCancel, response.Task.DeliveryState.Status);
    }

    [Fact]
    public async Task CancelTask_TerminalState_ThrowsFailedPrecondition()
    {
        var task = await CreateTask("cancel-done-1");

        // Move to terminal state
        await _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "cancel-done-1", StatusVersion = task.Version.StatusVersion },
                Status = new TaskStatus { Status = Status.DoneOk },
            },
        }, _context);

        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.CancelTask(
            new CancelTaskRequest { TaskId = "cancel-done-1" }, _context));
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task CancelTask_NonExistentTask_ThrowsNotFound()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.CancelTask(new CancelTaskRequest { TaskId = "no-such-task" }, _context));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task CancelTask_EmptyTaskId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _service.CancelTask(new CancelTaskRequest { TaskId = "" }, _context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task CancelTask_IncrementsStatusVersion()
    {
        var task = await CreateTask("cancel-ver-1");
        var originalVersion = task.Version.StatusVersion;

        var response = await _service.CancelTask(new CancelTaskRequest
        {
            TaskId = "cancel-ver-1",
            Author = new Principal { User = new User { UserId = "operator" } },
        }, _context);

        Assert.Equal(originalVersion + 1, response.Task.Version.StatusVersion);
    }

    // ───────────────────────────────────────────────
    // QueryTasks
    // ───────────────────────────────────────────────

    [Fact]
    public async Task QueryTasks_NoFilters_ReturnsAllTasks()
    {
        // Proto: "By default, with no filters applied, this returns the latest version of all tasks."
        await CreateTask("query-task-a");
        await CreateTask("query-task-b");

        var response = await _service.QueryTasks(new QueryTasksRequest(), _context);

        Assert.Equal(2, response.Tasks.Count);
    }

    [Fact]
    public async Task QueryTasks_StatusFilterInclusive_ReturnsMatching()
    {
        // Proto: "show only executing tasks" (FILTER_TYPE_INCLUSIVE)
        await CreateTask("q-inclusive-a");
        var taskB = await CreateTask("q-inclusive-b");

        // Move task B to Executing
        await _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "q-inclusive-b", StatusVersion = taskB.Version.StatusVersion },
                Status = new TaskStatus { Status = Status.Executing },
            },
        }, _context);

        var response = await _service.QueryTasks(new QueryTasksRequest
        {
            StatusFilter = new QueryTasksRequest.Types.StatusFilter
            {
                FilterType = QueryTasksRequest.Types.FilterType.Inclusive,
                Status = { Status.Executing },
            },
        }, _context);

        Assert.Single(response.Tasks);
        Assert.Equal("q-inclusive-b", response.Tasks[0].Version.TaskId);
    }

    [Fact]
    public async Task QueryTasks_StatusFilterExclusive_ExcludesMatching()
    {
        // Proto: "show all tasks except completed ones" (FILTER_TYPE_EXCLUSIVE)
        await CreateTask("q-exclusive-a");
        var taskB = await CreateTask("q-exclusive-b");

        // Move task B to DoneOk
        await _service.UpdateStatus(new UpdateStatusRequest
        {
            StatusUpdate = new StatusUpdate
            {
                Version = new TaskVersion { TaskId = "q-exclusive-b", StatusVersion = taskB.Version.StatusVersion },
                Status = new TaskStatus { Status = Status.DoneOk },
            },
        }, _context);

        var response = await _service.QueryTasks(new QueryTasksRequest
        {
            StatusFilter = new QueryTasksRequest.Types.StatusFilter
            {
                FilterType = QueryTasksRequest.Types.FilterType.Exclusive,
                Status = { Status.DoneOk },
            },
        }, _context);

        Assert.Single(response.Tasks);
        Assert.Equal("q-exclusive-a", response.Tasks[0].Version.TaskId);
    }

    [Fact]
    public async Task QueryTasks_ParentTaskId_ReturnsChildren()
    {
        // Proto: "If present matches Tasks with this parent Task ID."
        var parentRequest = CreateValidTaskRequest("parent-task1");
        await _service.CreateTask(parentRequest, _context);

        var childRequest = CreateValidTaskRequest("child-task-1");
        childRequest.Relations = new Relations
        {
            ParentTaskId = "parent-task1",
            Assignee = new Principal { System = new Anduril.Taskmanager.V1.System { EntityId = "e1" } },
        };
        await _service.CreateTask(childRequest, _context);

        var response = await _service.QueryTasks(new QueryTasksRequest
        {
            ParentTaskId = "parent-task1",
        }, _context);

        Assert.Single(response.Tasks);
        Assert.Equal("child-task-1", response.Tasks[0].Version.TaskId);
    }

    [Fact]
    public async Task QueryTasks_TimeRange_FiltersCorrectly()
    {
        // Proto: "If provided, only provides Tasks updated within the time range."
        await CreateTask("time-task-1");

        var beforeCreation = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-1));
        var afterCreation = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(1));

        var response = await _service.QueryTasks(new QueryTasksRequest
        {
            UpdateTimeRange = new QueryTasksRequest.Types.TimeRange
            {
                UpdateStartTime = beforeCreation,
                UpdateEndTime = afterCreation,
            },
        }, _context);

        Assert.Single(response.Tasks);

        // Query with a time range in the past should return nothing
        var pastRange = await _service.QueryTasks(new QueryTasksRequest
        {
            UpdateTimeRange = new QueryTasksRequest.Types.TimeRange
            {
                UpdateStartTime = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                UpdateEndTime = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            },
        }, _context);

        Assert.Empty(pastRange.Tasks);
    }

    // ───────────────────────────────────────────────
    // TaskStore events
    // ───────────────────────────────────────────────

    [Fact]
    public void Store_UpsertTask_NotifiesSubscribers()
    {
        var subscription = _store.Subscribe();

        var task = new Anduril.Taskmanager.V1.Task
        {
            Version = new TaskVersion { TaskId = "sub-task-1", DefinitionVersion = 1, StatusVersion = 1 },
            Status = new TaskStatus { Status = Status.Sent },
        };
        _store.UpsertTask(task, EventType.Created);

        Assert.True(subscription.Reader.TryRead(out var evt));
        Assert.Equal(EventType.Created, evt!.EventType);
        Assert.Equal("sub-task-1", evt.Task.Version.TaskId);

        _store.Unsubscribe(subscription);
    }
}
