using Anduril.Entitymanager.V1;
using Anduril.Taskmanager.V1;
using Google.Protobuf.WellKnownTypes;
using LatticeClient;
using Status = Anduril.Taskmanager.V1.Status;
using TaskStatus = Anduril.Taskmanager.V1.TaskStatus;

var address = args.Length > 0 ? args[0] : "http://localhost:5007";

try
{
    // ── Run with gRPC transport ──
    Console.WriteLine("╔══════════════════════════════════════╗");
    Console.WriteLine("║       gRPC Transport Test            ║");
    Console.WriteLine("╚══════════════════════════════════════╝");
    Console.WriteLine($"Connecting to LatticeServer at {address} (gRPC)...");

    using (var lattice = new LatticeConnection(address, TransportMode.Grpc))
    {
        await RunTestSequence(lattice, "grpc");
    }

    Console.WriteLine();

    // ── Run with REST transport ──
    Console.WriteLine("╔══════════════════════════════════════╗");
    Console.WriteLine("║       REST Transport Test            ║");
    Console.WriteLine("╚══════════════════════════════════════╝");
    Console.WriteLine($"Connecting to LatticeServer at {address} (REST)...");

    using (var lattice = new LatticeConnection(address, TransportMode.Rest))
    {
        await RunTestSequence(lattice, "rest");
    }

    Console.WriteLine("\n── All transport tests completed successfully! ──");
}
catch (Exception ex)
{
    Console.WriteLine($"\nERROR: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    return 1;
}

return 0;

static async System.Threading.Tasks.Task RunTestSequence(LatticeConnection lattice, string prefix)
{
    var entities = lattice.EntityManager;
    var tasks = lattice.TaskManager;

    var entityIdA = $"test-entity-A-{prefix}";
    var entityIdB = $"test-entity-B-{prefix}";
    var taskId = $"test-task-{prefix}-{DateTime.UtcNow:HHmmss}";

    // ── Step 1: Create entity A ──
    Console.WriteLine("\n── Step 1: Create entity A ──");
    await entities.PublishEntityAsync(new Entity
    {
        EntityId = entityIdA,
        IsLive = true,
        NoExpiry = true,
        Description = "Test Entity A",
        Aliases = new Aliases { Name = "Entity Alpha" },
    });
    Console.WriteLine($"  Created entity {entityIdA}");

    var entityA = await entities.GetEntityAsync(entityIdA);
    Console.WriteLine($"  Verified: {entityA.EntityId} - {entityA.Description}");

    // ── Step 2: Update entity A ──
    Console.WriteLine("\n── Step 2: Update entity A ──");
    await entities.PublishEntityAsync(new Entity
    {
        EntityId = entityIdA,
        IsLive = true,
        NoExpiry = true,
        Description = "Test Entity A (updated)",
        Aliases = new Aliases { Name = "Entity Alpha Updated" },
        Status = new Anduril.Entitymanager.V1.Status
        {
            PlatformActivity = "RECONNAISSANCE",
            Role = "Scout",
        },
    });
    Console.WriteLine($"  Updated entity {entityIdA}");

    entityA = await entities.GetEntityAsync(entityIdA);
    Console.WriteLine($"  Verified: {entityA.Description}, Activity={entityA.Status?.PlatformActivity}");

    // ── Step 3: Remove entity A ──
    Console.WriteLine("\n── Step 3: Remove entity A ──");
    await entities.PublishEntityAsync(new Entity
    {
        EntityId = entityIdA,
        IsLive = false,
        ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(1)),
    });
    Console.WriteLine($"  Removed entity {entityIdA}");

    try
    {
        await entities.GetEntityAsync(entityIdA);
        Console.WriteLine("  ERROR: Entity A still exists after removal!");
    }
    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
    {
        Console.WriteLine("  Verified: Entity A no longer exists");
    }
    catch (HttpRequestException ex) when (ex.Message.Contains("404"))
    {
        Console.WriteLine("  Verified: Entity A no longer exists");
    }

    // ── Step 4: Create entity B ──
    Console.WriteLine("\n── Step 4: Create entity B ──");
    await entities.PublishEntityAsync(new Entity
    {
        EntityId = entityIdB,
        IsLive = true,
        NoExpiry = true,
        Description = "Test Entity B - Agent",
        Aliases = new Aliases { Name = "Entity Bravo" },
    });
    Console.WriteLine($"  Created entity {entityIdB}");

    // ── Step 5: Update entity B ──
    Console.WriteLine("\n── Step 5: Update entity B ──");
    await entities.PublishEntityAsync(new Entity
    {
        EntityId = entityIdB,
        IsLive = true,
        NoExpiry = true,
        Description = "Test Entity B - Agent (updated)",
        Aliases = new Aliases { Name = "Entity Bravo Updated" },
        Status = new Anduril.Entitymanager.V1.Status
        {
            PlatformActivity = "INTERDICTION",
            Role = "Commander",
        },
    });
    Console.WriteLine($"  Updated entity {entityIdB}");

    var entityB = await entities.GetEntityAsync(entityIdB);
    Console.WriteLine($"  Verified: {entityB.Description}, Activity={entityB.Status?.PlatformActivity}");

    // ── Step 6: Create ListenAsAgent stream for entity B ──
    Console.WriteLine("\n── Step 6: Create ListenAsAgent stream for entity B ──");
    using var agentCts = new CancellationTokenSource();

    var agentRequest = new ListenAsAgentRequest
    {
        EntityIds = new EntityIds { EntityIds_ = { entityIdB } },
    };

    Anduril.Taskmanager.V1.Task? receivedTask = null;
    var taskReceivedSignal = new TaskCompletionSource<bool>();

    var agentListenTask = System.Threading.Tasks.Task.Run(async () =>
    {
        await foreach (var response in tasks.ListenAsAgentAsync(agentRequest, agentCts.Token))
        {
            if (response.ExecuteRequest != null)
            {
                receivedTask = response.ExecuteRequest.Task;
                Console.WriteLine($"  Agent received task: {receivedTask.Version.TaskId}");
                taskReceivedSignal.TrySetResult(true);
            }
            else if (response.CancelRequest != null)
            {
                Console.WriteLine($"  Agent received cancel for task: {response.CancelRequest.TaskId}");
            }
        }
    }, agentCts.Token);

    // Give the stream a moment to establish
    await System.Threading.Tasks.Task.Delay(500);
    Console.WriteLine("  Agent stream established, listening for tasks...");

    // ── Step 7: Create a task for entity B ──
    Console.WriteLine("\n── Step 7: Create a task for entity B ──");
    var createdTask = await tasks.CreateTaskAsync(new CreateTaskRequest
    {
        TaskId = taskId,
        Description = "Test task assigned to Entity B",
        Author = new Principal
        {
            User = new User { UserId = "test-user" },
        },
        Relations = new Relations
        {
            Assignee = new Principal
            {
                System = new Anduril.Taskmanager.V1.System
                {
                    ServiceName = "LatticeClientTest",
                    EntityId = entityIdB,
                },
            },
        },
    });
    Console.WriteLine($"  Created task {createdTask.Version.TaskId}, status={createdTask.Status.Status}");

    // Wait for agent to receive the task
    var received = await System.Threading.Tasks.Task.WhenAny(
        taskReceivedSignal.Task,
        System.Threading.Tasks.Task.Delay(5000));
    if (received != taskReceivedSignal.Task)
    {
        Console.WriteLine("  WARNING: Timed out waiting for agent to receive task");
    }

    // ── Step 8: Agent updates task through full status sequence ──
    Console.WriteLine("\n── Step 8: Agent updates task through status sequence ──");

    var statusSequence = new[]
    {
        Status.MachineReceipt,
        Status.Ack,
        Status.Wilco,
        Status.Executing,
        Status.WaitingForUpdate,
        Status.Executing,
        Status.DoneOk,
    };

    var currentTask = await tasks.GetTaskAsync(taskId);
    foreach (var status in statusSequence)
    {
        var updated = await tasks.UpdateStatusAsync(new StatusUpdate
        {
            Version = new TaskVersion
            {
                TaskId = taskId,
                StatusVersion = currentTask.Version.StatusVersion,
            },
            Status = new TaskStatus { Status = status },
            Author = new Principal
            {
                System = new Anduril.Taskmanager.V1.System
                {
                    ServiceName = "LatticeClientTest",
                    EntityId = entityIdB,
                },
            },
        });
        Console.WriteLine($"  {currentTask.Status.Status} -> {status} (status_version={updated.Version.StatusVersion})");
        currentTask = updated;
    }

    // Verify final state
    var finalTask = await tasks.GetTaskAsync(taskId);
    Console.WriteLine($"  Final task status: {finalTask.Status.Status}");

    // Stop agent listener
    await agentCts.CancelAsync();
    try { await agentListenTask; }
    catch (OperationCanceledException) { }
    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled) { }

    // ── Step 9: Remove entity B ──
    Console.WriteLine("\n── Step 9: Remove entity B ──");
    await entities.PublishEntityAsync(new Entity
    {
        EntityId = entityIdB,
        IsLive = false,
        ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(1)),
    });
    Console.WriteLine($"  Removed entity {entityIdB}");

    try
    {
        await entities.GetEntityAsync(entityIdB);
        Console.WriteLine("  ERROR: Entity B still exists after removal!");
    }
    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
    {
        Console.WriteLine("  Verified: Entity B no longer exists");
    }
    catch (HttpRequestException ex) when (ex.Message.Contains("404"))
    {
        Console.WriteLine("  Verified: Entity B no longer exists");
    }

    Console.WriteLine("\n── All steps completed successfully! ──");
}
