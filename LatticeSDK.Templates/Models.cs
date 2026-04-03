using System.Text.Json;

namespace LatticeSDK.Templates;

/// <summary>
/// A task delivered to the behavior. Contains the raw spec JSON so the behavior
/// can extract task-type-specific fields without a protobuf dependency.
/// </summary>
public record TaskPayload(string TaskId, string SpecificationJson, string Description);

/// <summary>
/// Fields the behavior can update on the entity. Only non-null fields are merged;
/// omit a field to leave it unchanged.
/// </summary>
public record EntityUpdate(
    double? Latitude,
    double? Longitude,
    double? AltitudeHaeMeters,
    string? Disposition,
    string? ExtraFieldsJson);

/// <summary>A status report from the behavior to the task manager.</summary>
public record TaskStatusUpdate(TaskStatusCode Status, string? ErrorMessage = null, string? ProgressJson = null);

/// <summary>Task status codes that map 1:1 to the Lattice proto Status enum.</summary>
public enum TaskStatusCode
{
    Ack,
    WillComply,
    Executing,
    WaitingForUpdate,
    DoneOk,
    DoneNotOk,
    Rejected,
    Cancelled,
}

/// <summary>
/// Deserialized form of a template's <c>config.json</c>. Passed to <see cref="ITaskableEntity.OnSpawn"/>.
/// </summary>
public record TemplateConfig(
    SpawnLocation? DefaultLocation,
    int TickIntervalMs,
    string? Category,
    string? DisplayName,
    IReadOnlyDictionary<string, JsonElement> Custom);

/// <summary>A lat/lon/alt coordinate used as a spawn origin or navigation target.</summary>
public record SpawnLocation(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double? AltitudeHaeMeters);

/// <summary>The last-known position of an entity, returned by <see cref="IEntityQuerier"/>.</summary>
public record EntityPosition(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double? AltitudeHaeMeters);
