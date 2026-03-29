using System.Text.RegularExpressions;
using Grpc.Core;
using Anduril.Taskmanager.V1;
using Google.Protobuf.WellKnownTypes;
using GrpcStatus = Grpc.Core.Status;
using TaskStatus = Anduril.Taskmanager.V1.TaskStatus;
using Status = Anduril.Taskmanager.V1.Status;
using Task = System.Threading.Tasks.Task;

namespace LatticeServer.Services;

public class TaskManagerService : TaskManagerAPI.TaskManagerAPIBase
{
    private static readonly Regex TaskIdRegex = new(@"^[A-Za-z0-9_\-.]{5,36}$", RegexOptions.Compiled);

    private readonly ILogger<TaskManagerService> _logger;
    private readonly TaskStore _store;

    public TaskManagerService(ILogger<TaskManagerService> logger, TaskStore store)
    {
        _logger = logger;
        _store = store;
    }

    public override Task<CreateTaskResponse> CreateTask(CreateTaskRequest request, ServerCallContext context)
    {
        // Generate or validate task ID
        var taskId = request.TaskId;
        if (string.IsNullOrEmpty(taskId))
        {
            taskId = Guid.NewGuid().ToString();
        }
        else if (!TaskIdRegex.IsMatch(taskId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument,
                "task_id must match [A-Za-z0-9_-.]{5,36}"));
        }

        // Reject if task already exists
        if (_store.GetTask(taskId) != null)
        {
            throw new RpcException(new GrpcStatus(StatusCode.AlreadyExists,
                $"Task {taskId} already exists"));
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
            Specification = request.Specification,
            CreatedBy = request.Author,
            LastUpdatedBy = request.Author,
            LastUpdateTime = now,
            CreateTime = now,
            Status = new TaskStatus
            {
                Status = Status.Sent,
            },
            Relations = request.Relations,
            Description = request.Description,
            IsExecutedElsewhere = request.IsExecutedElsewhere,
        };
        task.InitialEntities.Add(request.InitialEntities);

        if (request.RetryStrategy != null)
        {
            task.RetryStrategy = request.RetryStrategy;
        }

        _logger.LogInformation("CreateTask: created task {TaskId}", taskId);
        _store.UpsertTask(task, EventType.Created);

