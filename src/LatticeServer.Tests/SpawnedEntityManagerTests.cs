using System.Text.Json;
using LatticeSDK.Templates;
using LatticeServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LatticeServer.Tests;

public class SpawnedEntityManagerTests : IDisposable
{
    private readonly EntityStore _entityStore;
    private readonly TaskStore _taskStore;
    private readonly SpawnedEntityManager _manager;

    // Minimal entity JSON that produces a valid Entity proto with a generated ID
    private const string MinimalEntityJson =
        """{"entityId":"<new_uuid>","isLive":true,"noExpiry":true}""";

    public SpawnedEntityManagerTests()
    {
        _entityStore = new EntityStore();
        _taskStore = new TaskStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Templates:DefaultExpirySeconds"] = "300" })
            .Build();
        _manager = new SpawnedEntityManager(
            NullLogger<SpawnedEntityManager>.Instance, _entityStore, _taskStore, config);
    }

    public void Dispose()
    {
        _manager.Dispose();
        _entityStore.Shutdown();
        _taskStore.Shutdown();
    }

    private static TemplateDefinition MakeTemplate(
        string id = "test-template",
        string? rawEntityJson = null,
        TemplateConfig? config = null) =>
        new(
            TemplateId: id,
            SourcePath: "/fake/path",
            RawEntityJson: rawEntityJson ?? MinimalEntityJson,
            Config: config ?? new TemplateConfig(null, 1000, null, null, new Dictionary<string, JsonElement>()),
            BehaviorType: null,
            LoadContext: null,
            CustomTaskDescriptors: Array.Empty<Google.Protobuf.Reflection.MessageDescriptor>(),
            RawTaskConfigurationsJson: null);

    // -------------------------------------------------------------------------
    // Spawn — basic
    // -------------------------------------------------------------------------

    [Fact]
    public void Spawn_PublishesEntityToStore()
    {
        var entityId = _manager.Spawn(MakeTemplate());

        Assert.False(string.IsNullOrEmpty(entityId));
        Assert.NotNull(_entityStore.GetEntity(entityId));
    }

