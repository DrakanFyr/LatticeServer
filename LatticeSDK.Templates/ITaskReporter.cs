namespace LatticeSDK.Templates;

/// <summary>
/// Injected by the server — lets the behavior report task status updates
/// without depending on the protobuf or gRPC packages.
/// </summary>
public interface ITaskReporter
{
    /// <summary>
    /// Reports a status update for the given task ID.
    /// The server translates <paramref name="update"/> into a protobuf TaskStatus and calls TaskStore.UpsertTask.
    /// </summary>
    void ReportStatus(string taskId, TaskStatusUpdate update);
}
