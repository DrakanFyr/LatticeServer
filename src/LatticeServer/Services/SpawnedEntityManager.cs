using System.Collections.Concurrent;
using System.Text.Json;
using Anduril.Entitymanager.V1;
using Google.Protobuf.WellKnownTypes;
using LatticeSDK.Templates;
using LatticeServer.Helpers;
using ProtoTask = Anduril.Taskmanager.V1.Task;
using ProtoTaskStatus = Anduril.Taskmanager.V1.TaskStatus;
using ProtoTaskEventType = Anduril.Taskmanager.V1.EventType;
using ProtoStatus = Anduril.Taskmanager.V1.Status;
using ProtoErrorCode = Anduril.Taskmanager.V1.ErrorCode;
using ProtoTaskStore = LatticeServer.Services.TaskStore;

namespace LatticeServer.Services;

/// <summary>
/// Manages the lifecycle of spawned template instances: spawn, tick, task routing, and despawn.
/// </summary>
public class SpawnedEntityManager : IDisposable
{
    private readonly ILogger<SpawnedEntityManager> _logger;
    private readonly EntityStore _entityStore;
    private readonly ProtoTaskStore _taskStore;
    private readonly int _defaultExpirySeconds;
    private readonly ConcurrentDictionary<string, SpawnedInstance> _instances = new();

    private Timer? _tickTimer;
    private readonly CancellationTokenSource _cts = new();

    public SpawnedEntityManager(
        ILogger<SpawnedEntityManager> logger,
        EntityStore entityStore,
        TaskStore taskStore,
        IConfiguration configuration)
    {
        _logger = logger;
        _entityStore = entityStore;
        _taskStore = taskStore;
        _defaultExpirySeconds = configuration.GetValue("Templates:DefaultExpirySeconds", 300);

        // Start tick timer; recalculated after first spawn
        _tickTimer = new Timer(OnTick, null, TimeSpan.FromMilliseconds(32), TimeSpan.FromMilliseconds(32));

        // Subscribe to task store for routing
        _ = System.Threading.Tasks.Task.Run(() => RunTaskRouterAsync(_cts.Token));
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Spawn a new instance of the given template.</summary>
    public string Spawn(TemplateDefinition template, SpawnOptions? options = null)
    {
        // 1. Token substitution — use caller-supplied JSON if provided, else template default
        var rawJson = options?.EntityJsonOverride ?? template.RawEntityJson;
        var json = TokenSubstitutor.Substitute(rawJson);

        // 2. Resolve spawn location and merge into JSON.
        // When the caller supplies their own entity JSON, don't fall back to the
        // template's default location — the JSON already contains the desired location.
        // Only merge if explicit coordinates were passed via SpawnOptions.
        var location = ResolveLocation(options, options?.EntityJsonOverride != null ? null : template.Config);
        if (location != null)
        {
            json = MergeLocation(json, location);
        }

        // 3. Apply name override and extraJsonPatch
        if (options?.NameOverride != null || options?.ExtraJsonPatch != null)
        {
            json = ApplyOverrides(json, options);
        }

        // 4. Parse to Entity proto and publish
        Entity entity;
        try
        {
            entity = ProtobufJsonConverter.FromJson<Entity>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template '{Id}': failed to parse substituted entity JSON during spawn.", template.TemplateId);
            throw;
        }

        var entityId = entity.EntityId;
        if (string.IsNullOrEmpty(entityId))
            throw new InvalidOperationException($"Template '{template.TemplateId}': entity JSON produced no entityId after substitution.");

        _entityStore.PublishEntity(entity);
        _logger.LogInformation("Spawned entity '{EntityId}' from template '{TemplateId}'.", entityId, template.TemplateId);

        // 5. Wire behavior if present
        ITaskableEntity? behavior = null;
        CancellationTokenSource? agentCts = null;

        if (template.BehaviorType != null)
        {
            try
            {
                behavior = (ITaskableEntity)Activator.CreateInstance(template.BehaviorType)!;
                var publisher = new EntityPublisherAdapter(entityId, _entityStore, _logger, _defaultExpirySeconds);
                var reporter = new TaskReporterAdapter(entityId, _taskStore, _logger);
                var querier = new EntityQuerierAdapter(_entityStore);
                behavior.OnSpawn(entityId, publisher, reporter, querier, template.Config);
                agentCts = new CancellationTokenSource();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Template '{Id}': behavior OnSpawn failed for entity '{EntityId}'.", template.TemplateId, entityId);
                behavior = null;
            }
        }

        // 6. Register instance
        var instance = new SpawnedInstance(
            EntityId: entityId,
            TemplateId: template.TemplateId,
            TickIntervalMs: template.Config.TickIntervalMs,
            NextTickTime: DateTime.UtcNow,
            Behavior: behavior,
            AgentListenerCts: agentCts,
            LastEntity: entity);

        _instances[entityId] = instance;

        RecalcTimerInterval();
        return entityId;
    }

    /// <summary>Despawn a specific entity instance.</summary>
    public bool Despawn(string entityId)
    {
        if (!_instances.TryRemove(entityId, out var instance))
            return false;

        if (instance.Behavior != null)
        {
            try { instance.Behavior.OnDespawn(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Entity '{EntityId}': OnDespawn threw.", entityId); }
        }

        instance.AgentListenerCts?.Cancel();
        instance.AgentListenerCts?.Dispose();

        // Publish is_live=false
        var dead = instance.LastEntity.Clone();
        dead.IsLive = false;
        _entityStore.PublishEntity(dead);

        RecalcTimerInterval();
        _logger.LogInformation("Despawned entity '{EntityId}'.", entityId);
        return true;
    }

    /// <summary>Despawn all live instances.</summary>
    public int DespawnAll()
    {
        var ids = _instances.Keys.ToList();
        return ids.Count(Despawn);
    }

    public IEnumerable<SpawnedInstance> GetAll() => _instances.Values;

    public bool TryGet(string entityId, out SpawnedInstance instance)
        => _instances.TryGetValue(entityId, out instance!);

    // -------------------------------------------------------------------------
    // Tick loop
    // -------------------------------------------------------------------------

    private void OnTick(object? state)
    {
        var now = DateTime.UtcNow;
        foreach (var instance in _instances.Values)
        {
            if (now < instance.NextTickTime) continue;

            if (instance.Behavior != null)
            {
                var delta = (float)(now - instance.LastTickTime).TotalSeconds;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { instance.Behavior.OnUpdate(delta); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Entity '{EntityId}': OnUpdate threw (template: {TemplateId}).",
                            instance.EntityId, instance.TemplateId);
                    }
                });
            }
            else
            {
                // Non-taskable: re-publish to keep expiry fresh
                var existing = _entityStore.GetEntity(instance.EntityId);
                if (existing != null)
                {
                    var refreshed = existing.Clone();
                    refreshed.ExpiryTime = Timestamp.FromDateTime(
                        DateTime.UtcNow.AddSeconds(_defaultExpirySeconds));
                    _entityStore.PublishEntity(refreshed);
                }
            }

            var next = now.AddMilliseconds(instance.TickIntervalMs);
            _instances.TryUpdate(instance.EntityId,
                instance with { NextTickTime = next, LastTickTime = now },
                instance);
        }
    }

