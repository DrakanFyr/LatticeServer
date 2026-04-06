using LatticeSDK.Templates;

namespace LatticePluginTemplate.Android;

/// <summary>
/// Minimal template behavior. Replace this with your own simulation logic.
///
/// This class is loaded by LatticeServer at runtime from the APK's assets/behavior.dll.
/// LatticeServer discovers it by scanning for types implementing ITaskableEntity.
///
/// If your plugin does not need a behavior (entity only, no task handling or simulation),
/// delete this file and remove the behavior.dll AndroidAsset from the .csproj.
/// </summary>
public class PluginBehavior : ITaskableEntity
{
    private IEntityPublisher _publisher = null!;
    private ITaskReporter _reporter = null!;

    private double _lat;
    private double _lon;
    private double _altM;

    public void OnSpawn(string entityId, IEntityPublisher publisher, ITaskReporter reporter, IEntityQuerier querier, TemplateConfig config)
    {
        _publisher = publisher;
        _reporter = reporter;

        _lat  = config.DefaultLocation?.LatitudeDegrees ?? 0;
        _lon  = config.DefaultLocation?.LongitudeDegrees ?? 0;
        _altM = config.DefaultLocation?.AltitudeHaeMeters ?? 0;
    }

    public void OnUpdate(float deltaTimeSecs)
    {
        // Publish the entity's current position each tick.
        // Add your simulation logic here.
        _publisher.PublishEntityUpdate(new EntityUpdate(
            Latitude: _lat,
            Longitude: _lon,
            AltitudeHaeMeters: _altM,
            Disposition: null,
            ExtraFieldsJson: null));
    }

    public void OnTaskReceived(TaskPayload task)
    {
        _reporter.ReportStatus(task.TaskId, new TaskStatusUpdate(TaskStatusCode.WillComply));
        _reporter.ReportStatus(task.TaskId, new TaskStatusUpdate(TaskStatusCode.Executing));
        // Add task handling logic here.
    }

    public void OnTaskCancelRequest(string taskId)
    {
        _reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.Cancelled));
    }

    public void OnTaskCompleteRequest(string taskId)
    {
        _reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.DoneOk));
    }

    public void OnDespawn() { }
}
