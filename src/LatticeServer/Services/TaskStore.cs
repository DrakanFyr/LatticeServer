using System.Collections.Concurrent;
using System.Threading.Channels;
using Anduril.Taskmanager.V1;

namespace LatticeServer.Services;

/// <summary>
/// Thread-safe in-memory store for tasks. Tracks the latest version of each task
/// and notifies subscribers of changes.
/// </summary>
public class TaskStore
{
    private readonly ConcurrentDictionary<string, Anduril.Taskmanager.V1.Task> _tasks = new();
    private readonly List<Channel<TaskEvent>> _subscribers = [];
    private readonly Lock _subscriberLock = new();

    /// <summary>
    /// Stores a task. Returns the task event that was generated.
    /// </summary>
    public TaskEvent UpsertTask(Anduril.Taskmanager.V1.Task task, EventType eventType)
    {
        var taskId = task.Version.TaskId;
        _tasks[taskId] = task;

        var taskEvent = new TaskEvent
        {
            EventType = eventType,
            Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            Task = task,
            TaskView = TaskView.Manager,
        };
        NotifySubscribers(taskEvent);
        return taskEvent;
    }

    /// <summary>
    /// Gets a task by ID. Returns null if not found.
    /// </summary>
    public Anduril.Taskmanager.V1.Task? GetTask(string taskId)
    {
        _tasks.TryGetValue(taskId, out var task);
        return task;
    }

    /// <summary>
    /// Returns a snapshot of all current tasks.
    /// </summary>
    public IReadOnlyCollection<Anduril.Taskmanager.V1.Task> GetAllTasks()
    {
        return _tasks.Values.ToList();
    }

    /// <summary>
    /// Removes all tasks assigned to the given entity ID. Returns the count removed.
    /// </summary>
    public int DeleteTasksByAssignee(string assigneeEntityId)
    {
        var toRemove = _tasks.Values
            .Where(t => GetAssigneeEntityId(t) == assigneeEntityId)
            .Select(t => t.Version.TaskId)
            .ToList();

        foreach (var id in toRemove)
            _tasks.TryRemove(id, out _);

        return toRemove.Count;
    }

    private static string? GetAssigneeEntityId(Anduril.Taskmanager.V1.Task task)
    {
        var assignee = task.Relations?.Assignee;
        if (assignee == null) return null;
        return assignee.AgentCase switch
        {
            Principal.AgentOneofCase.System => assignee.System.EntityId,
            Principal.AgentOneofCase.Team   => assignee.Team.EntityId,
            _ => null,
        };
    }

    /// <summary>
    /// Creates a subscription channel that receives task events.
    /// </summary>
    public Channel<TaskEvent> Subscribe()
    {
        var channel = Channel.CreateUnbounded<TaskEvent>(new UnboundedChannelOptions
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
    public void Unsubscribe(Channel<TaskEvent> channel)
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

    private void NotifySubscribers(TaskEvent taskEvent)
    {
        lock (_subscriberLock)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(taskEvent);
            }
        }
    }
}