    private void RecalcTimerInterval()
    {
        var minMs = _instances.Values
            .Select(i => i.TickIntervalMs)
            .DefaultIfEmpty(1000)
            .Min();
        minMs = Math.Max(32, minMs);
        _tickTimer?.Change(TimeSpan.FromMilliseconds(minMs), TimeSpan.FromMilliseconds(minMs));
    }

    // -------------------------------------------------------------------------
    // Task routing
    // -------------------------------------------------------------------------

    private async System.Threading.Tasks.Task RunTaskRouterAsync(CancellationToken ct)
    {
        var subscription = _taskStore.Subscribe();
        try
        {
            await foreach (var taskEvent in subscription.Reader.ReadAllAsync(ct))
            {
                var task = taskEvent.Task;
                if (task == null) continue;

                var entityId = GetAssigneeEntityId(task);
                if (entityId == null) continue;

                if (!_instances.TryGetValue(entityId, out var instance) || instance.Behavior == null)
                    continue;

                try
                {
                    if (taskEvent.EventType == ProtoTaskEventType.Created)
                    {
                        var specJson = task.Specification != null
                            ? ProtobufJsonConverter.ToJson(task.Specification)
                            : "{}";
                        var payload = new TaskPayload(
                            task.Version.TaskId,
                            specJson,
                            task.Description);
                        instance.Behavior.OnTaskReceived(payload);
                    }
                    else if (task.Status?.Status == ProtoStatus.CancelRequested)
                    {
                        instance.Behavior.OnTaskCancelRequest(task.Version.TaskId);
                    }
                    else if (task.Status?.Status == ProtoStatus.CompleteRequested)
                    {
                        instance.Behavior.OnTaskCompleteRequest(task.Version.TaskId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Entity '{EntityId}': behavior threw during task routing.", entityId);
                }
            }
        }
        finally
        {
            _taskStore.Unsubscribe(subscription);
        }
    }

    private static string? GetAssigneeEntityId(ProtoTask task)
    {
        var assignee = task.Relations?.Assignee;
        if (assignee == null) return null;
        return assignee.AgentCase switch
        {
            Anduril.Taskmanager.V1.Principal.AgentOneofCase.System => assignee.System.EntityId,
            Anduril.Taskmanager.V1.Principal.AgentOneofCase.Team => assignee.Team.EntityId,
            _ => null,
        };
    }

    // -------------------------------------------------------------------------
    // Location resolution + JSON merging
    // -------------------------------------------------------------------------

    private static SpawnLocation? ResolveLocation(SpawnOptions? options, TemplateConfig? config)
    {
        if (options?.LatitudeDegrees.HasValue == true && options.LongitudeDegrees.HasValue)
        {
            return new SpawnLocation(
                options.LatitudeDegrees.Value,
                options.LongitudeDegrees.Value,
                options.AltitudeHaeMeters);
        }
        return config?.DefaultLocation;
    }

    private static string MergeLocation(string json, SpawnLocation loc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "location") continue;
                prop.WriteTo(writer);
            }

            writer.WritePropertyName("location");
            writer.WriteStartObject();

            if (doc.RootElement.TryGetProperty("location", out var existingLoc))
            {
                foreach (var locProp in existingLoc.EnumerateObject())
                {
                    if (locProp.Name == "position") continue;
                    locProp.WriteTo(writer);
                }
            }

            writer.WritePropertyName("position");
            writer.WriteStartObject();
            writer.WriteNumber("latitudeDegrees", loc.LatitudeDegrees);
            writer.WriteNumber("longitudeDegrees", loc.LongitudeDegrees);
            if (loc.AltitudeHaeMeters.HasValue)
                writer.WriteNumber("altitudeHaeMeters", loc.AltitudeHaeMeters.Value);
            writer.WriteEndObject(); // position

            writer.WriteEndObject(); // location
            writer.WriteEndObject(); // root

            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return json;
        }
    }

    private static string ApplyOverrides(string json, SpawnOptions options)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();
            var wroteAliases = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (options.NameOverride != null && prop.Name == "aliases")
                {
                    wroteAliases = true;
                    writer.WritePropertyName("aliases");
                    writer.WriteStartObject();
                    writer.WriteString("name", options.NameOverride);
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var aliasProp in prop.Value.EnumerateObject())
                        {
                            if (aliasProp.Name == "name") continue;
                            aliasProp.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                    continue;
                }
                prop.WriteTo(writer);
            }

            if (options.NameOverride != null && !wroteAliases)
            {
                writer.WritePropertyName("aliases");
                writer.WriteStartObject();
                writer.WriteString("name", options.NameOverride);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.Flush();
            json = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            if (options.ExtraJsonPatch.HasValue && options.ExtraJsonPatch.Value.ValueKind == JsonValueKind.Object)
            {
                json = DeepMergePatch(json, options.ExtraJsonPatch.Value);
            }

            return json;
        }
        catch
        {
            return json;
        }
    }

    private static string DeepMergePatch(string baseJson, JsonElement patch)
    {
        using var baseDoc = JsonDocument.Parse(baseJson);
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();

        var patchKeys = patch.EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var prop in baseDoc.RootElement.EnumerateObject())
        {
            if (patchKeys.Contains(prop.Name)) continue;
            prop.WriteTo(writer);
        }

        foreach (var patchProp in patch.EnumerateObject())
        {
            patchProp.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Dispose()
    {
        _cts.Cancel();
        _tickTimer?.Dispose();
        _cts.Dispose();
    }
}

