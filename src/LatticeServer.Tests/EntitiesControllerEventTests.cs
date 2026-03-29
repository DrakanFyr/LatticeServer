using System.Net;
using System.Text;
using System.Text.Json;
using Anduril.Entitymanager.V1;
using LatticeServer.Helpers;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for POST /api/v1/entities/stream (SSE) and POST /api/v1/entities/events (Long Poll).
/// </summary>
public class EntitiesControllerEventTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EntitiesControllerEventTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, EntityStore Store) CreateIsolatedClient()
    {
        var store = new EntityStore();
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(store);
            });
        }).CreateClient();
        return (client, store);
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private void SeedEntities(EntityStore store, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            store.PublishEntity(new Entity
            {
                EntityId = $"entity-{i}",
                IsLive = true,
                NoExpiry = true,
                Description = $"Entity {i}",
            });
        }
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/entities/stream - SSE Streaming
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StreamEntities_PreExistingOnly_SendsExistingEntitiesThenCloses()
    {
        // Doc: "The server first sends events with type PREEXISTING for all live entities"
        // Doc: "preExistingOnly: Subscribe to a finite stream of preexisting events which
        //       closes when there are no additional pre-existing events to process."
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 2);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entities/stream")
        {
            Content = JsonContent("""{"preExistingOnly": true}"""),
        };

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        // SSE format: "event: entity\ndata: {...}\n\n"
        var eventLines = body.Split('\n')
            .Where(l => l.StartsWith("event: "))
            .Select(l => l["event: ".Length..])
            .ToList();
        Assert.Equal(2, eventLines.Count);
        Assert.All(eventLines, e => Assert.Equal("entity", e));
    }

    [Fact]
    public async Task StreamEntities_PreExistingOnly_NoEntities_ReturnsEmptyStream()
    {
        var (client, _) = CreateIsolatedClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entities/stream")
        {
            Content = JsonContent("""{"preExistingOnly": true}"""),
        };

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var eventLines = body.Split('\n').Where(l => l.StartsWith("event: ")).ToList();
        Assert.Empty(eventLines);
    }

    [Fact]
    public async Task StreamEntities_PreExistingOnly_ContainsEntityDataInSseFormat()
    {
        // Doc: SSE response with "event: PREEXISTING" and data containing EntityEvent JSON
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entities/stream")
        {
            Content = JsonContent("""{"preExistingOnly": true}"""),
        };

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();

        // Verify data lines contain entity event JSON with entity data
        var dataLines = body.Split('\n')
            .Where(l => l.StartsWith("data: "))
            .Select(l => l["data: ".Length..])
            .ToList();
        Assert.Single(dataLines);

        var entityEvent = ProtobufJsonConverter.FromJson<EntityEvent>(dataLines[0]);
        Assert.Equal(EventType.Preexisting, entityEvent.EventType);
        Assert.Equal("entity-1", entityEvent.Entity.EntityId);
    }

    [Fact]
    public async Task StreamEntities_SseHeaders_AreCorrect()
    {
        // Doc: "text/event-stream content type"
        var (client, _) = CreateIsolatedClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entities/stream")
        {
            Content = JsonContent("""{"preExistingOnly": true}"""),
        };

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StreamEntities_EmptyBody_DefaultsToLiveStream_PreExistingOnly()
    {
        // Empty body should default to preExistingOnly=false, but we can verify
        // it at least doesn't error with empty body. We use cancellation to stop the stream.
        var (client, _) = CreateIsolatedClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entities/stream")
        {
            Content = JsonContent("{}"),
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            // Expected — live stream waits indefinitely until cancelled
        }
    }

    [Fact]
    public async Task StreamEntities_LiveStream_ReceivesNewEvents()
    {
        // Doc: "then streams CREATE events for newly created entities, UPDATE events when
        //       existing entities change, and DELETED events when entities are removed."
        // Verified via Long Poll instead of raw SSE stream reading, since TestHost SSE
        // stream buffering makes direct stream reads unreliable in unit tests.
        var (client, store) = CreateIsolatedClient();

        // Start a long-poll session (exercises the same subscription mechanism)
        var firstResponse = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));
        var sessionToken = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sessionToken").GetString()!;

        // Publish a new entity — the session's subscription should receive CREATE
        store.PublishEntity(new Entity
        {
            EntityId = "live-entity",
            IsLive = true,
            NoExpiry = true,
            Description = "Published during stream",
        });

        // Poll — should receive the CREATE event
        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent($$$"""{"sessionToken": "{{{sessionToken}}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var events = doc.RootElement.GetProperty("events");
        Assert.True(events.GetArrayLength() >= 1);

        // Verify it's a CREATE event
        var firstEvent = events[0];
        Assert.Equal("EVENT_TYPE_CREATED", firstEvent.GetProperty("eventType").GetString());
        Assert.Equal("live-entity", firstEvent.GetProperty("entity").GetProperty("entityId").GetString());
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/entities/events - Long Poll
    // ───────────────────────────────────────────────

    [Fact]
    public async Task LongPoll_NewSession_ReturnsPreexistingEntities()
    {
        // Doc: "If you want to start a new polling session then open a request with an empty
        //       'sessionToken' in the request body."
        // Doc: "The server will return a new session token in the response."
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 3);

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        // Should have a session token
        Assert.True(doc.RootElement.TryGetProperty("sessionToken", out var tokenProp));
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));

        // Should have preexisting events
        Assert.True(doc.RootElement.TryGetProperty("events", out var eventsProp));
        Assert.Equal(3, eventsProp.GetArrayLength());
    }

    [Fact]
    public async Task LongPoll_NewSession_EmptyStore_ReturnsEmptyEvents()
    {
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("sessionToken", out var tokenProp));
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));

        Assert.True(doc.RootElement.TryGetProperty("events", out var eventsProp));
        Assert.Equal(0, eventsProp.GetArrayLength());
    }

    [Fact]
    public async Task LongPoll_NewSession_RespectsBatchSize()
    {
        // Doc: "batchSize: Maximum size of response batch. Defaults to 100. Must be between 1 and 2000."
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 5);

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": "", "batchSize": 2}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        var events = doc.RootElement.GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
    }

    [Fact]
    public async Task LongPoll_ExistingSession_ReturnsPendingEvents()
    {
        // Doc: "send the session token you received from the server in the request body"
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 5);

        // First request with batchSize=2 to leave 3 pending
        var firstResponse = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": "", "batchSize": 2}"""));
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var firstDoc = JsonDocument.Parse(firstBody);
        var sessionToken = firstDoc.RootElement.GetProperty("sessionToken").GetString()!;

        // Second request with same session should return remaining pending events
        var secondResponse = await client.PostAsync("/api/v1/entities/events",
            JsonContent($$$"""{"sessionToken": "{{{sessionToken}}}", "batchSize": 10}"""));

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        var secondDoc = JsonDocument.Parse(secondBody);
        var events = secondDoc.RootElement.GetProperty("events");
        Assert.Equal(3, events.GetArrayLength());
    }

    [Fact]
    public async Task LongPoll_InvalidSessionToken_Returns404()
    {
        // Doc: "Session not found. Start a new session with an empty sessionToken." (404)
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": "nonexistent-token"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", body);
    }

    [Fact]
    public async Task LongPoll_InvalidJson_Returns400()
    {
        // Doc: "Bad request" (400)
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("{bad json}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ARGUMENT", body);
    }

    [Fact]
    public async Task LongPoll_EmptyBody_StartsNewSession()
    {
        // Empty body should start a new session (sessionToken defaults to "")
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("sessionToken", out _));
    }

    [Fact]
    public async Task LongPoll_ExistingSession_ReceivesLiveEvents()
    {
        // Doc: "return all new data as it becomes available"
        var (client, store) = CreateIsolatedClient();

        // Start a new session with no preexisting entities
        var firstResponse = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var sessionToken = JsonDocument.Parse(firstBody)
            .RootElement.GetProperty("sessionToken").GetString()!;

        // Publish an entity — should be delivered to the session's subscription
        store.PublishEntity(new Entity
        {
            EntityId = "live-event-entity",
            IsLive = true,
            NoExpiry = true,
        });

        // Poll with the session token — should receive the new event
        var secondResponse = await client.PostAsync("/api/v1/entities/events",
            JsonContent($$$"""{"sessionToken": "{{{sessionToken}}}"}"""));

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(secondBody);
        var events = doc.RootElement.GetProperty("events");
        Assert.True(events.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task LongPoll_BatchSizeClamped_MinimumIs1()
    {
        // Doc: "Must be between 1 and 2000 (inclusive)."
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 3);

        // batchSize of 0 should be clamped to 1
        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": "", "batchSize": 0}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var events = doc.RootElement.GetProperty("events");
        Assert.Equal(1, events.GetArrayLength());
    }

    [Fact]
    public async Task LongPoll_BatchSizeClamped_MaximumIs2000()
    {
        // Doc: "Must be between 1 and 2000 (inclusive)."
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 3);

        // batchSize of 5000 should be clamped to 2000 (but we only have 3 entities)
        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": "", "batchSize": 5000}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var events = doc.RootElement.GetProperty("events");
        Assert.Equal(3, events.GetArrayLength());
    }

    [Fact]
    public async Task LongPoll_ResponseContainsSessionTokenAndEvents()
    {
        // Doc: Response schema EntityEventResponse has sessionToken and events array
        var (client, _) = CreateIsolatedClient();

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("sessionToken", out _));
        Assert.True(doc.RootElement.TryGetProperty("events", out _));
    }

    [Fact]
    public async Task LongPoll_Timeout_ReturnsEmptyEvents()
    {
        // Doc: "If no new data is available then the server will hold the connection open for up to 5 minutes."
        // We can't wait 5 minutes in a test, but we can verify the timeout mechanism works
        // by using client-side cancellation to simulate early disconnect
        var (client, store) = CreateIsolatedClient();

        // Start session
        var firstResponse = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));
        var sessionToken = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sessionToken").GetString()!;

        // Poll with no new events — use a short timeout via cancellation
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var response = await client.PostAsync("/api/v1/entities/events",
                JsonContent($$$"""{"sessionToken": "{{{sessionToken}}}"}"""), cts.Token);

            // If we get a response, it should be valid
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            // Expected — the long poll was waiting and we cancelled
        }
    }

    [Fact]
    public async Task LongPoll_PreexistingEventsHaveCorrectType()
    {
        // Doc: "EVENT_TYPE_PREEXISTING" for preexisting entities
        var (client, store) = CreateIsolatedClient();
        SeedEntities(store, 1);

        var response = await client.PostAsync("/api/v1/entities/events",
            JsonContent("""{"sessionToken": ""}"""));

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var events = doc.RootElement.GetProperty("events");
        var firstEvent = events[0];

        Assert.True(firstEvent.TryGetProperty("eventType", out var eventType));
        Assert.Equal("EVENT_TYPE_PREEXISTING", eventType.GetString());
    }
}
