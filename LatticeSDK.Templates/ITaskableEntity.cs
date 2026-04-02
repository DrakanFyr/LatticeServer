namespace LatticeSDK.Templates;

/// <summary>
/// The interface every taskable behavior DLL must implement exactly once.
/// The server discovers the implementation via reflection on startup and on hot-reload.
/// </summary>
public interface ITaskableEntity
{
    /// <summary>
    /// Called once when the entity is first spawned.
    /// <paramref name="entityId"/> is the resolved GUID for this instance.
    /// </summary>
    void OnSpawn(string entityId, IEntityPublisher publisher, ITaskReporter reporter, IEntityQuerier querier, TemplateConfig config);

    /// <summary>
    /// Called on each server tick according to the template's configured interval.
    /// <paramref name="deltaTimeSecs"/> is wall-clock seconds since the last call.
    /// </summary>
    void OnUpdate(float deltaTimeSecs);

    /// <summary>Called when a new task is delivered to this entity (STATUS_SENT).</summary>
    void OnTaskReceived(TaskPayload task);

    /// <summary>Called when the task manager requests a cancel (STATUS_CANCEL_REQUESTED).</summary>
    void OnTaskCancelRequest(string taskId);

    /// <summary>Called when the task manager requests an explicit complete (STATUS_COMPLETE_REQUESTED).</summary>
    void OnTaskCompleteRequest(string taskId);

    /// <summary>Called just before the entity is despawned.</summary>
    void OnDespawn();
}