// -------------------------------------------------------------------------
// Spawned instance record
// -------------------------------------------------------------------------

public record SpawnedInstance(
    string EntityId,
    string TemplateId,
    int TickIntervalMs,
    DateTime NextTickTime,
    ITaskableEntity? Behavior,
    CancellationTokenSource? AgentListenerCts,
    Entity LastEntity)
{
    public DateTime LastTickTime { get; init; } = DateTime.UtcNow;
}

// -------------------------------------------------------------------------
// SpawnOptions
// -------------------------------------------------------------------------

public class SpawnOptions
{
    public double? LatitudeDegrees { get; set; }
    public double? LongitudeDegrees { get; set; }
    public double? AltitudeHaeMeters { get; set; }
    public string? NameOverride { get; set; }
    public JsonElement? ExtraJsonPatch { get; set; }
    /// <summary>
    /// When set, replaces the template's entity.json as the starting entity JSON.
    /// The template's behavior (dll) is still wired up normally.
    /// </summary>
    public string? EntityJsonOverride { get; set; }
}

// -------------------------------------------------------------------------
// IEntityPublisher adapter
// -------------------------------------------------------------------------

internal sealed class EntityPublisherAdapter : IEntityPublisher
{
    private readonly string _entityId;
    private readonly EntityStore _entityStore;
    private readonly ILogger _logger;
    private readonly int _expirySeconds;