    [Fact]
    public void Spawn_EachCallProducesUniqueEntityId()
    {
        var template = MakeTemplate();
        var id1 = _manager.Spawn(template);
        var id2 = _manager.Spawn(template);

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Spawn_RegistersInstance_VisibleViaGetAll()
    {
        var template = MakeTemplate();
        var id = _manager.Spawn(template);

        var instances = _manager.GetAll().ToList();
        Assert.Single(instances);
        Assert.Equal(id, instances[0].EntityId);
        Assert.Equal("test-template", instances[0].TemplateId);
    }

    // -------------------------------------------------------------------------
    // Spawn — location resolution priority
    // -------------------------------------------------------------------------

    [Fact]
    public void Spawn_SpawnOptionsLocation_OverridesEntityJsonLocation()
    {
        const string entityWithLocation = """
            {"entityId":"<new_uuid>","isLive":true,"noExpiry":true,
             "location":{"position":{"latitudeDegrees":10.0,"longitudeDegrees":20.0}}}
            """;
        var options = new SpawnOptions { LatitudeDegrees = 47.5, LongitudeDegrees = -122.0 };

        var entityId = _manager.Spawn(MakeTemplate(rawEntityJson: entityWithLocation), options);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal(47.5, entity.Location.Position.LatitudeDegrees);
        Assert.Equal(-122.0, entity.Location.Position.LongitudeDegrees);
    }

    [Fact]
    public void Spawn_ConfigDefaultLocation_UsedWhenNoOptions()
    {
        var config = new TemplateConfig(
            DefaultLocation: new SpawnLocation(55.0, 10.0, null),
            TickIntervalMs: 1000,
            Category: null,
            DisplayName: null,
            Custom: new Dictionary<string, JsonElement>());

        var entityId = _manager.Spawn(MakeTemplate(config: config), options: null);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal(55.0, entity.Location.Position.LatitudeDegrees);
        Assert.Equal(10.0, entity.Location.Position.LongitudeDegrees);
    }

    [Fact]
    public void Spawn_SpawnOptionsLocation_TakesPriorityOverConfigDefault()
    {
        var config = new TemplateConfig(
            DefaultLocation: new SpawnLocation(55.0, 10.0, null),
            TickIntervalMs: 1000,
            Category: null,
            DisplayName: null,
            Custom: new Dictionary<string, JsonElement>());
        var options = new SpawnOptions { LatitudeDegrees = 1.0, LongitudeDegrees = 2.0 };

        var entityId = _manager.Spawn(MakeTemplate(config: config), options);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal(1.0, entity.Location.Position.LatitudeDegrees);
        Assert.Equal(2.0, entity.Location.Position.LongitudeDegrees);
    }

    [Fact]
    public void Spawn_AltitudeIncluded_WhenProvided()
    {
        var options = new SpawnOptions { LatitudeDegrees = 1.0, LongitudeDegrees = 2.0, AltitudeHaeMeters = 500.0 };

        var entityId = _manager.Spawn(MakeTemplate(), options);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal(500.0, entity.Location.Position.AltitudeHaeMeters);
    }

    // -------------------------------------------------------------------------
    // Spawn — name override
    // -------------------------------------------------------------------------

    [Fact]
    public void Spawn_NameOverride_ReplacesExistingAlias()
    {
        const string entityWithAlias = """
            {"entityId":"<new_uuid>","isLive":true,"noExpiry":true,
             "aliases":{"name":"Original Name"}}
            """;
        var options = new SpawnOptions { NameOverride = "Bravo-7" };

        var entityId = _manager.Spawn(MakeTemplate(rawEntityJson: entityWithAlias), options);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal("Bravo-7", entity.Aliases.Name);
    }

    [Fact]
    public void Spawn_NameOverride_AddedWhenNoExistingAlias()
    {
        var options = new SpawnOptions { NameOverride = "Alpha-1" };

        var entityId = _manager.Spawn(MakeTemplate(), options);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal("Alpha-1", entity.Aliases.Name);
    }

    // -------------------------------------------------------------------------
    // Spawn — extraJsonPatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Spawn_ExtraJsonPatch_MergesTopLevelFields()
    {
        const string entityWithAlias = """
            {"entityId":"<new_uuid>","isLive":true,"noExpiry":true,
             "aliases":{"name":"Original"}}
            """;
        // Patch overrides aliases entirely
        var patch = JsonDocument.Parse("""{"aliases":{"name":"Patched"}}""").RootElement.Clone();
        var options = new SpawnOptions { ExtraJsonPatch = patch };

        var entityId = _manager.Spawn(MakeTemplate(rawEntityJson: entityWithAlias), options);
        var entity = _entityStore.GetEntity(entityId)!;

        Assert.Equal("Patched", entity.Aliases.Name);
    }

    // -------------------------------------------------------------------------
    // Despawn
    // -------------------------------------------------------------------------

    [Fact]
    public void Despawn_KnownEntity_ReturnsTrueAndRemovesFromStore()
    {
        var entityId = _manager.Spawn(MakeTemplate());
        Assert.NotNull(_entityStore.GetEntity(entityId));

        var result = _manager.Despawn(entityId);

        Assert.True(result);
        Assert.Null(_entityStore.GetEntity(entityId)); // is_live=false triggered DELETE
    }

    [Fact]
    public void Despawn_RemovesInstanceFromGetAll()
    {
        var entityId = _manager.Spawn(MakeTemplate());
        _manager.Despawn(entityId);

        Assert.Empty(_manager.GetAll());
    }

    [Fact]
    public void Despawn_UnknownEntity_ReturnsFalse()
    {
        var result = _manager.Despawn("nonexistent-id");
        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // DespawnAll
    // -------------------------------------------------------------------------

    [Fact]
    public void DespawnAll_ReturnsCountAndClearsAll()
    {
        var template = MakeTemplate();
        _manager.Spawn(template);
        _manager.Spawn(template);
        _manager.Spawn(template);

        var count = _manager.DespawnAll();

        Assert.Equal(3, count);
        Assert.Empty(_manager.GetAll());
    }

    [Fact]
    public void DespawnAll_WhenEmpty_ReturnsZero()
    {
        Assert.Equal(0, _manager.DespawnAll());
    }
}
