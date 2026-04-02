using System.Text.Json;
using LatticeSDK.Templates;

namespace LatticeTemplateSDK;

/// <summary>
/// Reference behavior: a simulated UAV that moves along a fixed heading,
/// and navigates toward a target when given an Investigate task.
/// </summary>
public class SimulatedUavBehavior : ITaskableEntity
{
    private IEntityPublisher _publisher = null!;
    private ITaskReporter _reporter = null!;
    private IEntityQuerier _querier = null!;

    // Current position
    private double _lat;
    private double _lon;
    private double _altM;

    // Motion
    private double _speedMps;
    private double _headingDeg;

    // Navigation state
    private string? _activeTaskId;
    private double? _targetLat;
    private double? _targetLon;
    private string? _targetEntityId;  // non-null when navigating to a live entity
    private bool _navigating;

    public void OnSpawn(string entityId, IEntityPublisher publisher, ITaskReporter reporter, IEntityQuerier querier, TemplateConfig config)
    {
        _publisher = publisher;
        _reporter = reporter;
        _querier = querier;

        _speedMps = config.Custom.TryGetValue("speedMps", out var s) ? s.GetDouble() : 15.0;
        _headingDeg = config.Custom.TryGetValue("headingDegrees", out var h) ? h.GetDouble() : 90.0;

        if (config.DefaultLocation != null)
        {
            _lat = config.DefaultLocation.LatitudeDegrees;
            _lon = config.DefaultLocation.LongitudeDegrees;
            _altM = config.DefaultLocation.AltitudeHaeMeters ?? 150.0;
        }
    }

    public void OnUpdate(float deltaTimeSecs)
    {
        // If tracking an entity, refresh the target position from the live entity store
        if (_navigating && _targetEntityId != null)
        {
            var pos = _querier.TryGetPosition(_targetEntityId);
            if (pos != null)
            {
                _targetLat = pos.LatitudeDegrees;
                _targetLon = pos.LongitudeDegrees;
            }
        }

        if (_navigating && _targetLat.HasValue && _targetLon.HasValue)
        {
            // Navigate toward target
            var dLat = _targetLat.Value - _lat;
            var dLon = _targetLon.Value - _lon;
            var dist = Math.Sqrt(dLat * dLat + dLon * dLon) * 111_320.0; // rough metres

            if (dist < 20.0)
            {
                // Arrived — only complete the task for point objectives; keep tracking live entities
                if (_targetEntityId == null)
                {
                    _navigating = false;
                    if (_activeTaskId != null)
                        _reporter.ReportStatus(_activeTaskId, new TaskStatusUpdate(TaskStatusCode.DoneOk));
                    _activeTaskId = null;
                }
            }
            else
            {
                var bearing = Math.Atan2(dLon, dLat); // radians
                var stepM = _speedMps * deltaTimeSecs;
                _lat += stepM / 111_320.0 * Math.Cos(bearing);
                _lon += stepM / 111_320.0 * Math.Sin(bearing);
            }
        }
        else
        {
            // Dead-reckoning on fixed heading
            var headingRad = _headingDeg * Math.PI / 180.0;
            var stepM = _speedMps * deltaTimeSecs;
            _lat += stepM / 111_320.0 * Math.Cos(headingRad);
            _lon += stepM / (111_320.0 * Math.Cos(_lat * Math.PI / 180.0)) * Math.Sin(headingRad);
        }

        _publisher.PublishEntityUpdate(new EntityUpdate(
            Latitude: _lat,
            Longitude: _lon,
            AltitudeHaeMeters: _altM,
            Disposition: null,
            ExtraFieldsJson: null));
    }

    public void OnTaskReceived(TaskPayload task)
    {
        _activeTaskId = task.TaskId;

        // Try to extract target from spec JSON
        _targetEntityId = null;
        try
        {
            using var doc = JsonDocument.Parse(task.SpecificationJson);
            var root = doc.RootElement;

            if (TryExtractEntityId(root, out var entityId))
            {
                // Entity objective — seed initial position and track live updates each tick
                _targetEntityId = entityId;
                var pos = _querier.TryGetPosition(entityId);
                if (pos != null)
                {
                    _targetLat = pos.LatitudeDegrees;
                    _targetLon = pos.LongitudeDegrees;
                }
                _navigating = true;
            }
            else if (TryExtractLatLon(root, out var lat, out var lon))
            {
                // Point objective — navigate to fixed coordinates
                _targetLat = lat;
                _targetLon = lon;
                _navigating = true;
            }
        }
        catch
        {
            // Spec JSON parse failure — still acknowledge
        }

        _reporter.ReportStatus(task.TaskId, new TaskStatusUpdate(TaskStatusCode.WillComply));
        _reporter.ReportStatus(task.TaskId, new TaskStatusUpdate(TaskStatusCode.Executing));
    }

    public void OnTaskCancelRequest(string taskId)
    {
        if (taskId == _activeTaskId)
        {
            _navigating = false;
            _activeTaskId = null;
            _targetEntityId = null;
        }
        _reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.Cancelled));
    }

    public void OnTaskCompleteRequest(string taskId)
    {
        if (taskId == _activeTaskId)
        {
            _navigating = false;
            _activeTaskId = null;
            _targetEntityId = null;
        }
        _reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.DoneOk));
    }

    public void OnDespawn() { }

    private static bool TryExtractEntityId(JsonElement root, out string entityId)
    {
        entityId = "";

        // Investigate/ISR task spec: objective.entityId
        if (root.TryGetProperty("objective", out var objective) &&
            objective.TryGetProperty("entityId", out var entityIdEl))
        {
            var id = entityIdEl.GetString();
            if (!string.IsNullOrEmpty(id))
            {
                entityId = id;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractLatLon(JsonElement root, out double lat, out double lon)
    {
        lat = 0; lon = 0;

        // Investigate/ISR task spec: objective.point.lla.lat / lon
        if (root.TryGetProperty("objective", out var objective) &&
            objective.TryGetProperty("point", out var point) &&
            point.TryGetProperty("lla", out var lla))
        {
            if (lla.TryGetProperty("lat", out var latEl) &&
                lla.TryGetProperty("lon", out var lonEl))
            {
                lat = latEl.GetDouble();
                lon = lonEl.GetDouble();
                return true;
            }
        }

        return false;
    }
}
