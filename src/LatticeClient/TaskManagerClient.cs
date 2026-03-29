using Anduril.Taskmanager.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace LatticeClient;

public class TaskManagerClient : ITaskManagerClient
{
    private readonly TaskManagerAPI.TaskManagerAPIClient _client;
    private readonly GrpcChannel? _ownedChannel;

    public TaskManagerClient(GrpcChannel channel)
    {
        _client = new TaskManagerAPI.TaskManagerAPIClient(channel);
    }

    public TaskManagerClient(string address)
    {
        _ownedChannel = GrpcChannel.ForAddress(address);
        _client = new TaskManagerAPI.TaskManagerAPIClient(_ownedChannel);
    }

    public async Task<Anduril.Taskmanager.V1.Task> CreateTaskAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.CreateTaskAsync(request, cancellationToken: cancellationToken);
        return response.Task;
    }

    public async Task<Anduril.Taskmanager.V1.Task> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var request = new GetTaskRequest { TaskId = taskId };
        var response = await _client.GetTaskAsync(request, cancellationToken: cancellationToken);
        return response.Task;
    }

    public async Task<QueryTasksResponse> QueryTasksAsync(
        QueryTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _client.QueryTasksAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<Anduril.Taskmanager.V1.Task> UpdateStatusAsync(
        StatusUpdate statusUpdate,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateStatusRequest { StatusUpdate = statusUpdate };
        var response = await _client.UpdateStatusAsync(request, cancellationToken: cancellationToken);
        return response.Task;
    }

    public async Task<Anduril.Taskmanager.V1.Task> CancelTaskAsync(
        string taskId,
        Principal? author = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CancelTaskRequest { TaskId = taskId };
        if (author != null)
        {
            request.Author = author;
        }
        var response = await _client.CancelTaskAsync(request, cancellationToken: cancellationToken);
        return response.Task;
    }

    public async IAsyncEnumerable<ListenAsAgentResponse> ListenAsAgentAsync(
        ListenAsAgentRequest? request = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request ??= new ListenAsAgentRequest();

        using var call = _client.ListenAsAgent(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    public void Dispose()
    {
        _ownedChannel?.Dispose();
        GC.SuppressFinalize(this);
    }
}
