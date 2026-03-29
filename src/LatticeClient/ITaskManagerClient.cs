using Anduril.Taskmanager.V1;

namespace LatticeClient;

/// <summary>
/// Interface for Task Manager operations, supported by both gRPC and REST transports.
/// </summary>
public interface ITaskManagerClient : IDisposable
{
    /// <summary>
    /// Create a new task.
    /// </summary>
    Task<Anduril.Taskmanager.V1.Task> CreateTaskAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a task by its ID.
    /// </summary>
    Task<Anduril.Taskmanager.V1.Task> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query tasks with filtering criteria.
    /// </summary>
    Task<QueryTasksResponse> QueryTasksAsync(
        QueryTasksRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the status of a task.
    /// </summary>
    Task<Anduril.Taskmanager.V1.Task> UpdateStatusAsync(
        StatusUpdate statusUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a task.
    /// </summary>
    Task<Anduril.Taskmanager.V1.Task> CancelTaskAsync(
        string taskId,
        Principal? author = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Listen for tasks as an agent. Returns a stream of agent requests
    /// (execute, cancel, complete) for tasks matching the selector criteria.
    /// </summary>
    IAsyncEnumerable<ListenAsAgentResponse> ListenAsAgentAsync(
        ListenAsAgentRequest? request = null,
        CancellationToken cancellationToken = default);
}
