using System.Collections.Concurrent;
using System.Threading.Channels;
using Anduril.Entitymanager.V1;

namespace LatticeServer.Services;

/// <summary>
/// Thread-safe in-memory store for entities. Tracks the latest version of each entity
/// and notifies subscribers of changes.
/// </summary>
public class EntityStore
{
    private readonly ConcurrentDictionary<string, Entity> _entities = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, Entity>> _overrides = new();
    private readonly List<Channel<EntityEvent>> _subscribers = [];
    private readonly Lock _subscriberLock = new();

    /// <summary>
    /// Publishes (creates or updates) an entity. If is_live is false, the entity is deleted.
    /// Returns the event that was generated.
    /// </summary>
    public EntityEvent PublishEntity(Entity entity)
    {
        var entityId = entity.EntityId;
        EntityEvent entityEvent;

        if (!entity.IsLive)
        {
            // is_live=false triggers a DELETE event
            if (_entities.TryRemove(entityId, out var removed))
            {
                entityEvent = new EntityEvent
                {
                    EventType = EventType.Deleted,
                    Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    Entity = removed,
                };
                NotifySubscribers(entityEvent);
                return entityEvent;
            }

            // Entity didn't exist, still return a delete event
            entityEvent = new EntityEvent
            {
                EventType = EventType.Deleted,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                Entity = entity,
            };
            return entityEvent;
        }

        var isNew = true;
        _entities.AddOrUpdate(
            entityId,
            entity,
            (_, _) =>
            {
                isNew = false;
                return entity;
            });

        entityEvent = new EntityEvent
        {
            EventType = isNew ? EventType.Created : EventType.Update,
            Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            Entity = entity,
        };
        NotifySubscribers(entityEvent);
        return entityEvent;
    }

    /// <summary>
    /// Gets an entity by ID. Returns null if not found.
    /// </summary>
    public Entity? GetEntity(string entityId)
    {
        _entities.TryGetValue(entityId, out var entity);
        return entity;
    }

    /// <summary>
    /// Returns a snapshot of all currently live entities.
    /// </summary>
    public IReadOnlyCollection<Entity> GetAllEntities()
    {
        return _entities.Values.ToList();
    }

    /// <summary>
    /// Stores an override for a given entity/field path combination.
    /// The override entity contains the overridden field values.
    /// </summary>
    public OverrideStatus ApplyOverride(string entityId, IEnumerable<string> fieldPaths, Entity overrideEntity)
    {
        if (!_entities.ContainsKey(entityId))
        {
            return OverrideStatus.Rejected;
        }

        _overrides.AddOrUpdate(
            entityId,
            _ =>
            {
                var dict = new Dictionary<string, Entity>();
                foreach (var path in fieldPaths)
                {
                    dict[path] = overrideEntity;
                }
                return dict;
            },
            (_, existing) =>
            {
                foreach (var path in fieldPaths)
                {
                    existing[path] = overrideEntity;
                }
                return existing;
            });

        // Notify subscribers of the update
        var entity = _entities.GetValueOrDefault(entityId);
        if (entity != null)
        {
            NotifySubscribers(new EntityEvent
            {
                EventType = EventType.Update,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                Entity = entity,
            });
        }

        return OverrideStatus.Applied;
    }

    /// <summary>
    /// Removes overrides for the given entity and field paths.
    /// </summary>
    public void RemoveOverride(string entityId, IEnumerable<string> fieldPaths)
    {
        if (_overrides.TryGetValue(entityId, out var overrides))
        {
            foreach (var path in fieldPaths)
            {
                overrides.Remove(path);
            }
            if (overrides.Count == 0)
            {
                _overrides.TryRemove(entityId, out _);
            }

            // Notify subscribers of the update
            var entity = _entities.GetValueOrDefault(entityId);
            if (entity != null)
            {
                NotifySubscribers(new EntityEvent
                {
                    EventType = EventType.Update,
                    Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    Entity = entity,
                });
            }
        }
    }

    /// <summary>
    /// Deletes an entity by ID, broadcasting a Deleted event. Returns false if not found.
    /// </summary>
    public bool DeleteEntity(string entityId)
    {
        if (_entities.TryRemove(entityId, out var entity))
        {
            _overrides.TryRemove(entityId, out _);
            NotifySubscribers(new EntityEvent
            {
                EventType = EventType.Deleted,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                Entity = entity,
            });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Creates a subscription channel that receives entity events.
    /// </summary>
    public Channel<EntityEvent> Subscribe()
    {
        var channel = Channel.CreateUnbounded<EntityEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
        });

        lock (_subscriberLock)
        {
            _subscribers.Add(channel);
        }

        return channel;
    }

    /// <summary>
    /// Removes a subscription channel.
    /// </summary>
    public void Unsubscribe(Channel<EntityEvent> channel)
    {
        lock (_subscriberLock)
        {
            _subscribers.Remove(channel);
        }
        channel.Writer.TryComplete();
    }

    /// <summary>
    /// Completes all subscriber channels, signalling EOF to all active streams.
    /// Call this during application shutdown.
    /// </summary>
    public void Shutdown()
    {
        lock (_subscriberLock)
        {
            foreach (var subscriber in _subscribers)
                subscriber.Writer.TryComplete();
            _subscribers.Clear();
        }
    }

    private void NotifySubscribers(EntityEvent entityEvent)
    {
        lock (_subscriberLock)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(entityEvent);
            }
        }
    }
}
