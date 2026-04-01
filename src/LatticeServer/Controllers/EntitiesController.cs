using System.Collections.Concurrent;
using Anduril.Entitymanager.V1;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace LatticeServer.Controllers;

[ApiController]
public class EntitiesController : ControllerBase
{
    private readonly ILogger<EntitiesController> _logger;
    private readonly EntityStore _store;

    // Long-poll session tracking: sessionToken -> (subscription channel, pending events queue)
    private static readonly ConcurrentDictionary<string, LongPollSession> _sessions = new();

    public EntitiesController(ILogger<EntitiesController> logger, EntityStore store)
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>
    /// PUT /api/v1/entities - Publish entity
    /// </summary>
    [HttpPut("api/v1/entities")]
    public async Task<IActionResult> PublishEntity()
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        Entity entity;
        try
        {
            entity = ProtobufJsonConverter.FromJson<Entity>(body);
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid entity JSON: {ex.Message}" });
        }

        if (string.IsNullOrEmpty(entity.EntityId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "entity_id is required" });
        }

        if (!entity.NoExpiry)
        {
            if (entity.ExpiryTime == null)
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = "expiry_time is required when no_expiry is false" });
            }

            var expiryDateTime = entity.ExpiryTime.ToDateTime();
            if (expiryDateTime <= DateTime.UtcNow)
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = "expiry_time must be in the future" });
            }

            if (expiryDateTime > DateTime.UtcNow.AddDays(30))
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = "expiry_time must be less than 30 days in the future" });
            }
        }

        _logger.LogInformation("REST PublishEntity for entity {EntityId}", entity.EntityId);
        _store.PublishEntity(entity);

        return ProtobufJsonRaw(entity);
    }

    /// <summary>
    /// GET /api/v1/entities/{entityId} - Get entity
    /// </summary>
    [HttpGet("api/v1/entities/{entityId}")]
    public IActionResult GetEntity(string entityId)
    {
        if (string.IsNullOrEmpty(entityId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "entity_id is required" });
        }

        var entity = _store.GetEntity(entityId);
        if (entity == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Entity {entityId} not found" });
        }

        _logger.LogInformation("REST GetEntity for {EntityId}", entityId);
        return ProtobufJsonRaw(entity);
    }

    /// <summary>
    /// PUT /api/v1/entities/{entityId}/override/{fieldPath} - Override entity
    /// </summary>
    [HttpPut("api/v1/entities/{entityId}/override/{**fieldPath}")]
    public async Task<IActionResult> OverrideEntity(string entityId, string fieldPath)
    {
        if (string.IsNullOrEmpty(entityId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "entity_id is required" });
        }

        if (string.IsNullOrEmpty(fieldPath))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "field_path is required" });
        }

        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        Entity overrideEntity;
        try
        {
            // Docs: request body is EntityOverride = { entity: Entity, provenance: Provenance }
            var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("entity", out var entityProp))
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = "Request body must contain an 'entity' field" });
            }
            overrideEntity = ProtobufJsonConverter.FromJson<Entity>(entityProp.GetRawText());
        }
        catch (System.Text.Json.JsonException ex)
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid entity JSON: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid entity JSON: {ex.Message}" });
        }

        // Ensure the entity ID is set from the path
        overrideEntity.EntityId = entityId;

        var status = _store.ApplyOverride(entityId, [fieldPath], overrideEntity);
        if (status == OverrideStatus.Rejected)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Entity {entityId} not found" });
        }

        _logger.LogInformation("REST OverrideEntity for {EntityId} field {FieldPath}", entityId, fieldPath);

        var entity = _store.GetEntity(entityId);
        return ProtobufJsonRaw(entity!);
    }

    /// <summary>
    /// DELETE /api/v1/entities/{entityId}/override/{fieldPath} - Remove entity override
    /// </summary>
    [HttpDelete("api/v1/entities/{entityId}/override/{**fieldPath}")]
    public IActionResult RemoveEntityOverride(string entityId, string fieldPath)
    {
        if (string.IsNullOrEmpty(entityId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "entity_id is required" });
        }

        if (string.IsNullOrEmpty(fieldPath))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "field_path is required" });
        }

        var entity = _store.GetEntity(entityId);
        if (entity == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Entity {entityId} not found" });
        }

        _store.RemoveOverride(entityId, [fieldPath]);
        _logger.LogInformation("REST RemoveEntityOverride for {EntityId} field {FieldPath}", entityId, fieldPath);

        // Re-fetch after override removal
        entity = _store.GetEntity(entityId);
        return ProtobufJsonRaw(entity!);
    }

    /// <summary>
    /// DELETE /api/v1/entities - Delete all entities
    /// </summary>
    [HttpDelete("api/v1/entities")]
    public IActionResult DeleteAllEntities()
    {
        var count = _store.DeleteAllEntities();
        _logger.LogInformation("REST DeleteAllEntities: deleted {Count} entities", count);
        return Ok(new { deletedCount = count });
    }

    /// <summary>
    /// DELETE /api/v1/entities/{entityId} - Delete entity
    /// </summary>
    [HttpDelete("api/v1/entities/{entityId}")]
    public IActionResult DeleteEntity(string entityId)
    {
        if (string.IsNullOrEmpty(entityId))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "entity_id is required" });
        }

        var deleted = _store.DeleteEntity(entityId);
        if (!deleted)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Entity {entityId} not found" });
        }

        _logger.LogInformation("REST DeleteEntity for {EntityId}", entityId);
        return Ok(new { entityId, deleted = true });
    }

    /// <summary>
    /// POST /api/v1/entities/stream - Stream entity events (SSE)
    /// </summary>
    [HttpPost("api/v1/entities/stream")]
    public async Task StreamEntities()
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        // Parse request parameters
        int heartbeatIntervalMs = 30000;
        bool preExistingOnly = false;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("heartbeatIntervalMS", out var hb) ||
                    doc.RootElement.TryGetProperty("heartbeatIntervalMs", out hb))
                {
                    heartbeatIntervalMs = hb.GetInt32();
                }
                if (doc.RootElement.TryGetProperty("preExistingOnly", out var peo))
                {
                    preExistingOnly = peo.GetBoolean();
                }
            }
            catch
            {
                // Use defaults if parse fails
            }
        }

        if (heartbeatIntervalMs <= 0) heartbeatIntervalMs = 30000;

        SseHelper.SetSseHeaders(Response);
        var ct = HttpContext.RequestAborted;

        _logger.LogInformation("REST StreamEntities started (preExistingOnly={PreExistingOnly})", preExistingOnly);

        // Send preexisting entities
        var existing = _store.GetAllEntities();
        foreach (var entity in existing)
        {
            var evt = new EntityEvent
            {
                EventType = EventType.Preexisting,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                Entity = entity,
            };
            await SseHelper.WriteProtobufEventAsync(Response, "entity", evt, ct);
        }

        if (preExistingOnly)
        {
            return;
        }

        // Subscribe and stream live updates
        var subscription = _store.Subscribe();
        try
        {
            var heartbeatTask = SendSseHeartbeats(Response, heartbeatIntervalMs, ct);
            var streamTask = StreamEntityEvents(subscription, Response, ct);

            await Task.WhenAny(heartbeatTask, streamTask);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _store.Unsubscribe(subscription);
        }
    }

    /// <summary>
    /// POST /api/v1/entities/events - Long-poll entity events
    /// </summary>
    [HttpPost("api/v1/entities/events")]
    public async Task<IActionResult> LongPollEntityEvents()
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        string sessionToken = "";
        int batchSize = 100;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("sessionToken", out var st))
                {
                    sessionToken = st.GetString() ?? "";
                }
                if (doc.RootElement.TryGetProperty("batchSize", out var bs))
                {
                    batchSize = bs.GetInt32();
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "INVALID_ARGUMENT", message = $"Invalid request: {ex.Message}" });
            }
        }

        batchSize = Math.Clamp(batchSize, 1, 2000);

        LongPollSession session;

        if (string.IsNullOrEmpty(sessionToken))
        {
            // New session: send preexisting entities
            session = new LongPollSession(_store);
            _sessions[session.Token] = session;

            var existing = _store.GetAllEntities();
            var events = existing.Select(e => new EntityEvent
            {
                EventType = EventType.Preexisting,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                Entity = e,
            }).ToList();

            var batch = events.Take(batchSize).ToList();
            session.EnqueueRemaining(events.Skip(batchSize));

            return Ok(BuildEntityEventResponse(session.Token, batch));
        }

        // Existing session
        if (!_sessions.TryGetValue(sessionToken, out session!))
        {
            return NotFound(new { code = "NOT_FOUND", message = "Session not found. Start a new session with an empty sessionToken." });
        }

        // Check if session has fallen behind
        var totalEntities = _store.GetAllEntities().Count;
        if (totalEntities > 0 && session.PendingCount > totalEntities * 3)
        {
            _sessions.TryRemove(sessionToken, out _);
            session.Dispose();
            return StatusCode(408, new { code = "SESSION_BEHIND", message = "Session has fallen too far behind. Start a new session." });
        }

        // Drain any queued events first
        var queuedEvents = session.DrainPending(batchSize);
        if (queuedEvents.Count > 0)
        {
            return Ok(BuildEntityEventResponse(session.Token, queuedEvents));
        }

        // Long-poll: wait for new events up to 5 minutes
        var ct = HttpContext.RequestAborted;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var collectedEvents = new List<EntityEvent>();
        try
        {
            // Wait for at least one event
            while (collectedEvents.Count < batchSize)
            {
                if (await session.Subscription.Reader.WaitToReadAsync(linkedCts.Token))
                {
                    while (collectedEvents.Count < batchSize &&
                           session.Subscription.Reader.TryRead(out var evt))
                    {
                        collectedEvents.Add(evt);
                    }
                    // Got at least one event, return immediately
                    if (collectedEvents.Count > 0)
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout or client disconnect — return whatever we have
        }

        return Ok(BuildEntityEventResponse(session.Token, collectedEvents));
    }

    private static object BuildEntityEventResponse(string sessionToken, List<EntityEvent> events)
    {
        var jsonEvents = events.Select(e => System.Text.Json.JsonSerializer.Deserialize<object>(
            ProtobufJsonConverter.ToJson(e))).ToList();

        return new
        {
            sessionToken,
            events = jsonEvents,
        };
    }

    private static async Task SendSseHeartbeats(HttpResponse response, int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(intervalMs, ct);
            await SseHelper.WriteHeartbeatAsync(response, ct);
        }
    }

    private static async Task StreamEntityEvents(
        System.Threading.Channels.Channel<EntityEvent> subscription,
        HttpResponse response,
        CancellationToken ct)
    {
        await foreach (var entityEvent in subscription.Reader.ReadAllAsync(ct))
        {
            await SseHelper.WriteProtobufEventAsync(response, "entity", entityEvent, ct);
        }
    }

    private ContentResult ProtobufJsonRaw(Google.Protobuf.IMessage message)
    {
        return Content(ProtobufJsonConverter.ToJson(message), "application/json");
    }
}

/// <summary>
/// Manages state for a long-poll entity events session.
/// </summary>
internal sealed class LongPollSession : IDisposable
{
    public string Token { get; } = Guid.NewGuid().ToString();
    public System.Threading.Channels.Channel<EntityEvent> Subscription { get; }

    private readonly EntityStore _store;
    private readonly Queue<EntityEvent> _pending = new();
    private readonly Lock _lock = new();

    public int PendingCount
    {
        get
        {
            lock (_lock) return _pending.Count;
        }
    }

    public LongPollSession(EntityStore store)
    {
        _store = store;
        Subscription = store.Subscribe();
    }

    public void EnqueueRemaining(IEnumerable<EntityEvent> events)
    {
        lock (_lock)
        {
            foreach (var e in events) _pending.Enqueue(e);
        }
    }

    public List<EntityEvent> DrainPending(int maxCount)
    {
        lock (_lock)
        {
            var result = new List<EntityEvent>();
            while (result.Count < maxCount && _pending.Count > 0)
            {
                result.Add(_pending.Dequeue());
            }
            return result;
        }
    }

    public void Dispose()
    {
        _store.Unsubscribe(Subscription);
    }
}