    public EntityPublisherAdapter(string entityId, EntityStore entityStore, ILogger logger, int expirySeconds)
    {
        _entityId = entityId;
        _entityStore = entityStore;
        _logger = logger;
        _expirySeconds = expirySeconds;
    }

    public void PublishEntityUpdate(EntityUpdate update)
    {
        var existing = _entityStore.GetEntity(_entityId);
        if (existing == null) return;

        var updated = existing.Clone();
        updated.ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(_expirySeconds));

        if (update.Latitude.HasValue || update.Longitude.HasValue || update.AltitudeHaeMeters.HasValue)
        {
            if (updated.Location == null) updated.Location = new Location();
            if (updated.Location.Position == null) updated.Location.Position = new Position();

            if (update.Latitude.HasValue)
                updated.Location.Position.LatitudeDegrees = update.Latitude.Value;
            if (update.Longitude.HasValue)
                updated.Location.Position.LongitudeDegrees = update.Longitude.Value;
            if (update.AltitudeHaeMeters.HasValue)
                updated.Location.Position.AltitudeHaeMeters = update.AltitudeHaeMeters.Value;
        }

        if (update.Disposition != null && updated.MilView != null)
        {
            if (System.Enum.TryParse<Anduril.Ontology.V1.Disposition>(update.Disposition, ignoreCase: true, out var disp))
                updated.MilView.Disposition = disp;
        }

        _entityStore.PublishEntity(updated);
    }
}

// -------------------------------------------------------------------------
// ITaskReporter adapter
// -------------------------------------------------------------------------

internal sealed class TaskReporterAdapter : ITaskReporter
{
    private readonly string _entityId;
    private readonly TaskStore _taskStore;
    private readonly ILogger _logger;

    public TaskReporterAdapter(string entityId, TaskStore taskStore, ILogger logger)
    {
        _entityId = entityId;
        _taskStore = taskStore;
        _logger = logger;
    }

    public void ReportStatus(string taskId, TaskStatusUpdate update)
    {
        var task = _taskStore.GetTask(taskId);
        if (task == null)
        {
            _logger.LogWarning("Entity '{EntityId}': ReportStatus called for unknown task '{TaskId}'.", _entityId, taskId);
            return;
        }

        var updated = task.Clone();
        updated.Version.StatusVersion++;
        updated.LastUpdateTime = Timestamp.FromDateTime(DateTime.UtcNow);
        updated.Status = new Anduril.Taskmanager.V1.TaskStatus { Status = MapStatus(update.Status) };

        if (update.ErrorMessage != null)
        {
            updated.Status.TaskError = new Anduril.Taskmanager.V1.TaskError
            {
                Code = update.Status == TaskStatusCode.Cancelled
                    ? ProtoErrorCode.Cancelled
                    : ProtoErrorCode.Failed,
                Message = update.ErrorMessage,
            };
        }

        _taskStore.UpsertTask(updated, Anduril.Taskmanager.V1.EventType.Update);
    }

    private static ProtoStatus MapStatus(TaskStatusCode code) => code switch
    {
        TaskStatusCode.Ack => ProtoStatus.Ack,
        TaskStatusCode.WillComply => ProtoStatus.Wilco,
        TaskStatusCode.Executing => ProtoStatus.Executing,
        TaskStatusCode.WaitingForUpdate => ProtoStatus.WaitingForUpdate,
        TaskStatusCode.DoneOk => ProtoStatus.DoneOk,
        TaskStatusCode.DoneNotOk => ProtoStatus.DoneNotOk,
        TaskStatusCode.Rejected => ProtoStatus.VersionRejected,
        TaskStatusCode.Cancelled => ProtoStatus.DoneNotOk,
        _ => ProtoStatus.Invalid,
    };
}

// -------------------------------------------------------------------------
// IEntityQuerier adapter
// -------------------------------------------------------------------------

internal sealed class EntityQuerierAdapter : IEntityQuerier
{
    private readonly EntityStore _entityStore;

    public EntityQuerierAdapter(EntityStore entityStore)
    {
        _entityStore = entityStore;
    }

    public EntityPosition? TryGetPosition(string entityId)
    {
        var entity = _entityStore.GetEntity(entityId);
        var pos = entity?.Location?.Position;
        if (pos == null) return null;

        return new EntityPosition(
            pos.LatitudeDegrees,
            pos.LongitudeDegrees,
            pos.AltitudeHaeMeters > 0 ? pos.AltitudeHaeMeters : null);
    }
}