        return Task.FromResult(new CreateTaskResponse { Task = task });
    }

    public override Task<GetTaskResponse> GetTask(GetTaskRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.TaskId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "task_id is required"));
        }

        var task = _store.GetTask(request.TaskId)
            ?? throw new RpcException(new GrpcStatus(StatusCode.NotFound, $"Task {request.TaskId} not found"));

        _logger.LogInformation("GetTask: returning task {TaskId}", request.TaskId);
        return Task.FromResult(new GetTaskResponse { Task = task });
    }

    public override Task<QueryTasksResponse> QueryTasks(QueryTasksRequest request, ServerCallContext context)
    {
        _logger.LogInformation("QueryTasks called");

        var allTasks = _store.GetAllTasks();
        IEnumerable<Anduril.Taskmanager.V1.Task> filtered = allTasks;

        // Filter by parent task ID (mutually exclusive with other filters)
        if (!string.IsNullOrEmpty(request.ParentTaskId))
        {
            filtered = filtered.Where(t =>
                t.Relations != null && t.Relations.ParentTaskId == request.ParentTaskId);
        }
        else
        {
            // Status filter
            if (request.StatusFilter is { Status.Count: > 0 })
            {
                var statuses = new HashSet<Status>(request.StatusFilter.Status.Cast<Status>());
                filtered = request.StatusFilter.FilterType == QueryTasksRequest.Types.FilterType.Exclusive
                    ? filtered.Where(t => !statuses.Contains(t.Status.Status))
                    : filtered.Where(t => statuses.Contains(t.Status.Status));
            }

            // Time range filter
            if (request.UpdateTimeRange != null)
            {
                if (request.UpdateTimeRange.UpdateStartTime != null)
                {
                    var start = request.UpdateTimeRange.UpdateStartTime;
                    filtered = filtered.Where(t => t.LastUpdateTime != null && t.LastUpdateTime >= start);
                }
                if (request.UpdateTimeRange.UpdateEndTime != null)
                {
                    var end = request.UpdateTimeRange.UpdateEndTime;
                    filtered = filtered.Where(t => t.LastUpdateTime != null && t.LastUpdateTime <= end);
                }
            }
        }

        var response = new QueryTasksResponse();
        response.Tasks.Add(filtered);
        return Task.FromResult(response);
    }

    public override Task<UpdateStatusResponse> UpdateStatus(UpdateStatusRequest request, ServerCallContext context)
    {
        var statusUpdate = request.StatusUpdate
            ?? throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "status_update is required"));

        if (statusUpdate.Version == null)
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "status_update.version is required"));
        }

        var taskId = statusUpdate.Version.TaskId;
        var task = _store.GetTask(taskId)
            ?? throw new RpcException(new GrpcStatus(StatusCode.NotFound, $"Task {taskId} not found"));

        // Reject updates to terminal tasks
        if (task.Status.Status is Status.DoneOk or Status.DoneNotOk)
        {
            throw new RpcException(new GrpcStatus(StatusCode.FailedPrecondition,
                $"Task {taskId} is in terminal state {task.Status.Status}"));
        }

        // Optimistic concurrency: reject stale (lower) status versions
        if (statusUpdate.Version.StatusVersion < task.Version.StatusVersion)
        {
            throw new RpcException(new GrpcStatus(StatusCode.Aborted,
                $"Status version mismatch: expected >= {task.Version.StatusVersion}, got {statusUpdate.Version.StatusVersion}"));
        }

        // Clone and apply update
        var updated = task.Clone();
        updated.Version.StatusVersion = statusUpdate.Version.StatusVersion;
        updated.Status = statusUpdate.Status;
        updated.LastUpdatedBy = statusUpdate.Author;
        updated.LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow);

        if (statusUpdate.ScheduledTime != null)
        {
            updated.ScheduledTime = statusUpdate.ScheduledTime;
        }

        _logger.LogInformation("UpdateStatus: task {TaskId} -> {Status}", taskId, statusUpdate.Status.Status);
        _store.UpsertTask(updated, EventType.Update);

        return Task.FromResult(new UpdateStatusResponse { Task = updated });
    }

    public override Task<CancelTaskResponse> CancelTask(CancelTaskRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.TaskId))
        {
            throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "task_id is required"));
        }

        var task = _store.GetTask(request.TaskId)
            ?? throw new RpcException(new GrpcStatus(StatusCode.NotFound, $"Task {request.TaskId} not found"));

        // Already terminal
        if (task.Status.Status is Status.DoneOk or Status.DoneNotOk)
        {
            throw new RpcException(new GrpcStatus(StatusCode.FailedPrecondition,
                $"Task {request.TaskId} is already in terminal state {task.Status.Status}"));
        }

        var updated = task.Clone();
        updated.LastUpdatedBy = request.Author;
        updated.LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow);
        updated.Version.StatusVersion++;

        // If the task hasn't been sent to an agent yet, cancel immediately
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
            // Task has been sent to an agent — mark as pending cancel
            updated.DeliveryState = new DeliveryState
            {
                Status = DeliveryStatus.PendingCancel,
            };
            updated.Status = new TaskStatus
            {
                Status = Status.CancelRequested,
            };
        }

        _logger.LogInformation("CancelTask: task {TaskId} -> {Status}", request.TaskId, updated.Status.Status);
        _store.UpsertTask(updated, EventType.Update);

        return Task.FromResult(new CancelTaskResponse { Task = updated });
    }

    public override async Task ListenAsAgent(
        ListenAsAgentRequest request,
        IServerStreamWriter<ListenAsAgentResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("ListenAsAgent stream started");

        var subscription = _store.Subscribe();
        try
        {
            await foreach (var taskEvent in subscription.Reader.ReadAllAsync(context.CancellationToken))
            {
                var task = taskEvent.Task;
                if (task == null) continue;

                // Filter by entity IDs if specified
                if (request.AgentSelectorCase == ListenAsAgentRequest.AgentSelectorOneofCase.EntityIds)
                {
                    var assigneeEntityId = GetAssigneeEntityId(task);
                    if (assigneeEntityId == null ||
                        !request.EntityIds.EntityIds_.Contains(assigneeEntityId))
                    {
                        continue;
                    }
                }

                ListenAsAgentResponse? response = null;

                if (taskEvent.EventType == EventType.Created)
                {
                    response = new ListenAsAgentResponse
                    {
                        ExecuteRequest = new ExecuteRequest { Task = task },
                    };
                }
                else if (task.Status?.Status == Status.CancelRequested)
                {
                    response = new ListenAsAgentResponse
                    {
                        CancelRequest = new CancelRequest
                        {
                            TaskId = task.Version.TaskId,
                            Assignee = task.Relations?.Assignee,
                            Author = task.LastUpdatedBy,
                        },
                    };
                }
                else if (task.Status?.Status == Status.CompleteRequested)
                {
                    response = new ListenAsAgentResponse
                    {
                        CompleteRequest = new CompleteRequest
                        {
                            TaskId = task.Version.TaskId,
                        },
                    };
                }

                if (response != null)
                {
                    await responseStream.WriteAsync(response, context.CancellationToken);
                }
            }
        }
        finally
        {
            _store.Unsubscribe(subscription);
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
}
